# 012 — Listings & Marketplace Feed

**Blocked by:** BQ-06 (debtor disclosure/default audience). Default: full debtor identity to the selected audience.

# Goal
Sellers publish verified receivables as time-boxed listings to all or selected verified factors; factors browse a feed and a detail view that only show listings they are allowed to see.

# Context
[product-spec.md](../docs/product-spec.md) §6.3–6.4, §7.2; INV-R2, INV-R4, INV-A2, INV-A4; [api.md](../docs/api.md) §3.3–3.4.

# Requirements
- `POST /receivables/{id}/listings` ♻ per api.md §3.3 (audience, exclusions, closesAt, optional reserve price).
- `POST /listings/{id}/withdraw` ♻ (seller; only if no accepted offer) → Withdrawn; receivable → Verified; Active offers → Superseded (implemented in 013; here emit `ListingWithdrawn`).
- Admin suspend/resume listing (Operations, with reason).
- `GET /marketplace/listings` (cursor pagination; filters: amount range, days to maturity, debtor PIB, sort by closesAt/amount) and `GET /marketplace/listings/{id}` with visibility rules: factor authorized; audience includes it; not excluded; not related (shared PIB/MB/BO flag → hidden pending review).
- Listing detail exposes the data in api.md §3.4; never the reserve price or other offers.
- Document downloads for listing attachments via 006 with watermarking + access log.
- UI: seller "publish listing" dialog; seller listing page; factor marketplace feed + detail.

# Business Rules
- `closesAt` ≤ maturity − `minDaysAtListing` and ≤ now + `maxListingDays`.
- Opening requires SEF status checked ≤ 24 h ago (re-poll if older; fail closed).
- One Open listing per receivable (filtered unique index).

# Technical Requirements
The visibility predicate is implemented once (a query object) and reused by feed, detail, documents and offer creation.

# API Changes
As above + admin `POST /admin/listings/{id}/suspend|resume`.

# Database Changes
`marketplace.listings`, `marketplace.listing_audience` (DDL in database.md).

# Security Considerations
Rate-limit feed and detail per user/org; audit listing state changes; access log for detail views and downloads; non-visible listing → 404.

# Tests
Visibility matrix (authorized/unauthorized, included/excluded, selected audience, expired authorization, related party); unique Open listing race (two parallel POSTs → one 201, one 409); SEF stale → re-poll; withdraw guard.

# Acceptance Criteria
- [ ] A factor never sees a listing outside its visibility (property-based test over random audiences).
- [ ] Seller sees its listings; factor feed paginates and filters.
- [ ] All transitions audited.

# Out of Scope
Offers (013), expiry job (020), appetite alerts (future).
