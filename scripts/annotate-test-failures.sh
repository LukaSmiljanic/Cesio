#!/usr/bin/env bash
# Turns failed tests in the dotnet test output into one GitHub annotation, readable without signing in.
# GitHub shows at most 10 annotations per step, so the failed test names and the distinct exception messages
# (including inner exceptions such as SqlException) are joined into a single multi-line annotation.
set -euo pipefail

output="${1:-test-output.txt}"
sed -E 's/\x1B\[[0-9;]*[A-Za-z]//g; s/^\s+//' "$output" \
  | grep -E "failed |SqlException|---> |Exception: |Exception : |^(Expected|Actual):|\.cs:line [0-9]+" \
  | grep -vE "^at (Microsoft|System)\." \
  | awk '!seen[$0]++' \
  | head -80 > failures.txt || true

body=$(sed -e 's/%/%25/g' failures.txt | awk 'BEGIN { ORS = "%0A" } { print }')
echo "::error title=Test failures::${body}"
