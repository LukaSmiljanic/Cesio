# Business Model

> Tax and legal items are marked. No VAT or licensing conclusion in this document is final (see LQ-03, LQ-43, LQ-44).

## 1. Revenue options considered

| Option | Pros | Cons | MVP? |
|--------|------|------|------|
| **Success fee paid by the factor, as a share of the factor's earnings** (the discount `F − P` it offered) | Free for sellers, which drives supply (supply is the hard side). The factor pays only when it wins, and always keeps the same share of its earnings. Scales with tenor and risk | The platform earns less when factors compete harder for the seller (incentive conflict, see [financial-model.md](financial-model.md) §2). Small revenue on short, low-discount deals | **Yes (default, 2026-10-22)** |
| Success fee as % of face value | Simple; revenue does not depend on price competition | Takes a varying share of the factor's margin (17–33% at 0.50% in the earlier model) | Replaced by the discount share |
| Success fee paid by the seller | Sellers already accept a discount; a fee is easy to show in the "net proceeds" | Adds friction to seller acquisition. The comparison UI must show net amounts | Configurable, off by default |
| Split fee (both) | Balances incentives | More complex to explain | Configurable |
| Fixed fee per deal | Predictable | Regressive for small invoices | Possible optional **minimum fee** (default none; never more than the discount) |
| Listing fee (seller) | Revenue even without a deal | Penalises unfunded sellers and kills supply | No |
| Factor subscription (access, seats, analytics) | Recurring revenue | Hard sell before liquidity exists | Post-MVP |
| Premium features (priority placement, analytics, appetite alerts) | Upsell | Placement must not distort fairness for sellers | Post-MVP |
| API access for factors / ERP integrations for sellers | Enterprise revenue | Needs a stable API and security review | Post-MVP |
| Enterprise / reverse-factoring programs (large buyers) | Large volumes | Different flow; needs legal review (LQ-15) | Future |

**Decision (ADR-007, product owner 2026-10-22, Accepted; permissibility: LQ-03, working assumption 2026-10-22, legal confirmation pending):** the MVP charges a **factor-paid success fee** = `(faceValue − purchasePrice) × share`, i.e. a percentage of the factor's earnings on the deal as offered. Optional minimum and maximum; the fee is never more than the discount. Seller-paid and split fees are supported by the fee engine but disabled. The share is configuration, not code.

Share: **15%** of the discount, no minimum, no cap (BQ-01, decided by the product owner 2026-10-22).

## 2. Who pays whom, when (MVP)

The platform **never** receives or forwards deal money (ADR-004). It invoices its own fee separately. A fee charge is due `fees.paymentTermsDays` after the end of the month the deal was funded in (working default 15, not legally settled: the invoicing and tax point depend on LQ-44).

```
Seller ──(assigns receivable, signed agreement)──────────────▶ Factor
Factor ──(purchase price, bank transfer to seller's account)──▶ Seller
Debtor ──(invoice amount at maturity, after notification)─────▶ Factor
Factor ──(platform fee, against platform's SEF invoice)───────▶ Platform
```

### Worked example: invoice of €100,000 (amounts shown in EUR for readability; the MVP currency is RSD)

Assumptions: gross face value €100,000 (incl. VAT), 90 days to maturity, non-recourse, purchase price €97,000 (discount €3,000), platform fee 15% of the discount paid by the factor.

| # | Moment | Event | Money movement | Receivable owner | Platform revenue |
|---|--------|-------|----------------|------------------|------------------|
| 1 | T0 | Seller lists | none | Seller | — |
| 2 | T0+2d | Seller accepts the factor's offer of €97,000 | none | Seller (reserved) | — |
| 3 | T0+4d | Both sign the factoring agreement (QES) | none | **Factor** (subject to LQ-25: ownership may pass at signing or at payment of the price) | — |
| 4 | T0+5d | Factor pays the seller | Factor → Seller **€97,000** | Factor | — |
| 5 | T0+5d | Seller confirms receipt → deal `Funded` | none | Factor | **Fee earned: €450** (revenue-recognition moment to be confirmed, LQ-45) |
| 6 | T0+6d | Platform issues a SEF invoice to the factor | invoice €450 (+ VAT €90 if taxable, LQ-43) | Factor | Receivable on our books |
| 7 | T0+21d | Factor pays the fee | Factor → Platform €450 (or €540) | Factor | Cash collected |
| 8 | T0+90d | Debtor pays at maturity | Debtor → Factor **€100,000** | Extinguished | — |

