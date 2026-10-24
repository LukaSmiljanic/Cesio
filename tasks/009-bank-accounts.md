# 009 — Bank Accounts

**Blocked by (partial, go-live only):** LQ-47, LQ-80, LQ-81, LQ-82 (evidence retention, disclosure of the account number, duplicate-account handling).

# Goal
Sellers (and factors, for reference) register bank accounts that compliance verifies. Only verified accounts outside the cool-down can receive funding.

# Context
[security.md](../docs/security.md) §2; INV-F2; [compliance.md](../docs/compliance.md) §1–2 (bank accounts); ADR-020.

# Requirements
- `POST /organizations/{id}/bank-accounts` 🔐 (Owner): account number (validated with `SerbianBankAccountNumber`), currency (RSD) → `Pending`. Compliance attaches the **NBS account register snapshot (mandatory)**; a bank letter may be attached as a supplement. For an organization that ever had a verified account (a deactivated one included), adding or changing one first needs confirmation through the official email in APR (task 007a).
- Compliance verifies (owner = org PIB per evidence) → `Verified`, with `usableFrom = verifiedAt + 72h` (parameter). Reject with a reason.
- Deactivate an account (Owner, step-up). Reactivation = new verification.
- When this task lands, the phase-2 stub of `IBankAccountQuery` (task 017) is replaced: `GET /deals/{id}/funding-accounts` and the funding report then use the real Verified, out-of-cool-down accounts (the factor sees the full number only at that step, access logged).
- `IBankAccountQuery.IsUsableForFunding(orgId, accountId, at)`.
- Notifications (event only; delivered by 019): account added/verified/deactivated → security recipients ([security.md](../docs/security.md) §2) + factors with in-flight deals with the org.
- Monitoring signal: the same account number registered by a different org → automatic compliance alert, a flag on the new record only (the other organization's account is neither blocked nor revealed; the owner does not see the flag) and blocked verification of the new record until resolved.

# Business Rules
A seller organization cannot be Approved without a Verified payout account (extends the task 007 decision guard).

Implementation notes (2026-10-24, simplest options): payout accounts are RSD only; an account added while the organization already has a Verified one starts as `ConfirmationPending` and becomes `Pending` through the `PayoutAccountChange` confirmation link (no official email → 409 `official_email_missing`); the officer uploads the NBS snapshot (and an optional bank letter) through `POST /admin/bank-accounts/{id}/evidence` and confirms that the holder's PIB is the organization's (`holderMatchesPib`); the cool-down is `bankAccounts.coolDownHours` (72) from verification; a duplicate across organizations flags every open record with that number, writes `Compliance.Alert`, blocks verification and is resolved per record with `clear-duplicate` (with a reason) or by rejecting the wrong record; the seeded bank codes are 160, 170, 205, 265, 325 and 340 (an unknown code gets "Nepoznata banka"), staff edit them with `PUT /admin/bank-codes/{code}`; additions are limited to 10 per user per hour (a working default); the counterparty factor sees the full number only through `GET /deals/{id}/funding-accounts` (access logged, task 017); the account events are published for task 019.
Account number displayed masked except the last 4 digits for non-owners; full number visible to the owning org, staff, and the counterparty factor in a deal at the funding step only.

# Technical Requirements
Bank name derived from the 3-digit bank code via a reference table (seeded, editable by staff).

# API Changes
Listed above + `POST /organizations/{id}/bank-accounts/{accountId}/deactivate` 🔐; admin `GET /admin/bank-accounts`, `POST /admin/bank-accounts/{id}/evidence`, `POST /admin/bank-accounts/{id}/verify` 🔐, `/reject` 🔐, `/clear-duplicate` 🔐, `GET /admin/bank-codes`, `PUT /admin/bank-codes/{code}` 🔐.

# Database Changes
`orgs.bank_accounts` (+ `usable_from`, `deactivated_at`, `duplicate_flag`).

# Security Considerations
Step-up on add/deactivate; audit with masked diffs; rate limit additions.

# Tests
Checksum validation; cool-down boundary; duplicate account across orgs flagged; masking per viewer.

# Acceptance Criteria
- [x] Unverified or cooling-down accounts are rejected by `IsUsableForFunding`.
- [x] Duplicate account across orgs produces an alert and blocks verification.

# Follow-ups from the phase 4 review (2026-10-24)
Fixed: an account added after any verified account (also a deactivated one) needs the official-email confirmation; a duplicate flags only the new record (the other organization's account is neither blocked nor revealed) and the owner does not see the flag; a rejected or deactivated number can be registered again (filtered unique index); currency CHECK; the evidence must be a clean KYB-evidence document; the funding report checks INV-F2 through `IsUsableForFundingAsync`. Left open: proving that the evidence was uploaded by staff (today: purpose and owner are checked).

# Out of Scope
Automated NBS lookup; payment initiation.
