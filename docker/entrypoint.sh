#!/bin/sh
# One image, three process roles (architecture.md §2): "api" (default), "worker", or "migrator" (the migration job, task 003).
set -eu

role="${1:-api}"
case "$role" in
  api) exec dotnet /app/api/Platform.Api.dll ;;
  worker) exec dotnet /app/worker/Platform.Worker.dll ;;
  migrator) exec dotnet /app/migrator/Platform.Migrator.dll ;;
  *) echo "Unknown role '$role'. Use 'api', 'worker' or 'migrator'." >&2; exit 64 ;;
esac
