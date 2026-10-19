# 017a — Fee Schedules & Fee Preview

(Numbered 017a because it is needed by 013 but belongs to the Fees area.)

**Blocked by:** BQ-01 for the **values** only. Build the engine with the default schedule from financial-model.md.

# Goal
A versioned, immutable fee schedule and a pure calculation service that produces the platform fee for a given face value and payer.

# Context
[financial-model.md](../docs/financial-model.md) §1, §4–5; [business-model.md](../docs/business-model.md) §1; ADR-007, ADR-008.

# Requirements
- `fees.fee_schedules` (version, definition JSON, effective_from, created_by, approved_by).
- Definition: payer (`Factor | Seller | Split(sellerPct)`), tiers (`flat` = the tier containing F applies to the whole amount, or `marginal`), minimumFee, maximumFee (optional), vatTreatment (`Standard20 | Exempt | TBD`), currency.
- `IFeeCalculator.Calculate(faceValue, scheduleVersion) → FeeQuote { perPayer: [{payer, net, vat}], scheduleVersion }`. Pure, deterministic, rounding per ADR-008 (round each payer's fee to minor units; VAT rounded separately).
- `IFeeScheduleQuery.Current(at)`.
- Admin: create a new schedule version (Finance) + approval by a second person (four-eyes) → becomes effective at `effective_from` (≥ now). Versions are immutable after creation.
- Seed version 1: Factor payer, flat 0.50%, min RSD 6,000, no max, vatTreatment `TBD` (VAT computed as 0 with a `vatPending=true` flag until LQ-43).

# Business Rules
- Fee ≥ min; ≤ max if set; currency must equal face value currency.
- A schedule referenced by any offer can never be modified.

# Technical Requirements
No DB access in the calculator (unit-testable). Golden tests reproduce financial-model.md §2.

# API Changes
Admin `GET/POST /admin/fee-schedules`, `POST /admin/fee-schedules/{version}/approve` 🔐. Internal contract for 013.

# Database Changes
`fees.fee_schedules`.

# Security Considerations
Four-eyes; audit with the full definition.

# Tests
Golden examples (RSD equivalents of 10k/100k/1M EUR examples), min-fee path, max cap, marginal vs. flat tiers, split payer rounding (sum of parts = total), currency mismatch throws.

# Acceptance Criteria
- [ ] Calculator reproduces documented examples exactly.
- [ ] Schedules immutable and four-eyes approved.

# Out of Scope
Charges/statements (018), VAT determination (LQ-43).
