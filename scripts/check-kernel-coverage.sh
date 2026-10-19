#!/usr/bin/env bash
# Fails unless the Shared.Kernel package in a Cobertura report has 100% branch coverage (task 002).
# Usage: scripts/check-kernel-coverage.sh <cobertura.xml>
set -euo pipefail

report=${1:?Usage: $0 <cobertura.xml>}

package=$(grep -oE '<package [^>]*name="Shared\.Kernel"[^>]*>' "$report" || true)
if [ -z "$package" ]; then
  echo "Shared.Kernel not found in $report." >&2
  exit 1
fi

branch_rate=$(echo "$package" | grep -oE 'branch-rate="[^"]*"' | cut -d'"' -f2)
if [ "$branch_rate" != "1" ]; then
  echo "Shared.Kernel branch coverage is $branch_rate; 100% (1) is required." >&2
  exit 1
fi

echo "Shared.Kernel branch coverage is 100%."
