# Security Model

Threat model: confidential commercial data (who sells to whom, at what price), money-moving decisions, and high fraud incentive (fake invoices, double financing, account takeover to redirect funds). Treat the platform as a fintech system.

## 1. Authentication
- ASP.NET Core Identity. Passwords: min 12 chars, breached-password check (k-anonymity HIBP API or a local list), Argon2id/PBKDF2 with high iterations (Identity v3 defaults or stronger).
- **MFA mandatory for every user** (TOTP or WebAuthn/passkeys). Seller accounts are shared by design (ADR-020), so TOTP is the default there and can be enrolled on several devices. Recovery codes are shown once and hashed. No SMS OTP for MFA (SIM-swap risk); SMS is used only for optional security alerts.
- **Step-up MFA** (re-authentication within 5 min) for: accepting an offer, confirming funding, recording signatures, changing payout accounts, adding members or changing the Signatory flag, changing SEF API keys, executing an MFA reset, and all platform admin approvals.
- Sessions: SPA uses a secure, HttpOnly, SameSite=Strict cookie (BFF). Idle timeout 30 min, absolute 12 h. Sessions are revoked on password/MFA change. Concurrent session list with remote sign-out.
- Lockout and progressive delay on failed logins; generic error messages; email-enumeration-safe flows.
- **Identity verification ≠ login.** A user's identity and link to the organization are verified once, at onboarding (SEF claim, pristupnica, NBS account and APR notice; see [compliance.md](compliance.md) §2). Daily login uses the credentials and MFA above. A QES is not used for daily login (card readers and middleware would make it impractical); it is used for signing the factoring agreement (ADR-018) and optionally for the pristupnica and powers of attorney. eID.gov.rs login is a possible future option (LQ-52).
- **Account recovery** (the weakest point: email-only recovery would let whoever controls the mailbox take over the company):
  - Password reset by email is allowed, but **MFA is never reset by email alone**. The user's own recovery codes come first.
  - **Seller account (one per organization):** without recovery codes, the reset needs a confirmation link sent to the organization's official email recorded in APR; a Compliance Officer then executes the reset.
  - **Factor organization with several users:** another Owner of the same organization approves the reset and a Compliance Officer executes it. If there is no other Owner, the seller path applies.
  - After any reset the account cannot perform step-up actions for **72 h**, and the security recipients (§2) are notified.
  - Every recovery is an audit event. Platform Support cannot reset MFA. Who approves: the APR email or another Owner; who executes: a Compliance Officer (four-eyes when the approval came only from the APR email).
- Platform staff authenticate with a separate admin realm (Entra ID SSO with conditional access), available only through the admin app and only from allowed networks/devices.

## 2. Account takeover & payment-redirect fraud
- New payout account → `Pending` until compliance verifies it in the NBS account register (snapshot mandatory). Adding or changing an account once the organization already has a Verified one (the first account is covered by the pristupnica), and changing the SEF key, also require confirmation through the organization's official email in APR (ADR-020). A changed account cannot be used for funding for **72 h** after verification.
- **Security recipients** (one list, used everywhere): all Owners of the organization + its official email in APR. They are notified on: new device login, MFA change or reset, member added, Signatory flag changed, payout account added/changed/deactivated, SEF key change. For payout-account events, factors with in-flight deals with the organization are notified too.
- Signer authority for the factoring agreement is validated against the organization's APR legal representatives or verified attorneys, not against the logged-in account.

## 3. Authorization (RBAC + resource-based)

### Organization roles (MVP, ADR-020)
Every member is **Owner**. **Signatory** is a flag on a member. Seller organizations have one account in the MVP.

| Permission | Owner | Owner + Signatory |
|-----------|:-:|:-:|
| Manage members and the Signatory flag (factor organizations only) | ✔ | ✔ |
| Payout accounts, SEF connection (changes confirmed via the APR email) | ✔ | ✔ |
| Import/submit receivables, create listings (seller) | ✔ | ✔ |
| Create/withdraw offers (factor) | ✔ | ✔ |
| **Accept offer, confirm CDD, confirm contract terms, upload signed contract, confirm funding, report funding/outcome** | | ✔ |
| View | ✔ | ✔ |

Signatory requires a link to an APR legal representative or a verified power of attorney. A seller's single account becomes Signatory when its pristupnica is accepted. Admin, Operator and Viewer roles are post-MVP.

