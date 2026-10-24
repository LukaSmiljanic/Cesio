# Task Backlog (MVP)

Rules: one task at a time. Read [CLAUDE.md](../CLAUDE.md) first. A task is done only when its acceptance criteria are ticked, the build and tests are green, and the diff has been reviewed.
"Blocked by" names the open items that gate **production launch** for that task. Since 2026-10-22 development does not wait for them (see "Building without legal answers" below). A "(partial)" entry concerns only the scope named in the task file.

| # | Task | Depends on | Blocked by (legal/business) |
|---|------|------------|-----------------------------|
| 000 | [Regulatory & commercial validation](000-regulatory-validation.md) | — | — |
| 001 | [Solution foundation](001-solution-foundation.md) | — | — |
| 002 | [Shared kernel: Money & identifiers](002-shared-kernel.md) | 001 | — |
| 003 | [Persistence, outbox, idempotency, audit](003-persistence-audit-foundation.md) | 002 | — |
| 004 | [Identity, MFA, SPA shell](004-identity-mfa.md) | 003 | — |
| 005 | [Organizations, memberships, roles](005-organizations.md) | 004 | LQ-73 (partial: factor Terms acceptor rule) |
| 006 | [Secure documents](006-documents.md) | 003 | — |
| 007 | [KYB case, beneficial owners, screening, holds](007-kyb-compliance.md) | 005, 006 | LQ-30, LQ-31 (screening list choice) |
| 007a | [Seller claim, pristupnica, official-email confirmations](007a-seller-claim-pristupnica.md) | 007, 010 | LQ-32 (signature level; MVP default per ADR-020), LQ-51 (partial: SEF-key consent wording in the pristupnica) |
| 008 | [Factor authorization verification](008-factor-authorization.md) | 007 | LQ-09 (partly answered), LQ-73 (partial: factor Terms acceptance) |
| 009 | [Bank accounts](009-bank-accounts.md) | 007, 007a | — |
| 010 | [SEF connection & invoice import](010-sef-invoice-import.md) | 005, 006 | LQ-51 (partial: key protection and liability terms; read-only client may proceed) |
| 011 | [Receivable declarations & verification](011-receivable-verification.md) | 010, 007 | LQ-10, LQ-23, declaration texts |
| 012 | [Listings & marketplace feed](012-listings.md) | 011, 008 | BQ-06 |
| 013 | [Offers](013-offers.md) | 012, 017a | LQ-60, LQ-04 (partial: offer wording, fee disclosure and terms; mechanics may proceed) |
| 014 | [Offer acceptance & deal creation](014-offer-acceptance.md) | 013 | LQ-25, LQ-60, LQ-61 (partial: wording, terms and signer rule; mechanics may proceed) |
| 015 | [Deal: CDD gate, contract, terms checklist](015-deal-contract.md) | 014 | LQ-29, LQ-61 (partial), ADR-018 |
| 016 | [Signature validation & assignment](016-signatures.md) | 015 | LQ-24, ADR-011 |
| 017 | [CEF step, debtor notification, funding, outcome](017-funding-outcome.md) | 016, 009 | LQ-14, LQ-17 |
| 017a | [Fee schedules & fee preview](017a-fee-schedules.md) | 003 | LQ-74 (partial: applying a new schedule version to existing factors in production) |
| 018 | [Fee charges & statements](018-fee-charges.md) | 017, 017a | LQ-43, LQ-44 |
| 019 | [Notifications](019-notifications.md) | 005 | — |
| 020 | [Scheduled jobs: expiry & SEF monitoring](020-scheduled-jobs.md) | 012–017 | — |
| 021 | [Disputes & holds UI](021-disputes.md) | 017 | LQ-41 |
| 022 | [Dashboards & admin back office](022-dashboards-admin.md) | 018 | — |
| 023 | [Security hardening & pen-test readiness](023-security-hardening.md) | all | — |
| 024 | [Azure infrastructure & deployment](024-deployment.md) | 001 (can start early) | LQ-35 |

Each feature task includes its API, persistence, tests **and** the minimal UI screens listed in it.

## Build order (product owner, 2026-10-19)
The table above lists the *logical* dependencies. The build order puts a thin end-to-end slice first, so the whole flow can be demonstrated early, and adds the full onboarding afterwards.

| Phase | Goal | Tasks |
|-------|------|-------|
| 0 | Prerequisites (not coding) | Git repository; .NET 10 SDK, Docker, Node; **SEF demo access for two companies** (seller + buyer, registered by their legal representative); lawyer package (000); ADR-010 decided |
| 1 | Foundation | 001 → 002 → 003 |
| 2 | **Thin slice:** a real SEF demo invoice becomes visible to a factor | 004 → 005 → 006 → 010 → 011 → 012, with **stubs** for 007/008 (below) |
| 3 | The deal | 017a → 013 → 014 → 015 → 016 → 017 → 018 |
| 4 | Full onboarding and background jobs | 007 → 007a → 008 → 009 → 019 → 020 |
| 5 | Hardening and deployment | 021 → 022 → 023 → 024 (024 infrastructure can start in phase 1) |

**Building without legal answers (product owner, 2026-10-22):** all tasks are built now, without waiting for open LQs; corrections follow in later versions. Where a task depends on an open LQ, implement the simplest option the docs allow, make it configurable where the docs say so, and reference the LQ in the code or task plan. An open LQ never becomes a hard-coded rule. "Blocked by" entries still apply to **production launch** (task 000 go/no-go), not to development. SEF demo access for the two test companies is arranged after the build; until then SEF is tested against a fake client.

**Phase 2 stubs (temporary, development and demo only):** `IKybQuery.IsApproved`, `IHoldQuery.HasActiveHold` and `IAuthorizationQuery.IsFactorAuthorized` are backed by a seed (an approved seller and a verified factor created by a dev-only seeding command), not by the KYB and authorization workflows. The stubs live behind the same contracts that tasks 007 and 008 implement later, so no caller changes. A stub must never be registered outside the `local`, `dev` and `staging` environments (startup check). Each task's plan states which stub it relies on. Task 011 uses an endpoint-backed variant instead of the seed: a seller is approved with `POST /dev/organizations/{id}/approve`, the ComplianceOfficer staff role is a user claim granted with `POST /dev/me/platform-roles`, the development approval also makes the caller's account Signatory (standing in for the pristupnica of task 007a for a seller and for the staff-created membership of task 008 for a factor), and the `DEBTOR_SCREENING` check warns until sanctions lists are imported. Task 007 (2026-10-24) replaced the hold stub with real holds and screening. Task 012's `IAuthorizationQuery` treats every factor organization as authorized until task 008; callers still require an Approved factor organization. Task 017's `IBankAccountQuery` gives every organization one Verified RSD payout account derived from its id until task 009.

