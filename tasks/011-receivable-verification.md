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

# Technical Requirements
A check pipeline as a list of `IReceivableCheck` implementations (ordered, each returning Pass/Warn/Fail + details). All thresholds come from business parameters.

# API Changes
`PUT /receivables/{id}/declaration`, `POST /receivables/{id}/documents`, `POST /receivables/{id}/submit`, `POST /receivables/{id}/cancel`; admin `GET /admin/receivables/review-queue`, `POST /admin/receivables/{id}/decision` 🔐.

# Database Changes
`receivables.seller_declarations`, `receivables.verification_checks`, `receivables.declaration_versions` (text + hash), parameters seeded.

# Security Considerations
Audit each check and decision. SEF unavailable → submission stays `PendingVerification` and is retried by a job (fail closed, never auto-verify).

# Tests
One test per check (pass/fail/warn); fingerprint duplicate across sellers → manual; SEF storno → Rejected; state guards (submit twice → 409); fraud-rejected re-import blocked.

# Acceptance Criteria
- [ ] All checks implemented and parameterised.
- [ ] Manual queue works with an audit trail.
- [ ] No path verifies a receivable without a fresh SEF approved status.

# Out of Scope
Listing (012), periodic SEF monitoring (020), CEF lookup (future).
