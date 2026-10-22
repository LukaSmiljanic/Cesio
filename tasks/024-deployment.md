# 024 — Azure Infrastructure & Deployment

**Blocked by:** LQ-35 (data transfer / region) for production. Dev and staging can be built earlier.

# Goal
Reproducible Azure environments (dev, staging, prod) defined in Bicep, with a gated CI/CD pipeline, a migration job, monitoring and backups.

# Context
[architecture.md](../docs/architecture.md) §2, §6; [database.md](../docs/database.md) §6–7; [audit.md](../docs/audit.md) §1 (WORM); ADR-016.

# Requirements
- Bicep modules: resource group per environment; Azure Container Apps environment (API app, worker app, migration job); Azure SQL Database (General Purpose; prod: zone-redundant, point-in-time restore 35 days + long-term retention, private endpoint) (ADR-021); Storage accounts (documents: private, Defender for Storage malware scanning; audit digests: immutable time-based retention policy); Key Vault (RBAC, purge protection); Container Registry; Log Analytics + Application Insights; ACS Email with a custom domain (SPF/DKIM/DMARC); a front door or managed ingress with TLS.
- Managed identities for app → Key Vault/Storage/DB (Microsoft Entra authentication for Azure SQL; no SQL logins in production).
- Forwarded headers from the known ingress/front door only, so rate limiting, audit IP addresses and the session list see the client IP (task 004); per-account limits on login, MFA and password reset if the IP limit proves too coarse.
- Azure SQL service tier and cost estimate (General Purpose vCore vs. serverless for dev/staging) documented before provisioning (ADR-021).
- DB roles (`app_rw`, `migrator`, `readonly_reporting`) created and granted by Platform.Migrator (task 003; database.md §1 and §6); the migration job runs with a principal that may manage roles, while API and worker connect as `app_rw` members.
- Pipeline: build → test → scan → signed image → deploy to staging (SEF demo) → smoke tests → manual approval → prod (SEF prod). Migrations via the migration job before app rollout (expand/contract).
- Alerts per security.md §8 and architecture health (5xx rate, latency, outbox backlog, job failures).
- Backup policy + quarterly restore test procedure documented.
- Region: EU (Proposed ADR-016); confirm before prod.

# Business Rules
No production customer data in dev/staging.

# Technical Requirements
All configuration via Key Vault references/app settings; no secrets in the pipeline logs.

# API Changes
None.

# Database Changes
Role bootstrap scripts.

# Security Considerations
Private endpoints for DB, Storage and Key Vault in prod; least-privilege RBAC; separate subscriptions (or at least separate RGs with policies) for prod.

# Tests
Smoke tests after deploy (health, login page, DB connectivity); IaC what-if in the PR; restore drill in staging.

# Acceptance Criteria
- [ ] `staging` deployable from main with one approval.
- [ ] WORM retention verified on the audit digest container.
- [ ] Restore drill executed and documented.

# Out of Scope
Multi-region DR, Kubernetes, CDN optimisation.
