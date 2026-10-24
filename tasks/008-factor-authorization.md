# 008 — Factor Authorization Verification

**Blocked by:** LQ-09, partly answered: the APR Registar faktoringa is the official source for factoring companies, the NBS list for banks ([compliance.md](../docs/compliance.md) §4). Remaining: whether an APR entry alone is sufficient evidence.
**Blocked by (partial):** LQ-79 (evidence retention and the re-check period; build with the business parameters). **Blocked by (partial):** LQ-73 (ADR-022). The factor accepts the Terms of Use for factors instead of signing a separate agreement (LQ-72: working assumption 2026-10-22, legal-questions.md §5). Who may accept them for the factor, and in what form, is open: store the acceptance (Terms version, user, timestamp, IP) and do not hard-code the acceptor rule.

# Goal
A factor organization can trade only with a verified, unexpired authorization (bank or approved factoring company) backed by evidence from an official source and approved by two compliance officers.

# Context
REG-01, REG-02 in [regulatory.md](../docs/regulatory.md); [compliance.md](../docs/compliance.md) §4; INV-A2 in [domain-model.md](../docs/domain-model.md).

# Requirements
- Admin: create a factor organization and invite its first user (product-spec §6.2; invitation-only, no SEF claim).
- Factor UI: submit an authorization. `FactoringCompany`: issuing authority, Fa. number / decision number, decision date, approval document, official URL. `Bank`: kind only (no decision exists; compliance checks the NBS list).
- Compliance UI: verify by attaching a source snapshot document, choosing the verification method, and adding notes (`OfficialRegisterLookup | AuthorityConfirmationLetter`) → first approval; a second officer approves → `Verified` with `reverifyBy` = verifiedAt + `reverifyDays` for the kind. Reject with a reason.
- Revoke action (immediate) with a reason.
- `IAuthorizationQuery.IsFactorAuthorized(orgId)` (at the current instant) = org Approved AND no active hold AND an authorization `Verified` and `reverifyBy` ≥ today.
- Event `FactorAuthorizationRevoked/Expired` → consumed later (013/014) to expire active offers and hold pre-signing deals. In this task, publish the events and add a consumer stub with a TODO referencing 020 (task 020 owns both consequences: expiring the Active offers and placing holds on deals before Signed).
- Filtered unique index: one `Verified` authorization per org.

# Business Rules
- `issuingAuthority` must be consistent with `kind` (Bank → NBS; FactoringCompany → MinistryOfFinance or SecuritiesCommission).
- Decision number, decision date and approval document are required for `FactoringCompany` and null for `Bank` (DB CHECK `ck_factor_auth_decision`).
- An authorization never changes after verification; a new one supersedes it (the old one → `Expired`).
- Implementation notes (2026-10-24, simplest options): the authority/kind check is a data-consistency rule, not a legal conclusion (LQ-09); the issuing authority values are `NationalBankOfSerbia`, `MinistryOfFinance` and `SecuritiesCommission` (domain-model.md); the factor's approval document is a `KybEvidence` document of its organization; the officer uploads the source snapshot through `POST /admin/factor-authorizations/{id}/snapshot` (stored as the factor's `KybEvidence`), and the first approval records the method, snapshot and notes; the second, different officer verifies (CHECK `ck_factor_auth_four_eyes` as the backstop); "today" is the Belgrade business date and `reverifyBy` is the last authorized day; a daily worker job (`FactorAuthorizationExpiryJob`) moves overdue authorizations to `Expired` and publishes `FactorAuthorizationExpired`; `IsFactorAuthorized` already refuses an overdue one before the job runs; the revoke and expiry consumers in Marketplace are stubs for task 020; staff create a factor organization (claimed at creation) and invite its first user, who becomes Owner by accepting; the Terms acceptance is recorded as for sellers, and the UI notes that who may accept for the factor is open (LQ-73); the reverify periods are business parameters, not legal requirements (LQ-79).

# Technical Requirements
Business parameters `factorAuthorization.reverifyDays.factoringCompany` (default 90) and `factorAuthorization.reverifyDays.bank` (default 30).

# API Changes
`POST/GET /organizations/{id}/factor-authorizations`; admin `GET /admin/factor-authorizations?status=`, `POST /admin/factor-authorizations/{id}/approve` 🔐, `/reject` 🔐, `/revoke` 🔐.

# Database Changes
`compliance.factor_authorizations` (DDL in database.md).

# Security Considerations
Four-eyes; audit with both approvers; evidence documents linked to the audit events.

# Tests
Expiry boundary (reverifyBy = today → authorized; yesterday → not); revoke → `IsFactorAuthorized` false immediately; same officer cannot give both approvals; inconsistent authority/kind → 422; bank without decision fields → accepted; factoring company without them → 422; reverifyBy uses the per-kind period.

# Acceptance Criteria
- [x] A factor without a Verified authorization gets false from `IsFactorAuthorized`.
- [x] Two-approver flow works and is audited.
- [x] Expiry/revocation events are published.

# Out of Scope
Automated register scraping; re-verification reminder emails (019/020).
