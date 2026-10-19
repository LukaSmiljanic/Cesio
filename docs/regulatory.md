# Regulatory Baseline (Serbia)

> **Status:** research summary as of 2026-10-05, updated 2026-10-17. This is **not legal advice**. Every item marked
> `LEGAL QUESTION — REQUIRES PROFESSIONAL REVIEW` must be closed by a Serbian fintech lawyer before production
> (tracked in [legal-questions.md](legal-questions.md) and [tasks/000-regulatory-validation.md](../tasks/000-regulatory-validation.md)).
>
> Confidence scale: **High** = read in a consolidated legal text or official source; **Medium** = consistent secondary
> sources (legal portals, law-firm commentary); **Low** = inferred or single source.

## 1. Sources consulted

| ID | Source | Notes |
|----|--------|-------|
| S1 | Zakon o faktoringu, "Sl. glasnik RS" 62/2013, 30/2018, **109/2025** — consolidated text, [paragraf.rs](https://www.paragraf.rs/propisi/zakon_o_faktoringu.html) | Main law |
| S2 | Zakon o izmenama i dopunama Zakona o faktoringu, "Sl. glasnik RS" 109/2025 — [paragraf.rs](https://www.paragraf.rs/izmene_i_dopune/041225-zakon-o-izmenama-i-dopunama-zakona-o-faktoringu.html), [neobilten PDF](https://www.neobilten.com/wp-content/uploads/2025/12/Zakon-o-izmenama-i-dopunama-Zakona-o-faktoringu-Sl.gl_.109-25.pdf) | Dec 2025 amendment |
| S3 | Zakon o sprečavanju pranja novca i finansiranja terorizma, "Sl. glasnik RS" 113/2017, 91/2019, 153/2020, 92/2023, 94/2024, 19/2025 — consolidated text (paragraf.rs) | AML/CFT |
| S4 | Ministry of Finance tender for the factoring register information system (bids due 2026-07-20) — [Danas](https://www.danas.rs/vesti/ekonomija/tender-za-registar-faktoringa/) | Shows the CEF **is not live yet** |
| S5 | Sistem elektronskih faktura (SEF): [efaktura.gov.rs](https://www.efaktura.gov.rs/), public API (demo/prod), API-key authentication | E-invoicing |
| S6 | Pravilnik o centralnom registru faktura, "Sl. glasnik RS" 45/2026 (applies from 2026-07-01) — [propisi.net](https://propisi.net/ministarstvo-finansija-pocinje-obavezno-registrovanje-u-crf/) | **Different register** (public-sector invoice payments) |
| S7 | Pravilnik o elektronskom fakturisanju — amendment applicable from 2026-04-01 — [KPMG](https://kpmg.com/rs/sr/analize-i-istrazivanja/poreske-vesti/2026/04/izmenjen-pravilnik-o-elektronskom-fakturisanju.html) | Invoice statuses |
| S9 | APR **Registar faktoringa** (Law on Factoring Art. 10): [public search](https://pretraga2.apr.gov.rs/FaktoringWebSearch) by MB / Fa. number / decision number, [list of factoring companies](https://pretraga2.apr.gov.rs/FactoringList), [certificates](https://www.apr.gov.rs/registri/faktoring/potvrde.2353.html). Checked 2026-10-17: 31 entries, the newest from June 2026 | Official source for non-bank factors |
| S10 | NBS list of banks — [web app](https://webappcenter.nbs.rs/WebApp/FinInstBank/LegalEntityFinInstitution/Index), [nbs.rs](https://www.nbs.rs/sr/finansijske-institucije/banke/spisak-banaka/) | Official source for banks |
| S8 | Commentary: [biznis.rs](https://biznis.rs/novac/banke/menja-se-zakon-o-faktoringu-poostrena-kontrola-naplate-potrazivanja/), [Biljana Trifunović](https://www.biljanatrifunovicifa.com/2025/12/izmene-zakona-o-faktoringu-2025-centralna-evidencija-izvrsna-isprava/) | Secondary |

**Identified but not yet read in full** (must be read during task 000):
- Zakon o elektronskom fakturisanju ("Sl. glasnik RS" 44/2021 and amendments)
- Zakon o elektronskom dokumentu, elektronskoj identifikaciji i uslugama od poverenja u elektronskom poslovanju ("Sl. glasnik RS" 94/2017, 52/2021)
- Zakon o zaštiti podataka o ličnosti ("Sl. glasnik RS" 87/2018)
- Zakon o centralnoj evidenciji stvarnih vlasnika ("Sl. glasnik RS" 41/2018 and amendments)
- Zakon o platnim uslugama ("Sl. glasnik RS" 139/2014 and amendments)
- Zakon o obligacionim odnosima (assignment of claims, *ustupanje potraživanja*)
- Zakon o porezu na dodatu vrednost (Art. 25: exemption for financial services)
- Zakon o računovodstvu (document retention)
- Zakon o tržištu kapitala / Securities Commission rules on factoring approvals (after the 2025 transfer)
- Bylaw on the Central Factoring Register (Minister of Finance; not yet issued as far as found)

## 2. Verified requirements

### REG-01 — Factoring is a regulated financial service; only banks and approved companies may be factors
- **Requirement:** Factoring = purchase of an existing or future, non-matured, short-term monetary receivable from the sale of goods or services. A factor must be a **bank** or a **company (d.o.o. / a.d.) with an approval (*saglasnost*)** to perform factoring. Minimum share capital for a factoring company: **RSD 40,000,000**.
- **Source:** S1 Art. 2, Art. 5–7.
- **Component:** Factors / Compliance (factor onboarding).
- **Implication:** A factor cannot trade on the platform until the platform has verified (a) bank status, or (b) a valid factoring approval. A boolean `isLicensed` is not enough. We need a `FactorAuthorization` record with evidence, source register, verification method, verifier, validity, and periodic re-check (see [compliance.md](compliance.md) §4).
- **Confidence:** High (capital amount: Medium; confirm against the current consolidated Art. 6).
- **Legal confirmation required:** Yes, for whether foreign factors may participate (S1 mentions foreign entities only for international factoring).

### REG-02 — Supervisor of non-bank factors moved from Ministry of Finance to the Securities Commission
- **Requirement:** Amendment 109/2025 transfers approval and supervision of factoring companies to the **Komisija za hartije od vrednosti** (Securities Commission). The provisions apply 6 months after the law entered into force (the law entered into force on the 8th day after publication in Dec 2025, so roughly mid-June 2026). Banks remain under the National Bank of Serbia (NBS).
- **Source:** S2 Art. 1–4, 17, 20, 23.
- **Component:** Compliance (authorization verification sources).
- **Implication:** The verification source for a factor depends on the date and the entity type. Approvals issued by the Ministry of Finance before the transfer must still be recognised. Store `issuingAuthority` as an enum: `MinistryOfFinance | SecuritiesCommission | NationalBankOfSerbia`.
- **Register (update 2026-10-17):** Approvals, their expiry and revocation are recorded in the **APR Registar faktoringa** (S1 Art. 10; S9), which has a public search and a public list. The list still has a column titled "decision of the Ministry" and includes 2026 entries, so it is used for both old and new approvals. Banks are checked in the NBS list (S10).
- **Confidence:** Medium (the exact application date must be confirmed; the APR register's existence and public search: High).
- **Legal confirmation required:** Yes. Is the APR register entry authoritative and sufficient as evidence, and are Securities Commission approvals recorded there without delay (LQ-09)?

### REG-03 — Eligible parties and receivables
- **Requirement:** Assignors and debtors are legal entities and entrepreneurs (extended by 109/2025 to cooperatives, registered agricultural holdings, institutions and others). Receivables arising from **personal or household purposes are excluded**. Receivables must be **short-term and not yet due** (existing or future). Since 109/2025, receivables payable from public budgets are explicitly in scope.
- **Source:** S1 Art. 2, as amended by S2 Art. 1.
- **Component:** Invoices / eligibility rules.
- **Implication:** Eligibility engine rules: debtor must be an identified business entity (PIB/MB); maturity must be in the future; tenor must be ≤ the short-term limit; no consumer debtors.
- **Confidence:** Medium. The exact definition of "short-term" (≤ 1 year?) must be read in Art. 2.
- **Legal confirmation required:** Yes, for the maximum tenor and the treatment of future receivables (MVP excludes them).

### REG-04 — Double assignment is prohibited; later agreements are null
- **Requirement:** Concluding several factoring agreements for the same receivable is prohibited. Subsequent agreements are **null and void (*ništav*)**. Penalties apply.
- **Source:** S1 Art. 22, Art. 34–35.
- **Component:** Receivables, Marketplace, Deals.
- **Implication:** This is a hard invariant: at most one active (non-cancelled) deal per receivable amount, enforced at the database level (see [domain-model.md](domain-model.md) INV-R1, [database.md](database.md)).
- **Confidence:** High.
- **Legal confirmation required:** Partial assignment semantics (two factors, each buying a different part of the same invoice).

### REG-05 — Central Factoring Register (Centralna evidencija faktoringa, "CEF")
- **Requirement:** The Ministry of Finance must establish a central electronic register of **e-invoices subject to factoring**, to prevent multiple assignment. It covers only e-invoices issued under the e-invoicing law, excluding invoices whose payment has already begun (if recorded in the central invoice register). Data recorded: PIB and name of assignor, factor and debtor; agreement number and date; invoice number, issue date and maturity; invoice amounts (net, VAT, gross); invoice status in SEF and in the CEF; assigned amount (full or partial). The **assignor** (or the debtor, in reverse factoring) must register the invoice "immediately after recording in SEF, no later than the next business day". Factor, assignor and debtor access the CEF with a **high-assurance electronic identification scheme**. The CEF connects automatically to SEF. Failure to register is a sanctioned offence. Deadlines: the CEF must be established within **18 months** (≈ June 2027), and the registration obligation applies **30 days after the CEF starts operating**.
- **Source:** S1/S2 new Art. 32a, Art. 34 item 7, transitional Art. 19, 21, 23.
- **Status as of 2026-10-05:** **Not operational.** The procurement tender closed on 2026-07-20 (S4), and no bylaw has been found.
- **Component:** Integrations (CEF adapter), Deals (registration step), Receivables (pre-listing check).
- **Implication:**
  1. Until the CEF exists, the platform's own registry is the **only** structural defence against double financing, and it only covers deals made on the platform. This is a residual risk to disclose to factors.
  2. Design a `CefRegistration` step on the deal (manual evidence in the MVP, API later) so that the switch-over is a configuration change, not a redesign.
  3. Whether the platform may register **on behalf of** users, or must only guide them, is unknown.
- **Confidence:** High (law text); Medium (timeline).
- **Legal confirmation required:** Yes. Exact trigger and timing of the registration obligation; whether CEF registration replaces debtor notification; whether intermediaries can act through the API.

### REG-06 — Debtor notification
- **Requirement:** The assignor must notify the debtor of the assignment. Since 109/2025 this may be done **electronically**, and the agreement may shift the duty to the factor (the electronic-notification paragraph applies from the CEF start date). A debtor who has been notified must pay the factor.
- **Source:** S1 Art. 24–26; S2 Art. 12, 23.
- **Component:** Deals (notification step), Notifications.
- **Implication:** The deal workflow includes a `DebtorNotification` record (who notified, channel, timestamp, evidence). The platform does **not** contact the debtor itself in the MVP unless the agreement and the legal review allow it.
- **Confidence:** Medium.
- **Legal confirmation required:** Yes.

### REG-07 — Factoring agreement form; enforceability
- **Requirement:** The factoring agreement must be in **written or electronic form**. Since 109/2025, a notarised (*solemnizovan*) agreement that contains the debtor's consent to enforcement is an **enforceable document** (*izvršna isprava*). Agreements and documentation count as credible documents (*verodostojne isprave*) in enforcement proceedings. Accessory rights and security instruments transfer automatically (new Art. 26a).
- **Source:** S1 Art. 19, 26a, 32 as amended.
- **Component:** Contracts.
- **Implication:** Electronic agreements are allowed. The signature standard (qualified electronic signature vs. advanced) must be confirmed. Notarisation is out of the MVP scope.
- **Confidence:** Medium-High.
- **Legal confirmation required:** Yes, for the required e-signature level and evidential value.

### REG-08 — Set-off and contractual assignment restrictions
- **Requirement:** During the agreement, the claim cannot be extinguished by set-off between the assignor and the debtor unless contractually allowed (new Art. 21 para 2). Assignment between legal entities is presumed permitted unless contractually restricted (Art. 31 para 2 as amended).
- **Source:** S2 Art. 10, 14.
- **Component:** Invoices (seller declarations).
- **Implication:** At submission the seller must declare that (a) the underlying contract does not prohibit assignment, and (b) there is no set-off agreement or counterclaim. Declarations are stored as versioned, signed statements.
- **Confidence:** Medium.
- **Legal confirmation required:** Yes.

### REG-09 — Recourse and regress
- **Requirement:** In recourse factoring, the assignor guarantees collectibility. Amendment 109/2025 requires a factor exercising regress to return the accompanying security instruments and to notify the debtor **within 8 days**.
- **Source:** S1 Art. 16 as amended by S2 Art. 7.
- **Component:** Offers (recourse type), Deals (post-maturity outcomes).
- **Implication:** `RecourseType` is a mandatory offer term. A `RecourseExercised` outcome must be recordable. Platform reminders are optional.
- **Confidence:** Medium.
- **Legal confirmation required:** No for the data model; Yes for platform obligations (likely none).

### REG-10 — Factoring companies are AML obligors; the platform probably is not
- **Requirement:** The AML Law Art. 4 lists **"faktoring društva"** (item 13) as obligors, as well as banks and payment institutions. Obligors must perform customer due diligence, identify beneficial owners (natural person with **≥ 25%** ownership or control), and **keep CDD data for 10 years** after the business relationship ends. Remote identification via a **qualified electronic certificate** is regulated in the law.
- **Source:** S3 Art. 3–4, CDD chapter, retention provisions.
- **Component:** Compliance.
- **Implication:**
  - Each factor remains legally responsible for its own CDD on the seller (and, depending on its policy, the debtor). The platform supports this by sharing a **KYB package** with the seller's consent. It does **not** replace the factor's CDD.
  - A technology platform is not on the obligor list unless it becomes a payment institution. It must still run strong KYB for trust and fraud reasons.
  - The deal workflow includes a **"factor CDD completed"** gate before contract signing.
- **Confidence:** High (obligor list, 25%, 10 years); Medium (applicability to the platform).
- **Legal confirmation required:** Yes. Is the platform an obligor? Can factors rely on the platform's CDD (third-party reliance rules)?

### REG-11 — SEF e-invoices: statuses, and factored invoices cannot be cancelled
- **Requirement:** B2B invoices between Serbian VAT entities are issued via SEF, which records statuses (sent, approved/rejected by the buyer, cancelled, storno). Since 2026-04-01 the status model includes "recorded, corrected, cancelled" for VAT records. Commentary on the e-invoicing bylaw states that an issuer who has disposed of a non-matured receivable through factoring may not cancel or storno that invoice. SEF exposes a public API authenticated with a company-generated **API key** (demo and production environments).
- **Source:** S5, S7; bylaw commentary (Paragraf).
- **Component:** Integrations (SEF adapter), Invoices (verification).
- **Implication:** The MVP accepts **only SEF invoices**, verifies them via the SEF API, requires buyer status **Approved**, and re-polls the status until settlement to detect cancellation, storno or credit notes.
- **Confidence:** Medium (exact status names and the factoring restriction must be confirmed against the bylaw and the API spec).
- **Legal confirmation required:** Yes. May a third party (the platform) use a seller's SEF API key? SEF terms of use.

### REG-12 — Central Invoice Register (CRF) is a different system
- **Requirement:** From 2026-07-01, invoices to public-sector entities must be registered in the Treasury's **Centralni registar faktura** (S6). This is a payment-monitoring register, **not** the factoring register. The CEF excludes invoices whose payment has started in the CRF.
- **Component:** Invoices (eligibility for public-sector debtors).
- **Implication:** For public-sector debtors, eligibility must check the CRF payment status (future integration). The MVP excludes public-sector debtors.
- **Confidence:** Medium.

## 3. Areas researched but not verified (do NOT implement as requirements yet)

| Area | Current understanding | Status |
|------|----------------------|--------|
| Platform licensing | Nothing found in the Law on Factoring that regulates technology intermediaries | LEGAL QUESTION — REQUIRES PROFESSIONAL REVIEW |
| Payment handling | If the platform receives or holds funds for others, it likely becomes a payment service under the Law on Payment Services, which needs an NBS licence | LEGAL QUESTION — REQUIRES PROFESSIONAL REVIEW. **Design decision: the platform never touches deal funds** (ADR-004) |
| Platform fee legality | No prohibition found | LEGAL QUESTION — REQUIRES PROFESSIONAL REVIEW |
| VAT on the platform fee | The VAT Law exempts certain financial services, including intermediation in credit. Whether a marketplace fee qualifies is unclear | LEGAL QUESTION — tax advisor |
| E-signature level | The e-document law defines qualified signatures. Which level the factoring agreement needs is unclear | LEGAL QUESTION — REQUIRES PROFESSIONAL REVIEW |
| Personal data | ZZPL 87/2018 (GDPR-like). Data transfer to EU Azure regions is likely permitted (adequacy list) | LEGAL QUESTION — confirm |
| Retention | AML 10 years (verified for obligors); accounting/e-invoice retention periods not verified | LEGAL QUESTION — REQUIRES PROFESSIONAL REVIEW |
| Sanctions | Serbia implements UN designations; the APML maintains a designated-persons list. Applicability of EU/OFAC lists depends on factors' banking relationships | LEGAL QUESTION — REQUIRES PROFESSIONAL REVIEW |

## 4. Regulatory principles adopted for design (until legal review says otherwise)

1. The platform **does not purchase** receivables and **does not hold or route deal funds**. The factor pays the seller directly.
2. The platform **does not make credit decisions**. Factors decide, price and perform their own CDD.
3. **Only verified factors** (bank or approved factoring company) can see listings or make offers.
4. **Only SEF e-invoices** with buyer-approved status are eligible in the MVP. This maximises verifiability and future CEF compatibility.
5. **Strict single assignment** is enforced by the database, not just by application logic.
6. Everything that affects money, ownership or compliance is **audited immutably** and kept for at least 10 years (pending confirmation).
7. The design is **CEF-ready**: the registration step exists in the workflow from day one.
