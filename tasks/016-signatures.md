# 016 — Signature Validation & Assignment

**Blocked by:** LQ-24 (required signature level), LQ-75 (signer matching) and ADR-011 (validation approach). Start with a spike to choose the library/service.

# Goal
The platform validates the qualified electronic signatures on the agreed factoring agreement and, when both parties' authorised signatories have validly signed, marks the deal `Signed` and the receivable `Assigned`.

# Context
[api.md](../docs/api.md) §3.7; [integrations.md](../docs/integrations.md) §7; REG-07; INV-R6.

# Requirements
1. **Spike (documented as ADR-011):** evaluate PAdES validation options for .NET (e.g. iText 9 + BouncyCastle, or the EU DSS validation service in a sidecar container) against Serbian QES samples: smart-card and cloud signatures from at least 2 Serbian qualified trust service providers. Criteria: chain to a trusted root, OCSP/CRL, LTV, signing time, licence cost.
2. Trust store: the Serbian qualified trust service providers' root/intermediate certificates, configurable and staff-maintained (source: the official register; see integrations.md §7).
3. `POST /deals/{id}/signed-contract` ♻🔐 per api.md §3.7. Validation per signature: integrity; the signed revision covers bytes identical to the agreed contract hash (incremental updates allowed; the original revision's hash must equal `agreed_contract_sha256`); certificate qualified, chain valid, not revoked at signing time; signer matched to a legal representative or verified attorney of the seller or factor by the rule of LQ-75 (until answered: by name only, see the implementation notes) (ADR-020: the seller account may be shared, so the signer is checked as a person, not as an account).
4. Store `deals.signatures` rows with the validation report (JSON) and the certificate details.
5. When both parties are valid, and the SEF status is fresh ≤ 1 h and still approved, and there is no hold → deal `Signed`, receivable `Assigned` (single transaction).
6. If the deadline has passed → 409 `deadline_passed`.
7. UI: upload signed PDF; per-signature validation result display.

# Business Rules
- At least one valid signature from an authorized person (representative or attorney) of each party is required (LQ-61, LQ-24 open).
- The platform does not sign anything itself.
- Implementation notes (2026-10-23, simplest options): ADR-011 is written as Proposed; the spike needs QES samples from two Serbian QTSPs, so validation runs behind `ISignatureValidator` with a fake validator in development (`Signatures:Validator=Fake`, local environments only) and an "unavailable" validator everywhere else, so production cannot sign until a real validator exists; the signer is matched by name (case and spacing ignored) to the legal representatives valid today, without the personal identifier (whether name matching suffices and how to treat Cyrillic/Latin spellings is LQ-75, open and without a working assumption, so this matching runs in development only: the production validator answers "unavailable"); diacritics and scripts are not normalized, only case and spacing; verified attorneys come with task 007 (LQ-61); every signature is stored with its result and report, including failures (evidence); the trust store is kept by the `PlatformAdmin` role (a user claim granted in development until the staff realm); a changed SEF status at signing answers 409 `receivable_ineligible` and leaves the deal in AwaitingSignatures.

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
- [x] Deal becomes Signed only with valid signatures from both parties' authorised signatories.
- [x] The receivable is Assigned in the same transaction.

# Out of Scope
Integrated remote signing, notarisation.
