# 016 — Signature Validation & Assignment

**Blocked by:** LQ-24 (required signature level) and ADR-011 (validation approach). Start with a spike to choose the library/service.

# Goal
The platform validates the qualified electronic signatures on the agreed factoring agreement and, when both parties' authorised signatories have validly signed, marks the deal `Signed` and the receivable `Assigned`.

# Context
[api.md](../docs/api.md) §3.7; [integrations.md](../docs/integrations.md) §7; REG-07; INV-R6.

# Requirements
1. **Spike (documented as ADR-011):** evaluate PAdES validation options for .NET (e.g. iText 9 + BouncyCastle, or the EU DSS validation service in a sidecar container) against Serbian QES samples: smart-card and cloud signatures from at least 2 Serbian qualified trust service providers. Criteria: chain to a trusted root, OCSP/CRL, LTV, signing time, licence cost.
2. Trust store: the Serbian qualified trust service providers' root/intermediate certificates, configurable and staff-maintained (source: the official register; see integrations.md §7).
3. `POST /deals/{id}/signed-contract` ♻🔐 per api.md §3.7. Validation per signature: integrity; the signed revision covers bytes identical to the agreed contract hash (incremental updates allowed; the original revision's hash must equal `agreed_contract_sha256`); certificate qualified, chain valid, not revoked at signing time; signer name (and personal identifier if present in the certificate) matched to a verified legal representative or attorney of the seller or factor (ADR-020: the seller account may be shared, so the signer is checked as a person, not as an account).
4. Store `deals.signatures` rows with the validation report (JSON) and the certificate details.
5. When both parties are valid, and the SEF status is fresh ≤ 1 h and still approved, and there is no hold → deal `Signed`, receivable `Assigned` (single transaction).
6. If the deadline has passed → 409 `deadline_passed`.
7. UI: upload signed PDF; per-signature validation result display.

# Business Rules
- At least one valid signature from an authorized person (representative or attorney) of each party is required.
- The platform does not sign anything itself.

# Technical Requirements
The validation component runs behind `ISignatureValidator`, with a fake implementation for tests. Revocation calls have a timeout; unavailability → fail closed (retry later).

# API Changes
`POST /deals/{id}/signed-contract`.

# Database Changes
`deals.signatures`; trust store table `deals.trusted_certificates` (staff-managed, audited).

# Security Considerations
Uploaded PDFs are scanned (006). Validation reports are stored for evidence. Audit each validation result.

# Tests
Fixtures: valid both parties; one party only; tampered content; signer not a representative or attorney; expired/revoked certificate; non-qualified certificate; hash of a different contract version; deadline passed.

# Acceptance Criteria
- [ ] ADR-011 written and accepted.
- [ ] Deal becomes Signed only with valid signatures from both parties' authorised signatories.
- [ ] The receivable is Assigned in the same transaction.

# Out of Scope
Integrated remote signing, notarisation.
