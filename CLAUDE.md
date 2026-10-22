# CLAUDE.md — Instructions for AI coding agents

B2B factoring marketplace for Serbia (Cesio). Sellers list SEF e-invoices; licensed factors make offers. **The platform never buys receivables and never handles deal funds.** It earns a factor-paid success fee of 15% of the factor's offered discount (ADR-007).

## Domain glossary
- **Seller (*ustupilac*):** company that sells a receivable from its invoice.
- **Debtor (*dužnik*):** company that owes on the invoice.
- **Factor (*faktor*):** licensed factoring company or bank that buys the receivable.
- **Assignment (*ustupanje*):** transfer of the receivable from seller to factor. Who notifies the debtor, and how, is open (LQ-14, task 017).
- **Recourse / non-recourse (*sa regresom / bez regresa*):** whether the factor can claim from the seller if the debtor does not pay.
- **Discount (*diskont*):** face value minus the purchase price the factor offers (`D = F − P`, [docs/financial-model.md](docs/financial-model.md)).
- **Platform fee:** share of the discount; rules in the `cesio-money-rules` skill.
- Invoice ≠ Receivable (ADR-012): the invoice is immutable SEF evidence; the receivable has the lifecycle. Full model: [docs/domain-model.md](docs/domain-model.md).

## Before you code
1. Read the task file in `tasks/` completely. Implement **only** that task.
2. Read the docs it references, at minimum: [docs/domain-model.md](docs/domain-model.md), [docs/architecture.md](docs/architecture.md), and [docs/decisions.md](docs/decisions.md). For anything touching money, ownership, compliance or security, also read [docs/database.md](docs/database.md), [docs/security.md](docs/security.md) and [docs/audit.md](docs/audit.md).
3. Write a short implementation plan (files, migrations, tests); for a task or a larger feature use the `planner` agent. For major changes (new module, schema change to financial tables, new dependency, ADR impact) **wait for approval**.

## Hard rules
- **Do not invent requirements or legal rules.** If something is not in the docs or the task, it is not a requirement. Legal uncertainty is tracked in [docs/legal-questions.md](docs/legal-questions.md). Never "resolve" one in code. A recorded *Radna pretpostavka* (working assumption) lets work proceed, but the LQ stays open.
- **Stop and ask** if a requirement is ambiguous and affects architecture, money, ownership, compliance or security.
- Never change an Accepted ADR, a state machine, or an invariant without an explicit instruction. Propose a doc change instead.
- **Money:** `decimal` / `decimal(19,4)` only, always with a currency (`Money` value object). No `float`/`double`/`real`. Rounding only where docs specify.
- **Double assignment / double financing:** never weaken INV-R1…R6. Keep the filtered unique indexes. Acceptance stays a single transaction (ADR-006).
- **Idempotency:** every financial or state-changing command endpoint requires `Idempotency-Key`.
- **Audit:** every state change and financial action writes an audit event **in the same transaction**. Never update or delete audit or financial rows; use compensating records.
- **Authorization:** every handler checks the organization context **and** resource ownership/visibility. A factor never sees competing offers. Add a cross-tenant test for every new endpoint.
- **Security:** no secrets in code or config; no public blob URLs; no PII, tokens or account numbers in logs.
- **Architecture:** modular monolith. Modules talk via `*.Contracts` or outbox events; no reaching into another module's internals or schema. Don't add microservices, Kafka, Redis, Kubernetes, Elasticsearch or a mediator framework.
- **Deployment** goes through the CI pipeline, never directly to production. Rules in the `cesio-deploy` skill.
- Prefer simple code. No new NuGet/npm dependency without stating why in the plan.
- Do not modify files unrelated to the task.

## Way of working
- One main session writes code, sequentially. No parallel coding sessions until the core (data model, fee calculation, state machines) is stable.
- Give agents a short, precise task and ask for a short conclusion, not a file dump.
- If the same attempt fails twice, stop and tell the user instead of trying again.

## Definition of done
- Unit tests for business rules and state transitions (including failure paths); integration tests (Testcontainers SQL Server) for persistence, constraints, concurrency and authorization.
- `dotnet build` (warnings as errors) and `dotnet test` pass; for frontend changes, `npm run build`, `npm run lint` and `npm test` pass.
- Review your own diff: no stray files, no commented-out code, no TODOs without a task reference.
- Update docs if behaviour changed (with the user's approval) and tick the task's acceptance criteria.
- The review agents below have run and their findings are handled.

## Review agents (run them without being asked)
All live in `.claude/agents/`. They are read-only and give an independent review, because they did not write the change.
- **`code-reviewer`**: after every finished change, **before** ticking acceptance criteria or saying the task is done. Give it the task file, the plan and the diff. Fix every KRITIČNO; fix every VAŽNO or list it to the user with a reason. If it says "nije spremno", the task is not done.
- **`money-reviewer`** (with `code-reviewer`, in parallel): when the change touches amounts, discount, fees, rounding or currencies.
- **`security-reviewer`** (with `code-reviewer`, in parallel): when the change touches authentication, authorization, other organizations' data, personal data, documents, audit or integrations.
- **`legal-review`**: when a task, feature or doc change touches licensing, money flow, contracts or e-signatures, KYC/AML, personal data, SEF/CEF, fees or tax, retention, liability or user-facing legal text. Run it when planning such a task (before coding) and when such docs change; also for research on regulation or external-system documentation (can run in the background). Add its proposed questions to `docs/legal-questions.md`, **in Serbian only**, following that file's §5 (the `cesio-add-legal-question` skill, installed outside the repo, does this). If it finds an open LQ the task depends on, stop and ask.
- **`doc-consistency`**: whenever you changed anything in `docs/`, `tasks/`, `README.md` or this file, before reporting the change as finished. Give it the list of changed files. Fix contradictions you caused; report the others to the user.
- Do not run them for questions, plans or read-only exploration. The exception is `legal-review`, which also runs when a task with legal impact is being planned. If an agent cannot run, say so instead of skipping silently.
- Report the agents' result to the user in one or two lines (counts and anything left open).

## Commands
```
cp .env.example .env         # once; set a local SA password
docker compose up -d         # sqlserver, azurite, smtp4dev
dotnet build                 # warnings as errors
dotnet test
cd web && npm ci && npm run dev
cd web && npm run build && npm run lint && npm test
dotnet tool restore && dotnet ef migrations add <Name> --project <module project> --context <Module>DbContext
```

## Structure
- `src/Platform.Api`, `src/Platform.Worker`: hosts (HTTP API, background jobs and outbox dispatcher).
- `src/Modules/<Module>/Modules.<Module>` + `Modules.<Module>.Contracts`: Identity, Organizations, Compliance, Receivables, Marketplace, Deals, Fees, Notifications, Audit.
- `src/Shared/Shared.Kernel`: `Money`, `Currency`, typed IDs, PIB/MB, `Result`. `src/Shared/Shared.Infrastructure`: `ModuleDbContext`, `UnitOfWork`, outbox, idempotency, request context.
- `src/Integrations/Integrations.<System>`: SEF, CEF, screening, signatures.
- `tests/`: kernel, architecture and API tests. `web/`: React SPA. `docs/`: specs and ADRs (`decisions.md`). `tasks/`: backlog.
