#!/bin/sh
# One image, two process roles (architecture.md §2): "api" (default) or "worker".
set -eu

role="${1:-api}"
case "$role" in
  api) exec dotnet /app/api/Platform.Api.dll ;;
  worker) exec dotnet /app/worker/Platform.Worker.dll ;;
  *) echo "Unknown role '$role'. Use 'api' or 'worker'." >&2; exit 64 ;;
esac
