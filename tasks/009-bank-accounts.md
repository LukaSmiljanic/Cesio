# 009 — Bank Accounts

# Goal
Sellers (and factors, for reference) register bank accounts that compliance verifies. Only verified accounts outside the cool-down can receive funding.

# Context
[security.md](../docs/security.md) §2; INV-F2; [compliance.md](../docs/compliance.md) §1–2 (bank accounts); ADR-020.

# Requirements
- `POST /organizations/{id}/bank-accounts` 🔐 (Owner): account number (validated with `SerbianBankAccountNumber`), currency (RSD) → `Pending`. Compliance attaches the **NBS account register snapshot (mandatory)**; a bank letter may be attached as a supplement. For an organization that already has a Verified account, adding or changing one first needs confirmation through the official email in APR (task 007a).
- Compliance verifies (owner = org PIB per evidence) → `Verified`, with `usableFrom = verifiedAt + 72h` (parameter). Reject with a reason.
- Deactivate an account (Owner, step-up). Reactivation = new verification.
- When this task lands, the phase-2 stub of `IBankAccountQuery` (task 017) is replaced: `GET /deals/{id}/funding-accounts` and the funding report then use the real Verified, out-of-cool-down accounts (the factor sees the full number only at that step, access logged).
- `IBankAccountQuery.IsUsableForFunding(orgId, accountId, at)`.
- Notifications (event only; delivered by 019): account added/verified/deactivated → security recipients ([security.md](../docs/security.md) §2) + factors with in-flight deals with the org.
- Monitoring signal: the same account number registered by a different org → automatic compliance alert (stored as a flag on both records) and blocked verification until resolved.

# Business Rules
A seller organization cannot be Approved without a Verified payout account (extends the task 007 decision guard).
Account number displayed masked except the last 4 digits for non-owners; full number visible to the owning org, staff, and the counterparty factor in a deal at the funding step only.

# Technical Requirements
Bank name derived from the 3-digit bank code via a reference table (seeded, editable by staff).

# API Changes
Listed above + admin `POST /admin/bank-accounts/{id}/verify` 🔐 and `/reject`.

# Database Changes
`orgs.bank_accounts` (+ `usable_from`, `deactivated_at`, `duplicate_flag`).

# Security Considerations
Step-up on add/deactivate; audit with masked diffs; rate limit additions.

# Tests
Checksum validation; cool-down boundary; duplicate account across orgs flagged; masking per viewer.

# Acceptance Criteria
- [ ] Unverified or cooling-down accounts are rejected by `IsUsableForFunding`.
- [ ] Duplicate account across orgs produces an alert and blocks verification.

# Out of Scope
Automated NBS lookup; payment initiation.
