# Domain Model

Ubiquitous language: English in code; Serbian legal terms in parentheses where useful.
State machines are defined in [product-spec.md](product-spec.md) §7. This document defines entities, ownership, invariants and the transition guards.

## 1. Module ownership of aggregates

| Module | Aggregates (roots in **bold**) |
|--------|-------------------------------|
| Identity | **User**, Session, MfaCredential |
| Organizations | **Organization** (Membership, BankAccount, LegalRepresentative, Pristupnica), TermsAcceptance, ConfirmationToken |
| Compliance | **KybCase** (BeneficialOwner, ScreeningResult, Document refs), **FactorAuthorization**, **Hold** |
| Receivables | **Debtor**, **Invoice** (InvoiceLine snapshot, SefStatusSnapshot), **Receivable** (SellerDeclaration, SupportingDocument refs, VerificationCheck) |
| Marketplace | **Listing** (Audience), **Offer** |
| Deals | **Deal** (ContractDocument, Signature, CefRegistration, DebtorNotification, FundingReport, FundingConfirmation, Outcome), **Dispute** |
| Fees | **FeeSchedule**, **FeeCharge**, FeeStatement |
| Documents | **StoredDocument** (shared technical service: blob metadata, hash, scan status) |
| Notifications | **Notification**, NotificationPreference |
| Audit | **AuditEvent** (append-only) |

Cross-module references are by **ID only**. There are no navigation properties across modules ([architecture.md](architecture.md) §3).

## 2. Invoice vs. Receivable — why they are separate

| Invoice | Receivable |
|---------|-----------|
| A tax document issued in SEF; owned by the e-invoicing process | A monetary claim against the debtor, which can be sold |
| Immutable evidence (UBL snapshot + hash) | Has a lifecycle (verify, list, assign, fund, settle) |
| Can be corrected by credit notes (separate SEF documents) | Its amount = invoice gross − credit notes − partial payments − retained amounts |
| One per SEF document | MVP: exactly **one receivable per invoice, full amount**. Future: several receivables per invoice (partial assignment, instalments with different maturities) or one receivable over several invoices (bundles) |

Keeping them separate lets partial assignment, instalments and bundles arrive later **without** changing the invoice model. It also gives the anti-double-financing invariant one place to live: the *receivable*.

## 3. Entities (key attributes)

**User** — id, email (unique, normalised), password hash, MFA credentials (TOTP/WebAuthn), status, lastLoginAt. A natural person. JMBG is not stored unless KYB needs it (LQ-30).

**Organization** — id, type (`Seller | Factor | Both`; MVP: one role per organization, see BQ-07), legalName, PIB (9 digits) and MB (8 digits), both unique among claimed organizations (Draft claims may share a PIB, ADR-020), claimedAt (set when a valid SEF key proves the PIB; for factors, at invitation), officialEmail (from APR), legalForm, registeredAddress, aprStatus, onboardingStatus (`Draft → Submitted → UnderReview → Approved | Rejected | Suspended`), riskRating.
- **Membership** — userId, orgId, roles set (MVP: `Owner` + optional `Signatory`; Admin/Operator/Viewer post-MVP, ADR-020), status. **Signatory** requires a linked LegalRepresentative or a verified power of attorney document; a seller's single account gets it when its pristupnica is accepted. Seller organizations have one member in the MVP (`orgs.maxSellerMembers`).
- **LegalRepresentative** — name, role (*zastupnik*), source (APR snapshot / document), validFrom/To, linked userId (optional).
- **BankAccount** — accountNumber (18-digit Serbian format, check digits validated), bankName, currency, verificationStatus, verification evidence (NBS account register snapshot, mandatory; bank letter optional supplement), usableFrom. Only `Verified` accounts can appear in funding.

**KybCase** — orgId, version, status (`Open → InReview → Approved | Rejected | NeedsInfo`), reviewer(s), decisionReason, nextReviewAt.
- **BeneficialOwner** — name, DOB, nationality, ownershipPct / controlType, source (APR CESV snapshot), PEP flag.
- **ScreeningResult** — subject (org/person), lists checked + versions, result (`Clear | PotentialMatch | ConfirmedMatch`), resolvedBy.

**FactorAuthorization** — orgId, kind (`Bank | FactoringCompany`), issuingAuthority (`NationalBankOfSerbia | MinistryOfFinance | SecuritiesCommission`), decisionNumber, decisionDate, evidenceDocumentId (all three required for `FactoringCompany`, null for `Bank`), officialSourceUrl, sourceSnapshotDocumentId (PDF/HTML capture), verifiedBy, verifiedAt, verificationMethod (`OfficialRegisterLookup | AuthorityConfirmationLetter`), status (`Pending | Verified | Rejected | Expired | Revoked`), reverifyBy (+90 days factoring company, +30 days bank).

