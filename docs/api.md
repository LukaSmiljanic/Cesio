# API Design

REST over JSON, base path `/api/v1`. Used by our SPA; a public factor API comes later with OAuth (see [security.md](security.md) §6).

## 1. Conventions
- **Org context:** header `X-Organization-Id` on all org-scoped endpoints. The server validates the membership and loads roles. Missing or invalid → 403.
- **Auth:** cookie session (BFF) + anti-forgery header on unsafe methods. Endpoints marked 🔐 require **step-up MFA** (≤ 5 min). Sign-in and account-recovery endpoints (`/auth/*`, `/me/sessions`, the no-op sample `/me/step-up-check`) act before or outside an organization context, so they take no `Idempotency-Key`; they are rate limited instead.
- **Idempotency:** endpoints marked ♻, and every unsafe endpoint of the Organizations row, require `Idempotency-Key` (UUID). Keys are scoped by the active organization, or by the user for commands outside one (creating an organization, accepting an invitation). Replay → same response. Same key with a different body → `422 idempotency_key_reuse`.
- **Concurrency:** resources return `ETag` (= version). Commands on existing aggregates accept `If-Match`; a mismatch → `412`. PATCH of an organization or a member requires it (`428 precondition_required` when missing).
- **Money:** `{ "amount": "97000.00", "currency": "RSD" }`. The amount is a **string** decimal (no JSON float), validated against the currency's minor units.
- **Dates:** business dates `YYYY-MM-DD`; instants ISO-8601 UTC.
- **Errors:** RFC 7807 with `errorCode` (stable, snake_case), `traceId`, and `errors` for field validation. Common codes: `validation_failed` 400, `not_found` 404 (also used instead of 403 for resources in other tenants, to avoid leaking existence), `forbidden` 403, `conflict_state` 409 (invalid transition), `concurrency_conflict` 412, `step_up_required` 401, `hold_active` 423, `upstream_unavailable` 503 (SEF etc.).
- **Pagination:** cursor-based `?cursor=&limit=` (max 100). Responses `{ items, nextCursor }`.
- **Audit:** every unsafe endpoint writes at least one audit event ([audit.md](audit.md) §3). Notable reads (listing detail, document download) write to the access log.
- Commands are modelled as **sub-resource actions** (`POST /deals/{id}/funding-report`), which keeps state transitions explicit.

## 2. Resource map

