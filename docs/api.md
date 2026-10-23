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
| Auth/me | `GET /auth/antiforgery`, `POST /auth/register`, `POST /auth/confirm-email`, `POST /auth/login`, `POST /auth/mfa/enrol/start`, `POST /auth/mfa/enrol/confirm`, `POST /auth/mfa/verify`, `POST /auth/mfa/provisioning` 🔐, `POST /auth/step-up`, `POST /auth/password/forgot`, `POST /auth/password/reset`, `POST /auth/logout`, `POST /me/step-up-check` 🔐 (sample step-up action, task 004), `GET /me`, `GET /me/organizations`, `GET/DELETE /me/sessions/{id}`, `POST /auth/mfa-reset-requests`, `GET /confirmations/{token}` (public, rate-limited; official-email confirmations, task 007a) |
| Organizations | `POST /organizations` (seller Draft claim; the PIB is reserved only by a valid SEF key, ADR-020), `GET/POST /organizations/{id}/pristupnica`, `GET /organizations/{id}`, `PATCH /organizations/{id}` (draft only), `POST /organizations/{id}/submit`, `GET /organizations/{id}/members`, `POST /organizations/{id}/members` 🔐 (factor organizations; seller organizations have one account), `PATCH/DELETE /organizations/{id}/members/{memberId}` 🔐, `GET/POST /organizations/{id}/bank-accounts` 🔐, `GET/PUT /organizations/{id}/sef-connection` 🔐 (PUT; the key is validated against SEF and never returned), `GET /organizations/{id}/kyb`, `POST /organizations/{id}/kyb/beneficial-owners`, `GET/POST /organizations/{id}/terms-acceptances`, `GET/POST /organizations/{id}/legal-representatives`, `PUT/DELETE /organizations/{id}/legal-representatives/{representativeId}` (draft only), `POST /invitations/accept` (the invited email only), `GET /terms/current`; development only: `POST /dev/factor-organizations` (until staff create factors, task 008). |
| Factor authorization | `POST /organizations/{id}/factor-authorizations`, `GET /organizations/{id}/factor-authorizations` |
| Documents | `POST /documents` (multipart; returns id + scan status), `GET /documents/{id}` (metadata), `GET /documents/{id}/content` (authorized stream/redirect) |
| Invoices & receivables (seller) | `POST /receivables/import-from-sef` ♻, `GET /receivables`, `GET /receivables/{id}`, `PUT /receivables/{id}/declaration`, `POST /receivables/{id}/documents`, `POST /receivables/{id}/submit` ♻, `POST /receivables/{id}/cancel` ♻ |
| Listings (seller) | `POST /receivables/{id}/listings` ♻, `GET /listings/{id}`, `POST /listings/{id}/withdraw` ♻, `GET /listings/{id}/offers` |
| Marketplace (factor) | `GET /marketplace/listings` (filters: amount, tenor, debtor PIB, recourse preference), `GET /marketplace/listings/{id}`, `GET /marketplace/listings/{id}/documents/{docId}/content` |
| Offers | `POST /marketplace/listings/{id}/offers` ♻ (factor), `GET /offers` (factor's own), `GET /offers/{id}`, `POST /offers/{id}/withdraw` ♻ (factor), `POST /offers/{id}/reject` ♻ (seller), `POST /offers/{id}/accept` ♻🔐 (seller Signatory) |
| Deals | `GET /deals`, `GET /deals/{id}`, `POST /deals/{id}/cdd-decision` ♻🔐 (factor Signatory), `POST /deals/{id}/contract` ♻, `POST /deals/{id}/terms-confirmation` ♻🔐, `POST /deals/{id}/signed-contract` ♻🔐, `POST /deals/{id}/cef-registration` ♻, `POST /deals/{id}/debtor-notification` ♻, `POST /deals/{id}/funding-report` ♻🔐, `POST /deals/{id}/funding-confirmation` ♻🔐, `POST /deals/{id}/outcome` ♻🔐, `POST /deals/{id}/cancel` ♻🔐, `GET /deals/{id}/kyb-package` (factor), `GET /deals/{id}/timeline` |
| Disputes | `POST /deals/{id}/disputes` ♻, `GET /disputes/{id}`, `POST /disputes/{id}/comments` |
| Fees | `GET /fees/charges`, `GET /fees/statements/{period}` |
| Notifications | `GET /notifications`, `POST /notifications/{id}/read`, `GET/PUT /notification-preferences` |
| Admin (`/api/v1/admin`, staff realm) | KYB queue and decisions 🔐, factor authorization verification 🔐 (two approvals), receivable review queue, holds, organizations suspend/reinstate 🔐, factor organization create + invite 🔐, official email record 🔐, pristupnica decision 🔐, MFA reset execute 🔐, disputes log, business parameters 🔐, fee schedules 🔐, audit search/export, integration health |

## 3. Key endpoint specifications

### 3.1 `POST /receivables/import-from-sef` ♻
- **Auth:** seller org, any member. The org is claimed (Draft allowed, ADR-020) and has a `Connected` SEF connection.
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

### 3.5 `POST /marketplace/listings/{id}/offers` ♻
- **Auth:** factor, any member.
- **Request:** `{ "purchasePrice": {"amount":"97000.00","currency":"RSD"}, "recourseType": "NonRecourse", "fundingDeadlineBusinessDays": 2, "validUntil": "2026-10-10T15:00:00Z", "conditions": "Subject to standard agreement v3" }`
- **Validation:** listing Open and visible; INV-F1; currency = listing currency; validUntil ≤ listing closesAt + max acceptance window; no Active offer by this factor (409 `offer_exists`, client must withdraw first); conditions ≤ 1000 chars.
- **Response 201:** the Offer, incl. `platformFee` and `feeScheduleVersion` snapshot.
- **Audit:** `Offer.Submitted`. Notification to the seller.

### 3.6 `POST /offers/{id}/accept` ♻🔐
- **Auth:** seller org, **Signatory**, step-up MFA, `If-Match` on the listing version.
- **Request:** `{ "acknowledgements": { "netProceedsShown": true, "termsVersion": "2026-10" } }`
- **Behaviour (single transaction, row locks on listing + receivable):** re-validate offer Active & unexpired; factor still authorized; no holds; fresh SEF status ≤ 1 h (re-poll before opening the transaction); INV-R5. Then offer → Accepted, the others → Superseded, listing → Closed, receivable → Reserved, Deal created (`AwaitingFactorCdd`, signature deadline computed), outbox events, audit.
- **Response 201:** `{ "dealId": "...", "reference": "DL-2026-000123" }`
- **Errors:** 409 `offer_not_active`, 409 `listing_not_open`, 409 `receivable_ineligible` (SEF changed), 412 `concurrency_conflict`, 423 `hold_active`, 401 `step_up_required`, 503 `upstream_unavailable`.
- **Audit:** `Offer.Accepted`, `Offer.Superseded` ×n, `Listing.Closed`, `Receivable.Reserved`, `Deal.Created`.

### 3.7 `POST /deals/{id}/signed-contract` ♻🔐
- **Auth:** either party's Signatory (the second upload completes the step).
- **Request:** `{ "documentId": "..." }` (PDF with PAdES signatures, scanned clean).
- **Validation:** deal `AwaitingSignatures`; before signatureDeadline; the document's *signed content* corresponds to the agreed contract hash (the original byte range is preserved under incremental signatures); every signature is cryptographically valid, the certificate is qualified and not revoked at signing time, and the signer maps to an APR legal representative or verified attorney of the respective org; SEF fresh ≤ 1 h; no holds.
- **Response 200:** the deal with `signatures[]` validation details. When both parties are valid → `Signed`.
- **Errors:** 422 `signature_invalid` (with details), 422 `signer_not_authorized`, 422 `document_mismatch`, 409 `deadline_passed`.
- **Audit:** `Deal.SignatureValidated` per signature, `Deal.Signed`, `Receivable.Assigned`.

### 3.8 `POST /deals/{id}/funding-report` ♻🔐
- **Auth:** factor Signatory.
- **Request:** `{ "amount": Money, "valueDate": "2026-10-14", "paymentReference": "…", "payeeBankAccountId": "…" }`
- **Validation:** deal `Signed`; the payee account belongs to the seller, is Verified and out of cool-down (INV-F2); amount = purchasePrice (otherwise accepted, but an automatic `FundingDiscrepancy` dispute is opened); valueDate not in the future beyond 1 business day.
- **Response 200**, deal → `FundingReported`. **Audit:** `Deal.FundingReported`.

### 3.9 `POST /deals/{id}/funding-confirmation` ♻🔐
- **Auth:** seller Signatory.
- **Request:** `{ "received": true }` or `{ "received": false, "reason": "…" }`.
- **Behaviour:** true → deal `Funded`, receivable `Funded`, FeeCharge created (via the outbox, idempotent). false → Dispute (`FundingDiscrepancy`) + Hold on the deal.
- **Audit:** `Deal.Funded` / `Dispute.Opened`.

### 3.10 `POST /deals/{id}/outcome` ♻🔐
- **Auth:** factor Signatory. **Request:** `{ "type": "Settled|RecourseExercised|Defaulted", "date": "...", "amountCollected": Money? }`. Deal must be `Funded`. → `Closed`; the receivable gets the matching terminal state. **Audit:** `Deal.Closed`.

## 4. Webhooks / events (future)
Factor API subscribers: `listing.opened` (matching appetite), `offer.accepted`, `deal.state_changed`. Signed (HMAC), retried, idempotent by event ID.
