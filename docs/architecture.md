# Architecture

## 1. Summary
A **modular monolith**: one deployable ASP.NET Core application (API + background workers in the same codebase, two process roles), one SQL Server database (ADR-021) with a schema per module, and a React + TypeScript SPA. Hosted on our own Linux server with Docker (ADR-023).

Expected load (≈ 5–50 deals/business day, hundreds of organizations) is tiny. The risks are **correctness, security and auditability**, not scale. Every architectural choice optimises for those.

## 2. Stack
| Concern | Choice | Notes |
|---------|--------|-------|
| Runtime | .NET 10 (LTS), ASP.NET Core minimal APIs or controllers (pick one, ADR-002) | C# nullable enabled, warnings as errors |
| Persistence | EF Core + `Microsoft.EntityFrameworkCore.SqlServer`; raw SQL allowed for locking/reporting | One `DbContext` per module, each mapped to its own schema |
| DB | SQL Server 2022 in a container: Express edition for the MVP (Standard later, no code change); Developer edition in Docker locally | ADR-021, ADR-023 |
| Frontend | React + TypeScript + Vite; TanStack Query; React Router; a form library + zod; shadcn/ui + Tailwind + TanStack Table (ADR-010) | Caddy serves the SPA and proxies `/api` on the same origin (ADR-023) |
| Auth | ASP.NET Core Identity (own user store) + mandatory TOTP/WebAuthn MFA, cookie auth for the SPA (same-site, BFF pattern) | ADR-005. Entra External ID was considered |
| Files | The server's disk under `Storage:Root` (quarantine and clean folders), ClamAV malware scanning | No public URLs ever (ADR-023) |
| Secrets | Encrypted in the database (`platform.secrets`) with ASP.NET Core data protection; the key ring is protected by an X.509 certificate | SEF API keys stored per organization (ADR-023) |
| Email | Any SMTP server with a login, behind `IEmailSender` | |
| Background jobs | `BackgroundService` + database outbox + application locks (`sp_getapplock`) | No Hangfire/Redis until a need is documented |
| Observability | OpenTelemetry; JSON console logs (MEL) collected by Docker, with PII scrubbing | A log server (Seq, Loki) later |
| Hosting | One Linux server with Docker per environment; `docker compose` runs API, worker, migrator, SQL Server, ClamAV and Caddy; one application image plus a web image (`deploy/`) | ADR-023 |
| CI / CD | GitHub Actions; Docker multi-stage build; images in GitHub Container Registry, deployed over SSH | ADR-023 |
| Local dev | `docker compose` (SQL Server Developer, smtp4dev) | |

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
    Documents/                  -- stored files: upload, malware scan, hash, authorized download (task 006; access rules come from the owning modules through IDocumentAccessPolicy)
    Notifications/              -- in-app + email, templates, preferences
    Audit/                      -- append-only audit store, query API
  Shared/
    Shared.Kernel/              -- Money, Currency, Pib, Mb, AccountNumber, Result, clock, IDs, domain events
    Shared.Infrastructure/      -- outbox, idempotency, EF conventions, file storage, secret store, business-day calendar
  Integrations/
    Integrations.Sef/           -- SEF API client (read-only operations only)
    Integrations.Cef/           -- interface + "not operational" implementation (MVP)
    Integrations.Screening/     -- sanctions/PEP adapter (local lists in MVP)
    Integrations.Signatures/    -- PAdES/QES validation
tests/ (per module: Domain.Tests, Application.Tests, Integration.Tests with Testcontainers SQL Server)
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
- `local` (compose), `dev`, `staging` (SEF **demo** environment), `prod` (SEF production). A separate server per environment; no prod data outside prod.
- Pipeline: build → unit tests → integration tests (Testcontainers) → architecture tests → SAST/dependency scan → container image (signed) → deploy to staging → smoke tests → manual approval → prod. DB migrations are applied by a dedicated migration job (Platform.Migrator) which runs with an administrative login that may manage roles, grants and the application login (database.md §6), and must be backward compatible (expand/contract).
- Location of the server and of off-site backups: not decided (ADR-023), pending LQ-35.

## 7. Scaling path (only when needed)
Scale the API horizontally (stateless; the data protection key directory shared by the replicas). Move reporting to a read replica. If outbox throughput ever becomes a bottleneck, introduce a message broker, documented as an ADR.
