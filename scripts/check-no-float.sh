#!/usr/bin/env bash
# Fails when floating-point or SQL money types appear where amounts are modelled (ADR-008, task 001):
# domain code (src/**/Domain/**, Shared.Kernel), migrations (**/Migrations/**), EF entity configurations and DbContexts.
# Lines starting with a line comment (//, --) are ignored. In configuration and DbContext files the SQL types
# are matched only inside string literals, so the Money value object and variables named "money" are not reported.
set -euo pipefail

cd "$(dirname "$0")/.."

csharp_types='\b(float|double|Single|Double|Half)\b'
sql_types='\b(float|real|money|smallmoney|double precision)\b'
sql_types_in_strings="\"[^\"]*${sql_types}"
comment_line='^[^:]+:[0-9]+:[[:space:]]*(//|--)'

list() {
  find . \( -name .git -o -name node_modules -o -name bin -o -name obj \) -prune -o -type f "$@" -print
}

mapfile -t domain_files < <(list -path '*/src/*' \( -path '*/Domain/*' -o -path '*/Shared.Kernel/*' \) -name '*.cs')
mapfile -t migration_files < <(list -path '*/Migrations/*' \( -name '*.cs' -o -name '*.sql' \))
mapfile -t mapping_files < <(list -name '*.cs' -not -path '*/Migrations/*' \( -path '*/Configurations/*' -o -name '*Configuration.cs' -o -name '*DbContext.cs' \))

scan() { # pattern grep-flags files...
  local pattern=$1 flags=$2
  shift 2
  [ $# -eq 0 ] && return 0
  grep -HnE $flags "$pattern" -- "$@" | grep -vE "$comment_line" || true
}

findings=$(
  scan "$csharp_types" '' "${domain_files[@]}" "${migration_files[@]}" "${mapping_files[@]}"
  # Migrations hold SQL, often in multi-line strings, so every line is checked.
  # The Money value object appears in EF snapshots as a CLR name ("...FaceValue#Money", "Shared.Kernel.Money"),
  # which is not the SQL money type.
  scan "$sql_types" -i "${migration_files[@]}" | grep -vE '[#.]Money\b' || true
  scan "$sql_types_in_strings" -i "${mapping_files[@]}"
)

if [ -n "$findings" ]; then
  echo "Floating-point or SQL money types are not allowed for amounts (ADR-008). Use decimal / decimal(19,4) with Money:" >&2
  echo "$findings" | sort -u >&2
  exit 1
fi

echo "No float, double, real, money or smallmoney types found."
