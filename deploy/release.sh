#!/bin/sh
# Starts one version on the server (ADR-023), run by the deploy workflow over SSH from this folder:
#   ./release.sh <api image digest> <web image digest>
# Migrations run first (expand/contract, cesio-migration skill). The digests go into .env only after the migration
# succeeded, so a failed migration leaves the running version and .env unchanged.
# Rollback: run it again with the previous digests (kept in .env.previous).
set -eu
cd "$(dirname "$0")"

api_digest="$1"
web_digest="$2"
for digest in "$api_digest" "$web_digest"; do
  case "$digest" in
    sha256:*) ;;
    *) echo "Digests must look like sha256:<hex>." >&2; exit 64 ;;
  esac
  case "${digest#sha256:}" in
    "" | *[!a-f0-9]*) echo "Digests must look like sha256:<hex>." >&2; exit 64 ;;
  esac
done

# Variables in the environment take precedence over .env for docker compose.
export CESIO_IMAGE_DIGEST="$api_digest" CESIO_WEB_IMAGE_DIGEST="$web_digest"
docker compose pull --quiet
docker compose up -d --wait sqlserver
docker compose --profile migrate run --rm migrator

cp .env .env.previous
sed -i -e "s/^CESIO_IMAGE_DIGEST=.*/CESIO_IMAGE_DIGEST=$api_digest/" \
       -e "s/^CESIO_WEB_IMAGE_DIGEST=.*/CESIO_WEB_IMAGE_DIGEST=$web_digest/" .env

docker compose up -d --remove-orphans
docker image prune -f > /dev/null
