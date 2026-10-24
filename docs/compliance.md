# Compliance Model

Position (pending LQ-28/29): the platform is probably **not** an AML obligor. **Factors are** (REG-10). The platform runs KYB to protect market integrity and to give factors a reliable starting package. **Each factor stays responsible for its own CDD**. The deal workflow enforces this with a "factor CDD completed" gate.

## 1. KYB for every organization (MVP)

| Step | Data / evidence | Source | Check |
|------|-----------------|--------|-------|
| Legal entity identity | PIB, MB, legal name, form, address, status, activity code, founding date | APR business register (public search; snapshot PDF stored) | Active, not in liquidation/bankruptcy; PIB/MB checksums |
| Legal representatives | Names, roles, scope of authority | APR register snapshot | The pristupnica signer (and every Signatory) maps to a representative or a verified power of attorney (who qualifies and the power-of-attorney form are pending LQ-61) |
| Representative signature | Pristupnica signed with a QES, or a scanned hand-signed copy confirmed through the official email in APR (ADR-020; level per LQ-32) | Pristupnica upload | Signer = APR representative or attorney; QES valid and not revoked (MVP: checked manually; automated validation of deal contracts arrives with task 016 and ADR-011) |
| Beneficial owners | Natural persons with ≥ 25% ownership/control (AML Law definition) | APR Central Register of Beneficial Owners (snapshot) + seller declaration | Declaration = register; discrepancies → NeedsInfo |
| Sanctions & PEP | Org, representatives, BOs | Lists: UN consolidated, Serbian designated persons (APML), EU, OFAC (pending LQ-31). PEP: self-declaration in the MVP; a provider later | Name screening with fuzzy matching; potential matches resolved manually with notes |
| Bank accounts | Account number in the org's name | NBS account register lookup snapshot (mandatory); a bank letter is only a supplement | Owner = org PIB |
| Terms & consents | Terms of Use version, privacy notice, KYB data-sharing consent | Platform | Versioned acceptance with user, timestamp, IP |
| Risk rating | Low / Medium / High (rules: org age < 1 year, high-risk jurisdiction BOs, PEP, complex structure, adverse media optional) | Computed + officer override (MVP: set by the officer with a reason, task 007) | High → four-eyes approval, review every 12 months (Medium 24, Low 36) |

Outcome: `KybCase` approved/rejected with a reason. An organization cannot transact until `Approved` (INV-A1).

## 2. Seller-specific
- SEF connection proves the org controls the SEF account for that PIB (a key that works returns invoices with supplier PIB = org PIB).
- **A working SEF key proves access, not authority.** Accountants (*knjigovođe*) often hold SEF access for many clients, so a person with a valid key may still have no right to represent the company. The SEF key is never sufficient on its own; the pristupnica, the NBS account and the APR notice close the gap. The seller account itself is shared by design and the company is liable for its use (ADR-020; enforceability pending LQ-26).
- **Seller onboarding rule (ADR-020).** A seller organization is `Approved` only when all of these hold:
  1. **Claimed by SEF key:** the SEF key returns invoices whose supplier PIB equals the organization's PIB. Unproven Draft claims block nobody and expire after 7 days. Two different valid SEF users for one PIB → compliance resolves it through the APR legal representative.
  2. **Pristupnica accepted:** signed by a legal representative listed in APR, or by an attorney with a power of attorney signed by one. MVP: a QES-signed PDF, or a scanned hand-signed copy confirmed through the official email in APR. The signer's name matches the APR representative or the attorney. Signature level pending LQ-32; which identifiers each Serbian QES issuer puts in a certificate is not yet verified.
  3. **Payout account verified:** listed under the organization's PIB/MB in the NBS account register; a snapshot is stored.
  Any failed check → `NeedsInfo` and manual review. A QES is **not** required to open the account; it is required when the factoring agreement is signed (ADR-018, LQ-24).
- **Out-of-band notice:** on approval, a notice goes to the official email recorded in APR ("your company was registered on Cesio"); fallback: a letter to the registered seat. A representative thus learns of a registration made without their knowledge.
- Seller declarations on each receivable (REG-08): delivery performed, no prohibition on assignment, no set-off/counterclaim, not previously assigned or pledged, information accurate. These are versioned texts accepted by the user, with evidential value per LQ-24.

## 3. Debtor checks (per receivable)
- Debtor exists and is active (APR snapshot; manual in the MVP, cached for 30 days).
- Sanctions screening of the debtor entity.
- Optional: NBS "accounts in enforced collection" (*blokade*) lookup. Shown to factors as information; it does not decide eligibility in the MVP (BQ-08).
- Related-party check: debtor's representatives/BOs overlap with the seller's → manual review.

