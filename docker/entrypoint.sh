#!/bin/sh
# One image, three process roles (architecture.md §2): "api" (default), "worker", or "migrator" (the migration job, task 003).
# Each role starts in its own folder, which is its content root, so it finds its appsettings*.json.
set -eu

role="${1:-api}"
case "$role" in
  api) cd /app/api && exec dotnet Platform.Api.dll ;;
  worker) cd /app/worker && exec dotnet Platform.Worker.dll ;;
  migrator) cd /app/migrator && exec dotnet Platform.Migrator.dll ;;
  *) echo "Unknown role '$role'. Use 'api', 'worker' or 'migrator'." >&2; exit 64 ;;
esac