**Hold** — subjectType (`Organization | Receivable | Deal`), subjectId, kind (`ComplianceHold | Disputed | FraudInvestigation`), reason, placedBy, placedAt, liftedBy, liftedAt.

**Debtor** — id, PIB, MB, legalName, status snapshot (active / in liquidation / blocked account, if a source is available), sanctionsScreening ref. Shared reference data; **not** a tenant.

**Invoice** — id, sellerOrgId, debtorId, sefSalesInvoiceId (SEF identifier), invoiceNumber, issueDate, dueDate, currency, netAmount, vatAmount, grossAmount, ublDocumentId + sha256, sefStatus (last known), sefStatusCheckedAt, importedAt. Lines are stored as a JSON snapshot (they are not queried).

**Receivable** — id, invoiceId, sellerOrgId, debtorId, faceValue (Money), maturityDate, state, hold (derived from Hold), version (concurrency), fingerprint (see INV-R3).
- **SellerDeclaration** — declarationTextVersion, answers, acceptedByUserId, acceptedAt, ip. Immutable.
- **VerificationCheck** — code (e.g. `SEF_APPROVED`, `DUPLICATE_FINGERPRINT`), result (`Pass | Fail | Warn`), details, ranAt. Append-only.

**Listing** — id, receivableId, sellerOrgId, state, opensAt, closesAt, audience (`AllVerifiedFactors | Selected(orgIds)`), excludedFactorIds, reservePrice (Money, optional, hidden from factors), version.

