# 013 — Offers

# Goal
Authorized factors submit, view and withdraw immutable offers on listings they can see; sellers view, compare and reject offers.

# Context
[product-spec.md](../docs/product-spec.md) §6.4, §7.3; INV-F1, INV-A2; [api.md](../docs/api.md) §3.5; ADR-013; task 017a.

# Requirements
- `POST /marketplace/listings/{id}/offers` ♻ per api.md §3.5. Snapshot the current fee schedule version and the computed platform fee (for the factor payer) on the offer.
- `POST /offers/{id}/withdraw` ♻ (factor) → Withdrawn. `POST /offers/{id}/reject` ♻ (seller, optional reason) → Rejected.
- `GET /offers` (factor's own, filter by state) and `GET /offers/{id}` (factor owner or listing seller only).
- `GET /listings/{id}/offers` (seller): offers with **derived metrics**: discount `F − P`, discount %, seller net proceeds (minus fee if seller pays), indicative annual cost `(D/P)×365/t` with t = days from (today + funding deadline) to maturity. Labelled "indicative" (LQ-04).
- Consumers: `ListingWithdrawn/Expired/Suspended-ended` → Active offers Superseded; `FactorAuthorizationRevoked/Expired` → that factor's Active offers Expired.
- UI: factor offer form with fee preview and confirmation; factor "my offers"; seller offer comparison table (sortable by net proceeds, cost, recourse).

# Business Rules
- One Active offer per factor per listing (partial unique index); edit = withdraw + new.
- `0 < P < F`, same currency; `validUntil` ≤ listing closesAt + `maxAcceptanceWindowHours` (parameter, default 0 = must be accepted before close); `fundingDeadlineBusinessDays` 1–30.
- A factor cannot offer on a listing of an organization with which it shares PIB/MB/BO flags.

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
- [ ] Seller comparison shows correct net proceeds and indicative cost.
- [ ] Offer immutability enforced (no update endpoint; DB trigger on financial columns).

# Out of Scope
Acceptance (014), auction mechanics, counter-offers.
