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
- Dependency, container and IaC scans clean (or accepted with a documented rationale).
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

# Acceptance Criteria
- [ ] Cross-tenant suite covers 100% of org-scoped endpoints.
- [ ] Pen-test report received; criticals/highs fixed and retested.
- [ ] Runbooks committed.

# Out of Scope
ISO 27001 certification, bug bounty.
