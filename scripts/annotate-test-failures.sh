#!/usr/bin/env bash
# Turns failed tests in the dotnet test output into GitHub annotations, which are readable without signing in.
set -euo pipefail

grep -E "^\s*failed |^\s*(Assert|Xunit|System|Microsoft)\.[A-Za-z.]*(Exception|Error)|^\s*(Expected|Actual):" "${1:-test-output.txt}" \
  | head -60 \
  | while IFS= read -r line; do echo "::error title=Test failure::${line}"; done
