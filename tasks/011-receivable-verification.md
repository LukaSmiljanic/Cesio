# 011 — Receivable Declarations, Submission & Verification

**Blocked by:** LQ-10 (maximum tenor), LQ-23 (SEF approval as acknowledgement), and approved declaration texts from task 000. Until then: max tenor is a parameter (default 180 days), and the declaration text is version `DRAFT-0`, which blocks production.

# Goal
A seller completes declarations and attachments for a Draft receivable and submits it. Automated checks plus a manual review queue decide Verified or Rejected.

# Context
[product-spec.md](../docs/product-spec.md) §6.3, §7.1; INV-R3/R4/R5; REG-03, REG-08, REG-11; [compliance.md](../docs/compliance.md) §3.

# Requirements
1. `PUT /receivables/{id}/declaration`: answers to the current declaration version (all mandatory yes/no items; any "no" blocks submission with an explanation). Stored append-only with user, time, IP.
2. Attach supporting documents (purpose `ReceivableSupport`; at least 1 required: contract/order or delivery proof; clean scan).
3. `POST /receivables/{id}/submit` ♻ → `PendingVerification`; run the checks:

| Code | Rule | Fail/Warn |
|------|------|-----------|
| `SEF_FRESH_APPROVED` | Fresh SEF status (re-poll) = buyer approved; not cancelled/storno | Fail |
| `SELLER_IS_ISSUER` | Supplier PIB = seller PIB | Fail |
| `SELLER_APPROVED` | Org Approved, no hold | Fail |
| `NOT_MATURED` | maturity − today ≥ `minDaysAtListing` | Fail |
| `MAX_TENOR` | maturity − today ≤ `maxTenorDays` | Fail |
| `AMOUNT_LIMITS` | min ≤ faceValue ≤ max | Fail |
| `FACE_VALUE_CONSISTENT` | faceValue ≤ gross − credit notes known | Fail |
| `DEBTOR_SCREENING` | Debtor sanctions screening | PotentialMatch → Warn; Confirmed → Fail |
| `DEBTOR_NOT_SELLER` | Debtor PIB ≠ seller PIB | Fail |
| `RELATED_PARTY` | Shared representatives/BOs between seller and debtor (where known) | Warn |
| `DUPLICATE_FINGERPRINT` | Same fingerprint on any other non-rejected receivable | Warn |
| `NEW_DEBTOR_FOR_SELLER` | First receivable on this debtor | Warn (configurable) |
| `DECLARATION_COMPLETE` | Current version, all yes | Fail |

   Any Fail → `Rejected` (reasons recorded). Any Warn, or faceValue above the auto-approve threshold → manual queue. Otherwise → `Verified`.
4. Admin review queue: receivable detail, checks, documents, SEF snapshot; decide Verified/Rejected with a reason code + comment; may place a FraudInvestigation hold.
5. Seller: `POST /receivables/{id}/cancel` (Draft/Verified only).
6. UI: declaration form, attachments, submit, status/reasons view; admin queue.

# Business Rules
- Checks are append-only `verification_checks` rows; re-verification produces new rows.
- A receivable rejected with reason `Fraud` blocks re-import of the same SEF invoice (unique index still holds because Rejected is excluded, so add an explicit application check for fraud-rejected invoices).
- Implementation notes (2026-10-23, simplest options; phase-2 stubs per tasks/README): holds were a no-holds stub until task 007 (real since 2026-10-24); organizations are approved in development with `POST /dev/organizations/{id}/approve`; the ComplianceOfficer role is a user claim granted in development with `POST /dev/me/platform-roles` until the staff realm (task 007); `DEBTOR_SCREENING` warns (manual review) until screening exists (task 007); `RELATED_PARTY` passes with "no data yet"; configurable defaults: `minDaysAtListing` 15 (product-spec.md §parameters), `maxTenorDays` 180 (working default only; LQ-10 stays open), amount limits 500,000–200,000,000 RSD (BQ-03 default until decided), `autoApproveMaxFaceValue` 0 (no automatic approval); seller read endpoints `GET /receivables/declaration` (current declaration text) and `GET /receivables/{id}/checks` (seller-scoped check codes, results and reasons for the status view); a re-import of a non-fraud rejected invoice creates a new Draft receivable; when SEF is unavailable the receivable stays PendingVerification for the task 020 job; Requirement 4's FraudInvestigation hold is deferred: it is not in this task's delivery and comes when holds are implemented (task 007).

# Technical Requirements
A check pipeline as a list of `IReceivableCheck` implementations (ordered, each returning Pass/Warn/Fail + details). All thresholds come from business parameters.

# API Changes
`PUT /receivables/{id}/declaration`, `POST /receivables/{id}/documents`, `POST /receivables/{id}/submit`, `POST /receivables/{id}/cancel`, `GET /receivables/declaration`, `GET /receivables/{id}/checks`; admin `GET /admin/receivables/review-queue`, `GET /admin/receivables/{id}`, `POST /admin/receivables/{id}/decision` 🔐.

# Database Changes
`receivables.seller_declarations`, `receivables.verification_checks`, `receivables.declaration_versions` (text + hash), parameters seeded.

# Security Considerations
Audit each check and decision. SEF unavailable → submission stays `PendingVerification` and is retried by a job (fail closed, never auto-verify).

# Tests
One test per check (pass/fail/warn); fingerprint duplicate across sellers → manual; SEF storno → Rejected; state guards (submit twice → 409); fraud-rejected re-import blocked.

# Acceptance Criteria
- [x] All checks implemented and parameterised.
- [x] Manual queue works with an audit trail.
- [x] No path verifies a receivable without a fresh SEF approved status.

# Out of Scope
Listing (012), periodic SEF monitoring (020), CEF lookup (future).
