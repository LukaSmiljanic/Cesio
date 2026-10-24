# Security Model

Threat model: confidential commercial data (who sells to whom, at what price), money-moving decisions, and high fraud incentive (fake invoices, double financing, account takeover to redirect funds). Treat the platform as a fintech system.

## 1. Authentication
- ASP.NET Core Identity. Passwords: min 12 chars, breached-password check (k-anonymity HIBP API or a local list), Argon2id/PBKDF2 with high iterations (Identity v3 defaults or stronger).
- **MFA mandatory for every user** (TOTP or WebAuthn/passkeys). Seller accounts are shared by design (ADR-020), so TOTP is the default there and can be enrolled on several devices. Recovery codes are shown once and hashed. No SMS OTP for MFA (SIM-swap risk); SMS is used only for optional security alerts.
- **Step-up MFA** (re-authentication within 5 min) for: accepting an offer, confirming funding, recording signatures, changing payout accounts, adding members or changing the Signatory flag, changing SEF API keys, approving or executing an MFA reset, and all platform admin approvals.
- Sessions: SPA uses a secure, HttpOnly, SameSite=Strict cookie (BFF). Idle timeout 30 min, absolute 12 h. Sessions are revoked on password/MFA change. Concurrent session list with remote sign-out.
- Lockout and progressive delay on failed logins; generic error messages; email-enumeration-safe flows.
- **Identity verification ≠ login.** A user's identity and link to the organization are verified once, at onboarding (SEF claim, pristupnica, NBS account and APR notice; see [compliance.md](compliance.md) §2). Daily login uses the credentials and MFA above. A QES is not used for daily login (card readers and middleware would make it impractical); it is used for signing the factoring agreement (ADR-018) and optionally for the pristupnica and powers of attorney. eID.gov.rs login is a possible future option (LQ-52).
- **Account recovery** (the weakest point: email-only recovery would let whoever controls the mailbox take over the company):
  - Password reset by email is allowed, but **MFA is never reset by email alone**. The user's own recovery codes come first.
  - **Seller account (one per organization):** without recovery codes, the reset needs a confirmation link sent to the organization's official email recorded in APR; a Compliance Officer then executes the reset.
  - **Factor organization with several users:** another Owner of the same organization approves the reset and a Compliance Officer executes it. If there is no other Owner, the seller path applies.
  - After any reset the account cannot perform step-up actions for **72 h** (`security.stepUpBlockHoursAfterMfaReset`), and the security recipients (§2) are notified.
  - Every recovery is an audit event. Platform Support cannot reset MFA. Who approves: the APR email or another Owner; who executes: a Compliance Officer (four-eyes when the approval came only from the APR email).
- Platform staff authenticate with a separate admin realm (Entra ID SSO with conditional access), available only through the admin app and only from allowed networks/devices.

## 2. Account takeover & payment-redirect fraud
- New payout account → `Pending` until compliance verifies it in the NBS account register (snapshot mandatory). Adding or changing an account once the organization ever had a verified one, a deactivated one included (the first account is covered by the pristupnica), and changing the SEF key, also require confirmation through the organization's official email in APR (ADR-020). A changed account cannot be used for funding for **72 h** after verification. Confirmation links are single use, stored hashed and valid `confirmations.tokenHours` (72); a validated new SEF key waits as a separate pending secret until its confirmation (task 007a).
- **Security recipients** (one list, used everywhere): all Owners of the organization + its official email in APR. They are notified on: new device login, MFA change or reset, member added, Signatory flag changed, payout account added/verified/deactivated, SEF key change or invalidation. For payout-account events, factors with in-flight deals with the organization are notified too.
- Signer authority for the factoring agreement is validated against the organization's APR legal representatives or verified attorneys, not against the logged-in account.

## 3. Authorization (RBAC + resource-based)

Platform staff roles (claims until the staff realm): ComplianceOfficer, Operations, Finance, PlatformAdmin and Auditor role (task 022: reads and exports the audit log, nothing else).

### Organization roles (MVP, ADR-020)
Every member is **Owner**. **Signatory** is a flag on a member. Seller organizations have one account in the MVP.

| Permission | Owner | Owner + Signatory |
|-----------|:-:|:-:|
| Manage members and the Signatory flag (factor organizations only) | ✔ | ✔ |
| Payout accounts, SEF connection (changes confirmed via the APR email) | ✔ | ✔ |
| Import/submit receivables, create listings (seller) | ✔ | ✔ |
| Create/withdraw offers (factor) | ✔ | ✔ |
| **Accept offer, confirm CDD, confirm contract terms, cancel a deal before signing, upload signed contract, confirm funding, report funding/outcome** | | ✔ |
| Upload the factoring agreement (factor) | ✔ | ✔ |
| Download the seller's KYB package (factor of the deal; needs the seller's KYB-sharing consent, audited; never for the staff `Support` role) | ✔ | ✔ |
| View | ✔ | ✔ |

