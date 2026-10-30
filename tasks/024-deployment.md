# 024 — Server Infrastructure & Deployment

**Blocked by:** LQ-35 (where the data may be hosted) and LQ-87 (hosting provider as processor) for production; LQ-47 and LQ-70 before the retention of audit digests and backups is fixed; LQ-38 for log retention; LQ-86 before the first restore drill. Staging can be built earlier.

# Goal
Reproducible environments (staging, production), each on its own Linux server with Docker (ADR-023), with a gated CI/CD pipeline, a migration step, monitoring and backups.

# Context
[architecture.md](../docs/architecture.md) §2, §6; [database.md](../docs/database.md) §6–7; [audit.md](../docs/audit.md) §1; ADR-021, ADR-023. ADR-016 (Azure) is superseded; the earlier Azure templates were never deployed and were removed on 2026-10-30.

# Requirements
- One `docker compose` file per environment (`deploy/compose.yml`): API, worker, migrator (one-off), SQL Server Express (ADR-021, ADR-023), ClamAV, and Caddy, which terminates HTTPS, serves the SPA and forwards `/api` and `/health` to the API. Only Caddy publishes ports.
- The API and worker connect with the SQL login `cesio_app` (member of `app_rw`), which the migrator creates or rotates; the migrator runs with the administrative login (database.md §6).
- Forwarded headers trusted only from the compose network of Caddy, so rate limiting, audit IP addresses and the session list see the client IP (task 004).
- Pipeline: build → test → scan → signed images → deploy to staging (SEF demo) → smoke tests → manual approval → prod (SEF prod), promoting the same images. Migrations run before the apps start (expand/contract).
- Alerts per security.md §8 and architecture health (5xx rate, latency, outbox backlog, job failures).
- Backup policy + quarterly restore test procedure documented ([runbooks.md](../docs/runbooks.md) §3).
- Server location: not decided (ADR-023); confirm before prod (LQ-35).

# Business Rules
No production customer data in staging. The one exception is the quarterly restore drill ([runbooks.md](../docs/runbooks.md) §3), which copies production into staging and deletes the copy; it is open under LQ-86 and is not run before LQ-86 is answered.

# Technical Requirements
Settings in `appsettings.{Staging,Production}.json`; passwords only in `.env` on the server; no secrets in the repository or the pipeline logs.

# API Changes
None.

# Database Changes
Role and login bootstrap in Platform.Migrator.

# Security Considerations
SSH by key only; firewall open for 22, 80 and 443 only; SQL Server and ClamAV not reachable from outside; a separate server, key and passwords per environment; the data protection certificate kept apart from the backups of the database.

# Tests
Smoke tests after deploy (health, login, DB connectivity); the CI image smoke test with the Production settings; restore drill in staging.

# Acceptance Criteria
- [ ] `staging` deployable from main with one approval.
- [ ] Audit digests copied off the server daily, where they cannot be overwritten.
- [ ] Restore drill executed and recorded in [runbooks.md](../docs/runbooks.md) §3.

# Out of Scope
Multi-server DR, Kubernetes, CDN optimisation.

# Follow-ups from the phase 4 review (2026-10-24)
- [done 2026-10-24] Configure forwarded headers (`UseForwardedHeaders` with the known proxies).
- [done 2026-10-24] Require `Auth:PublicBaseUrl` outside Development (links in emails and confirmations).

# Follow-ups from task 023 (2026-10-24)
- [done 2026-10-30] Persist ASP.NET data protection keys (a volume, encrypted with a certificate; ADR-023).
- Documents storage: the backups of the `files` volume (runbooks.md §3).
- [done 2026-10-30] Container image and configuration scans in the pipeline (Trivy); record the result of the first run in runbooks.md §5.
- [done 2026-10-30] The SPA's CSP and security headers at the hosting layer (`deploy/Caddyfile`).
- Verify the DB grants (`app_rw` cannot UPDATE/DELETE the audit log or delete fee charges) in the deployed staging database.
- Launch checklist: the items above, the first restore drill and the external penetration test with no open critical or high findings (task 023).

# Implementation notes (2026-10-30, ADR-023; no server provisioned yet)
- **Files:** `deploy/compose.yml`, `deploy/Caddyfile`, `deploy/release.sh`, `deploy/.env.example`, `deploy/README.md` (server setup, backups), `web/Dockerfile` (SPA built into the Caddy image).
- **Settings:** `appsettings.{Staging,Production}.json` of the API and worker hold `Auth:PublicBaseUrl`, `Sef:BaseUrl`, `Email:*`, `Storage:Root`, `DataProtection:*`, `Documents:Scanner = ClamAV`, `ClamAV:*` and, for the API, `ForwardedHeaders:KnownNetworks = 172.30.0.0/24` (the compose network). The domains and SMTP host are `.invalid` placeholders until they are known. Passwords come from `.env` as environment variables.
- **Pipeline:** `.github/workflows/deploy.yml`.
  - A configuration scan (Trivy) runs before every deployment and on PRs that touch `deploy/**`, the Dockerfiles or the workflow.
  - Staging deploys after `ci.yml` succeeds on `main` (`workflow_run`): build both images, scan them (Trivy), push them to GHCR by commit SHA and sign them (cosign, keyless).
  - Production deploys on a `v*` tag on a `main` commit; it does not build but takes the digests staging pushed for that commit.
  - Both: verify the signatures, copy `compose.yml` and `release.sh` over SSH, run `release.sh <api digest> <web digest>` (pull, migrator, apps), smoke test (`/health/ready`, `/api/v1/auth/antiforgery`) through the public domain.
  - The approvals are environment protection rules in GitHub: `staging` with one reviewer, `production` with a manual approval.
  - Each GitHub environment holds `DEPLOY_HOST`, `DEPLOY_USER`, `DEPLOY_PATH`, `DEPLOY_KNOWN_HOSTS`, `CESIO_DOMAIN` (variables) and `DEPLOY_SSH_KEY` (secret).
- **Rollback:** `release.sh` with the digests in `.env.previous`; migrations stay backward compatible.
- **Not done:**
  - Alerts (security.md §8) and a log server.
  - Scheduled backups and the off-site copy (the procedure is in deploy/README.md and runbooks.md §3; the script depends on the server).
  - `READ_COMMITTED_SNAPSHOT ON` on the database (database.md §4).
  - The servers themselves, the domains and the SMTP provider.
