# 015 — Deal: Factor CDD Gate, Contract Upload, Terms Checklist

**Blocked by:** LQ-29 (KYB package sharing basis), ADR-018 confirmation. Partially LQ-61: which persons count as authorised signatories and what form a power of attorney needs.

# Goal
After acceptance, the factor performs its own CDD using a shared KYB package, then uploads its factoring agreement. Both parties confirm that the agreement matches the deal terms, which fixes the contract hash for signing.

# Context
[product-spec.md](../docs/product-spec.md) §6.5 steps 1–2, §7.4; ADR-015, ADR-018; REG-10.

# Requirements
1. `GET /deals/{id}/kyb-package` (factor of the deal): a ZIP generated on demand with the seller's KYB summary (JSON + PDF), APR snapshots, BO data, screening summary (not the raw hits), and receivable documents. Only if the seller's KYB-sharing consent exists. Download audited.
2. `POST /deals/{id}/cdd-decision` ♻🔐 (factor Signatory): `{ "decision": "Approved" | "Declined", "reason"? }`. Approved → `AwaitingContract`. Declined → deal `Cancelled`, receivable → `Verified` (the seller may re-list), offer stays Accepted (historical).
3. `POST /deals/{id}/contract` ♻ (any member of the factor; roles are post-MVP, security.md): upload a PDF (clean scan). May be replaced until the terms are confirmed by both (each replacement resets confirmations).
4. `POST /deals/{id}/terms-confirmation` ♻🔐 (each party's Signatory): checklist (parties PIB/names, invoice number & SEF ID, face value, purchase price, recourse type, funding deadline) displayed from the deal record; the party confirms that the uploaded document matches. When both confirm → store `agreed_contract_sha256` → `AwaitingSignatures`.
5. `POST /deals/{id}/cancel` ♻🔐: either party before `Signed`, with a reason code → `Cancelled`, receivable → `Verified`.
6. Signature deadline: if not `Signed` by `signatureDeadline` → the 020 job cancels (here: expose the domain method + test).
7. UI: deal page steps (CDD, contract, checklist, cancel), with clear "who needs to act" banners.

# Business Rules
- No contract upload before CDD Approved.
- Cancelling does not reopen the listing automatically.
- All transitions blocked while a hold is active on the deal, receivable, seller or factor.
- Implementation notes (2026-10-23, simplest options; phase-2 stubs per tasks/README): `GET /deals/{id}/kyb-package` answers 409 `kyb_consent_missing` without the seller's KYB-sharing consent; with it (task 007) it is a ZIP of the KYB summary (JSON: company, representatives, beneficial owners, risk rating, screening summary) and the receivable's documents, access logged and audited; the factor's Signatory comes from the development approval until task 008; "Operator+" for the contract upload is any member of the factor until roles exist; the contract is a clean PDF with purpose `Contract` and both parties may read every version (access policy of the Deals module); a replacement voids earlier confirmations because confirmations belong to one contract version; cancellation needs a Signatory, step-up and a reason code of letters; the signature deadline rule is `DealStepService.CancelIfSignatureDeadlinePassedAsync`, called by the job of task 020; watermarking of the KYB package waits for the package itself; ADR-018 is built as Proposed and is confirmed before production (task 000).

# Technical Requirements
Deal is the aggregate root; sub-records in `deals.contract_documents`, `deals.terms_confirmations`.

# API Changes
As listed.

# Database Changes
`deals.cdd_decisions`, `deals.contract_documents`, `deals.terms_confirmations`; `deals.deals.agreed_contract_sha256`.

# Security Considerations
KYB package contains personal data: watermark, audit, require consent, deny to staff Support role. 404 for non-parties.

# Tests
State guards per step; confirmations reset on contract replacement; decline path returns the receivable to Verified; hold blocks; KYB package without consent → 409.

# Acceptance Criteria
- [x] Deal reaches `AwaitingSignatures` only with both confirmations on the same document hash.
- [x] All steps audited with actors.

# Out of Scope
Signature validation (016), platform contract templates.
