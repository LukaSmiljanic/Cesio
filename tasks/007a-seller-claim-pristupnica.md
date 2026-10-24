# 007a — Seller Claim, Pristupnica, Official-Email Confirmations

**Blocked by:** LQ-32 (signature level for the pristupnica; build the MVP default from ADR-020), LQ-76/LQ-77/LQ-78 and LQ-62/LQ-63 (wording and launch only; mechanics proceed), and partially LQ-51 (wording of the SEF-key consent in the pristupnica; mechanics proceed). LQ-21 (SEF key use) proceeds on the product owner's working assumption of 2026-10-22 (legal-questions.md §5).

# Goal
A seller organization owns its PIB only after a valid SEF key proves it, is authorized by a pristupnica signed by an APR legal representative, and the company is informed through its official email in APR. The same email channel confirms sensitive changes and MFA recovery.

# Context
ADR-020; [compliance.md](../docs/compliance.md) §2; [security.md](../docs/security.md) §1–3; [product-spec.md](../docs/product-spec.md) §6.1; [integrations.md](../docs/integrations.md) §3 (official email).

# Requirements
1. **Claim:** when a seller's SEF key is validated (task 010), the SEF connection request claims the PIB in the same transaction (`SefConnectionValidated` is still published for other consumers): if no claimed organization has that PIB → set `claimed_at`, close the other Draft claims for the same PIB that have no validated SEF key (status `ClaimClosed`) and notify their users "this company is already registered, ask its administrator for access" (no owner identity revealed). If a claimed organization already exists → the connection is refused with `pib_already_claimed` and a compliance alert is created (two different valid SEF users for one PIB).
2. **Draft expiry:** unclaimed Drafts older than `orgs.draftClaimDays` (default 7) are closed by a daily worker job (`DraftExpiryJob`).
3. **Official email:** compliance records the organization's official email from the APR snapshot (`official_email`, source document, recordedBy). Missing email → a flag for the letter-to-seat fallback (manual).
4. **Pristupnica:** `GET /organizations/{id}/pristupnica` generates a PDF from the organization data (company, representatives, beneficial owners, Terms version, account holder, SEF and KYB-sharing consents) and stores its SHA-256. `POST /organizations/{id}/pristupnica` uploads the signed copy: a QES-signed PDF, or a scanned hand-signed copy. A scanned copy needs a confirmation through the official email before compliance can accept it.
5. **Compliance review of the pristupnica:** record signer name, matched APR representative or power-of-attorney document, method (`Qes | ScannedConfirmedByEmail`), and validation notes (QES checked manually in the MVP). Accept → the seller's single membership gets the Signatory flag. Reject with a reason.
6. **Confirmation links** to the official email: signed, single-use tokens (hashed at rest, 72 h expiry, bound to what they confirm; a newer request for the same subject voids the older link) for: scanned pristupnica, payout account add/change (task 009 uses it), SEF key change, MFA reset.
7. **MFA reset without recovery codes** ([security.md](../docs/security.md) §1): user request → confirmation via the official email (seller) or approval by another Owner (factor) → a Compliance Officer executes (four-eyes when only the email approved) → 72 h step-up block → notification to the security recipients.
8. **Approval notice:** on `OrganizationApproved` for a seller, send "your company was registered on Cesio" to the official email.
9. **KYB decision guard** (extends task 007): a seller organization can be Approved only if it is claimed and its pristupnica is accepted.
10. UI: seller onboarding checklist (PIB → SEF → company data → payout account → pristupnica → review), pristupnica download/upload, compliance pristupnica review screen, MFA-reset request page.

