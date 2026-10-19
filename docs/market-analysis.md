# Market Analysis — Seller Onboarding on Comparable Platforms

> Research 2026-10-19 from public web pages only. Not verified with the companies. Basis for [ADR-020](decisions.md).

## 1. Platforms looked at

| Platform | Country | Type | Who buys invoices |
|----------|---------|------|-------------------|
| [SEF LINK](https://seflink.rs/faktoring/) | Serbia | Marketplace app; sends a request to several factors | Partner factors (named on its site: Instant Factoring, Finspot, Peterhof, Digitalni faktoring) |
| [PlatiMe](https://platime.hr/naplata-potrazivanja/) | Croatia | Marketplace for trading e-invoices | Any registered company or person in Croatia (with a warning that regular buying needs a factoring licence) |
| [FINA Faktoring Platforma](https://www.fina.hr/digitalizacija-poslovanja/faktoring-platforma/odgovori-na-vasa-najcesca-pitanja) | Croatia | State agency platform; does not buy | Factoring companies and banks |
| [Instant Factoring](https://instantfactoring.com/rs/kako-funkcionise) | Serbia | Factor (buys itself) | — |
| [Finspot](https://finspot.com/en/factoring/) | Serbia, Croatia | Factor (buys itself) | — |

## 2. What a seller has to do

| | PlatiMe | FINA | SEF LINK | Instant Factoring | Finspot |
|---|---|---|---|---|---|
| Sign-up data | Company name, OIB, email, phone, IBAN, address ([terms](https://platime.hr/wp-content/uploads/2026/05/Opci-uvjeti-poslovanja_plati-me_10.6.2025.pdf) Art. 2) | Pristupnica + FINA certificate on a USB token | Not public | Contact + company data, ~3 min | Company name + VAT/ID number |
| Identity check | None described; the operator "activates" the account (Art. 3); the user guarantees everyone using the account is authorized (Art. 5) | Certificate (ID card at issuance) | Not public | Not public | Automated checks against public registers |
| Invoice source | Only e-invoices sent through its e-invoice partner | Only e-invoices via FINA e-Račun | Probably SEF | Upload | Upload |
| Debtor confirmation | **Mandatory**: the debtor confirms the e-invoice and thereby guarantees delivery and no dispute (Art. 10) | — | Not public | — | — |
| Contract | Electronic signature, same day (Art. 15); type not stated; paper fallback | Framework agreement with the factor, then per-invoice specification | Not public | Signed with an **SMS code** | **Framework agreement once**, then a limit |
| Money | Buyer pays the seller's account directly (Art. 15) | — | Factor pays within 24 h | Within 24 h | Within 24 h |

## 3. How the lightest model stays safe (PlatiMe)
1. **Invoice channel:** invoices must come through the e-invoice system, so they cannot be invented.
2. **Debtor confirmation:** the strongest protection.
3. **Contractual liability:** the user warrants data accuracy and the authority of everyone using the account.
4. **Money to the company's own account.**

## 4. Mapping to Cesio

| PlatiMe | Cesio equivalent |
|---------|------------------|
| E-invoice channel | SEF key: only invoices the PIB really issued |
| Debtor confirmation | SEF buyer status "Approved" (legal effect: LQ-23) |
| Warranties in the terms | Terms of Use + seller declarations (LQ-26, LQ-40) |
| IBAN typed by the user | Payout account verified in the NBS register (stronger) |
| Anyone may buy | Only licensed factors, who run their own CDD (extra layer) |

## 5. Open points
- How SEF LINK verifies sellers, signs contracts and charges fees: ask a partner factor or sign up as a user.
- PlatiMe price lists (two PDFs on its [pricing page](https://platime.hr/cjenik/)) not yet read.
