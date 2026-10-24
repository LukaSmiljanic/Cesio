# 022 — Dashboards & Admin Back Office

# Goal
Role-specific dashboards for sellers, factors and staff, and a consolidated admin back office with Entra ID SSO for staff.

# Context
[product-spec.md](../docs/product-spec.md) §9–10; [security.md](../docs/security.md) §1, §3 (platform roles).

# Requirements
- Seller dashboard: receivables by state, open listings with offer counts and best net proceeds, deals needing action, funded volume (month/YTD), average indicative cost.
- Factor dashboard: matching new listings, active offers, win rate, deals needing action, portfolio by maturity bucket, overdue outcomes, fees due.
- Admin dashboard: queues (KYB, authorizations, receivable review, bank accounts, disputes), deals stuck per step > N days, GMV and take rate (month), funnel (imported → verified → listed → offers → accepted → funded), integration health (SEF error rate, outbox backlog, email failures, job status).
- Business parameters editor (versioned, audited, step-up; four-eyes for money-related parameters).
- Audit search (Auditor/Compliance): by entity, org, actor, action, date range; export (audited).
- Staff SSO: Entra ID (OIDC) for the admin app, with group → platform role mapping and conditional access; remove local staff passwords after migration.
- CSV exports: deals (party view), receivables (seller), fee statements (018).

# Business Rules
Metrics are computed from read queries; no denormalised financial totals become a source of truth.

Implementation notes (2026-10-24, simplest options): each module exposes read-only figures through its contracts (`IReceivableStats`, `IMarketplaceStats`, `IDealStats`, `IFeeStats`, `IComplianceStats`, `IOrganizationStats`, `INotificationStats`, `OutboxMonitor`), and the API host composes the dashboards; amounts are summed per currency and never across currencies; "deals needing action" counts the party's open deals before Funded; the indicative cost is discount ÷ face value over its funded deals; a deal is "stuck" after 5 days without a state change; the funnel is per calendar month (UTC); the parameters editor is for PlatformAdmin and Finance, every change is a new version (step-up, audited), and keys starting with `fees.` or ending in `FaceValue` / `Amount` are proposed and approved by a second person (`platform.parameter_proposals`); audit search and export are for ComplianceOfficer and the new Auditor role, both access-logged, without IP or user agent (LQ-38); CSV exports: `GET /deals/export` (the party's view), `GET /receivables/export` (the seller), access-logged and formula-safe. No new indexes and no reporting principal were needed. Not built: Entra ID staff SSO (it needs the OpenID Connect package, a new dependency that waits for the product owner's approval) and masked PII for a Support role (no Support role exists yet).

# Technical Requirements
Read-only SQL queries (Dapper or EF no-tracking) against the primary DB in the MVP; indexes added as needed (document them in database.md).

# API Changes
`GET /dashboards/seller`, `GET /dashboards/factor`, admin `GET /admin/dashboard`, `GET/PUT /admin/parameters/{key}`, `GET /admin/audit`, `GET /admin/audit/export`.

# Database Changes
Indexes only (documented). If a reporting principal is used, define the `readonly_reporting` grants here (database.md §6: no personal-data columns, no `platform.idempotency_keys`).

# Security Considerations
Dashboards respect tenant isolation (tests); Support role sees masked PII; audit exports.

# Tests
Dashboard numbers vs. seeded scenarios; cross-tenant isolation; parameter change requires step-up and creates a new version.

# Acceptance Criteria
- [x] All three dashboards show correct figures for a seeded data set.
- [ ] Staff log in via Entra ID in staging.

# Out of Scope
BI tooling, data warehouse, premium analytics.
