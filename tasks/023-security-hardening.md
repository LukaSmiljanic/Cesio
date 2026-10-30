# 023 — Security Hardening & Pen-Test Readiness

# Goal
Close the security checklist before an external penetration test and the production launch.

# Context
[security.md](../docs/security.md) (all sections); [audit.md](../docs/audit.md) §6.

# Requirements
- Automated cross-tenant test suite covering **every** endpoint (generated from endpoint metadata: each org-scoped endpoint is called with a foreign org's resource ID and must return 404/403).
- Security headers: CSP (no inline scripts), HSTS, X-Content-Type-Options, Referrer-Policy, Permissions-Policy, frame-ancestors none.
- Rate-limit review for all endpoints; anti-scraping limits on the marketplace.
- Log scrubbing tests (no secrets, tokens, account numbers or emails in plaintext logs).
- Dependency, container and configuration scans clean (or accepted with a documented rationale).
- Threat model document update (STRIDE per module) in `docs/security.md` appendix.
- Incident response runbook, key rotation runbook (SEF keys, data protection keys), backup restore drill record.
- Verify DB grants in a deployed environment (app role cannot UPDATE/DELETE audit or financial immutable columns).
- Engage an external pen-test firm (scope: web app, API, auth, tenant isolation, file upload, admin realm). Fix critical/high findings.

# Business Rules
No production launch with open critical/high findings.

# Technical Requirements
Security tests run in CI on every PR.

# API Changes
None (fixes only).

# Database Changes
None expected (grants verification).

# Security Considerations
This task is the security gate.

# Tests
As listed above.

Implementation notes (2026-10-24):
- The cross-tenant suite (`MarketplaceTests.CrossTenant.cs`) finds every endpoint with organization-scoped metadata. It calls each one as another seller and another factor, using the ids of a real signed deal. It accepts 403, 404, 405 and 400, because a body validated before the lookup says nothing about the resource. A short allowlist holds the endpoints that only return the caller's own lists. A second test calls every `/admin/` endpoint as a customer and expects 404.
- API responses carry a strict CSP (`default-src 'none'; frame-ancestors 'none'`) and the other headers (`SecurityHeaders.cs`); HSTS outside Development. The SPA's CSP belongs to its hosting, task 024.
- `SecurityHardeningTests` covers three things:
  - the headers;
  - logs, captured through a test logger provider, never containing the email, SEF key, bank account number or password of a full flow;
  - the `app_rw` role being refused UPDATE and DELETE on the audit log, and DELETE on fee charges, as a real database user.
- STRIDE appendix: [security.md](../docs/security.md) Appendix A. Runbooks, the rate-limit review and the scan record: [runbooks.md](../docs/runbooks.md).
- Open:
  - forwarded client IP for the auth limiter, persisted data protection keys, container and configuration scans, the first restore drill and DB grants in a deployed environment: all task 024;
  - a staff "sign out user" action (none exists; noted in the runbook);
  - the external penetration test, which the product owner engages.

# Acceptance Criteria
- [x] Cross-tenant suite covers 100% of org-scoped endpoints.
- [ ] Pen-test report received; criticals/highs fixed and retested.
- [x] Runbooks committed.

# Out of Scope
ISO 27001 certification, bug bounty.