Outcomes: the seller receives **€97,000** (cost €3,000). The factor's gross spread is €3,000, net of the platform fee €2,550 (the VAT cost is extra if the fee is taxable and the factor cannot deduct it). Platform revenue: **€450**.

If the **seller** pays the fee instead, the seller nets €96,550 and the factor keeps €3,000. The offer-comparison UI must always show the **seller's net proceeds**.

### Recourse case
If the debtor defaults under a recourse agreement, the factor claims from the seller (REG-09). The platform fee is **not refunded** by default (business decision BQ-05). The platform only records the outcome.

### Cancellation before funding
No fee is charged if the deal is cancelled or terminated before `Funded`.

## 3. VAT on the platform fee — `LEGAL QUESTION — REQUIRES PROFESSIONAL REVIEW` (LQ-43, tax advisor)
- Base assumption for modelling: **20% VAT applies** (the conservative case for the payer).
- Possible exemption: VAT Law Art. 25 exempts certain financial services *including intermediation* in credit transactions. Whether a marketplace success fee for factoring falls under it is **unconfirmed** (LQ-43).
- Impact: factoring companies' own factoring income may be partly exempt, which limits their input-VAT deduction. So a 20% VAT on our fee may be a real 20% cost increase for them. This matters for pricing.
- The platform, as a VAT payer, must issue B2B invoices **via SEF** (LQ-44).

## 4. Unit economics drivers (to validate)
- Average ticket size (the MVP minimum amount filters out tiny invoices).
- Conversion rate from listing to funded deal.
- Repeat rate per seller (factoring is recurring).
- Cost to acquire and verify each organization: KYB is manual in the MVP, so this is a cost.
- Factor concentration: if 1–2 factors win most deals, sellers lose the competition benefit and factors gain pricing power over the platform.

## 5. Open business questions (owner: founders/product)
| ID | Question | Default until decided |
|----|----------|-----------------------|
| BQ-01 | Fee share of the factor's discount, optional minimum/maximum, and payer | **Decided (product owner 2026-10-22):** 15% of the discount, factor-paid, no minimum, no cap |
| BQ-02 | Is RSD-only acceptable for launch, given the share of EUR-indexed domestic invoices? | RSD only |
| BQ-03 | Minimum and maximum receivable size | min RSD 500,000, max RSD 200,000,000 |
| BQ-04 | Offer transparency: sealed offers, or show factors their rank / best price? | Sealed |
| BQ-05 | Is the platform fee refunded on recourse, default or termination after funding? | No refund after funding |
| BQ-06 | Default listing audience and debtor disclosure (full identity to all verified factors vs. masked teaser + NDA) | Full identity to the selected audience; seller can restrict |
| BQ-07 | Can one organization be both seller and factor (e.g. a bank's own receivables)? | No, one role per organization |
| BQ-08 | Should a debtor's blocked account (NBS enforced collection) block eligibility? | Information only |
| BQ-09 | Who provides the agreement: each factor's template or a platform standard? | Factor's template (ADR-018) |
| BQ-10 | Launch partners: which factors pilot, and is there exclusivity or a fee holiday? | — |
| BQ-11 | Listing mechanics: fixed window then choose, or accept any time? | Accept any time before `closesAt` |
| BQ-12 | What if the seller never confirms funding? Auto-confirm after N days, or always manual? | Never automatic; operations follow-up |
| BQ-13 | Factors: several users per organization from day one, or one shared account like sellers (ADR-020)? Ask the pilot factors | Several users (Owner + Signatory flag) |

## 6. Questions for an accountant or lawyer
LQ-03 (fee permissibility), LQ-43 (VAT), LQ-44 (SEF invoicing and tax point), LQ-45 (revenue recognition, credit notes), LQ-46 (foreign parties), LQ-69 (platform reporting to the Tax Administration).
