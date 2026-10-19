# 024 — Azure Infrastructure & Deployment

**Blocked by:** LQ-35 (data transfer / region) for production. Dev and staging can be built earlier.

# Goal
Reproducible Azure environments (dev, staging, prod) defined in Bicep, with a gated CI/CD pipeline, a migration job, monitoring and backups.

# Context
[architecture.md](../docs/architecture.md) §2, §6; [database.md](../docs/database.md) §6–7; [audit.md](../docs/audit.md) §1 (WORM); ADR-016.

# Requirements
- Bicep modules: resource group per environment; Azure Container Apps environment (API app, worker app, migration job); Azure Database for PostgreSQL Flexible Server (prod: zone-redundant HA, PITR 35 days, private networking); Storage accounts (documents: private, Defender for Storage malware scanning; audit digests: immutable time-based retention policy); Key Vault (RBAC, purge protection); Container Registry; Log Analytics + Application Insights; ACS Email with a custom domain (SPF/DKIM/DMARC); a front door or managed ingress with TLS.
- Managed identities for app → Key Vault/Storage/DB (Entra auth for PostgreSQL where feasible).
- DB roles (`app_rw`, `migrator`, `readonly_reporting`) created by a bootstrap script; grants per database.md §6.
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
