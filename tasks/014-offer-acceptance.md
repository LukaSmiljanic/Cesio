# 014 — Offer Acceptance & Deal Creation

**Blocked by (partial):** LQ-25 (whether acceptance itself is binding), LQ-60 (offer subject to the factor's CDD) and LQ-61 (who may accept for a company). As far as we know today, they affect only the UI text, the Terms and the Signatory rule, not the transaction mechanics. If an answer changes the acceptance model or the signer rule, stop and ask.

# Goal
A seller Signatory accepts one offer. In a single atomic transaction this closes the listing, supersedes other offers, reserves the receivable and creates the Deal. Double acceptance is impossible.

# Context
[api.md](../docs/api.md) §3.6; ADR-006; INV-R1, INV-R2, INV-R4, INV-R5, INV-A3; [database.md](../docs/database.md) §4.

# Requirements
1. `POST /offers/{id}/accept` ♻🔐 exactly as in api.md §3.6.
2. Before opening the transaction: re-poll SEF (fail closed on unavailability); check the factor is authorized; check holds on seller, receivable and factor.
3. Transaction (Deals module application service, shared connection):
   - `SELECT … WITH (UPDLOCK, ROWLOCK)` on the listing and receivable rows; verify versions/states;
   - offer → Accepted; other Active offers → Superseded; listing → Closed; receivable → Reserved;
   - insert Deal (`AwaitingFactorCdd`, reference `DL-YYYY-NNNNNN`, terms copied from the offer, fee snapshot, `signatureDeadline` = the end of the day `signatureDeadlineBusinessDays` business days after acceptance, product-spec §parameters);
   - deal_state_history row; audit events; outbox events (`OfferAccepted`, `DealCreated`, `OfferSuperseded`×n).
4. Deal read endpoints: `GET /deals`, `GET /deals/{id}`, `GET /deals/{id}/timeline` (seller and factor of the deal only).
5. UI: accept confirmation dialog showing net proceeds and key terms + step-up; deal page skeleton with timeline.

# Business Rules
- Only Signatory, with step-up ≤ 5 min.
- The offer must be Active and `validUntil` > now; the listing Open; the receivable Listed; the SEF status fresh ≤ 1 h and approved; faceValue still consistent.
- Implementation notes (2026-10-23, simplest options): the write contracts are `IListingWriter.CloseForAcceptanceAsync` and `IReceivableWriter.ReserveAsync`; the row locks are taken with `SELECT … WITH (UPDLOCK, ROWLOCK)` before the rows are loaded; a refused step throws `DomainConflictException` and the unit of work rolls the step back to a savepoint, so a 4xx answer (which the idempotency middleware commits) never leaves partial writes; the deal reference comes from the sequence `deals.deal_reference_seq`; the signature deadline is the end of the day `deals.signatureDeadlineBusinessDays` (default 5, product-spec §parameters) business days after acceptance, Monday–Friday, UTC; `If-Match` with the listing version is checked when sent; a changed SEF status at acceptance makes the receivable Ineligible (the listing stays as it is until task 020); seller Signatory comes from the development approval until the pristupnica of task 007a; holds became real with task 007.

# Technical Requirements
Write contracts: `Marketplace.Contracts.IListingWriter.CloseForAcceptance(...)`, `Receivables.Contracts.IReceivableWriter.Reserve(...)`, each operating on the shared transaction. No events are processed synchronously inside the transaction.

# API Changes
`POST /offers/{id}/accept`, `GET /deals`, `GET /deals/{id}`, `GET /deals/{id}/timeline`.

# Database Changes
`deals.deals`, `deals.deal_state_history`, including `ux_deal_live_per_receivable` (INV-R1) and `UNIQUE(offer_id)`.

# Security Considerations
Audit all transitions with the step-up timestamp; non-party access → 404.

# Tests (mandatory)
- **Concurrency:** 2 sellers' sessions accepting two different offers on the same listing in parallel (20 iterations) → exactly one deal; the other gets 409/412.
- **DB guarantee:** a direct insert of a second live deal for the same receivable fails on the unique index.
- Acceptance after offer expiry → 409; SEF storno at acceptance → 409 `receivable_ineligible` and the receivable → Ineligible; hold active → 423; non-Signatory → 403; missing step-up → 401.
- Idempotent replay returns the same dealId.

# Acceptance Criteria
- [x] All tests above green (the 423 hold path uses the holds of task 007).
- [x] One transaction per acceptance verified (rollback on any failure leaves no partial state).

# Out of Scope
Deal steps after creation (015–017).
