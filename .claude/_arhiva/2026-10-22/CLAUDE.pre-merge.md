# CLAUDE.md — Instructions for AI coding agents

B2B factoring marketplace for Serbia. Sellers list SEF e-invoices; licensed factors make offers. **The platform never buys receivables and never handles deal funds.**

## Before you code
1. Read the task file in `tasks/` completely. Implement **only** that task.
2. Read the docs it references, at minimum: [docs/domain-model.md](docs/domain-model.md), [docs/architecture.md](docs/architecture.md), and [docs/decisions.md](docs/decisions.md). For anything touching money, ownership, compliance or security, also read [docs/database.md](docs/database.md), [docs/security.md](docs/security.md) and [docs/audit.md](docs/audit.md).
3. Write a short implementation plan (files, migrations, tests). For major changes (new module, schema change to financial tables, new dependency, ADR impact) **wait for approval**.

## Hard rules
- **Do not invent requirements or legal rules.** If something is not in the docs or the task, it is not a requirement. Legal uncertainty is tracked in [docs/legal-questions.md](docs/legal-questions.md). Never "resolve" one in code.
- **Stop and ask** if a requirement is ambiguous and affects architecture, money, ownership, compliance or security.
- Never change an Accepted ADR, a state machine, or an invariant without an explicit instruction. Propose a doc change instead.
- **Money:** `decimal` / `decimal(19,4)` only, always with a currency (`Money` value object). No `float`/`double`/`real`. Rounding only where docs specify.
- **Double assignment / double financing:** never weaken INV-R1…R6. Keep the filtered unique indexes. Acceptance stays a single transaction (ADR-006).
- **Idempotency:** every financial or state-changing command endpoint requires `Idempotency-Key`.
- **Audit:** every state change and financial action writes an audit event **in the same transaction**. Never update or delete audit or financial rows; use compensating records.
- **Authorization:** every handler checks the organization context **and** resource ownership/visibility. Add a cross-tenant test for every new endpoint.
- **Security:** no secrets in code or config; no public blob URLs; no PII, tokens or account numbers in logs.
- **Architecture:** modular monolith. Modules talk via `*.Contracts` or outbox events; no reaching into another module's internals or schema. Don't add microservices, Kafka, Redis, Kubernetes, Elasticsearch or a mediator framework.
- Prefer simple code. No new NuGet/npm dependency without stating why in the plan.
- Do not modify files unrelated to the task.

## Definition of done
- Unit tests for business rules and state transitions (including failure paths); integration tests (Testcontainers SQL Server) for persistence, constraints, concurrency and authorization.
- `dotnet build` (warnings as errors) and `dotnet test` pass; for frontend changes, `npm run build`, `npm run lint` and `npm test` pass.
- Review your own diff: no stray files, no commented-out code, no TODOs without a task reference.
- Update docs if behaviour changed (with the user's approval) and tick the task's acceptance criteria.
- The review agents below have run and their findings are handled.

## Review agents (run them without being asked)
All three live in `.claude/agents/`. They are read-only and give an independent review, because they did not write the change.
- **`legal-review`**: when a task, feature or doc change touches licensing, money flow, contracts or e-signatures, KYC/AML, personal data, SEF/CEF, fees or tax, retention, liability or user-facing legal text. Run it when planning such a task (before coding) and when such docs change. Add its proposed questions to `docs/legal-questions.md`, **in Serbian only**, following that file's §5. If it finds an open LQ the task depends on, stop and ask.
- **`code-rules-review`**: after implementing a task, **before** ticking acceptance criteria or saying the task is done. Give it the task file and the diff. Fix every Blocker; fix every Major or list it to the user with a reason. If it says "Not ready", the task is not done.
- **`doc-consistency`**: whenever you changed anything in `docs/`, `tasks/`, `README.md` or this file, before reporting the change as finished. Give it the list of changed files. Fix contradictions you caused; report the others to the user.
- Do not run them for questions, plans or read-only exploration. The exception is `legal-review`, which also runs when a task with legal impact is being planned. If an agent cannot run, say so instead of skipping silently.
- Report the agents' result to the user in one or two lines (counts and anything left open).

## Commands (once the solution exists — task 001)
```
docker compose up -d         # sqlserver, azurite, smtp4dev
dotnet build
dotnet test
cd web && npm ci && npm run dev
```
