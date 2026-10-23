# 017 — CEF Step, Debtor Notification, Funding, Outcome

**Blocked by:** LQ-14 (debtor notification), LQ-17 (CEF trigger). Both sub-records are evidence-only in the MVP, so the mechanics can be built now.

# Goal
After signing, the parties record CEF registration and debtor notification evidence; the factor reports funding to the seller's verified account; the seller confirms receipt; the factor later records the outcome.

# Context
[api.md](../docs/api.md) §3.8–3.10; REG-05, REG-06, REG-09; INV-F2; ADR-004; [integrations.md](../docs/integrations.md) §2.

# Requirements
1. `ICefGateway` with `CefNotOperationalGateway`. On `DealSigned`, create a `CefRegistration` with status `NotApplicable` + a note "CEF not operational on <date>" when the gateway reports not operational; otherwise `Pending`. `POST /deals/{id}/cef-registration` ♻ (seller): reference + evidence document → `Registered`. A business parameter `cef.operationalFrom` switches behaviour.
2. `POST /deals/{id}/debtor-notification` ♻ (responsible party; MVP: either party may record it): channel, sentAt, evidence document.
3. `POST /deals/{id}/funding-report` ♻🔐 per api.md §3.8 (uses `IBankAccountQuery.IsUsableForFunding`). An amount ≠ purchase price → still `FundingReported` + an automatic `FundingDiscrepancy` dispute (021 provides the UI; here create the record and a deal hold).
4. `POST /deals/{id}/funding-confirmation` ♻🔐 per api.md §3.9. `received: true` → `Funded` + receivable `Funded` + `DealFunded` event (018 creates the fee charge). `false` → dispute + hold.
5. `POST /deals/{id}/outcome` ♻🔐 per api.md §3.10.
6. `POST /admin/deals/{id}/terminate` ♻🔐 (staff realm; compliance/Operations only, api.md §3.10a; with evidence and both parties' written confirmation documents) for `Signed`/`FundingReported` deals → `Terminated`, receivable → `Verified` (INV-R6).
7. UI: post-signing steps on the deal page; funding report form showing the seller's verified account (unmasked to the factor at this step only); confirmation buttons; outcome form.

# Business Rules
- Funding cannot be reported before `Signed`, nor to a non-verified or cooling-down account.
- Funding confirmation is never automatic (BQ-12 default).
- Outcome only after `Funded`. A `Funded` deal can never be terminated or cancelled.
- CEF `Pending` (once the CEF is live) shows a warning to both parties but does not block funding (LQ-17 may change this to blocking; keep it a parameter `cef.blockFundingUntilRegistered`, default false).
- Implementation notes (2026-10-23, simplest options; phase-2 stubs per tasks/README): payout accounts come with task 009, so `IBankAccountQuery` is a stub giving every organization one Verified RSD account derived from its id (local/dev/staging only); the factor reads the seller's account through `GET /deals/{id}/funding-accounts` while the deal is Signed (access logged; the audit keeps only the masked number); the CEF record is created in the signing transaction instead of an outbox consumer (same module); holds come with task 007, so an Open dispute (`deals.disputes`, minimal) holds the deal: every step answers 423 until task 021 resolves it; termination is an admin endpoint for Operations or compliance staff, and the confirmations must be documents of the respective party; the outcome date may not be in the future; business parameters now accept JSON scalars (`ISJSON(value, VALUE)`), which `cef.operationalFrom` and `cef.blockFundingUntilRegistered` need.

# Technical Requirements
All transitions via Deal aggregate methods with guards; outbox events `DealFundingReported`, `DealFunded`, `DealClosed`, `DealTerminated`.

# API Changes
As listed.

# Database Changes
`deals.cef_registrations`, `deals.debtor_notifications`, `deals.funding_reports`, `deals.funding_confirmations`, `deals.outcomes (UNIQUE deal_id)`, `deals.disputes` (minimal; extended in 021).

# Security Considerations
Step-up on funding/outcome; account number exposure is limited and audited; non-parties → 404.

# Tests
Full happy path from Signed to Closed; mismatch amount → dispute + hold; confirmation false → dispute; outcome before Funded → 409; terminate a Funded deal → 409; CEF parameter switch behaviour; idempotent replays.

# Acceptance Criteria
- [x] Deal can be driven end to end to `Closed` in an integration test.
- [x] Every step audited; no automatic money-state transition.

# Out of Scope
Fee charges (018), reminders (020), dispute UI (021), CEF API integration.
