# Product Specification

Working name: **Cesio** (from Latin *cessio*, assignment of a claim). Referred to as "the platform" in these docs.

## 1. Vision
A trusted digital marketplace where Serbian businesses turn verified e-invoices into cash within days. Licensed factors compete transparently for those receivables. Every step is verifiable, auditable, and protected against double financing.

The platform is **technology only**: it verifies, matches, documents and records. It does not buy receivables, lend, or move deal money ([regulatory.md](regulatory.md) §4).

## 2. Target market
- **Sellers:** Serbian SMEs and mid-caps (d.o.o., a.d., entrepreneurs) issuing B2B invoices via SEF to creditworthy Serbian business debtors, with payment terms of roughly 30–180 days.
- **Factors:** banks and factoring companies with a valid approval (REG-01/02).
- **Geography:** Serbia only in the MVP. Currency: **RSD** in the MVP (EUR is a future feature; see [decisions.md](decisions.md) ADR-009).

## 3. Actors

| Actor | Description | Key rights |
|-------|-------------|-----------|
| **Seller** (organization) | Owner of receivables (assignor, *ustupilac*) | Import and submit invoices, publish listings, accept offers, sign agreements, confirm receipt of funds |
| **Factor** (organization) | Bank or approved factoring company | Browse eligible listings, request documents, make offers, complete CDD, sign, report funding and outcomes |
| **Organization member** | A natural person acting for an organization | Owner, with an optional Signatory flag (ADR-020, [security.md](security.md) §3). One account per seller organization in the MVP |
| **Platform staff** | Our employees | Compliance Officer, Operations, Finance, Support, Platform Admin; four-eyes rule for approvals |
| **Debtor** (external) | The seller's customer who owes the invoice | **Has no account in the MVP.** Identified by PIB/MB; data comes from SEF and public registers. May later receive notifications |

## 4. Core concepts
- **Invoice:** the source document (SEF e-invoice). It is evidence and is immutable once imported.
- **Receivable:** the assignable monetary claim derived from an invoice (gross amount minus credit notes and partial payments). Ownership and assignment attach to the **receivable**, not to the invoice ([domain-model.md](domain-model.md) §2).
- **Listing:** time-boxed exposure of one receivable to an audience of factors. A receivable can be re-listed after a listing expires or a deal is cancelled.
- **Offer:** a binding, immutable proposal from one factor for one listing.
- **Deal:** the transaction created when a seller accepts an offer. It carries the contract, signatures, CEF registration, debtor notification, funding and outcome.

## 5. MVP scope

