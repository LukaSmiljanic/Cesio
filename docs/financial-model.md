# Financial Model

All figures are **illustrative**. The fee share (15%) is decided (ADR-007; permissibility pending LQ-03, working assumption 2026-10-22); average discount, day-count convention and VAT treatment are assumptions (marked ⚠).
The MVP currency is **RSD**. Examples use EUR because they were requested that way.

## 1. Definitions

| Symbol | Name | Definition |
|--------|------|-----------|
| `F` | Face value | Gross amount of the assigned receivable (invoice gross amount incl. VAT − credit notes − partial payments). MVP: the full receivable only |
| `P` | Purchase price | Amount the factor pays the seller (stated directly in the offer) |
| `t` | Tenor (days) | Calendar days from the expected funding date to maturity |
| `D` | Discount = the factor's offered earnings | `F − P`: the difference between the invoice value and the price the factor offered (interest-like discount plus any fees, as offered) |
| `s` | Platform fee share | Share of `D` the platform charges: **15%** (BQ-01, decided by the product owner 2026-10-22) |
| `PF` | Platform fee | `D × s`, rounded to 2 decimals, half away from zero (ADR-008). Optional minimum and maximum (default: none). Never more than `D` |
| `cost_seller` | Implied annual cost to seller | `(D / P) × 365 / t` (shown in the UI as an *indicative* figure, LQ-04) |
| `y_factor` | Factor's indicative annualised yield, net of platform fee | `((D − PF) / P) × 365 / t`, assuming full collection |

**Fee basis (ADR-007, 2026-10-22):** the platform takes a percentage of the factor's earnings on the deal, i.e. of the discount `D = F − P` stated in the accepted offer. It is not a percentage of the invoice value. `D` is fixed when the offer is made, so the fee is known before the factor submits it and is snapshotted on the offer. The fee is **not** based on what the factor later actually collects; recourse, default or late payment do not change it (BQ-05).

Why offers state `P` directly instead of a rate: the price is unambiguous, it compares cleanly, and the platform does not have to impose a day-count convention on factors. The platform **derives** rates for display only.

Future pricing models (not in the MVP): advance + reserve (e.g. 90% upfront, rest at collection minus fees), and an explicit discount rate + processing fee. With these models, the definition of `D` for the fee must be revisited.

## 2. Per-deal examples (factor-paid fee, 15% of the discount, no minimum, non-recourse, full collection)

| | €10,000 invoice | €100,000 invoice | €1,000,000 invoice |
|---|---|---|---|
| Tenor `t` | 45 days | 90 days | 60 days |
| Purchase price `P` | €9,850.00 | €97,000.00 | €985,000.00 |
| Discount `D` (factor's offered earnings) | €150.00 (1.50%) | €3,000.00 (3.00%) | €15,000.00 (1.50%) |
| Platform fee `PF` (15% of `D`) | €22.50 | €450.00 | €2,250.00 |
| VAT on fee if taxable ⚠ (20%) | €4.50 | €90.00 | €450.00 |
| **Seller receives** | **€9,850.00** | **€97,000.00** | **€985,000.00** |
| Seller implied annual cost | 12.35% | 12.54% | 9.26% |
| Factor collects at maturity | €10,000.00 | €100,000.00 | €1,000,000.00 |
| Factor net margin (before VAT, funding cost, losses) | €127.50 | €2,550.00 | €12,750.00 |
| Factor indicative yield (net of fee) | 10.50% | 10.66% | 7.87% |
| Platform fee as a share of the face value | 0.225% | 0.45% | 0.225% |

Calculation check for €100,000: `D = 100,000 − 97,000 = 3,000`; `cost = 3,000/97,000 × 365/90 = 12.54%`; `PF = 3,000 × 0.15 = 450`; `y = 2,550/97,000 × 365/90 = 10.66%`.

**Observations (business risk, BQ-01):**
- The factor always keeps the same share (85%) of its offered earnings. That is easier to explain to factors than a percentage of the invoice value, and it scales automatically with tenor and risk.
- **Incentive conflict:** the more competitive the offer (higher `P` for the seller), the smaller `D` and the lower the platform's revenue. The platform earns most when sellers get the worst price. The offer comparison must stay neutral (sorted by the seller's net amount), and this needs to be explainable to sellers and possibly to the lawyer (LQ-02, LQ-03).
- Revenue per deal is small on short, low-discount deals (€22.50 on the €10,000 example). An optional minimum fee can protect the platform, but it must never exceed `D` and should be validated with factors.
- The fee engine must support **share, optional minimum and maximum, and payer split** from day one.

