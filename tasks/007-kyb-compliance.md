# 007 — KYB Case, Beneficial Owners, Screening, Holds

**Blocked by:** LQ-30 (minimum KYB scope) and LQ-31 (sanctions lists). Until they are answered, implement the lists as configuration and default to UN + APML designated persons + EU + OFAC.

# Goal
Submitted organizations go through a KYB case reviewed by compliance staff, with beneficial owners, sanctions/PEP screening, risk rating, four-eyes for high risk, and a generic Hold mechanism.

# Context
[compliance.md](../docs/compliance.md) §1–2, §5–7; [domain-model.md](../docs/domain-model.md) (KybCase, BeneficialOwner, ScreeningResult, Hold); ADR-014.

# Requirements
1. **Staff accounts:** Identity users flagged as staff with platform roles (`PlatformAdmin, ComplianceOfficer, Operations, Finance, Support, Auditor`), MFA mandatory, admin endpoints under `/api/v1/admin` with role policies. Staff cannot belong to customer organizations.
2. On `OrganizationSubmitted` → create a `KybCase (Open)`.
3. Seller/factor UI: beneficial owners form (name, DOB, nationality, % / control type), PEP self-declaration per person, upload of APR extract (optional), KYB data-sharing consent.
4. Compliance UI (queue): case detail with company data, representatives, BOs, documents; record APR snapshot documents (company + BO register) and mark representatives verified; resolve screening hits; set risk rating (rules + override with reason); decide `Approved | Rejected | NeedsInfo` with a reason.
5. **Four-eyes:** risk `High` requires a second, different ComplianceOfficer to approve.
6. Screening: `ISanctionsScreening` with a local implementation. A daily import job loads lists (configured URLs/files; parsers for UN XML and EU XML; OFAC SDN; APML list format to be confirmed, with manual CSV upload as fallback). Fuzzy name match (normalised, transliteration Cyrillic↔Latin, Jaro-Winkler threshold configurable) on org names and persons. Results: `Clear | PotentialMatch | ConfirmedMatch`.
7. Holds: `compliance.holds` + `IHoldQuery.HasActiveHold(subjectType, id, kinds)`; staff can place/lift holds on organizations (receivable/deal holds used later). Lifting a `FraudInvestigation` hold requires four-eyes.
8. Approval sets the organization to `Approved` (event `OrganizationApproved`), with `nextReviewAt` per risk rating.

# Business Rules
- An organization with an unresolved PotentialMatch cannot be approved.
- A ConfirmedMatch → mandatory rejection + escalation note (LQ-33 for further steps).
- BO declaration ≠ register snapshot → `NeedsInfo`.
- The decision guard is extensible: task 007a adds "seller claimed by SEF key + pristupnica accepted", task 009 adds "Verified payout account" (compliance.md §2).

# Technical Requirements
Expose `Modules.Compliance.Contracts`: `IHoldQuery`, `IKybQuery`, `IScreeningService` (also used for debtors in 011).

# API Changes
Customer: `GET /organizations/{id}/kyb`, `POST /organizations/{id}/kyb/beneficial-owners`. Admin: `GET /admin/kyb-cases`, `GET /admin/kyb-cases/{id}`, `POST /admin/kyb-cases/{id}/decision` 🔐, `POST /admin/kyb-cases/{id}/screening/{resultId}/resolution`, `POST /admin/holds`, `POST /admin/holds/{id}/lift` 🔐.

# Database Changes
`compliance.kyb_cases`, `beneficial_owners`, `screening_lists`, `screening_list_entries`, `screening_results`, `holds`.

# Security Considerations
PII access is limited to ComplianceOfficer/Auditor; Support sees masked data. All decisions audited with the reviewer(s). Personal data minimisation: no ID document images unless required (LQ-30).

# Tests
Four-eyes enforced (same officer twice → 403); transliteration match ("Petrović" vs "Петровић"); approval blocked by an unresolved hit; hold blocks a sample guarded action; cross-tenant KYB access → 404.

# Acceptance Criteria
- [ ] End-to-end: submit org → screening → decision → org Approved.
- [ ] High-risk path requires two officers.
- [ ] List import job idempotent and audited.

# Out of Scope
External screening provider, automated APR/BO retrieval, ongoing re-screening automation (020 adds the daily re-screen of approved orgs only if trivial; otherwise a future task).
