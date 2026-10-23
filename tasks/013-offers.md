# 013 — Offers

**Blocked by (partial):** LQ-60 (legal nature of an offer that is subject to the factor's CDD) and LQ-04 (offer-comparison wording and disclosure of the factor-paid fee to sellers). The mechanics (immutable offers, validity, withdrawal) may be built. Offer wording in the UI and the Terms waits for the answer. If the answer changes the offer model (e.g. offers cannot be binding), stop and ask.

# Goal
Authorized factors submit, view and withdraw immutable offers on listings they can see; sellers view, compare and reject offers.

# Context
[product-spec.md](../docs/product-spec.md) §6.4, §7.3; INV-F1, INV-A2; [api.md](../docs/api.md) §3.5; ADR-013; task 017a.

# Requirements
- `POST /marketplace/listings/{id}/offers` ♻ per api.md §3.5. Snapshot the current fee schedule version and the computed platform fee (for the factor payer) on the offer.
- `POST /offers/{id}/withdraw` ♻ (factor) → Withdrawn. `POST /offers/{id}/reject` ♻ (seller, optional reason) → Rejected.
- `GET /offers` (factor's own, filter by state) and `GET /offers/{id}` (factor owner or listing seller only).
- `GET /listings/{id}/offers` (seller): offers with **derived metrics**: discount `F − P`, discount %, seller net proceeds (minus fee if seller pays), indicative annual cost `(D/P)×365/t` (if the seller pays the fee `PF`: `((D + PF)/(P − PF))×365/t`, per financial-model.md §2) with t = days from (today + funding deadline) to maturity. Labelled "indicative" (LQ-04).
- Consumers: `ListingWithdrawn/Expired/Suspended-ended` → Active offers Superseded; `FactorAuthorizationRevoked/Expired` → that factor's Active offers Expired.
- UI: factor offer form with fee preview and confirmation; factor "my offers"; seller offer comparison table (sortable by net proceeds, cost, recourse).

# Business Rules
- One Active offer per factor per listing (filtered unique index); edit = withdraw + new.
- `0 < P < F`, same currency; `validUntil` ≤ listing closesAt + `maxAcceptanceWindowHours` (parameter, default 0 = must be accepted before close); `fundingDeadlineBusinessDays` 1–30.
- A factor cannot offer on a listing of an organization with which it shares PIB/MB/BO flags.
- Implementation notes (2026-10-23, simplest options): the related-party rule is the listing visibility rule of task 012 (same PIB as the seller or the debtor; MB and BO links come with task 007); the offer stores the whole platform fee and the seller's part (0 for the factor-paid seed schedule) with the fee schedule version; `ListingWithdrawn` supersedes Active offers inside the withdrawal transaction (same module), and listing expiry (task 020) and FactorAuthorizationRevoked/Expired (task 008) will do the same when those exist; offer expiry at `validUntil` comes with task 020; business days are Monday–Friday (no holiday calendar yet); `GET /marketplace/listings/{id}/fee-preview` gives the factor its fee before submitting; the seller comparison shows the factor's name; UI wording waits for LQ-60 and LQ-04, so the comparison labels the cost "indicative" only.

# Technical Requirements
Derived metrics computed server-side using `Money` and decimal (display rounding to 2 decimals for %).

# API Changes
As listed.

# Database Changes
`marketplace.offers` (DDL in database.md).

# Security Considerations
Factors never see other factors' offers (tests). Audit submit/withdraw/reject/expire/supersede. Rate limit offer creation.

# Tests
Validation matrix; duplicate active offer race; supersede on withdraw/expiry; authorization revoked → offers expired; a factor reading another factor's offer → 404; derived metric golden values match financial-model.md.

# Acceptance Criteria
- [x] Seller comparison shows correct net proceeds and indicative cost.
- [x] Offer immutability enforced (no update endpoint; column-level DENY + AFTER UPDATE trigger on financial columns, declared with `HasTrigger` in the EF mapping; database.md §1).

# Out of Scope
Acceptance (014), auction mechanics, counter-offers.
