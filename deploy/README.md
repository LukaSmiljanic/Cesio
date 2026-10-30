# Server setup (ADR-023)

One Linux server with Docker per environment (staging, production). Everything runs from this folder with
`compose.yml`: the API, the worker, the migrator, SQL Server Express, ClamAV and Caddy (HTTPS and the SPA). Only
Caddy publishes ports (80, 443). Deployments come from `.github/workflows/deploy.yml`; never start a new version by hand
except a rollback (below).

## Once per server

1. Install Docker Engine with the compose plugin. Create a deploy user in the `docker` group that logs in with an SSH
   key only, and allow only ports 22, 80 and 443 in the firewall.
2. Point the domain's DNS at the server (Caddy gets the certificate on first start).
3. Create the folder, for example `/opt/cesio`, owned by the deploy user, with `secrets/` and `backups/` in it
   (`chmod 700 secrets`; `sudo chown 10001 backups`, the SQL Server user in its container).
4. `cp .env.example .env` (the workflow copies `compose.yml` and `release.sh`; copy `.env.example` once by hand) and
   fill it in. `chmod 600 .env`. Passwords: long and random, letters and digits only, because they go into connection strings
   and `.env` (`openssl rand -base64 33 | tr -d '/+='`, which also meets the SQL Server complexity rule).
5. The data protection certificate, which protects the key ring that encrypts the stored secrets (SEF API keys):

   ```sh
   openssl req -x509 -newkey rsa:4096 -nodes -subj /CN=cesio-dataprotection -days 3650 \
     -keyout secrets/key.pem -out secrets/cert.pem
   openssl pkcs12 -export -inkey secrets/key.pem -in secrets/cert.pem -out secrets/dataprotection.pfx
   rm secrets/key.pem secrets/cert.pem
   chmod 644 secrets/dataprotection.pfx   # the container user reads it; the folder stays 700
   ```

   Put the export password into `DATA_PROTECTION_CERT_PASSWORD`. Keep a copy of the `.pfx` and its password outside
   the server (password manager): without it, the stored secrets cannot be decrypted after a restore.
6. Let the server pull the images: `docker login ghcr.io` with a GitHub token that only has `read:packages`.
7. In GitHub, the environment (`staging` or `production`) gets the variables `DEPLOY_HOST`, `DEPLOY_USER`,
   `DEPLOY_PATH` (the folder above), `DEPLOY_KNOWN_HOSTS` (`ssh-keyscan <host>` output, checked against the server)
   and `CESIO_DOMAIN`, and the secret `DEPLOY_SSH_KEY` (a key used only for deploying).
8. Replace the `.invalid` placeholders in `appsettings.{Staging,Production}.json` (public URL, SMTP server) with the
   real values and merge that to main.

## Each release

The workflow verifies the image signatures, copies `compose.yml` and `release.sh`, and runs
`release.sh <api digest> <web digest>`: it pulls the new images, starts SQL Server, runs the migrator of the new version (which also
creates or rotates the `cesio_app` login with `APP_DB_PASSWORD`), and only then writes the digests into `.env` (the old
file stays as `.env.previous`) and starts the new API, worker and web. A failed migration leaves `.env` and the running
version unchanged. It then smoke tests `/health/ready`.

Rollback: take the digests from `.env.previous` and run `./release.sh <api> <web>` on the server. Migrations are
expand/contract, so the previous version runs against the newer schema.

## Data to back up

| What | Where | Note |
|---|---|---|
| Database | volume `sqldata` | `BACKUP DATABASE cesio TO DISK = '/var/opt/mssql/backups/...'` lands in `./backups` |
| Documents and audit digests | volume `files` | copy the audit digests off the server as well |
| Data protection keys | volume `keys` | useless without the certificate |
| Certificate and `.env` | `./secrets`, `./.env` | also kept outside the server |

Copy backups off the server; procedures and the restore drill are in [docs/runbooks.md](../docs/runbooks.md).
