# 008 — Factor Authorization Verification

**Blocked by:** LQ-09, partly answered: the APR Registar faktoringa is the official source for factoring companies, the NBS list for banks ([compliance.md](../docs/compliance.md) §4). Remaining: whether an APR entry alone is sufficient evidence.

# Goal
A factor organization can trade only with a verified, unexpired authorization (bank or approved factoring company) backed by evidence from an official source and approved by two compliance officers.

# Context
REG-01, REG-02 in [regulatory.md](../docs/regulatory.md); [compliance.md](../docs/compliance.md) §4; INV-A2 in [domain-model.md](../docs/domain-model.md).

# Requirements
- Admin: create a factor organization and invite its first user (product-spec §6.2; invitation-only, no SEF claim).
- Factor UI: submit an authorization. `FactoringCompany`: issuing authority, Fa. number / decision number, decision date, approval document, official URL. `Bank`: kind only (no decision exists; compliance checks the NBS list).
- Compliance UI: verify by attaching a source snapshot document, choosing the verification method, and adding notes (`OfficialRegisterLookup | AuthorityConfirmationLetter`) → first approval; a second officer approves → `Verified` with `reverifyBy` = verifiedAt + `reverifyDays` for the kind. Reject with a reason.
- Revoke action (immediate) with a reason.
- `IAuthorizationQuery.IsFactorAuthorized(orgId, atInstant)` = org Approved AND no active hold AND an authorization `Verified` and `reverifyBy` ≥ today.
- Event `FactorAuthorizationRevoked/Expired` → consumed later (013/014) to expire active offers and hold pre-signing deals. In this task, publish the events and add a consumer stub with a TODO referencing 020.
- Partial unique index: one `Verified` authorization per org.

# Business Rules
- `issuingAuthority` must be consistent with `kind` (Bank → NBS; FactoringCompany → MinistryOfFinance or SecuritiesCommission).
- Decision number, decision date and approval document are required for `FactoringCompany` and null for `Bank` (DB CHECK `ck_factor_auth_decision`).
- An authorization never changes after verification; a new one supersedes it (the old one → `Expired`).

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
- [ ] A factor without a Verified authorization gets false from `IsFactorAuthorized`.
- [ ] Two-approver flow works and is audited.
- [ ] Expiry/revocation events are published.

# Out of Scope
Automated register scraping; re-verification reminder emails (019/020).