## 4. Factor authorization verification (beyond `isLicensed`)
1. The factor submits its kind. A factoring company also submits the issuing authority, Fa. number / decision number and date, the approval document (PDF) and the official register URL. A bank submits nothing more: no decision exists for banks.
2. The Compliance Officer verifies against the **official source** and stores a snapshot (PDF/HTML print) with timestamp and source URL, and records the method (`OfficialRegisterLookup | AuthorityConfirmationLetter`):
   - **Factoring company:** the **APR Registar faktoringa** (S9). The approval is recorded there under Art. 10 of the Law on Factoring. Check in the [public search](https://pretraga2.apr.gov.rs/FaktoringWebSearch) (select the "Matični broj" option first) or in the [list of factoring companies](https://pretraga2.apr.gov.rs/FactoringList). The Fa. number, MB and decision number must match what the factor submitted, and the status must be "Евидентирано одобрење". Optionally the factor attaches a recent APR certificate (*potvrda*) from the register. The PDF of the approval decision alone is **not** sufficient evidence, because an approval can expire (Art. 8) or be revoked (Art. 9).
   - **Bank:** the [NBS list of banks](https://webappcenter.nbs.rs/WebApp/FinInstBank/LegalEntityFinInstitution/Index) (S10), matched by MB and name. Banks need no separate factoring approval (Law on Factoring Art. 2), so there is no decision to attach. Foreign banks are not on this list and are out of MVP scope (LQ-11).
   - **Not found in either source:** the organization cannot be verified, except through the Commission-confirmation path below. Typical reasons: it is a bank (use the NBS check); it does not do "factoring" as defined by the law (e.g. buying overdue debt, collection, brokering); it is exempt (a state export-finance company, Art. 36(3)); its approval has ended; a new Securities Commission approval is not yet recorded in APR (then require the Commission's decision and confirm it with the Commission, `AuthorityConfirmationLetter`); or it operates without approval. Open questions: LQ-09.
3. A second officer approves (four-eyes) → `Verified`, `reverifyBy` = +90 days for a factoring company, +30 days for a bank (`factorAuthorization.reverifyDays` per kind).
4. Scheduled job: a re-check task is created 14 days (factoring company) or 7 days (bank) before `reverifyBy`. If not done by the deadline → `Expired` → INV-A2 consequences.
5. Revocation (news, authority notice, a bank removed from the NBS list, a company removed from the APR register) → `Revoked` immediately, Hold on in-flight deals, notifications.
6. Factor users with the Signatory flag must be legal representatives or attorneys of the factor (same as sellers).

## 5. Transaction monitoring (MVP: rules + manual review; no automated reporting)
Alerts into a compliance queue:
- Seller volume > X× its 3-month average; first deal above threshold.
- Concentration: a single debtor > Y% of a seller's listed volume; many sellers selling on the same new debtor.
- Fingerprint duplicates; SEF status changes after listing (cancel/storno/credit note).
- Same bank account across different organizations.
- Rapid bank account change before funding.
- Funding discrepancy, or seller confirmation not received after N days.
- Deals cancelled after acceptance repeatedly by the same party.

**Suspicious activity:** the platform is (probably) not an obligor, so it cannot file STRs to the APML in that capacity. Actions: place a Hold, document internally, inform the affected factor (subject to tipping-off guidance), and escalate to legal. All of this depends on LQ-33.

## 6. Auditability & retention
- Every KYB decision, screening result, authorization verification and hold is an audit event with evidence references ([audit.md](audit.md)).
- Retention: until LQ-47 is answered, **keep everything** (default planning assumption: ≥ 10 years after the end of the relationship, per the AML timeframe for obligors).
- Personal data minimisation: store only what KYB needs; masked views for support; a DPIA before launch (LQ-37).

## 7. MVP vs. future automation
| Area | MVP | Future |
|------|-----|--------|
| APR data | Manual entry + snapshot upload by compliance | Automated APR data service (contract) |
| BO register | Manual lookup + snapshot | Automated |
| Sanctions | Locally imported lists (UN, APML, EU, OFAC) refreshed daily, fuzzy match | Screening provider with ongoing monitoring |
| PEP | Self-declaration + officer judgement | Provider PEP database |
| Identity | Pristupnica (QES, or scanned signature confirmed via the APR email), manually checked | Automated QES validation (task 016, ADR-011), eID.gov.rs login (LQ-52) |
| Factor authorization | Manual check of the APR factoring register / NBS bank list + periodic re-check | Automated monitoring of the APR list and the NBS list |
| Monitoring | Rule-based alerts, manual review | Scoring, case management, CEF data |
| Factor CDD package | Downloadable KYB bundle (PDFs + JSON) shared with consent | API push to the factor's onboarding system |
