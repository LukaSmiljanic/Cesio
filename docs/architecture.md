# Architecture

## 1. Summary
A **modular monolith**: one deployable ASP.NET Core application (API + background workers in the same codebase, two process roles), one PostgreSQL database with a schema per module, and a React + TypeScript SPA. Hosted on Azure in an EU region.

Expected load (≈ 5–50 deals/business day, hundreds of organizations) is tiny. The risks are **correctness, security and auditability**, not scale. Every architectural choice optimises for those.

## 2. Stack
| Concern | Choice | Notes |
|---------|--------|-------|
| Runtime | .NET 10 (LTS), ASP.NET Core minimal APIs or controllers (pick one, ADR-002) | C# nullable enabled, warnings as errors |
| Persistence | EF Core + Npgsql; raw SQL allowed for locking/reporting | One `DbContext` per module, each mapped to its own schema |
| DB | PostgreSQL 16+ (Azure Flexible Server, zone-redundant HA in production) | |
| Frontend | React + TypeScript + Vite; TanStack Query; React Router; a form library + zod; component library TBD (ADR-010) | The SPA is served by Azure Static Web Apps or from the API container |
| Auth | ASP.NET Core Identity (own user store) + mandatory TOTP/WebAuthn MFA, cookie auth for the SPA (same-site, BFF pattern) | ADR-005. Entra External ID was considered |
| Files | Azure Blob Storage (private containers only), Defender for Storage malware scanning | No public URLs ever |
| Secrets | Azure Key Vault + managed identity | SEF API keys stored per organization as Key Vault secrets |
| Email | Azure Communication Services Email (or another transactional provider) behind `IEmailSender` | |
| Background jobs | `BackgroundService` + Postgres outbox + advisory locks | No Hangfire/Redis until a need is documented |
| Observability | OpenTelemetry → Azure Monitor / Application Insights; structured logging (Serilog or MEL) with PII scrubbing | |
| Hosting | Azure Container Apps (API + worker as two apps from one image) | App Service for Containers is an equal alternative |
| IaC / CI | Bicep; GitHub Actions or Azure DevOps; Docker multi-stage build | |
| Local dev | `docker compose` (Postgres, Azurite, MailHog/smtp4dev) | |

