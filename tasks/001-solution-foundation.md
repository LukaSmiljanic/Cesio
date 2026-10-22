# 001 — Solution Foundation

# Goal
Create the empty, buildable modular-monolith solution, local environment and CI, with architecture rules enforced from day one.

# Context
[architecture.md](../docs/architecture.md) §2–4, §6; ADR-001, ADR-002.

# Requirements
- .NET 10 solution with the folder layout from architecture.md §3: `Platform.Api`, `Platform.Worker`, one project set per module (`Modules.X`, `Modules.X.Contracts`), `Shared.Kernel`, `Shared.Infrastructure`, `Integrations.*` (empty), and test projects.
- `Directory.Build.props`: nullable enabled, `TreatWarningsAsErrors`, analyzers on, central package management (`Directory.Packages.props`).
- Health endpoints `/health/live` and `/health/ready` (DB check added in 003).
- `docker-compose.yml`: SQL Server 2022 Developer (`mcr.microsoft.com/mssql/server`), Azurite, smtp4dev (ADR-021).
- Multi-stage `Dockerfile` producing one image; the entrypoint argument selects the API or the worker.
- `web/`: Vite React TS scaffold with ESLint, Prettier, Vitest; build only (UI shell in 004).
- CI pipeline (GitHub Actions): restore, build, test, `npm ci && npm run build && npm test`, CodeQL, dependency review.
- Architecture tests: modules may reference only `Shared.*`, other modules' `.Contracts` and `Integrations.*`; `.Contracts` must not reference implementations; `Integrations.*` reference only `Shared.*`. The module-to-module direction in architecture.md §3 is not enforced by this task.
- CI check script that fails if `float`, `double`, `real`, `double precision` or `money` appear in `src/**/Domain/**`, `**/Migrations/**`, or entity configurations.

# Business Rules
None.

# Technical Requirements
OpenTelemetry wired with a console exporter locally. ProblemDetails enabled. `TimeProvider` registered.

# API Changes
Health endpoints only.

# Database Changes
None.

# Security Considerations
No secrets in the repo; add `.gitignore` and secret scanning (gitleaks in CI). HTTPS redirection and HSTS configured (except local).

# Tests
An architecture test project with the rules above (one deliberately violating sample verified locally, then removed). A smoke test that the API starts (WebApplicationFactory).

# Acceptance Criteria
- [x] `docker compose up -d && dotnet build && dotnet test` green on a clean machine (after `cp .env.example .env` and setting a local SA password, see README).
- [x] CI green on a PR. (Green on `main`, 2026-10-22; the product owner chose to push to `main` without PRs.)
- [x] An architecture violation fails the build.
- [x] A float in a domain file fails the CI check.

# Out of Scope
Auth, DB schema, UI screens, Azure resources.
