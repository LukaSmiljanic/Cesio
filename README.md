# Cesio — B2B Factoring Marketplace (Serbia)

A digital marketplace where Serbian businesses (sellers) offer verified SEF e-invoices to licensed factoring companies and banks (factors), who compete with offers. The platform verifies, matches, documents and audits. **It does not purchase receivables or handle deal funds.**

**Phase:** documentation and regulatory validation. The empty solution foundation (task 001) exists; no business features yet. Production is blocked on the P0 items in [docs/legal-questions.md](docs/legal-questions.md).

## Documentation map
| Document | Purpose |
|----------|---------|
| [CLAUDE.md](CLAUDE.md) | Rules for AI coding agents |
| [docs/product-spec.md](docs/product-spec.md) | Actors, MVP scope, journeys, lifecycles and states |
| [docs/business-model.md](docs/business-model.md) | How the platform earns money; money and ownership flow |
| [docs/financial-model.md](docs/financial-model.md) | Formulas, worked examples, GMV revenue scenarios |
| [docs/regulatory.md](docs/regulatory.md) | Verified Serbian legal requirements with sources and system implications |
| [docs/legal-questions.md](docs/legal-questions.md) | The only register of legal and tax questions (LQ-xx), in Serbian, organised by app segment, with the deliverables requested from the law firm |
| [docs/kljucna-pitanja-za-advokata.txt](docs/kljucna-pitanja-za-advokata.txt) | Plain-text brief for the lawyer: how the platform works (incl. Terms of Use only, no separate agreement with factors) and the 9 key questions |
| [docs/pitanja-za-advokata.md](docs/pitanja-za-advokata.md) | Version to send to the lawyer: only the legal (non-tax) questions, numbered one by one, each with a short plain explanation of what we want to build |
| [docs/domain-model.md](docs/domain-model.md) | Entities, invariants, transition guards |
| [docs/database.md](docs/database.md) | SQL Server design, constraints, concurrency, idempotency |
| [docs/architecture.md](docs/architecture.md) | Modular monolith, modules, stack, deployment |
| [docs/security.md](docs/security.md) | AuthN/Z, tenant isolation, documents, fraud controls |
| [docs/compliance.md](docs/compliance.md) | KYB, beneficial owners, sanctions, factor authorization, monitoring |
| [docs/api.md](docs/api.md) | REST API conventions and key endpoints |
| [docs/integrations.md](docs/integrations.md) | SEF, CEF, APR, NBS, e-signature, email |
| [docs/audit.md](docs/audit.md) | Immutable audit trail |
| [docs/decisions.md](docs/decisions.md) | Architecture/product decision log |
| [docs/market-analysis.md](docs/market-analysis.md) | How comparable platforms onboard sellers (basis for ADR-020) |
| [docs/ux-design.md](docs/ux-design.md) | UX principles, information architecture, screens, visual direction, validation plan |
| [design/prototype.html](design/prototype.html) | Clickable prototype (fictional data) for user interviews |
| [tasks/](tasks/) | Implementation backlog (start with 000) |

## Development workflow
1. Define the requirement → 2. Research regulatory/business implications → 3. Update docs → 4. Create a task → 5. The AI reads the docs → 6. The AI writes a plan → 7. The user approves major plans → 8. Implement → 9. Tests → 10. Build → 11. Run tests → 12. Review the diff → 13. Update docs → 14. Mark the task done.

Product requirements change only through steps 1–4, never silently during implementation.

## Stack
.NET 10 · ASP.NET Core · EF Core · SQL Server (Azure SQL) · React + TypeScript · Docker · Azure (Container Apps, Azure SQL Database, Blob, Key Vault).

## Local development
Prerequisites: .NET SDK 10, Node.js 22.13+ (see `web/.nvmrc`), Docker.

```
cp .env.example .env         # then set a local-only MSSQL_SA_PASSWORD in .env (never commit it)
docker compose up -d         # SQL Server 2022 Developer, Azurite, smtp4dev (UI: http://localhost:5000)
dotnet build
dotnet test
cd web && npm ci && npm run dev
```

- Connection strings are not in config files. Set them once per project with user secrets (or `ConnectionStrings__Cesio` / `ConnectionStrings__AuditDigests` environment variables), using the SA password from `.env`:
  ```
  dotnet user-secrets --project src/Platform.Api set ConnectionStrings:Cesio "Server=localhost,1433;Database=cesio;User Id=sa;Password=<from .env>;TrustServerCertificate=True"
  ```
  The same `Cesio` value is needed for `src/Platform.Worker` and `src/Platform.Migrator`; the worker also needs `ConnectionStrings:AuditDigests` = `UseDevelopmentStorage=true` (Azurite), and the API `ConnectionStrings:Documents` = `UseDevelopmentStorage=true` for uploads.
- Database: `dotnet run --project src/Platform.Migrator` applies every module's migrations and the `app_rw` / `migrator` / `readonly_reporting` roles. New migration: `dotnet tool restore && dotnet ef migrations add <Name> --project <module project> --context <Module>DbContext --output-dir Persistence/Migrations`.
- API: `dotnet run --project src/Platform.Api` → `https://localhost:7080/health/live`, `/health/ready` (includes the database). The session cookies are Secure and SameSite=Strict, so the API runs on HTTPS: trust the development certificate once with `dotnet dev-certs https --trust`.
- SPA: `cd web && npm run dev` → `http://localhost:5173`; Vite proxies `/api` to the API. Confirmation and password-reset emails arrive in smtp4dev (`http://localhost:5000`).
- Worker: `dotnet run --project src/Platform.Worker` (outbox dispatcher, idempotency cleanup, daily audit chain digest).
- Container: `docker build -t cesio .` then `docker run cesio api`, `docker run cesio worker` or `docker run cesio migrator`.
- Integration tests (`tests/Platform.Integration.Tests`) start their own SQL Server with Testcontainers, so Docker must be running for `dotnet test`.
- Rules checked in CI: web lint and format, architecture tests (`tests/Architecture.Tests`), `scripts/check-no-float.sh` (ADR-008), gitleaks, CodeQL, dependency review.
