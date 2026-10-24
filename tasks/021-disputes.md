# 021 — Disputes & Holds UI

**Blocked by:** LQ-41 (platform role in disputes). The MVP records and facilitates; it does not adjudicate.

# Goal
Parties can raise disputes on deals; disputes place holds, notify parties and compliance, and are tracked to resolution with an audit trail.

# Context
[product-spec.md](../docs/product-spec.md) §7.5; ADR-014; [domain-model.md](../docs/domain-model.md) (Dispute, Hold).

# Requirements
- `POST /deals/{id}/disputes` ♻ (either party): type (`FundingDiscrepancy | ContractMismatch | InvoiceDisputedByDebtor | FraudSuspicion | FeeDispute | Other`), description, attachments → places a `Disputed` hold on the deal (except `FeeDispute`, which does not block the deal).
- `GET /disputes/{id}`, `POST /disputes/{id}/comments` (parties + staff; comments are visible to both parties unless marked staff-internal).
- Staff (Operations/Compliance): assign, add internal notes, resolve (`Resolved` with a resolution summary) → lifts the hold (a FraudInvestigation hold lift requires four-eyes); `Withdrawn` by the raising party.
- Hold policy table (configuration in code, documented): which transitions each hold kind blocks.
- UI: dispute button and thread on the deal page; admin dispute queue; hold badges on entities.

# Business Rules
- Implementation notes (2026-10-24, simplest options): disputes and comments in deals.disputes / deals.dispute_comments; every kind except FeeDispute places a Disputed hold through IHoldWriter, lifted when the last such dispute is resolved or withdrawn; staff endpoints for Operations and ComplianceOfficer; a pending fee reversal can be withdrawn (`POST /admin/fees/charges/{id}/withdraw-reversal`). The hold policy table is `HoldPolicy` in code and in domain-model.md; UI: disputes on the deal page and `/admin/disputes`.
- A dispute never changes financial records by itself; any resulting change goes through normal commands (e.g. a fee reversal in 018).
- A `Funded` deal with a dispute stays `Funded`.

# Technical Requirements
Reuse the `compliance.holds` mechanism; disputes in `deals.disputes` + `deals.dispute_comments`.

# API Changes
As listed + admin `GET /admin/disputes`, `POST /admin/disputes/{id}/resolve` 🔐.

# Database Changes
`deals.disputes` (extended), `deals.dispute_comments`.

# Security Considerations
Only deal parties and staff can view; internal notes never exposed to customers (test); audit all actions.

# Tests
Dispute → hold blocks the next deal transition; resolve → unblocked; internal note invisible to parties; four-eyes for fraud hold lift.

# Acceptance Criteria
- [x] Automatic disputes from 017 appear in the queue.
- [x] Hold policy enforced and tested for each kind.

# Follow-ups from the phase 2–3 review (2026-10-24)
- A `FundingDiscrepancy` dispute opened by a differing funding amount holds the deal (task 017): resolving it must let the seller confirm receipt, or end the deal.
- A pending fee reversal cannot be withdrawn yet; add a way for Finance to withdraw or reject it.

# Out of Scope
Mediation, arbitration, debtor participation.
