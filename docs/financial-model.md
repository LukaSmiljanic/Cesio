# Financial Model

All figures are **illustrative**. Fee rates, day-count convention and VAT treatment are assumptions (marked ⚠).
The MVP currency is **RSD**. Examples use EUR because they were requested that way. An illustrative rate of 1 EUR ≈ 117.2 RSD is used only for minimum-fee conversions.

## 1. Definitions

| Symbol | Name | Definition |
|--------|------|-----------|
| `F` | Face value | Gross amount of the assigned receivable (invoice gross amount incl. VAT − credit notes − partial payments). MVP: the full receivable only |
| `P` | Purchase price | Amount the factor pays the seller (stated directly in the offer) |
| `t` | Tenor (days) | Calendar days from the expected funding date to maturity |
| `D` | Discount | `F − P` (the factor's total gross charge: interest-like discount plus any fees, as offered) |
| `r_f` | Platform fee rate | ⚠ assumption 0.50% |
| `min` | Minimum platform fee | ⚠ assumption RSD 6,000 (≈ €50) |
| `PF` | Platform fee | `max(min, F × r_f)`, rounded to 2 decimals, half away from zero (ADR-008) |
| `cost_seller` | Implied annual cost to seller | `(D / P) × 365 / t` (shown in the UI as an *indicative* figure, LQ-04) |
| `y_factor` | Factor's indicative annualised yield, net of platform fee | `((F − P − PF) / P) × 365 / t`, assuming full collection |

Why offers state `P` directly instead of a rate: the price is unambiguous, it compares cleanly, and the platform does not have to impose a day-count convention on factors. The platform **derives** rates for display only.

Future pricing models (not in the MVP): advance + reserve (e.g. 90% upfront, rest at collection minus fees), and an explicit discount rate + processing fee.

## 2. Per-deal examples (factor-paid fee, 0.50%, min €50, non-recourse, full collection)

| | €10,000 invoice | €100,000 invoice | €1,000,000 invoice |
|---|---|---|---|
| Tenor `t` | 45 days | 90 days | 60 days |
| Purchase price `P` | €9,850.00 | €97,000.00 | €985,000.00 |
| Discount `D` | €150.00 (1.50%) | €3,000.00 (3.00%) | €15,000.00 (1.50%) |
| Platform fee `PF` | €50.00 (min = 0.50%) | €500.00 | €5,000.00 |
| VAT on fee if taxable ⚠ (20%) | €10.00 | €100.00 | €1,000.00 |
| **Seller receives** | **€9,850.00** | **€97,000.00** | **€985,000.00** |
| Seller implied annual cost | 12.35% | 12.54% | 9.26% |
| Factor collects at maturity | €10,000.00 | €100,000.00 | €1,000,000.00 |
| Factor net margin (before VAT, funding cost, losses) | €100.00 | €2,500.00 | €10,000.00 |
| Factor indicative yield (net of fee) | 8.23% | 10.45% | 6.18% |
| Platform fee as a share of the factor's gross spread | 33.3% | 16.7% | 33.3% |

Calculation check for €100,000: `D = 100,000 − 97,000 = 3,000`; `cost = 3,000/97,000 × 365/90 = 12.54%`; `PF = 100,000 × 0.005 = 500`; `y = 2,500/97,000 × 365/90 = 10.45%`.

**Observation (business risk):** on short or low-spread deals, a flat 0.50% takes a large share of the factor's margin (33% in examples A and C). Options: a lower rate for large tickets (tiers), a cap, or a rate tied to tenor (e.g. `F × annualRate × t/365`). This is a pricing decision (BQ-01) to validate with factors before launch. The fee engine must support **tiers, caps, minimums and payer split** from day one.

### Same €100,000 deal, seller-paid fee
Seller receives `97,000 − 500 = €96,500` → implied cost `3,500/96,500 × 365/90 = 14.71%`. The factor keeps €3,000.

## 3. Platform revenue at monthly GMV (GMV = sum of `F` of funded deals)

Monthly revenue = `GMV × effective take rate`. The minimum fee raises the effective rate when there are many small tickets; that effect is ignored here.

| Monthly GMV | 0.25% | 0.50% (base) | 0.75% | 1.00% |
|-------------|-------|--------------|-------|-------|
| €1M | €2,500 | €5,000 | €7,500 | €10,000 |
| €5M | €12,500 | €25,000 | €37,500 | €50,000 |
| €10M | €25,000 | €50,000 | €75,000 | €100,000 |
| €50M | €125,000 | €250,000 | €375,000 | €500,000 |

Annualised at base 0.50%: €60k / €300k / €600k / €3.0M.

Deal-count intuition at the €100k average ticket: €10M GMV/month ≈ 100 funded deals/month ≈ 5 per business day. That is well within the capacity of a single modular monolith ([architecture.md](architecture.md)).

Revenue excludes VAT (if any) and is before credit losses on unpaid fees, payment-processing costs and refunds.

## 4. Configuration model (implemented by the Fees module)

```
FeeSchedule (versioned, effective_from, immutable once used)
  payer:            Factor | Seller | Split(sellerPct)
  basis:            FaceValue
  tiers:            [{ fromAmount, toAmount?, rate }]   -- marginal or flat (flag)
  minimumFee:       Money
  maximumFee:       Money?
  vatTreatment:     Standard20 | Exempt | TBD  ⚠ until LQ-43
  currency:         RSD
```
The applicable schedule is **snapshotted onto the offer at creation** (the factor sees its fee before offering) and copied to the deal at acceptance. Later schedule changes never re-price existing deals.

## 5. Rounding & currency rules
- Amounts: `decimal`/`numeric(19,4)` internally, rounded to the currency's minor unit (RSD: 2) at defined points: fee calculation and display. Never floats.
- Every amount carries a currency. Cross-currency arithmetic throws.
- The day count for display metrics is actual/365 (display only, ⚠).

## 6. Items requiring confirmation
- VAT on the fee (LQ-43), tax point (LQ-44), revenue recognition (LQ-45).
- Whether showing "implied annual cost" creates advisory obligations (LQ-04).
- EUR-indexed invoices (LQ-50).