**Not used** (no documented requirement): microservices, Kafka/Service Bus, Kubernetes, Elasticsearch, Redis, MediatR-style frameworks (allowed only if ADR'd), CQRS with separate stores.

## 3. Modules and boundaries

```
src/
  Platform.Api/                 -- host: HTTP endpoints, auth, middleware, composition root
  Platform.Worker/              -- host: background jobs (same modules, different entrypoint)
  Modules/
    Identity/                   -- users, MFA, sessions
    Organizations/              -- orgs, memberships, roles, bank accounts, legal reps, terms acceptance
    Compliance/                 -- KYB cases, BOs, screening, factor authorizations, holds
    Receivables/                -- debtors, invoices (SEF import), receivables, declarations, verification
    Marketplace/                -- listings, audience, offers
    Deals/                      -- deal workflow, contract, signatures, CEF, notification, funding, outcomes, disputes
    Fees/                       -- fee schedules, fee charges, statements
    Notifications/              -- in-app + email, templates, preferences
    Audit/                      -- append-only audit store, query API
  Shared/
    Shared.Kernel/              -- Money, Currency, Pib, Mb, AccountNumber, Result, clock, IDs, domain events
    Shared.Infrastructure/      -- outbox, idempotency, EF conventions, blob storage, Key Vault, business-day calendar
  Integrations/
    Integrations.Sef/           -- SEF API client (read-only operations only)
    Integrations.Cef/           -- interface + "not operational" implementation (MVP)
    Integrations.Screening/     -- sanctions/PEP adapter (local lists in MVP)
    Integrations.Signatures/    -- PAdES/QES validation
tests/ (per module: Domain.Tests, Application.Tests, Integration.Tests with Testcontainers PostgreSQL)
web/   (React SPA)
```

How the requested modules were consolidated:
| Requested | Placed in | Why |
|-----------|-----------|-----|
| Companies, Sellers, Factors | Organizations | Seller and factor are roles of an organization; separate modules would duplicate tenancy logic |
| Verification, Compliance | Compliance | One place for KYB, screening, authorizations and holds |
| Invoices | Receivables | Invoice and receivable are separate entities but share one consistency boundary |
| Transactions, Assignments, Contracts, Payments | Deals | All are steps of one deal aggregate and must change together atomically |
| Fees, Notifications, Audit, Identity, Marketplace, Offers | As named (Offers sit inside Marketplace) | |

### Module rules
1. Each module exposes a **public contract** (`Modules.X.Contracts`: interfaces, DTOs, integration events). Internals are `internal`. An architecture test (NetArchTest/ArchUnitNET) fails the build on violations.
2. **Synchronous calls** between modules go through contracts (e.g. `Marketplace` asks `Compliance.IAuthorizationQuery.IsFactorAuthorized(orgId)`).
3. **Side effects** across modules go through **domain/integration events via the outbox** (e.g. `DealFunded` → Fees creates a charge, Notifications sends email).
4. **Exception:** offer acceptance spans Marketplace + Receivables + Deals and **must be atomic**. It is implemented as one application service in the **Deals** module that calls Marketplace and Receivables *write contracts* within a shared `DbTransaction` (all module DbContexts share one connection). This is documented in ADR-006. It is the only allowed cross-module transaction pattern, and it is used solely for ownership/money transitions.

### Dependency direction
```
Identity ← Organizations ← Compliance ← Receivables ← Marketplace ← Deals → Fees
                                                         ↑                  ↓
                                   Notifications, Audit (event consumers; nobody depends on them for logic)
Integrations.* ← used by Receivables (SEF), Deals (CEF, Signatures), Compliance (Screening)
```

## 4. Layering inside a module
`Domain` (entities, value objects, invariants; no EF attributes) → `Application` (commands/queries, authorization checks, transactions) → `Infrastructure` (EF mappings, repositories, adapters) → `Endpoints` (HTTP mapping). Keep it pragmatic: no generic repositories, no unnecessary interfaces.

## 5. Cross-cutting
- **Authorization pipeline:** authentication → active organization resolution (`X-Organization-Id` header validated against memberships) → policy (role) → **resource check in the application layer** (does this org own or have access to this entity?). The resource check is never skipped; see [security.md](security.md).
- **Idempotency middleware** for commands marked `[Idempotent]`.
- **Audit:** an application-level `IAuditWriter` is called inside the command's transaction (not an EF interceptor guessing intent). An EF interceptor additionally records field-level diffs for configured tables.
- **Clock:** `TimeProvider` is injected; business dates use the Europe/Belgrade time zone for "today" and business-day logic.
- **Errors:** RFC 7807 `ProblemDetails` with stable `errorCode`s.
- **Configuration:** business parameters live in the DB (versioned, audited), not in appsettings.

## 6. Environments & deployment
- `local` (compose), `dev`, `staging` (SEF **demo** environment), `prod` (SEF production). Separate Azure subscriptions or resource groups; no prod data outside prod.
- Pipeline: build → unit tests → integration tests (Testcontainers) → architecture tests → SAST/dependency scan → container image (signed) → deploy to staging → smoke tests → manual approval → prod. DB migrations are applied by a dedicated migration job with the `migrator` role, and must be backward compatible (expand/contract).
- Region: an EU region (e.g. Germany West Central / West Europe), pending LQ-35. Backups stay in the paired EU region.

## 7. Scaling path (only when needed)
Scale the API horizontally (stateless; data protection keys in Blob + Key Vault). Move reporting to a read replica. If outbox throughput ever becomes a bottleneck, introduce Azure Service Bus, documented as an ADR.
