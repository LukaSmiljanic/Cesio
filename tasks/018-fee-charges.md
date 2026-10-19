# 018 — Fee Charges & Statements

**Blocked by:** LQ-43 (VAT) and LQ-44 (SEF invoicing, tax point) for production values. Mechanics can be built now.

# Goal
When a deal is funded, exactly one fee charge per payer is created from the deal's fee snapshot; Finance can mark charges invoiced/paid and export monthly statements for invoicing through our accounting system.

# Context
[business-model.md](../docs/business-model.md) §2–3; [financial-model.md](../docs/financial-model.md); INV-F3; task 017a.

# Requirements
- Consumer of `DealFunded`: create `fees.fee_charges` (`kind=Charge`, status `Due`, `due_at` = statement period end + payment terms parameter) per payer from the deal's fee snapshot. Idempotent via the partial unique index.
- Finance admin: list charges (filters: period, payer, status), mark `Invoiced` (external SEF invoice reference), mark `Paid` (date, reference), create a `Reversal` (with reason; four-eyes) that links to the original via `reverses_id`.
- Monthly statement per payer org: `GET /fees/statements/{yyyy-mm}` (payer org view) and an admin CSV export (deal reference, receivable invoice number, face value, fee net, VAT, total, funded date).
- Customer view: `GET /fees/charges` for the payer org.
- UI: factor "Fees" page; Finance admin page.

# Business Rules
- No charge for deals that never reached `Funded`.
- Charges are never edited; status transitions only `Due → Invoiced → Paid`; corrections via Reversal rows.
- Fee refund policy on recourse/default: none by default (BQ-05).

# Technical Requirements
Money sums per currency; CSV with invariant-culture decimals and a UTF-8 BOM for Excel.

# API Changes
`GET /fees/charges`, `GET /fees/statements/{period}`; admin `GET /admin/fees/charges`, `POST /admin/fees/charges/{id}/invoiced|paid|reverse` 🔐, `GET /admin/fees/export?period=`.

# Database Changes
`fees.fee_charges` (DDL in database.md) + `fees.fee_payments`.

# Security Considerations
Finance role only for admin actions; exports audited.

# Tests
Duplicate `DealFunded` delivery → one charge; reversal four-eyes; statement totals; payer sees only its charges.

# Acceptance Criteria
- [ ] Funded deal → exactly one charge per payer with the snapshot amount.
- [ ] Monthly export matches the ledger of charges minus reversals.

# Out of Scope
Issuing SEF invoices from the platform (future integration), payment collection, dunning.
