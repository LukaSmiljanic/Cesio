# Decision Log (ADRs)

Status: **Accepted** (build on it) · **Proposed** (default until confirmed; flagged in tasks) · **Open** (must be decided before the dependent task starts).
Changing an Accepted decision requires a new ADR that supersedes it. AI agents must not change decisions silently.

| ID | Title | Status | Depends on |
|----|-------|--------|-----------|
| ADR-001 | Modular monolith (.NET, PostgreSQL, React, Azure) | Accepted | — |
| ADR-002 | ASP.NET Core minimal APIs with endpoint groups per module | Accepted | — |
| ADR-003 | Schema per module, UUIDv7 keys, EF Core per-module DbContext | Accepted | — |
| ADR-004 | Platform never receives, holds or routes deal funds | Accepted (legal confirmation pending) | LQ-06 |
| ADR-005 | Own identity (ASP.NET Core Identity), mandatory MFA, BFF cookie | Accepted | — |
| ADR-006 | Offer acceptance as one cross-module DB transaction | Accepted | — |
| ADR-007 | Factor-paid success fee, configurable tiers/min/cap/payer | Proposed | BQ-01, LQ-03, LQ-43 |
| ADR-008 | Money = decimal + ISO currency; rounding half away from zero at fee calculation | Accepted | — |
| ADR-009 | MVP limited to RSD, SEF e-invoices, full assignment, domestic private-sector debtors | Proposed | BQ-02, BQ-03 |
| ADR-010 | Frontend component library: shadcn/ui + Tailwind + TanStack Table (see [ux-design.md](ux-design.md) §8) | Proposed | before task 004 (UI); confirm after prototype interviews |
| ADR-011 | PAdES/QES validation approach (library vs. QTSP validation service) | Open | before task 016 (until then a Compliance Officer checks QES manually, e.g. the pristupnica in task 007a) |
| ADR-012 | Invoice and Receivable are separate entities | Accepted | — |
| ADR-013 | Offers state a purchase price and are immutable | Accepted | — |
| ADR-014 | Holds are an overlay, not lifecycle states | Accepted | — |
| ADR-015 | Deal includes a factor CDD gate before contract | Accepted | LQ-29 |
| ADR-016 | Azure EU region hosting | Proposed | LQ-35 |
| ADR-017 | Audit: same-transaction writes, hash chain, daily WORM digests | Accepted | — |
| ADR-018 | Contract = factor's own agreement, signed externally with QES, validated by the platform | Proposed | LQ-24, LQ-27 |
| ADR-019 | Production launch waits for an operational CEF; build and test everything before that | Proposed | CEF bylaw/API, LQ-17, LQ-18 |
| ADR-020 | Minimal seller onboarding: one shared account per seller, PIB claimed by a valid SEF key, pristupnica, NBS-verified payout account, APR-email confirmations | Proposed | LQ-21, LQ-26, LQ-32, BQ-13 |

## ADR-001 Modular monolith
**Context:** small team, low transaction volume, high correctness needs. **Decision:** one deployable unit with strict module boundaries enforced by architecture tests. **Consequences:** simple ops and atomic transactions; modules can be extracted later if a real need appears.

## ADR-004 No handling of deal funds
**Context:** handling third-party funds likely requires a payment-services licence (LQ-06) and adds AML exposure. **Decision:** the factor pays the seller directly; the platform records the factor's report and the seller's confirmation. **Consequences:** the platform cannot guarantee settlement. Funding discrepancies are disputes between the parties. The platform's fee is collected by a separate invoice, so there is credit risk on fees.

## ADR-006 Atomic acceptance
**Context:** acceptance changes the offer, listing, receivable and deal; partial failure could double-sell. **Decision:** a single application service in Deals coordinates the write contracts of Marketplace and Receivables on one shared connection/transaction, with row locks and unique partial indexes. **Consequences:** a deliberate, documented exception to "modules communicate via events".

## ADR-008 Money
`decimal` in C#, `numeric(19,4)` in PG, currency always paired (`Money` value object); arithmetic across currencies throws; results are rounded to minor units only at defined points (fee calculation, display). No floats, enforced by a CI check.

## ADR-009 MVP scope limits
**Why:** SEF gives machine-verifiable invoices with buyer acceptance and a future CEF link; RSD avoids FX legal questions (LQ-50); full assignment keeps INV-R1 simple. **Revisit** after legal review and pilot feedback.

## ADR-012 Invoice ≠ Receivable
See [domain-model.md](domain-model.md) §2. Enables partial assignment and bundles later without remodelling invoices.

## ADR-013 Price-based immutable offers
Factors state the purchase price; the platform derives rates for display only. Immutability gives a clean audit and prevents last-second edits; changes are withdraw + resubmit.

