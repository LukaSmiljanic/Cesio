#!/usr/bin/env bash
# Turns failed tests in the dotnet test output into one GitHub annotation, readable without signing in.
# GitHub shows at most 10 annotations per step, so the failed test names and the distinct exception messages
# (including inner exceptions such as SqlException) are joined into a single multi-line annotation.
set -euo pipefail

output="${1:-test-output.txt}"
{
  grep -E "^\s*failed " "$output" | sed -E 's/^\s+//' | sort -u | head -40
  grep -E "(Exception|Error)( :|:) " "$output" | sed -E 's/^\s+//' | sort -u | head -40
  grep -E "^\s*(Expected|Actual):" "$output" | sed -E 's/^\s+//' | head -20
} > failures.txt || true

body=$(sed -e 's/%/%25/g' failures.txt | awk 'BEGIN { ORS = "%0A" } { print }')
echo "::error title=Test failures::${body}"
