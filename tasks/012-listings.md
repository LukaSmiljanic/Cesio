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
- Implementation notes (2026-10-23, simplest options; phase-2 stubs per tasks/README): `IAuthorizationQuery` is a stub that treats every factor organization as authorized (local/dev/staging only) until task 008, so the feed still requires an Approved factor organization without a hold; the related-party rule compares the factor's PIB with the seller's and the debtor's (MB and beneficial-owner links come with task 007); `maxListingDays` defaults to 30 (working default, the docs give no value); the seller must be Approved without a hold (product-spec §5); the feed uses an opaque offset cursor; listing documents (the invoice UBL and the supporting documents) are read through `GET /documents/{id}/content` with an access-log entry, without a watermark yet (task 006 has no watermarking); the detail shows the fee rule (`fees.discountShare`, default 0.15, ADR-007); the seller sees its listings at `GET /listings`; Operations staff are a user claim granted with `POST /dev/me/platform-roles` until the staff realm (task 007).

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
- [x] A factor never sees a listing outside its visibility (property-based test over random audiences).
- [x] Seller sees its listings; factor feed paginates and filters.
- [x] All transitions audited.

# Out of Scope
Offers (013), expiry job (020), appetite alerts (future).