# Business Rules
- Only a valid SEF key claims a PIB; typing a PIB reserves nothing.
- An unclaimed Draft is visible only to its creator.
- The pristupnica signer must match an APR representative, or an attorney whose power of attorney a representative signed.
- Implementation notes (2026-10-24, simplest options): the claim runs inside the SEF connection request (task 010), not in an outbox consumer, so the refusal reaches the user at once; claims of one PIB are serialised by an application lock, the filtered unique index is the backstop; a closed Draft that later proves the PIB gets `pib_already_claimed` and the alert, an expired one must start again; the compliance alert is the audit event `Compliance.Alert` (an alert queue comes with task 019); the draft expiry is a daily worker job (`DraftExpiryJob`); the PDF is rendered by a small built-in writer behind `IPristupnicaRenderer` (Helvetica, letters outside WinAnsi without diacritics; no new dependency), its text is the data part only until the lawyer's wording (LQ-32, LQ-51), and the payout account holder is the organization until task 009; GET re-renders while nothing is uploaded and stores a new document only when the content changed; a scan uploaded before the official email is recorded gets its confirmation link when compliance records the email; compliance reads both PDFs through `GET /admin/pristupnice/{id}/content` (access-logged); the confirmation page in the SPA confirms only on a click, so link scanners confirm nothing; a claimed organization's SEF key change is validated, kept as a pending secret and applied by the `SefKeyChange` confirmation (no official email → 409 `official_email_missing`); `PayoutAccountChange` tokens exist for task 009; `mfa_reset_requests` live in `orgs` (the approval needs the organization), Identity only executes the reset (`IMfaReset`: authenticator and recovery codes removed, sessions revoked, step-up blocked for `security.stepUpBlockHoursAfterMfaReset` hours); the MFA reset request takes the user's only organization (several → `organization_required`); the security recipients are the Owners and the official email; claim attempts are limited to 10 per user per hour (a separate per-PIB limit is not built: claims of one PIB are serialised and every refusal is alerted); the API method `Scanned` is stored as `ScannedConfirmedByEmail`; `POST /confirmations/{token}` uses the token (the SPA posts it only after the click); the pristupnica PDF is marked as a draft until the lawyer's wording (the pristupnica holds the data only until LQ-76 is answered; LQ-32 and LQ-51 stay open).

# Technical Requirements
PDF generation behind an interface (no new dependency without justification in the plan). Parameters: `orgs.draftClaimDays`, `confirmations.tokenHours` (72), `security.stepUpBlockHoursAfterMfaReset` (72).

# API Changes
`GET/POST /organizations/{id}/pristupnica`, `GET /organizations/{id}/mfa-reset-requests`, `POST /organizations/{id}/mfa-reset-requests/{requestId}/approve` ♻🔐 (every state-changing endpoint here requires `Idempotency-Key` except `POST /auth/mfa-reset-requests` and `POST /confirmations/{token}`, which are rate limited (the confirmation token is single use)), `POST /auth/mfa-reset-requests`, `POST /confirmations/{token}` `{ purpose }` (public, rate-limited; 410 `confirmation_gone`, 400 for another purpose); `PUT /organizations/{id}/sef-connection` of a claimed organization answers 202 `PendingConfirmation` (409 `official_email_missing`); admin `POST /admin/organizations/{id}/official-email` 🔐, `GET /admin/pristupnice`, `GET /admin/pristupnice/{id}/content`, `POST /admin/pristupnice/{id}/decision` 🔐, `GET /admin/mfa-resets`, `POST /admin/mfa-resets/{id}/execute` 🔐.

# Database Changes
`orgs.organizations.claimed_at`, `official_email` (see database.md); `orgs.pristupnice`; `orgs.confirmation_tokens` (hashed); `orgs.mfa_reset_requests` (in orgs, see the implementation notes); `auth.users.step_up_blocked_until`.

# Security Considerations
Tokens hashed and single-use; the "already registered" message reveals no owner data; rate limits on claim attempts per user (10 per hour; per PIB, claims are serialised and every refusal is alerted). Audit: claim created/won/closed, official email recorded, pristupnica uploaded/accepted/rejected, confirmation sent/used/expired, MFA reset requested/executed.

# Tests
- Race: two Draft claims for one PIB validate SEF keys at the same time → exactly one wins (filtered unique index); the other is refused with `pib_already_claimed` and a compliance alert, because it also holds a valid SEF key (Requirement 1).
- Claim refused when the PIB is already claimed; compliance alert created.
- Draft expiry boundary.
- Token: single use; expired → 410; wrong purpose → 400; a link confirms only what it was sent for (the signed document, the new SEF key), and a newer request voids older links.
- Scanned pristupnica cannot be accepted before email confirmation.
- Signatory flag set only after acceptance; seller approval blocked without claim or accepted pristupnica.
- MFA reset: 72 h step-up block enforced; Support role cannot execute.
- Cross-tenant suite for all new endpoints.

# Acceptance Criteria
- [x] End to end: PIB → SEF claim → pristupnica → approval → notice to the official email.
- [x] Nobody can block a company by typing its PIB.
- [x] Sensitive changes and MFA reset require the official-email confirmation (SEF key and MFA reset now; the payout account uses the same tokens in task 009).
- [x] All audit events present.

# Follow-ups from the phase 4 review (2026-10-24)
Fixed: confirmation is a POST after the click (no Idempotency-Key: the token is single use), a link is bound to what it confirms (the signed document, the hash of the new SEF key) and a newer request voids older links, an expired link is audited once; the official email needs its APR source document, and a change notifies the old address and the Owners and voids open links; the Owners are told when a SEF key change is requested. Left open: a second officer for changing an already recorded official email (four-eyes).

# Out of Scope
Automated QES validation (task 016, ADR-011); automated APR retrieval; multi-user seller organizations; eID login (LQ-52).
