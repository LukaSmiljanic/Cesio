# 005 — Organizations, Memberships, Roles

**Blocked by (partial):** LQ-73 (ADR-022): who may accept the Terms of Use for a factor organization and in what form. Terms acceptance is stored as specified below; no acceptor rule is enforced until LQ-73 is answered.

# Goal
A user can start a seller organization as a private Draft claim, factor organizations can have several members with an Owner role and a Signatory flag, and organizations can be submitted for review (ADR-020). Org context and resource authorization are enforced on every request.

# Context
[domain-model.md](../docs/domain-model.md) §3 (Organization, Membership, LegalRepresentative); [security.md](../docs/security.md) §3; ADR-020; [api.md](../docs/api.md) §1, Organizations.

# Requirements
- `orgs` schema: organizations, memberships, legal_representatives, terms_acceptances.
- `POST /organizations` (type Seller, PIB; other company data is filled in later) → a private `Draft` claim; the creator becomes Owner. Several Draft claims may exist for the same PIB; none is visible to other users. PIB/MB uniqueness applies only to claimed organizations (filtered unique indexes in database.md); the claim itself happens in task 007a. Factor organizations are created by staff (admin endpoint in task 008); until then a seed/test path creates them (the Development-only `POST /dev/factor-organizations`).
- Roles: every member is `Owner`; `Signatory` is a flag (ADR-020). Business parameter `orgs.maxSellerMembers` (default 1): inviting a member into a seller organization at the limit → 422.
- Members (factor organizations): invite by email (token, 7-day expiry), accept, remove (the last Owner cannot be removed). Inviting a member and changing the Signatory flag require step-up.
- Legal representatives: CRUD in Draft (staff-verified in 007). The Signatory flag can be set only on a member linked to a legal representative or with a power-of-attorney document (verification in 007; for a seller's single account the flag is set when its pristupnica is accepted, task 007a).
- Terms acceptance: store terms version, user, timestamp, IP (texts come from task 000; use a placeholder version until then).
- `POST /organizations/{id}/submit`: requires the minimum data present → `Submitted` (KYB case created in 007; here only emit `OrganizationSubmitted`).
- Org-context middleware: `X-Organization-Id` validated against active membership; roles loaded into the request context. EF global query filter by org on org-owned tables (defence in depth).
- UI: start-seller wizard (step 1: PIB), members page with Signatory flag (factor organizations), org switcher.

# Business Rules
- One role per organization type (BQ-07): Seller **or** Factor.
- Only `Approved` organizations can transact (enforced in later tasks via `IOrganizationQuery.IsApproved`).

# Technical Requirements
Expose `Modules.Organizations.Contracts`: `IOrganizationQuery` (status, type, roles of user in org, signatory check).

# API Changes
As listed in api.md (Organizations, members, terms acceptances).

# Database Changes
`orgs.*` tables with the unique constraints from database.md.

# Security Considerations
Audit all membership and role changes. Invitation tokens are hashed. 404 instead of 403 for orgs the user does not belong to.

# Tests
- Cross-tenant suite: a user of org A cannot read or modify org B (all endpoints).
- Last-Owner protection; Signatory flag without a representative link → 422; seller organization member limit → 422.
- Two users can each hold a Draft claim for the same PIB without seeing each other.
- Concurrency: two simultaneous Signatory-flag edits → one 412.

# Acceptance Criteria
- [ ] Wizard creates a Draft claim; factor members and the Signatory flag work end to end.
- [ ] Org-context middleware is applied to all org-scoped routes (test enumerates endpoints).
- [ ] Audit events for claim created, invite, accept, Signatory flag change, submit.

# Out of Scope
KYB review, APR lookups, bank accounts, beneficial owners (007/009). Security-recipient notifications for member and Signatory changes (security.md §2) → task 019. Powers of attorney as a Signatory basis need verified documents (006/007); until then they are rejected. Revoking a pending invitation → later (it expires after 7 days).