Signatory requires a link to an APR legal representative or a verified power of attorney. A seller's single account becomes Signatory when its pristupnica is accepted. Admin, Operator and Viewer roles are post-MVP.

### Platform roles
`PlatformAdmin` (configuration, no compliance decisions), `ComplianceOfficer` (KYB, authorizations, holds), `Operations` (deal follow-up, disputes log), `Finance` (fees), `Support` (read-only, masked PII), `Auditor` (read-only audit). **Four-eyes** applies to: organization approval for high-risk cases, factor authorization verification, lifting fraud holds, fee schedule changes, and fee charge reversals. No platform role can create offers, accept offers or sign on behalf of users.

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
- Rate limiting (ASP.NET Core rate limiter): login, MFA, password reset (strict); per-user limits on marketplace reads (anti-scraping) and document downloads (per-org limits if per-user proves too coarse; review in [runbooks.md](runbooks.md) §4).
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
Runbooks (incident response, key rotation, backup restore drill), the rate-limit review and the scan record: [runbooks.md](runbooks.md).

## Appendix A. Threat model (STRIDE per module, task 023)
S = spoofing, T = tampering, R = repudiation, I = information disclosure, D = denial of service, E = elevation of privilege. Each cell names the main control; "gap" marks what is still open.

| Module | S | T | R | I | D | E |
|--------|---|---|---|---|---|---|
| Identity | Password + mandatory TOTP MFA, step-up for sensitive actions, session revocation | Anti-forgery tokens on cookie requests; sessions server-side | Login, MFA and session events audited | No user enumeration on login/reset; generic errors; no passwords or tokens in logs (tested) | Per-IP auth rate limit (gap: the client IP behind the proxy, task 024) | Staff roles are claims from the staff realm; customer users cannot hold them (admin endpoints return 404 to customers, tested) |
| Organizations | Ownership claim by SEF key or the APR official-email link; bank accounts verified with a 72 h cool-down | Changes of official data go through confirmation; payout accounts audited | Every change audited with the actor | Members see only their organizations (generated cross-tenant suite) | Claim and bank-account limits of 10 per hour per user | Owner and Signatory checked per organization; four-eyes for an existing official email change is open |
| Compliance | Factor authorization verified by two officers against the official source | Screening results and holds are append-only | Holds, reviews and decisions audited | Compliance data staff-only | Screening jobs serialised by a job lock | Only ComplianceOfficer verifies or lifts; FraudInvestigation holds cannot be lifted by the system |
| Receivables | Invoices only from SEF with the seller as issuer | The invoice is immutable SEF evidence (ADR-012); triggers block updates | Import and state changes audited | Seller-only visibility; SEF keys in Key Vault, write-only | SEF polling per organization in background jobs | State machine enforced in the domain; INV-R1…R6 unique indexes |
| Marketplace | Only authorized factors make offers | Offer amounts as `Money`, snapshot at acceptance | Offers and acceptance audited | A factor never sees competing offers; audience control (tested) | Per-user marketplace read limit 120/min (anti-scraping) | Acceptance single transaction (ADR-006) |
| Deals | Parties are the seller and the winning factor only | Signed documents hashed; funding report and confirmation by both sides | Deal steps, disputes and comments audited | Internal dispute notes staff-only | — | Holds block the next step (tested for every hold kind) |
| Fees | — | Fee charges immutable; reversals are compensating rows | Charges, reversals and withdrawals audited | A factor sees only its own charges | — | Fee and money parameters need four-eyes |
| Documents | Upload by the owning organization only | SHA-256 per file; malware scan before use | Downloads access-logged | No public blob URLs; short-lived streamed downloads | Per-user document limit 120/min; size limits | Visibility checked per document (cross-tenant suite) |
| Notifications | Links point at our origin only (gap: mandatory public base URL, task 024) | Outbox rows written in the business transaction | Deliveries recorded | No PII in subjects beyond what the recipient owns | Deduplicated reminders | — |
| Audit | — | Append-only: app role has no UPDATE/DELETE (tested), hash chain verified daily | This is the record | Search and export for ComplianceOfficer and Auditor only, access-logged | Export is step-up | — |
| Platform / hosts | Managed identity to Key Vault, SQL and Blob | Migrations by a separate principal; CI deploys only | Job runs recorded in `platform.job_runs` | Security headers and strict CSP on the API (tested); SPA CSP is hosting config (task 024) | Jobs serialised by locks | Data protection keys (gap: persisted to Blob + Key Vault, task 024) |