| Area | Endpoints |
|------|-----------|
| Auth/me | `GET /auth/antiforgery`, `POST /auth/register`, `POST /auth/confirm-email`, `POST /auth/login`, `POST /auth/mfa/enrol/start`, `POST /auth/mfa/enrol/confirm`, `POST /auth/mfa/verify`, `POST /auth/mfa/provisioning` 🔐, `POST /auth/step-up`, `POST /auth/password/forgot`, `POST /auth/password/reset`, `POST /auth/logout`, `POST /me/step-up-check` 🔐 (sample step-up action, task 004), `GET /me`, `GET /me/organizations`, `GET/DELETE /me/sessions/{id}`, `POST /auth/mfa-reset-requests` (after the password, before MFA; rate-limited; `{ "organizationId": null }` unless the user belongs to several organizations), `POST /confirmations/{token}` `{ purpose }` (public, rate-limited, no Idempotency-Key because there is no user to scope it by: the token is single use, a repeat is 410; official-email confirmations, task 007a; the email links to the SPA page, which posts only after the user's click; a link is bound to what it confirms and a newer request voids it: 410 `confirmation_gone` when unknown, used or expired, 400 for another purpose) |
| Organizations | `POST /organizations` (seller Draft claim; the PIB is reserved only by a valid SEF key, ADR-020), `GET/POST /organizations/{id}/pristupnica` (GET renders and stores the PDF while none is uploaded; POST `{ documentId, method: Qes|Scanned }`, `Scanned` stored as `ScannedConfirmedByEmail`; a scan is confirmed through the official email), `GET /organizations/{id}/mfa-reset-requests`, `POST /organizations/{id}/mfa-reset-requests/{requestId}/approve` ♻🔐 (another Owner of a factor organization), `GET /organizations/{id}`, `PATCH /organizations/{id}` (draft only), `POST /organizations/{id}/submit`, `GET /organizations/{id}/members`, `POST /organizations/{id}/members` 🔐 (factor organizations; seller organizations have one account), `PATCH/DELETE /organizations/{id}/members/{memberId}` 🔐, `GET/POST /organizations/{id}/bank-accounts` 🔐 (POST ♻🔐: `{ accountNumber, currency: RSD }` → Pending; once the organization ever had a verified account (also a deactivated one) → ConfirmationPending until the official-email confirmation, 409 `official_email_missing`), `POST /organizations/{id}/bank-accounts/{accountId}/deactivate` ♻🔐, `GET/PUT /organizations/{id}/sef-connection` 🔐 (PUT; the key is validated against SEF and never returned; the first valid key claims the PIB, 409 `pib_already_claimed` when another organization owns it; a claimed organization's new key waits for the official-email confirmation, 202 `PendingConfirmation`, 409 `official_email_missing`), `GET/POST /organizations/{id}/factor-authorizations` ♻ (factor organizations; task 008), `GET /organizations/{id}/kyb`, `POST /organizations/{id}/kyb/beneficial-owners` ♻ (replaces the declaration), `POST /organizations/{id}/kyb/sharing-consent` ♻, `GET/POST /organizations/{id}/terms-acceptances`, `GET/POST /organizations/{id}/legal-representatives`, `PUT/DELETE /organizations/{id}/legal-representatives/{representativeId}` (draft only), `POST /invitations/accept` (the invited email only), `GET /terms/current`; development only: `POST /dev/factor-organizations` (development shortcut; staff create factors with `POST /admin/factor-organizations`, task 008), `POST /dev/organizations/{id}/approve` (development shortcut past the KYB decision of task 007), `POST /dev/me/platform-roles` (grants a staff role such as ComplianceOfficer until a staff admin UI exists). |
| Factor authorization | `POST /organizations/{id}/factor-authorizations`, `GET /organizations/{id}/factor-authorizations` |
| Documents | `POST /documents` (multipart; returns id + scan status), `GET /documents/{id}` (metadata), `GET /documents/{id}/content` (authorized stream/redirect) |
| Invoices & receivables (seller) | `POST /receivables/import-from-sef` ♻, `GET /receivables`, `GET /receivables/{id}`, `GET /receivables/declaration` (current text), `GET /receivables/{id}/checks`, `PUT /receivables/{id}/declaration`, `POST /receivables/{id}/documents`, `POST /receivables/{id}/submit` ♻, `POST /receivables/{id}/cancel` ♻ |
| Listings (seller) | `POST /receivables/{id}/listings` ♻, `GET /listings` (the seller's own), `GET /listings/{id}`, `POST /listings/{id}/withdraw` ♻, `GET /listings/{id}/offers` |
| Marketplace (factor) | `GET /marketplace/listings` (filters: amount, tenor, debtor PIB, recourse preference), `GET /marketplace/listings/{id}`, `GET /marketplace/listings/{id}/documents/{docId}/content` (later scope; in the MVP a factor reads a listing's documents through `GET /documents/{id}/content`, granted by the listing visibility rule) |
| Offers | `GET /marketplace/listings/{id}/fee-preview?purchasePrice=` (factor; the fee before submitting), `POST /marketplace/listings/{id}/offers` ♻ (factor), `GET /offers` (factor's own), `GET /offers/{id}`, `POST /offers/{id}/withdraw` ♻ (factor), `POST /offers/{id}/reject` ♻ (seller), `POST /offers/{id}/accept` ♻🔐 (seller Signatory) |
| Deals | `GET /deals`, `GET /deals/{id}` (the deal, the state of its steps and whose turn it is), `GET /deals/{id}/timeline` (the seller and the factor of the deal only), `POST /deals/{id}/cdd-decision` ♻🔐 (factor Signatory), `POST /deals/{id}/contract` ♻, `POST /deals/{id}/terms-confirmation` ♻🔐, `POST /deals/{id}/signed-contract` ♻🔐, `POST /deals/{id}/cef-registration` ♻, `POST /deals/{id}/debtor-notification` ♻, `GET /deals/{id}/funding-accounts` (factor, deal Signed: the seller's usable payout accounts unmasked, access logged), `POST /deals/{id}/funding-report` ♻🔐, `POST /deals/{id}/funding-confirmation` ♻🔐, `POST /deals/{id}/outcome` ♻🔐, `POST /deals/{id}/cancel` ♻🔐, `GET /deals/{id}/kyb-package` (factor) |
| Disputes | `POST /deals/{id}/disputes` ♻, `GET /disputes/{id}`, `POST /disputes/{id}/comments` |
| Fees | `GET /fees/charges` (the payer org's charges and effective reversals), `GET /fees/statements/{period}` (yyyy-mm: lines and totals per currency, reversals subtracted); admin (Finance): `GET /admin/fees/charges?period=&payerOrgId=&status=`, `POST /admin/fees/charges/{id}/invoiced` ♻🔐 (external SEF invoice reference), `POST /admin/fees/charges/{id}/paid` ♻🔐, `POST /admin/fees/charges/{id}/reverse` ♻🔐 (proposes a reversal), `POST /admin/fees/charges/{id}/approve-reversal` ♻🔐 (a second Finance user), `GET /admin/fees/export?period=` (CSV, audited) |
| Notifications | `GET /notifications?before=&limit=` (cursor: `next` is the createdAt to pass as `before`), `GET /notifications/unread-count`, `POST /notifications/{id}/read` ♻, `GET/PUT /notification-preferences` ♻ (`{ language: sr|en, items: [{ type, channel: inapp|email, enabled }] }`; turning off a mandatory type is 422 `notification_mandatory`), `GET/PUT /organizations/{id}/factor-appetite` ♻ (factor organizations: RSD amount and tenor ranges for the hourly digest), admin `GET /admin/notification-failures` (Operations, PlatformAdmin) |
| Admin (`/api/v1/admin`, staff realm) | KYB queue and decisions 🔐 (`GET /admin/kyb-cases`, `GET /admin/kyb-cases/{id}`, `POST /admin/kyb-cases/{id}/screening`, `POST /admin/kyb-cases/{id}/screening/{resultId}/resolution`, `POST /admin/kyb-cases/{id}/risk`, `POST /admin/kyb-cases/{id}/decision` 🔐; ComplianceOfficer), sanctions lists `GET /admin/screening-lists`, `POST /admin/screening-lists/{source}` (CSV upload, idempotent by content), factor authorization verification (`GET /admin/factor-authorizations?status=`, `POST /admin/factor-authorizations/{id}/snapshot` (the officer's print of the official source), `POST /admin/factor-authorizations/{id}/approve` 🔐 (first officer with method and snapshot, then a second, different officer), `/reject` 🔐, `/revoke` 🔐; ComplianceOfficer), receivable review queue, listings suspend/resume (Operations, with reason), holds (`GET /admin/holds`, `POST /admin/holds` 🔐, `POST /admin/holds/{id}/lift` 🔐; a FraudInvestigation lift needs a second officer), organizations suspend/reinstate 🔐, factor organization create + invite `POST /admin/factor-organizations` 🔐 (ComplianceOfficer; `{ pib, legalName, firstUserEmail }`), official email record `POST /admin/organizations/{id}/evidence` (the APR excerpt, stored as the organization's KYB evidence), `POST /admin/organizations/{id}/official-email` 🔐 (`{ email, sourceDocumentId }`, the source is mandatory; a change notifies the old address and the Owners and voids open links), payout accounts `GET /admin/bank-accounts?status=`, `POST /admin/bank-accounts/{id}/evidence` (NBS snapshot or bank letter), `POST /admin/bank-accounts/{id}/verify` 🔐 (`{ evidenceDocumentId, supplementDocumentId, holderMatchesPib }`; 409 `duplicate_account`), `/reject` 🔐, `/clear-duplicate` 🔐, bank codes `GET /admin/bank-codes`, `PUT /admin/bank-codes/{code}` 🔐, pristupnice `GET /admin/pristupnice`, `GET /admin/pristupnice/{id}/content?which=generated|signed`, `POST /admin/pristupnice/{id}/decision` 🔐, MFA resets `GET /admin/mfa-resets`, `POST /admin/mfa-resets/{id}/execute` 🔐 (ComplianceOfficer; two officers when only the official email approved), disputes log, business parameters 🔐, fee schedules 🔐, audit search/export, integration health (`GET /admin/notification-failures`), scheduled jobs `GET /admin/jobs`, `POST /admin/jobs/{name}/run` ♻🔐 (Operations, PlatformAdmin; task 020), deal termination before funding `POST /admin/deals/{id}/terminate` ♻🔐 (Operations or ComplianceOfficer, with evidence and both parties' written confirmations), signature trust store `GET/POST /admin/trusted-certificates`, `POST /admin/trusted-certificates/{id}/deactivate` 🔐 (PlatformAdmin) |

## 3. Key endpoint specifications

### 3.1 `POST /receivables/import-from-sef` ♻
- **Auth:** seller org, any member. The org is claimed by its SEF key (it may still be a Draft, ADR-020) and has a `Connected` SEF connection.
- **Request:** `{ "sefInvoiceIds": ["123456", "..."] }` (max 50) **or** `{ "issueDateFrom": "2026-09-01", "issueDateTo": "2026-09-30" }`.
- **Behaviour:** fetch each invoice from SEF; verify supplier PIB = org PIB; upsert the debtor; store the UBL + hash; create an Invoice and a `Draft` receivable. Duplicates are reported per item, not as a failure of the whole batch.
- **Response 200:** `{ "results": [ { "sefInvoiceId": "...", "status": "Imported|AlreadyImported|NotIssuer|NotFound|Unsupported", "receivableId": "..."? , "reason": "..." } ] }`
- **Errors:** 409 `sef_connection_invalid`, 503 `upstream_unavailable`.
- **Audit:** `Invoice.Imported` + `Receivable.Created` per item.

### 3.2 `POST /receivables/{id}/submit` ♻
- **Auth:** seller, any member, owns the receivable. No hold.
- **Request:** `{}` (the declaration must already be saved; supporting documents scanned clean).
- **Validation:** state `Draft`; declaration version = current; required documents present.
- **Response 202:** `{ "state": "PendingVerification" }`. Checks run synchronously where cheap; SEF re-check may complete asynchronously.
- **Errors:** 409 `conflict_state`, 400 `declaration_incomplete`, 423 `hold_active`.
- **Audit:** `Receivable.Submitted`, then one `Receivable.CheckRecorded` per check, then `Receivable.Verified|Rejected|SentToManualReview`.

### 3.3 `POST /receivables/{id}/listings` ♻
- **Auth:** seller, any member.
- **Request:** `{ "closesAt": "2026-10-12T15:00:00Z", "audience": { "type": "AllVerifiedFactors" | "Selected", "factorOrgIds": [] }, "excludedFactorOrgIds": [], "reservePrice": Money? }`
- **Validation:** receivable `Verified`; INV-R2; INV-R4 (fresh SEF ≤ 24 h, otherwise re-poll); `closesAt` ≤ maturity − minDaysAtListing and ≤ max listing duration; reservePrice < faceValue.
- **Response 201:** the Listing.
- **Errors:** 409 `receivable_not_verified`, 409 `listing_already_open`, 422 `maturity_too_close`, 503 `upstream_unavailable` (fail closed).
- **Audit:** `Listing.Opened`; receivable `Verified→Listed`.

### 3.4 `GET /marketplace/listings` / `{id}`
- **Auth:** factor org, Approved, authorization Verified (INV-A2), any role.
- **Response:** listing summary; detail includes debtor (name, PIB, MB, status snapshot), invoice number/dates/amounts, SEF status + checked-at, seller name and KYB status, document list, closesAt, the factor's own active offer (if any), and the platform fee rule for this factor (share of the discount; the amount is shown as soon as the factor enters a price). **Never**: other offers, reserve price.
- **Errors:** 404 if not visible to this factor.
- **Audit:** access log entry on detail and document download.

### 3.4a `GET /marketplace/listings/{id}/fee-preview?purchasePrice=97000.00`
- **Auth:** a factor organization that sees the listing (§3.4); otherwise 404.
- **Response:** `{ "discount": Money, "factorFee": Money, "platformFee": Money, "vatPending": true, "feeScheduleVersion": 1 }` from the current fee schedule (task 017a); the same amounts are snapshotted when the offer is submitted.
- **Errors:** 400 `validation_failed` (the price is not above zero and below the face value), 404.

### 3.5 `POST /marketplace/listings/{id}/offers` ♻
- **Auth:** factor, any member.
- **Request:** `{ "purchasePrice": {"amount":"97000.00","currency":"RSD"}, "recourseType": "NonRecourse", "fundingDeadlineBusinessDays": 2, "validUntil": "2026-10-10T15:00:00Z", "conditions": "Subject to standard agreement v3" }`
- **Validation:** listing Open and visible; INV-F1; currency = listing currency; validUntil ≤ listing closesAt + max acceptance window; no Active offer by this factor (409 `offer_exists`, client must withdraw first); conditions ≤ 1000 chars.
- **Response 201:** the Offer, incl. `platformFee` and `feeScheduleVersion` snapshot.
- **Audit:** `Offer.Submitted`. Notification to the seller.

### 3.6 `POST /offers/{id}/accept` ♻🔐
- **Auth:** seller org, **Signatory**, step-up MFA, `If-Match` on the listing version (MVP: checked when sent).
- **Request:** `{ "acknowledgements": { "netProceedsShown": true, "termsVersion": "2026-10" } }`
- **Behaviour (single transaction, row locks on listing + receivable):** re-validate offer Active & unexpired; factor still authorized; no holds; fresh SEF status ≤ 1 h (re-poll before opening the transaction); INV-R5. Then offer → Accepted, the others → Superseded, listing → Closed, receivable → Reserved, Deal created (`AwaitingFactorCdd`, signature deadline = end of the day `deals.signatureDeadlineBusinessDays` business days later, product-spec §parameters), outbox events, audit.
- **Response 201:** `{ "dealId": "...", "reference": "DL-2026-000123" }`
- **Errors:** 404 `not_found` (not the seller's offer), 403 `signatory_required`, 409 `offer_not_active`, 409 `listing_not_open`, 409 `receivable_ineligible` (SEF changed), 412 `concurrency_conflict`, 423 `hold_active`, 401 `step_up_required`, 503 `upstream_unavailable`.
- **Audit:** `Offer.Accepted`, `Offer.Superseded` ×n, `Listing.Closed`, `Receivable.Reserved`, `Deal.Created`.

### 3.6a Deal steps before signing (task 015)
- `POST /deals/{id}/cdd-decision` ♻🔐 (factor Signatory): `{ "decision": "Approved" | "Declined", "reason": "…" }` (reason required for Declined, ≤ 400). Approved → `AwaitingContract`; Declined → `Cancelled`, receivable → `Verified`.
- `POST /deals/{id}/contract` ♻ (factor member): `{ "documentId": "…" }`, a clean PDF uploaded with purpose `Contract`; allowed in `AwaitingContract`; a new version voids earlier confirmations. Both parties may read every version.
- `POST /deals/{id}/terms-confirmation` ♻🔐 (each party's Signatory): `{ "contractDocumentId": "…", "confirmed": true }` for the current version; when both parties confirmed it, `agreedContractSha256` is set → `AwaitingSignatures`.
- `POST /deals/{id}/cancel` ♻🔐 (either party's Signatory, before `Signed`): `{ "reasonCode": "letters", "comment": "≤ 1000" }` → `Cancelled`, receivable → `Verified`; the listing does not reopen.
- `GET /deals/{id}/kyb-package` (factor of the deal): needs the seller's KYB-sharing consent (task 007), otherwise 409 `kyb_consent_missing`; the ZIP holds the KYB summary JSON and the receivable's documents.
- **Errors:** 404 for non-parties, 403 `signatory_required`, 409 `conflict_state` (wrong state), 409 `contract_changed`, 409 `already_confirmed`, 422 `wrong_document`, 423 `hold_active`, 401 `step_up_required`.

### 3.7 `POST /deals/{id}/signed-contract` ♻🔐
- **Auth:** either party's Signatory (the second upload completes the step).
- **Request:** `{ "documentId": "..." }` (PDF with PAdES signatures, scanned clean).
- **Validation:** deal `AwaitingSignatures`; before signatureDeadline; the document's *signed content* corresponds to the agreed contract hash (the original byte range is preserved under incremental signatures); every signature is cryptographically valid, the certificate is qualified and not revoked at signing time, and the signer maps to an APR legal representative or verified attorney of the respective org; SEF fresh ≤ 1 h; no holds.
- **Response 200:** the deal with `signatures[]` validation details. When both parties are valid → `Signed`.
- **Errors:** 422 `signature_invalid` (with details), 422 `certificate_not_qualified`, 422 `certificate_untrusted`, 422 `certificate_revoked`, 422 `signer_not_authorized`, 422 `document_mismatch`, 409 `deadline_passed`, 409 `receivable_ineligible` (SEF changed; the deal stays in AwaitingSignatures), 423 `hold_active`, 503 `upstream_unavailable` (validator, revocation source or SEF unreachable; fail closed, retry later). Every validated signature is stored with its result, also when the request fails.
- **Audit:** `Deal.SignatureValidated` per signature, `Deal.Signed`, `Receivable.Assigned`.

### 3.8 `POST /deals/{id}/funding-report` ♻🔐
- **Auth:** factor Signatory.
- **Request:** `{ "amount": Money, "valueDate": "2026-10-14", "paymentReference": "…", "payeeBankAccountId": "…" }`
- **Validation:** deal `Signed`; the payee account belongs to the seller, is Verified and out of cool-down (INV-F2); amount = purchasePrice (otherwise accepted, but an automatic `FundingDiscrepancy` dispute is opened); valueDate not in the future beyond 1 business day.
- **Response 200** `{ "state": "FundingReported", "amountMatches": true }`. **Errors:** 409 `conflict_state`, 422 `account_not_usable`, 409 `cef_not_registered` (only with `cef.blockFundingUntilRegistered`, LQ-17), 423 `hold_active` (an open dispute holds the deal). **Audit:** `Deal.FundingReported` (amount, value date, masked payee account).

### 3.9 `POST /deals/{id}/funding-confirmation` ♻🔐
- **Auth:** seller Signatory.
- **Request:** `{ "received": true }` or `{ "received": false, "reason": "…" }`.
- **Behaviour:** true → deal `Funded`, receivable `Funded`, FeeCharge created (via the outbox, idempotent). false → Dispute (`FundingDiscrepancy`) + Hold on the deal.
- **Audit:** `Deal.Funded` / `Dispute.Opened`.

### 3.10 `POST /deals/{id}/outcome` ♻🔐
- **Auth:** factor Signatory. **Request:** `{ "type": "Settled|RecourseExercised|Defaulted", "date": "...", "amountCollected": Money? }`. Deal must be `Funded`. → `Closed`; the receivable gets the matching terminal state. **Audit:** `Deal.Closed`.

### 3.10a `POST /admin/deals/{id}/terminate` ♻🔐
- **Auth:** staff with the `Operations` or `ComplianceOfficer` role (anyone else gets 404), step-up.
- **Request:** `{ "reason": "…", "evidenceDocumentIds": ["…"], "sellerConfirmationDocumentId": "…", "factorConfirmationDocumentId": "…" }`; every document is clean and belongs to a party, and each party's confirmation is its own document. The MVP asks for both parties' confirmations and the evidence (task 017), the stricter reading of product-spec §7.4.
- **Behaviour:** deal `Signed` or `FundingReported` → `Terminated`; receivable `Assigned` → `Verified` (INV-R6). A `Funded` deal can never be terminated.
- **Errors:** 409 `conflict_state`, 400 `validation_failed`, 422 `evidence_missing`. **Audit:** `Deal.Terminated`, `Deal.TerminatedByStaff` (with the evidence), `Receivable.Verified`.

## 4. Webhooks / events (future)
Factor API subscribers: `listing.opened` (matching appetite), `offer.accepted`, `deal.state_changed`. Signed (HMAC), retried, idempotent by event ID.