### Platform roles
`PlatformAdmin` (configuration, no compliance decisions), `ComplianceOfficer` (KYB, authorizations, holds), `Operations` (deal follow-up, disputes log), `Finance` (fees), `Support` (read-only, masked PII), `Auditor` (read-only audit). **Four-eyes** applies to: organization approval for high-risk cases, factor authorization verification, lifting fraud holds, and fee schedule changes. No platform role can create offers, accept offers or sign on behalf of users.

### Tenant isolation
- Every query in an org context is filtered by `org_id` in the application layer (EF global query filters as defence in depth). Every command also re-checks resource ownership.
- **Marketplace visibility** is computed server-side: listing is Open, factor is authorized and not excluded, and the audience includes it. IDs are sequential GUIDs with random bytes (unguessable enough, but **never** relied on for security).
- A factor sees **only its own offers**. A seller sees all offers on its listings. Competing factors never see each other's identities or prices (BQ-04 may change this).
- Integration tests include a **cross-tenant access suite** for every endpoint (e.g. factor B tries to read factor A's offer → 404).

## 4. Documents
- Private Blob containers. Downloads go through an authorized API endpoint that streams the file or issues a **user-delegation SAS valid ≤ 60 s**, single blob, read-only, after the authorization check. Every download is audited.
- Upload: allow-list of types (PDF, XML, PNG/JPG), magic-byte check, ≤ 20 MB, filename sanitised, stored under a random key. Files stay in `quarantine` until the malware scan (Defender for Storage) is clean, then move to `clean`. Infected files are blocked and the uploader is notified.
- PDFs shown to factors are watermarked with the factor org and timestamp (MVP: on download).
- Every stored document keeps its SHA-256. The contract hash is fixed at the terms-checklist step; signatures must match it.

## 5. Encryption & secrets
- TLS 1.2+ everywhere, HSTS. DB and Blob encryption at rest (platform-managed keys in the MVP; customer-managed keys later if required).
- Key Vault for all secrets; managed identities; no secrets in config or repo (secret scanning in CI). SEF API keys: one secret per organization, write-only from the UI (never displayed again), and access is logged.
- ASP.NET data protection keys are persisted to Blob and protected by Key Vault.

## 6. API security
- Same-origin SPA with cookies + anti-forgery tokens for state-changing requests. CORS is locked to our origins.
- Rate limiting (ASP.NET Core rate limiter): login, MFA, password reset (strict); per-user and per-org limits on marketplace reads (anti-scraping) and document downloads.
- Input validation on every request DTO; output DTOs only (never entities). Strict JSON options (no polymorphic deserialisation).
- Future factor API: OAuth 2.0 client credentials with per-org clients, scopes and mTLS or private_key_jwt.

## 7. Fraud prevention controls
| Threat | Controls |
|--------|----------|
| Fake invoice | SEF-only, buyer status Approved, seller = SEF issuer, debtor active check, supporting documents, manual review thresholds, fingerprint duplicates |
| Double financing on platform | DB invariants INV-R1/R2, single transaction acceptance |
| Double financing off platform | Seller declaration, SEF status re-polling (storno/cancel), CEF check when live, factor disclosure (LQ-20) |
| Collusive seller/debtor | Related-party check (shared BOs/representatives), concentration alerts, new-debtor flags |
| Funds redirection | Bank account verification, 72 h cool-down, step-up MFA, notifications |
| Fake factor | Authorization verification against the official source, four-eyes, periodic re-verification |
| Insider abuse | Least-privilege platform roles, four-eyes, immutable audit, PII masking for support |
| Scraping/data leakage | Audience control, rate limits, watermarking, download audit |

## 8. Logging & monitoring
- Structured logs with correlation IDs. **Never log**: passwords, tokens, API keys, full document contents, bank account numbers (masked), personal identifiers (masked). A log-scrubbing enricher plus unit tests enforce this. Stored error texts (e.g. `platform.outbox.last_error`) hold the exception type only, never the message.
- Alerts: authentication anomalies, spikes in 403/404 (enumeration), SEF sync failures, outbox backlog, failed malware scans, audit-chain verification failures, admin actions outside business hours.
- Security event log is retained ≥ 1 year online (subject to LQ-38/LQ-47).

## 9. Secure SDLC
Dependency scanning, SAST (CodeQL), container image scanning, IaC scanning; threat model review per major feature; external **penetration test before production**; incident response runbook; backup restore drills.
