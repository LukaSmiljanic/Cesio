# 017a — Fee Schedules & Fee Preview

(Numbered 017a because it is needed by 013 but belongs to the Fees area.)

**Blocked by (partial):** LQ-74 (whether a new schedule version binds factors who accepted earlier Terms, in production; building versions may proceed). BQ-01 is decided (15% of the discount, factor-paid, no minimum, no cap; ADR-007). LQ-03 (whether a fee based on the factor's discount is permitted) proceeds on the product owner's working assumption of 2026-10-22 (legal-questions.md §5). Store the basis as a field of the schedule (only `Discount` in the MVP); if the lawyer's answer rules it out, stop and ask.

# Goal
A versioned, immutable fee schedule and a pure calculation service that produces the platform fee from an offer's discount (face value − purchase price) and payer (ADR-007).

# Context
[financial-model.md](../docs/financial-model.md) §1, §4–5; [business-model.md](../docs/business-model.md) §1; ADR-007, ADR-008.

# Requirements
- `fees.fee_schedules` (version, definition JSON, effective_from, created_by, approved_by).
- Definition: payer (`Factor | Seller | Split(sellerPct)`), basis `Discount`, share (decimal, MVP 0.15), minimumFee (optional), maximumFee (optional), vatTreatment (`Standard20 | Exempt | TBD`), currency.
- `IFeeCalculator.Calculate(faceValue, purchasePrice, scheduleVersion) → FeeQuote { discount, perPayer: [{payer, net, vat}], scheduleVersion }`. Computes `discount = faceValue − purchasePrice` and `fee = discount × share`, then applies the optional min/max and never exceeds the discount. Pure and deterministic, rounding per ADR-008 (round each payer's fee to minor units; VAT rounded separately).
- `IFeeScheduleQuery.Current(at)`.
- Admin: create a new schedule version (Finance) + approval by a second person (four-eyes) → becomes effective at `effective_from` (≥ now). Versions are immutable after creation.
- Seed version 1: Factor payer, share 15% of the discount, no min, no max, vatTreatment `TBD` (VAT computed as 0 with a `vatPending=true` flag until LQ-43).

# Business Rules
- Fee = discount × share; ≥ min if set; ≤ max if set; **never more than the discount**; face value and purchase price must have the same currency, and `0 < purchasePrice < faceValue` (INV-F1).
- The fee is fixed by the offer's purchase price; later events (recourse, default, late collection) never change it.
- A schedule referenced by any offer can never be modified.
- Implementation notes (2026-10-23): every version is immutable once created (triggers block updates except the one-time approval, and deletes), which covers "referenced by an offer"; the seed is created and approved by the system; Finance staff are a user claim granted with `POST /dev/me/platform-roles` until the staff realm (task 007); the MVP accepts only the Discount basis in RSD; `IFeeCalculator.Calculate` takes the schedule itself (callers load it through `IFeeScheduleQuery`), so it stays pure; the total fee is rounded once and a split gives the seller its rounded part and the factor the rest.

# Technical Requirements
No DB access in the calculator (unit-testable). Golden tests reproduce financial-model.md §2.

# API Changes
Admin `GET/POST /admin/fee-schedules`, `POST /admin/fee-schedules/{version}/approve` 🔐. Internal contract for 013.

# Database Changes
`fees.fee_schedules`.

# Security Considerations
Four-eyes; audit with the full definition.

# Tests
Golden examples (RSD equivalents of the 10k/100k/1M EUR examples in financial-model.md §2), min-fee path, min fee larger than the discount (capped at the discount), max cap, split payer rounding (sum of parts = total), currency mismatch throws, purchase price ≥ face value throws.

# Acceptance Criteria
- [x] Calculator reproduces documented examples exactly.
- [x] Schedules immutable and four-eyes approved.

# Out of Scope
Charges/statements (018), VAT determination (LQ-43).