**Offer** — id, listingId, factorOrgId, purchasePrice (Money), recourseType (`Recourse | NonRecourse`), fundingDeadlineBusinessDays, validUntil, conditionsText (≤ 1000 chars), feeScheduleSnapshot (version), computedPlatformFee (Money), sellerFee (the seller's part of the fee, 0 when the factor pays), vatPending, state, rejectionReason (set once, at rejection), createdByUserId. **Immutable** except for state and rejectionReason.

**Deal** — id, receivableId, listingId, offerId, sellerOrgId, factorOrgId, purchasePrice, faceValue, recourseType, feeSnapshot, state, signatureDeadline, fundingDeadline, version.
- **ContractDocument** — documentId, sha256, uploadedBy, termsChecklist confirmations (seller & factor).
- **Signature** — party, signerName, certificate subject/issuer/serial, signingTime, validationResult, signer matched to an APR legal representative or verified attorney of the organization (bool + reference).
- **CefRegistration** — status (`NotApplicable | Pending | Registered | Failed`), cefReference, evidenceDocumentId, recordedBy.
- **DebtorNotification** — responsibleParty, channel, sentAt, evidenceDocumentId.
- **FundingReport** — amount, valueDate, paymentReference, payerAccount, payeeBankAccountId, reportedBy. **FundingConfirmation** — confirmed / discrepancy, by, at.
- **Outcome** — type (`Settled | RecourseExercised | Defaulted`), amountCollected, date, reportedBy.

**Dispute** — id, dealId/receivableId, type, raisedBy (org/user), description, status (`Open | Resolved | Withdrawn`), resolution notes, linked Hold.

**FeeSchedule** — version, payer, basis (`Discount` = faceValue − purchasePrice), share, min, max, vatTreatment, effectiveFrom, createdBy, approvedBy/approvedAt (four-eyes). Immutable from creation; only the one-time approval is set. The fee never exceeds the discount.
**FeeCharge** — dealId, payerOrgId, amount (Money), vatAmount, status (`Pending | Due | Invoiced | Paid | Cancelled | CreditNoted`), dueAt, externalInvoiceRef. Corrections create new rows; rows are never edited.

**AuditEvent** — see [audit.md](audit.md).

## 4. Value objects & enums
- `Money(decimal Amount, Currency Currency)`: Amount ≥ 0 unless explicitly signed (adjustments); arithmetic only within the same currency; rounding via `Currency.MinorUnits`. `Currency` = ISO 4217 code, allow-list: `RSD` (MVP), `EUR` (future).
- `Pib` (9 digits + mod-11 check), `MaticniBroj` (8 digits), `SerbianBankAccountNumber` (3+13+2, mod-97 check), `SefInvoiceId`.
- `DateRange`, `BusinessDayCalendar` (Serbian public holidays, configurable table).
- `Fingerprint` (see INV-R3).
- Enums: all states listed above; `RecourseType`; `OrgRole`; `PlatformRole`; `HoldKind`; `DisputeType`; `IssuingAuthority`; `VerificationMethod`.

## 5. Invariants

### Double assignment / double financing
- **INV-R1:** A receivable has **at most one** deal in a non-terminal or successful state (`AwaitingFactorCdd … Funded, Closed`). Enforced by a **filtered unique index** on `deals(receivable_id) WHERE state <> 'Cancelled' AND state <> 'Terminated'`.
- **INV-R2:** A receivable has **at most one** `Open` listing (filtered unique index).
- **INV-R3 (duplicate invoices):** one receivable per SEF invoice ID (unique). One invoice per `(seller_org_id, invoice_number, issue_date)` (unique). Additionally, a **fingerprint** `hash(debtor_pib, gross_amount, currency, issue_date, due_date)` is checked across **all sellers**. A match is a `Warn` check that forces manual review (it catches re-issued invoices under new numbers and collusion between related sellers).
- **INV-R4:** A receivable can be listed or assigned only if the latest SEF status check (≤ 24 h old at listing, ≤ 1 h old at acceptance and at signing) shows buyer `Approved` and not cancelled/storno.
- **INV-R5:** `faceValue` ≤ invoice gross − known credit notes. Re-checked at acceptance.
- **INV-R6:** After a deal reaches `Signed`, the receivable can never become available again unless the deal is `Terminated` with evidence. `Funded` deals can never be terminated.

### Authorization & trust
- **INV-A1:** Only organizations with `onboardingStatus = Approved` and no active Hold can create listings, make offers, accept or sign.
- **INV-A2:** A factor can view listing details or offer only while it has a `Verified`, non-expired FactorAuthorization. If an authorization expires or is revoked, the factor's Active offers expire, and its in-flight deals before `Signed` are frozen (Hold) for compliance review.
- **INV-A3:** Offer acceptance and the other in-app Signatory actions require a member with the **Signatory** flag; contract signatures are validated against the organization's APR legal representatives or verified attorneys (ADR-020; which persons qualify and the power-of-attorney form are pending LQ-61); the acceptance step requires MFA step-up within the last 5 minutes.
- **INV-A4:** A factor can never see a listing whose audience excludes it, and it can never offer on its own organization's receivables or on related parties (same PIB/MB, or flagged shared beneficial owner → manual review).

### Financial consistency
- **INV-F1:** An offer's `purchasePrice` < `faceValue`, and > 0, in the same currency.
- **INV-F2:** Funding can be reported only to a `Verified` bank account of the seller organization. The reported amount must equal `purchasePrice` (seller-paid fee: also equal; the fee is invoiced separately). A different amount → automatic discrepancy dispute.
- **INV-F3:** A FeeCharge is created exactly once per deal, at `Funded` (idempotent on dealId + feeScheduleVersion). Monetary rows are append-only; corrections are compensating rows.
- **INV-F4:** All state transitions that affect money or ownership run in **one DB transaction**, with optimistic concurrency checks on every aggregate touched, and write an AuditEvent in the same transaction.

## 6. Key transition guards (pseudocode)

```
Receivable.Submit(declaration)         requires state=Draft, declaration complete, seller Approved
Receivable.CompleteVerification(checks) requires state=PendingVerification; any Fail → Rejected; any Warn → manual
Listing.Open()                         requires receivable Verified, INV-R2, INV-R4, maturity − today ≥ minDaysAtListing
Offer.Submit()                         requires listing Open, factor authorized (INV-A2), audience ok, INV-F1, no Active offer by same factor
Offer.Accept()   [single transaction]  requires offer Active & valid, listing Open, receivable Listed, INV-R4 (fresh), INV-A3
                                        → offer Accepted; others Superseded; listing Closed; receivable Reserved; Deal created
Deal.ConfirmCdd() / DeclineCdd()       factor Signatory; decline → Cancelled, receivable → Verified
Deal.RecordSignatures()                both valid QES, signers are APR representatives or verified attorneys of each party, hash matches uploaded contract, before signatureDeadline,
                                        INV-R4 fresh → Signed; receivable Assigned
Deal.ReportFunding()                   requires Signed, INV-F2
Deal.ConfirmFunding()                  seller; → Funded; receivable Funded; FeeCharge created (INV-F3)
Deal.RecordOutcome()                   factor; requires Funded → Closed; receivable Settled/RecourseReturned/Defaulted
Any forward transition                 blocked while an applicable Hold is active
```

## 7. Domain events (in-process, published through the outbox)
`OrganizationClaimed`, `OrganizationApproved`, `SefConnectionValidated`, `PristupnicaAccepted`, `MfaResetExecuted`, `FactorAuthorizationVerified/Expired/Revoked`, `ReceivableVerified/Rejected/BecameIneligible`, `ListingOpened/Closed/Expired`, `OfferSubmitted/Withdrawn/Accepted/Superseded`, `DealCreated/Signed/FundingReported/Funded/Closed/Cancelled`, `HoldPlaced/Lifted`, `SefStatusChanged`, `FeeChargeCreated`.
Consumers: Notifications, Fees, Audit enrichment, read models. Handlers must be idempotent.
