# 004 — Identity, MFA, SPA Shell

**Blocked by:** — (ADR-010 accepted 2026-10-22: shadcn/ui + Tailwind + TanStack Table).

# Goal
Users can register, verify email, enrol mandatory MFA, sign in/out, and perform step-up authentication. A SPA shell with auth flows exists.

# Context
[security.md](../docs/security.md) §1–2; ADR-005; [api.md](../docs/api.md) Auth/me.

# Requirements
- ASP.NET Core Identity with an `auth` schema (`identity` is a T-SQL reserved word); email confirmation; password policy (≥ 12 chars, breached-password check via a k-anonymity range API behind an interface with an offline fallback list).
- MFA: TOTP (with QR provisioning) required before any org access; WebAuthn/passkeys optional (a second factor type may be deferred to a follow-up if large; it must be noted in the plan). Recovery codes (10, hashed, shown once). TOTP can be enrolled on more than one device: after step-up, the user can re-display the provisioning QR (seller accounts are shared by design, ADR-020).
- BFF cookie auth: HttpOnly, Secure, SameSite=Strict; anti-forgery token endpoint + header validation; idle 30 min / absolute 12 h.
- Step-up: `POST /auth/step-up` re-verifies TOTP and stamps `mfa_step_up_at` in the session; a `RequireStepUp(5 min)` endpoint filter returns 401 `step_up_required`.
- Sessions list/revoke; revoke all on password or MFA change.
- Lockout (5 failures → progressive delay); enumeration-safe responses; rate limiting on auth endpoints.
- SPA: routes for register, login, MFA enrol/verify, step-up modal, logout, session list; an API client with anti-forgery and ProblemDetails handling.

# Business Rules
- No access to any org-scoped endpoint without completed MFA enrolment.
- No SMS MFA.

# Technical Requirements
Email sending via an `IEmailSender` abstraction (smtp4dev locally).

# API Changes
`/auth/*`, `/me`, `/me/sessions`.

# Database Changes
`auth` schema (Identity tables + sessions + recovery codes).

# Security Considerations
Audit: login success/failure, MFA enrol/change, password reset, session revoke, lockout. No tokens in logs.

# Tests
Integration: full register → confirm → MFA → login flow; step-up expiry; lockout; CSRF rejection without the header; session revocation invalidates the cookie.

# Acceptance Criteria
- [ ] A new user cannot reach `/me/organizations` before MFA enrolment.
- [ ] Step-up-protected sample endpoint returns 401 after 5 minutes.
- [ ] All audited events present.

# Out of Scope
Staff accounts and platform roles → task 007; Entra ID SSO for staff → task 022; organizations; MFA reset without recovery codes (needs the organization's official email and staff) → task 007a.