**In scope**
1. Organization registration with KYB (APR data, legal representatives, beneficial owners, sanctions/PEP screening, manual compliance approval).
2. Factor authorization verification (bank or approval evidence, admin verification, expiry and re-check).
3. Seller SEF connection (API key) and invoice import by SEF ID; automated validation and eligibility rules; manual review queue.
4. Listings visible to all verified factors or to a seller-selected subset; a seller may exclude specific factors.
5. Offers: purchase price, recourse type, funding deadline, validity; withdraw before acceptance.
6. Offer comparison with derived metrics (discount, implied annual cost) and acceptance by a Signatory.
7. Deal workflow: factor CDD gate → agreement upload (the factor's own template) → **qualified e-signature** by both parties (signed externally; the platform validates signatures) → CEF registration evidence (manual until the CEF is live) → debtor notification evidence → funding report by the factor → receipt confirmation by the seller → outcome reporting.
8. Platform fee calculation, fee statement, and export for invoicing (fee invoices are issued via SEF by our accounting system; integration later).
9. In-app and email notifications.
10. Dashboards for seller, factor and admin; CSV exports.
11. Immutable audit trail; admin back office (verification queues, holds, disputes log).

**Future (post-MVP)**
- CEF API integration (once the register and its API exist); APR automated data feed; integrated remote QES signing.
- EUR and EUR-indexed receivables; partial assignment; bundles/portfolios.
- Reverse factoring (debtor-initiated programs); debtor portal and confirmations.
- Public-sector debtors (CRF status check).
- Factor API access; auction mode with bidding windows; subscriptions and premium analytics.
- Automated transaction monitoring rules; ongoing sanctions re-screening via a provider.
- Payment initiation (IPS QR) — only after LQ-07.

**Explicitly out of scope**
- Platform purchasing receivables or holding funds (ADR-004).
- Consumer debtors (prohibited, REG-03); non-SEF invoices; export/foreign debtors; future receivables.
- Credit scoring presented as advice; collections; secondary trading of assigned receivables.

## 6. User journeys

### 6.1 Seller onboarding (ADR-020)
1. A user registers (email + password + mandatory MFA; TOTP so the account can be shared) and enters the company's **PIB** → a private Draft claim that blocks nobody and expires after 7 days.
2. The user connects **SEF** with an API key (stored in Key Vault). The platform checks that the key returns invoices with supplier PIB = the claimed PIB → the organization now owns the PIB, and the user sees the importable invoices and their total value. The platform only reads through the key, but SEF keys have no read-only scope (LQ-51).
3. Company data and beneficial owners are prefilled from APR (by compliance in the MVP; see [integrations.md](integrations.md)). The user enters the payout account; compliance verifies it in the NBS account register (an automated picker from the register is a later feature).
4. A legal representative signs the **pristupnica**: company data, beneficial owners, Terms, authorization of the account, KYB-sharing and SEF consents. QES-signed PDF, or a scanned hand-signed copy confirmed through the official email in APR (LQ-32).
5. Sanctions/PEP screening and compliance review (four-eyes for high risk) → Approved; a notice goes to the official email in APR. Before approval the seller can connect SEF and import invoices; submitting receivables and publishing listings require Approved.

One account per seller organization in the MVP; the company decides who uses it and is liable for its use (ADR-020; pending LQ-26).

### 6.2 Factor onboarding
Factors join **by invitation** after a framework agreement with the platform (no SEF claim step). Company data, beneficial owners, screening and Terms work as for sellers, plus a **factor authorization** step: type (bank / factoring company); a factoring company also gives the issuing authority, decision number and date, the approval document and a link to the official register entry (banks have no decision). The compliance officer verifies against the official source and records the method and a snapshot. The authorization gets a re-verification date. The factor also defines its users, its signatories, and optionally its appetite filters (amount range, tenor, debtor sector).

Official sources: factoring companies are checked in the APR factoring register, banks in the NBS list of banks ([compliance.md](compliance.md) §4).

**Bank variant (proposal, pending legal review):**
1. **No self-service sign-up.** A framework agreement between the bank and the platform comes first; then the platform sends an **invitation** to the bank's first user.
2. The organization is created by MB and checked against the NBS list of banks. There is no factoring approval document to attach.
3. Bank employees who make offers or sign are usually not the bank's legal representatives, so Signatory status needs an **authorisation or power of attorney** signed by representatives listed in APR.
4. Re-verified every 30 days (`factorAuthorization.reverifyDays` for banks); a bank that disappears from the list → authorization `Revoked`.
5. Expect the bank to run its own vendor due diligence on the platform (security, data protection).

### 6.3 Invoice to listing
1. The seller selects SEF invoices (search by number/date, or pick from the imported list) → **Draft** receivables are created.
2. The seller completes declarations (no prior assignment, no assignment prohibition, no set-off/dispute, goods/services delivered) and attaches supporting documents (contract, delivery note) → **Submit**.
3. Automated checks: SEF status = Approved by buyer; not cancelled or storno'd; seller is the issuer; debtor is an active business; not already on the platform (duplicate detection); maturity within the allowed window; amount within limits; debtor not sanctioned; the seller has no compliance hold.
4. Checks pass → **Verified** (with low-risk auto-approval if configured), or go to the manual review queue → Verified / Rejected.
5. The seller publishes a listing: audience, listing duration (≤ maturity − buffer), optional reserve price, and an optional note.

### 6.4 Offer and acceptance
1. Eligible factors see the listing (debtor identity, amounts, dates, SEF status, documents; download is watermarked and logged).
2. A factor submits an offer: purchase price, recourse type, funding deadline (business days after signature), offer validity, and conditions (free text, limited).
3. The seller compares offers (purchase price, discount, implied annual cost, recourse, factor name) and a **Signatory** accepts one (step-up MFA).
4. Acceptance atomically: closes the listing, marks the other offers as Superseded, reserves the receivable, creates the Deal, and notifies everyone.

### 6.5 Deal execution
1. **Factor CDD:** the factor reviews the shared KYB package and confirms "CDD completed – approved" (or declines → deal cancelled, receivable returns to Verified).
2. **Contract:** the factor uploads its factoring agreement (PDF) populated with the deal data. The platform checks that key terms (parties, invoice, amount, price, recourse) match the accepted offer by checklist confirmation from both parties.
3. **Signing:** both parties sign with a QES; the signed PDF is uploaded and the platform validates the signatures and signer authority (the signer must be an APR legal representative or verified attorney of that organization; the account may be shared, ADR-020). → **Signed** = the receivable is **Assigned** (subject to LQ-25).
4. **CEF registration:** the seller records CEF registration evidence (once the CEF is live; until then, "not applicable – CEF not operational" is recorded automatically with a timestamp).
5. **Debtor notification:** the responsible party (per the agreement) records notification evidence.
6. **Funding:** the factor reports payment (amount, value date, payment reference, to the seller's verified account). The seller confirms receipt or opens a discrepancy. If the seller does not respond within N business days, the deal is flagged for operations follow-up (it is never auto-confirmed in the MVP).
7. **Platform fee** becomes due upon funding confirmation; a fee statement is generated.
8. **Outcome:** the factor reports Settled (debtor paid), RecourseExercised, or Defaulted (non-recourse loss), with a date and amount. Overdue reminders are sent.

## 7. Lifecycles and states

### 7.1 Receivable
| State | Meaning | Next |
|-------|---------|------|
| `Draft` | Imported from SEF, editable declarations | Submitted, Cancelled |
| `PendingVerification` | Automated and/or manual checks running | Verified, Rejected |
| `Rejected` | Not eligible (reasons recorded). Terminal | — |
| `Verified` | Eligible, not on the market | Listed, Ineligible, Cancelled |
| `Listed` | Has exactly one open listing | Verified (listing withdrawn/expired), Reserved, Ineligible |
| `Reserved` | Offer accepted; deal in progress, not yet signed | Assigned, Verified (deal cancelled) |
| `Assigned` | Factoring agreement signed; ownership with the factor (see LQ-25) | Funded, Verified (deal terminated, see note) |
| `Funded` | Seller confirmed receipt of the purchase price | Settled, RecourseReturned, Defaulted |
| `Settled` | Factor reports debtor payment. Terminal | — |
| `RecourseReturned` | Factor exercised recourse; claim returned to seller. Terminal | — |
| `Defaulted` | Non-recourse loss reported by the factor. Terminal | — |
| `Ineligible` | Became ineligible after verification (SEF cancel/storno, maturity too close, debtor sanction). Terminal for trading | — |
| `Cancelled` | Seller cancelled before any deal. Terminal | — |

Overlay (not a state): **`Hold`** (`None | ComplianceHold | Disputed | FraudInvestigation`). While a hold is set, every forward transition is blocked except those allowed by the hold policy. This avoids the combinatorial "Disputed" state.

Note: `Assigned → Verified` (termination after signing, before funding) requires both parties' confirmation or a compliance decision with evidence, because ownership may already have passed (LQ-25).

### 7.2 Listing
`Open → Closed (offer accepted) | Withdrawn (by seller, only if no accepted offer) | Expired (deadline) | Suspended (by platform; can resume to Open or end as Withdrawn)`.

### 7.3 Offer
`Active → Accepted | Withdrawn (by factor) | Rejected (by seller) | Expired (validity passed) | Superseded (listing closed, withdrawn, expired or suspended-ended)`.
Offers are **immutable**: to change terms, a factor withdraws and submits a new offer. Each factor has at most one Active offer per listing.

### 7.4 Deal
| State | Meaning |
|-------|---------|
| `AwaitingFactorCdd` | Created on acceptance |
| `AwaitingContract` | CDD approved; waiting for the agreement document |
| `AwaitingSignatures` | Agreement uploaded and terms checklist confirmed by both |
| `Signed` | Both valid QES verified → receivable `Assigned` |
| `FundingReported` | Factor reported payment |
| `Funded` | Seller confirmed receipt → fee due |
| `Closed` | Outcome recorded (Settled / RecourseExercised / Defaulted) |
| `Cancelled` | Ended before signing (factor declined CDD, a party withdrew, signature deadline missed, compliance) |
| `Terminated` | Ended after signing, before funding (needs evidence) |

Sub-records tracked independently: `CefRegistration` (NotApplicable / Pending / Registered / Failed) and `DebtorNotification` (Pending / Sent).
Rules: funding cannot be reported before `Signed`; the deal cannot move to `Closed` before `Funded`. The deal and receivable transitions happen in the **same database transaction**.

### 7.5 Cancellation, rejection, disputes
- **Rejection** (receivable): automated or reviewer reasons from a fixed catalogue plus a comment. The seller may submit a *new* receivable after fixing the cause, unless the reason is fraud.
- **Cancellation** (deal, pre-signing): the party and reason are recorded; the listing does **not** reopen automatically. The receivable returns to `Verified` and the seller may re-list it.
- **Disputes:** the MVP records disputes (type: funding discrepancy, contract mismatch, invoice dispute raised by debtor, fraud suspicion, fee dispute), sets a Hold, notifies the parties and compliance, and tracks resolution notes. **The platform does not adjudicate** commercial disputes between the seller and the factor (LQ-41).

## 8. Notifications (MVP)
Email + in-app. Events: verification result, KYB decision, authorization expiring (factoring company 14/7 days, bank 7/2 days before `reverifyBy`), new matching listing (factor digest, configurable), offer received/withdrawn/expiring, offer accepted/superseded, deal step required (per party), signature deadline approaching, funding reported/confirmed, fee statement issued, outcome overdue, hold placed/lifted, SEF status changed (cancel/storno detected). SMS is used only for security (MFA fallback is not offered; see [security.md](security.md)).

## 9. Dashboards & reporting
- **Seller:** receivables by state, open listings with offer counts, deals needing action, funded volume, average cost.
- **Factor:** new listings matching appetite, active offers, win rate, deals needing action, portfolio by maturity, overdue outcomes, fees owed.
- **Admin:** verification queues (KYB, authorization, receivables), holds and disputes, SEF sync failures, GMV, take rate, funnel conversion, deals stuck by step, fee receivables.
- **Exports:** CSV of deals and fee statements, plus an audit extract per entity (admin).

## 10. Configurable business parameters (admin, versioned, audited)
Minimum/maximum receivable amount; minimum days to maturity at listing (default 15) and at signing (default 7); maximum tenor (pending LQ-10); listing maximum duration; offer maximum validity; signature deadline (default 5 business days); funding deadline maximum; fee schedule; auto-verification thresholds.
