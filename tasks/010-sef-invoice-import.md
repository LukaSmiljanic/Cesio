# 010 — SEF Connection & Invoice Import

**Blocked by (partial):** LQ-51 (protection and liability terms for holding a full-scope SEF key; the read-only client may proceed). LQ-21 (third-party use of SEF API keys) proceeds on the product owner's working assumption of 2026-10-22 that this use is permitted (legal-questions.md §5); the LQ stays open for the lawyer.

# Goal
A seller connects its SEF account with an API key, and imports sales invoices into the platform as immutable Invoice records with Draft receivables.

# Context
[integrations.md](../docs/integrations.md) §1; REG-11; [api.md](../docs/api.md) §3.1; [database.md](../docs/database.md) §2–3; ADR-012.

# Requirements
1. **Verify the SEF API first** (time-boxed spike, documented in `docs/integrations.md`): from the official Swagger of the demo environment, record the exact endpoints for: get sales invoice by ID, list sales invoice IDs by date, get status/changes, download UBL; the status enum values; rate limits; whether credit notes reference original invoices. **Do not code against guessed endpoints.**
2. `Integrations.Sef`: typed `HttpClient` with `ApiKey` header from the secret store (one secret per org, encrypted in the database since ADR-023); read-only methods only; Polly retry (3x, exponential + jitter, only on 5xx/timeout); 10 s timeout; correlation ID logging; a fake implementation with fixtures for tests.
3. `PUT /organizations/{id}/sef-connection` 🔐 (Owner): accepts the key, stores it encrypted in the secret store (`sef-key-{orgId}`, ADR-023), validates by calling a cheap read endpoint, and checks that the returned supplier PIB = org PIB. Status `Connected | Invalid`. The key is never returned. A successful validation publishes `SefConnectionValidated(orgId, supplierPib)`; the same request claims the PIB (task 007a, ADR-020); a claimed organization's key change waits for the official-email confirmation (task 007a). Allowed on a Draft claim (import before approval is allowed; publishing is not).
4. `POST /receivables/import-from-sef` ♻ per api.md §3.1: creates `receivables.debtors` (upsert by PIB), `receivables.invoices` (UBL stored as a document with SHA-256; amounts, dates, currency), `receivables.sef_status_history` row, and a `Draft` receivable (faceValue = gross − known credit notes, maturity = due date, fingerprint computed).
5. UI: SEF connection page; import screen (by IDs or date range) with per-item results; receivables list (Draft).

# Business Rules
- Only invoices where the org is the **supplier** are importable (`NotIssuer` otherwise).
- Currency other than RSD → `Unsupported` (ADR-009).
- Debtor must be identified by a PIB (B2B); otherwise `Unsupported`.
- Re-importing an already imported SEF invoice returns `AlreadyImported` with the existing receivable ID (if it is not Rejected/Cancelled).
- Implementation notes (2026-10-23): faceValue = gross, because no credit notes are known at import; credit notes, invoices with a deducted prepayment (PayableAmount ≠ gross) and amounts with more than 2 decimals are `Unsupported`. An invoice imported by another organization is reported as `NotFound` to the caller.

# Technical Requirements
UBL parsing with `System.Xml` (no new dependency unless justified); amounts parsed as decimal with invariant culture; `gross = net + vat` asserted (mismatch → `Unsupported` + log).

# API Changes
`PUT /organizations/{id}/sef-connection`, `POST /receivables/import-from-sef`, `GET /receivables`, `GET /receivables/{id}`.

# Database Changes
`receivables.debtors`, `invoices`, `receivables`, `sef_status_history` with the constraints in database.md §2 (`UNIQUE(sef_invoice_id)`, `UNIQUE(seller_org_id, invoice_number, issue_date)`, filtered unique index on receivable per invoice).

# Security Considerations
The key is stored encrypted (ADR-023) and never logged; SEF 401 → connection `Invalid` + event for notification. Audit: connection set/rotated/invalidated, invoice imported (with UBL hash).

# Tests
Fake SEF: import success; NotIssuer; duplicate import; foreign currency; malformed UBL; 401 handling; retry on 503; unique-constraint race (two parallel imports of the same invoice → one invoice).

# Acceptance Criteria
- [x] SEF endpoints used are documented from the official Swagger (integrations.md §1.1).
- [ ] Import against the SEF demo environment works with a test company key (manual check, recorded in the PR).
- [x] No write endpoint exists in the SEF client (`ISefClient`: key check, list IDs, get invoice, get UBL).

# Out of Scope
Verification/eligibility (011), status monitoring job (020), CEF.