### Same €100,000 deal, seller-paid fee
Seller receives `97,000 − 450 = €96,550` → implied cost `3,450/96,550 × 365/90 = 14.49%`. The factor keeps €3,000.

## 3. Platform revenue at monthly GMV (GMV = sum of `F` of funded deals)

Monthly revenue = `GMV × average discount % × fee share`. ⚠ The average discount across deals is assumed to be **2.0%** of `F`.

| Monthly GMV | 10% share | 15% share (base) | 20% share | 25% share |
|-------------|-----------|-----------|------------------|-----------|
| *Effective take rate on GMV* | *0.20%* | *0.30%* | *0.40%* | *0.50%* |
| €1M | €2,000 | €3,000 | €4,000 | €5,000 |
| €5M | €10,000 | €15,000 | €20,000 | €25,000 |
| €10M | €20,000 | €30,000 | €40,000 | €50,000 |
| €50M | €100,000 | €150,000 | €200,000 | €250,000 |

Annualised at base (15% share, 2.0% average discount): €36k / €180k / €360k / €1.8M.

Sensitivity: revenue moves one-to-one with the average discount. At a 1.5% average discount the base take rate is 0.225%; at 3.0% it is 0.45%. Lower market rates or stronger competition between factors reduce revenue even at the same GMV.

Deal-count intuition at the €100k average ticket: €10M GMV/month ≈ 100 funded deals/month ≈ 5 per business day. That is well within the capacity of a single modular monolith ([architecture.md](architecture.md)).

Revenue excludes VAT (if any) and is before credit losses on unpaid fees, payment-processing costs and refunds.

## 4. Configuration model (implemented by the Fees module)

```
FeeSchedule (versioned, effective_from, immutable from creation except the one-time four-eyes approval)
  payer:            Factor | Seller | Split(sellerPct)
  basis:            Discount            -- D = faceValue − purchasePrice of the offer
  share:            decimal             -- MVP: 0.15
  minimumFee:       Money?              -- default none; the fee is never more than D
  maximumFee:       Money?
  vatTreatment:     Standard20 | Exempt | TBD  ⚠ until LQ-43
  currency:         RSD
```
The applicable schedule and the computed fee are **snapshotted onto the offer at creation** (the factor sees its fee before offering) and copied to the deal at acceptance. Later schedule changes never re-price existing offers or deals.

## 5. Rounding & currency rules
- Amounts: `decimal` / `decimal(19,4)` internally, rounded to the currency's minor unit (RSD: 2) at defined points: fee calculation and display. Never floats.
- Fee rounding (task 017a): the total fee is rounded once, half away from zero, to minor units. With a split payer, the seller's part is rounded and the factor's part is the total minus it, so the parts always add up to the total. VAT is rounded separately per payer; with `vatTreatment = TBD` it is 0 and the quote carries `vatPending` until LQ-43.
- Every amount carries a currency. Cross-currency arithmetic throws.
- The day count for display metrics is actual/365 (display only, ⚠).

## 6. Items requiring confirmation
- Whether a fee calculated as a share of the factor's earnings is permitted, and whether it makes the platform look like a participant in the factoring business (LQ-03, LQ-02; working assumption 2026-10-22: permitted, pending the lawyer).
- VAT on the fee (LQ-43), tax point (LQ-44), revenue recognition (LQ-45).
- Whether showing "implied annual cost" creates advisory obligations (LQ-04).
- EUR-indexed invoices (LQ-50).
