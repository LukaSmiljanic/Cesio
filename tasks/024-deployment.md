# 024 — Azure Infrastructure & Deployment

**Blocked by:** LQ-35 (data transfer / region) for production; LQ-47 and LQ-70 before the audit WORM policy is locked (locking is irreversible) and for the retention values; LQ-38 for log retention; LQ-86 before the first restore drill. Dev and staging can be built earlier.

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
No production customer data in dev/staging. The one exception is the quarterly restore drill ([runbooks.md](../docs/runbooks.md) §3), which copies production into staging and deletes the copy; it is open under LQ-86 and is not run before LQ-86 is answered.

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
- [ ] Restore drill executed and recorded in [runbooks.md](../docs/runbooks.md) §3.

# Out of Scope
Multi-region DR, Kubernetes, CDN optimisation.

# Follow-ups from the phase 4 review (2026-10-24)
- [done 2026-10-24] Configure forwarded headers (`UseForwardedHeaders` with the known proxies): the per-IP rate limits of `/auth/*` and `/confirmations` would otherwise share one quota behind the proxy.
- [done 2026-10-24] Require `Auth:PublicBaseUrl` outside Development (links in emails and confirmations).

# Follow-ups from task 023 (2026-10-24)
- Persist ASP.NET data protection keys to Blob, protected by a Key Vault key (security.md §5; runbooks.md §2).
- Documents storage: soft delete and versioning (runbooks.md §3).
- Container image and IaC scans in the pipeline; record the result in runbooks.md §5.
- The SPA's CSP and security headers at the hosting layer (the API sets its own, task 023).
- Verify the DB grants (`app_rw` cannot UPDATE/DELETE the audit log or delete fee charges) in the deployed staging database.
- Launch checklist: the items above, the first restore drill and the external penetration test with no open critical or high findings (task 023).

# Implementation notes (2026-10-24, simplest options; nothing provisioned yet)
- **Templates:**
  - `infra/registry.bicep` creates the container registry.
  - `infra/main.bicep` creates everything else for one environment in its resource group: Log Analytics and Application Insights; two managed identities; Key Vault (RBAC, purge protection); a documents storage account; an audit storage account; Azure SQL; a Container Apps environment with the API, the worker and the migration job.
  - Documents storage: no shared keys and no public access; versioning and 30-day soft delete.
  - Audit storage: the `audit-digests` container has a 3650-day time-based immutability policy. The retention period is open (LQ-38/LQ-47), and the policy is locked by hand only after LQ-47 is answered in writing (3650 days is a placeholder).
  - Azure SQL: Entra-only, with an Entra group as admin. Production runs General Purpose Gen5 with 2 vCores, zone redundancy, 35-day point-in-time restore and long-term retention (12 weeks, 12 months, 10 years; a placeholder pending LQ-47 and LQ-48); log retention 365 days in prod (placeholder, LQ-38, LQ-47). Dev and staging run serverless with 1 vCore.
- **Identities:** the API and the worker run as the app identity. The migration job adds that identity to Azure SQL as an Entra user and a member of `app_rw` (`Database:AppPrincipals`, `DatabaseMigrator.AddApplicationPrincipalAsync`). The migration job runs as its own identity.
  - One-time step per environment: a member of the SQL admin group adds the migrator identity to that group.
- **Blob access:** when the connection string is an `https://` endpoint, blob access goes through the managed identity (`BlobContainers`), so no account key exists in the configuration.
- **Pipeline:** `.github/workflows/deploy.yml` and `deploy-environment.yml`.
  - An IaC scan (Trivy config) runs before every deployment: after a successful CI on `main`, and on `v*` tags. It also runs on PRs that touch `infra/**` or `deploy*.yml`, and those PRs get a what-if against staging.
  - Staging deploys after `ci.yml` (build, tests, scans) succeeds on `main` (`workflow_run`). Production deploys on a `v*` tag; the workflow checks that the tag points at a `main` commit. Production does not build: it takes the image staging built for that commit, verifies its signature, imports it by digest and copies the signature. The production identity therefore needs AcrPull on the staging registry (`STAGING_REGISTRY` variable).
  - Staging: registry → build → image scan (Trivy) → push → keyless signature (cosign). Both environments: verify the signature → migration job → apps by digest → smoke test (`/health/ready`, `/api/v1/auth/antiforgery`).
  - The approvals are environment protection rules in GitHub: `staging` with one reviewer, `production` with a manual approval. They act before the migrations.
  - The Azure login is OIDC federation, so there are no secrets in the pipeline.
  - GitHub environment variables:
    - Azure: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `RESOURCE_GROUP`;
    - application: `PUBLIC_BASE_URL`, `SEF_BASE_URL`;
    - database: `SQL_ADMIN_GROUP_ID`, `SQL_ADMIN_GROUP_NAME`;
    - email: `EMAIL_SMTP_HOST`, `EMAIL_FROM`;
    - production only: `STAGING_REGISTRY`, the staging registry's login server.
  - The environment `staging-plan` carries the same variables for the PR what-if. It runs PR code, so its identity is read-only (Reader on the staging resource group), is federated to that environment only and is not the deployer.
- **Gaps and limits:**
  - The IaC scan fails on HIGH/CRITICAL findings. Expected findings are public network access on SQL, Key Vault and Storage and the `AllowAzureServices` rule. They are accepted risks until private endpoints exist, and each one goes into `.trivyignore` with its reason after the first run lists the rule IDs.
  - Production has `CanNotDelete` locks on the audit storage account, the SQL server and the Key Vault.
  - `ingressNetwork` defaults to `0.0.0.0/0` (one hop). This is safe only while the container is reachable through the Container Apps ingress alone. Set the ingress range or the front door's range when a VNet or front door is added.
  - The smoke test does not cover the SPA login page until the SPA hosting exists.
- **Rollback:** activate the previous Container Apps revision (single revision mode). This is why migrations must stay backward compatible.
- **Application changes:**
  - Forwarded headers are read only from `ForwardedHeaders:KnownNetworks`, one hop (`ForwardedHeaders.cs`). The container is reachable only through the Container Apps ingress, which appends the client address.
  - `Auth:PublicBaseUrl` is required outside Development (`PlatformDefaults`); the CI image smoke test sets it.
- **Not done:**
  - **Needs approval of new packages:**
    - data protection keys in Blob + Key Vault (`Azure.Extensions.AspNetCore.DataProtection.Blobs` and `.Keys`);
    - the Application Insights exporter (`Azure.Monitor.OpenTelemetry.Exporter`; the connection string is already set);
    - ACS Email. The SMTP relay of ACS needs SMTP credentials, and the sender has no authentication yet; alternatively `Azure.Communication.Email`.
    - Until the data protection package is approved, the API runs one replica, since more replicas would not share cookie keys; every restart signs users out.
  - Private endpoints and a VNet for production (SQL, Storage and Key Vault are public with Entra-only or RBAC access for now; SQL also has the `AllowAzureServices` firewall rule).
  - Alerts (security.md §8).
  - Defender for Storage (subscription level).
  - The SPA hosting and its CSP.
  - The cost estimate in the Azure pricing calculator for the SKUs above (ADR-021).
  - The production region (LQ-35, ADR-016 Proposed).
  - The templates have not been compiled or what-if checked here (no Azure CLI in this environment); the first PR run does that.
