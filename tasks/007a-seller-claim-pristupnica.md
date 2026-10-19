# 007a — Seller Claim, Pristupnica, Official-Email Confirmations

**Blocked by:** LQ-32 (signature level for the pristupnica; build the MVP default from ADR-020) and LQ-21 (SEF key use; demo environment only until answered).

# Goal
A seller organization owns its PIB only after a valid SEF key proves it, is authorized by a pristupnica signed by an APR legal representative, and the company is informed through its official email in APR. The same email channel confirms sensitive changes and MFA recovery.

# Context
ADR-020; [compliance.md](../docs/compliance.md) §2; [security.md](../docs/security.md) §1–3; [product-spec.md](../docs/product-spec.md) §6.1; [integrations.md](../docs/integrations.md) §3 (official email).

# Requirements
1. **Claim:** consume `SefConnectionValidated(orgId, supplierPib)` (task 010). In one transaction: if no claimed organization has that PIB → set `claimed_at`, close the other Draft claims for the same PIB that have no validated SEF key (status `ClaimClosed`) and notify their users "this company is already registered, ask its administrator for access" (no owner identity revealed). If a claimed organization already exists → the connection is refused with `pib_already_claimed` and a compliance alert is created (two different valid SEF users for one PIB).
2. **Draft expiry:** unclaimed Drafts older than `orgs.draftClaimDays` (default 7) are closed by a daily job (use the job infrastructure if present; otherwise note it for task 020 in the plan).
3. **Official email:** compliance records the organization's official email from the APR snapshot (`official_email`, source document, recordedBy). Missing email → a flag for the letter-to-seat fallback (manual).
4. **Pristupnica:** `GET /organizations/{id}/pristupnica` generates a PDF from the organization data (company, representatives, beneficial owners, Terms version, account holder, SEF and KYB-sharing consents) and stores its SHA-256. `POST /organizations/{id}/pristupnica` uploads the signed copy: a QES-signed PDF, or a scanned hand-signed copy. A scanned copy needs a confirmation through the official email before compliance can accept it.
5. **Compliance review of the pristupnica:** record signer name, matched APR representative or power-of-attorney document, method (`Qes | ScannedConfirmedByEmail`), and validation notes (QES checked manually in the MVP, ADR-011). Accept → the seller's single membership gets the Signatory flag. Reject with a reason.
6. **Confirmation links** to the official email: signed, single-use tokens (hashed at rest, 72 h expiry) for: scanned pristupnica, payout account add/change (task 009 uses it), SEF key change, MFA reset.
7. **MFA reset without recovery codes** ([security.md](../docs/security.md) §1): user request → confirmation via the official email (seller) or approval by another Owner (factor) → a Compliance Officer executes (four-eyes when only the email approved) → 72 h step-up block → notification to the security recipients.
8. **Approval notice:** on `OrganizationApproved` for a seller, send "your company was registered on Cesio" to the official email.
9. **KYB decision guard** (extends task 007): a seller organization can be Approved only if it is claimed and its pristupnica is accepted.
10. UI: seller onboarding checklist (PIB → SEF → company data → payout account → pristupnica → review), pristupnica download/upload, compliance pristupnica review screen, MFA-reset request page.

# Business Rules
- Only a valid SEF key claims a PIB; typing a PIB reserves nothing.
- An unclaimed Draft is visible only to its creator.
- The pristupnica signer must match an APR representative, or an attorney whose power of attorney a representative signed.

# Technical Requirements
PDF generation behind an interface (no new dependency without justification in the plan). Parameters: `orgs.draftClaimDays`, `confirmations.tokenHours` (72), `security.stepUpBlockHoursAfterMfaReset` (72).

# API Changes
`GET/POST /organizations/{id}/pristupnica`, `POST /auth/mfa-reset-requests`, `GET /confirmations/{token}` (public, rate-limited); admin `POST /admin/organizations/{id}/official-email` 🔐, `POST /admin/pristupnice/{id}/decision` 🔐, `POST /admin/mfa-resets/{id}/execute` 🔐.

# Database Changes
`orgs.organizations.claimed_at`, `official_email` (see database.md); `orgs.pristupnice`; `orgs.confirmation_tokens` (hashed); `identity.mfa_reset_requests`.

# Security Considerations
Tokens hashed and single-use; the "already registered" message reveals no owner data; rate limits on claim attempts per user and per PIB. Audit: claim created/won/closed, official email recorded, pristupnica uploaded/accepted/rejected, confirmation sent/used/expired, MFA reset requested/executed.

# Tests
- Race: two Draft claims for one PIB validate SEF keys at the same time → exactly one wins (partial unique index); the other is refused with `pib_already_claimed` and a compliance alert, because it also holds a valid SEF key (Requirement 1).
- Claim refused when the PIB is already claimed; compliance alert created.
- Draft expiry boundary.
- Token: single use; expired → 410; wrong purpose → 400.
- Scanned pristupnica cannot be accepted before email confirmation.
- Signatory flag set only after acceptance; seller approval blocked without claim or accepted pristupnica.
- MFA reset: 72 h step-up block enforced; Support role cannot execute.
- Cross-tenant suite for all new endpoints.

# Acceptance Criteria
- [ ] End to end: PIB → SEF claim → pristupnica → approval → notice to the official email.
- [ ] Nobody can block a company by typing its PIB.
- [ ] Sensitive changes and MFA reset require the official-email confirmation.
- [ ] All audit events present.

# Out of Scope
Automated QES validation (task 016, ADR-011); automated APR retrieval; multi-user seller organizations; eID login (LQ-52).