## ADR-014 Holds as overlay
Disputes, compliance holds and fraud investigations can occur in any state; modelling them as states would multiply transitions. A Hold blocks forward transitions per a policy table.

## ADR-015 Factor CDD gate
Factors are AML obligors and must do their own CDD. The deal cannot proceed to contract until the factor confirms CDD (or declines → cancel without penalty).

## ADR-017 Audit
See [audit.md](audit.md).

## ADR-018 Contract handling
The MVP does not generate legal contracts: the factor uploads its own agreement populated with the deal terms, both parties confirm a terms checklist (parties, receivable, face value, price, recourse), and sign with a QES. The platform validates the signatures. A platform template is a future option (LQ-27).

## ADR-019 Launch gated on the CEF
**Context:** until the Central Factoring Register (CEF) operates, there is no reliable way to detect an invoice assigned off-platform. SEF status normally stays `Approved` after an off-platform assignment, and the SEF factoring fields (`vatNumberFactoringCompany`, `factoringContractNumber`) are not documented well enough to rely on (LQ-22; see [integrations.md](integrations.md) §1.1). The platform's own registry only protects deals made on the platform (REG-05). **Decision (product owner, 2026-10-13):** develop and test the full platform against the SEF demo and fakes now. **Do not launch production until the CEF is operational** and a CEF lookup (already assigned → block) runs before listing and before signing. **Consequences:**
- The `CefNotOperationalGateway` / `NotApplicable` path is for development, testing and demos only, not for production.
- A task for the CEF integration (lookup + registration) is written once the CEF bylaw and its API are published. Until then, `ICefGateway` stays the integration seam.
- Revisit `cef.blockFundingUntilRegistered` (task 017) with LQ-17 when the CEF rules are known.
- Launch timing depends on the CEF (statutory deadline ≈ June 2027) and can slip with it. A closed pilot before the CEF would need a new ADR and an answer to LQ-20.

## ADR-020 Minimal seller onboarding
**Context (product owner, 2026-10-19):** the MVP should start with as little friction as possible and add complexity later. A market check ([market-analysis.md](market-analysis.md)) shows that a comparable marketplace (PlatiMe, Croatia) asks sellers only for company data. It relies on the e-invoice channel, debtor confirmation, contractual warranties and payment to the company's own account, not on a heavy identity check. On Cesio only licensed factors buy, and they run their own CDD and check who signs the agreement (ADR-015, ADR-018).

**Decision:**
1. **One account per seller organization** in the MVP. The account represents the company, and the company is liable for every action taken through it (Terms of Use, pristupnica; enforceability pending LQ-26). The company decides who uses it (e.g. its accountant). MFA is TOTP so the account can be used on several devices; passkeys are optional. The membership table stays; the business parameter `orgs.maxSellerMembers` (default 1) enforces the limit.
2. **Factor organizations may have several users**, because banks cannot share credentials (BQ-13).
3. **Roles collapse to Owner + a Signatory flag.** Every member is Owner. Signatory requires a link to an APR legal representative or a verified power of attorney. A seller's single account becomes Signatory when its pristupnica is accepted. Admin, Operator and Viewer are post-MVP.
4. **A PIB is claimed by a valid SEF key, not by typing it.** Entering a PIB creates a private Draft claim that blocks nobody and expires after 7 days. The first claim whose SEF key returns invoices with supplier PIB = the claimed PIB wins; other drafts for that PIB are closed with "already registered, ask its administrator". Two different valid SEF users for one PIB → compliance resolves it through the APR legal representative.
5. **Pristupnica:** one document signed by an APR legal representative (or an attorney): it confirms company data and beneficial owners, accepts the Terms, authorizes the account, and consents to KYB sharing and to the platform's use of the SEF key. The MVP accepts a QES-signed PDF, or a scanned hand-signed copy confirmed through the company's official email recorded in APR. The required signature level is LQ-32.
6. **The payout account must be listed under the organization's PIB/MB in the NBS account register** (snapshot mandatory; a bank letter is only a supplement).
7. **Sensitive changes go through the company, not the account:** changing the payout account or the SEF key, and MFA recovery without recovery codes, need confirmation via the official email recorded in APR (for a factor organization with several users, MFA recovery can instead be approved by another Owner) and notify the security recipients. A **72 h** cooling period applies to a changed payout account (no funding) and after an MFA reset (no step-up actions).
8. **A QES is not an onboarding step.** It is needed when the factoring agreement is signed by a person authorized for the seller (level per LQ-24, ADR-018).

**Consequences:** audit identifies the account, not the person (IP and device are logged). Multi-user seller accounts, more roles and eID login are later additions. **Assumption to verify:** the APR record of every company contains an email address for official correspondence; if it is missing, fall back to a letter to the registered seat.
