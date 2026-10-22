# 000 — Regulatory & Commercial Validation

**Owner:** founders + Serbian fintech law firm + tax advisor. **Not a coding task.** It runs in parallel with tasks 001–006 (foundation work that does not depend on legal answers). It **blocks** tasks 010+ wherever marked, and it blocks production.

# Goal
Get written professional answers to all P0 questions in [docs/legal-questions.md](../docs/legal-questions.md), confirm or correct [docs/regulatory.md](../docs/regulatory.md), and validate pricing with factors.

# Context
Research as of 2026-10-05: the Law on Factoring was amended by "Sl. glasnik RS" 109/2025. Supervision of factoring companies moved to the Securities Commission, and a Central Factoring Register (CEF) was mandated (not yet operational). The platform model (no fund handling, no purchasing) is designed to avoid licensing, but this is unconfirmed.

# Requirements
1. Engage a Serbian law firm with fintech/financial-regulatory experience. Send it `legal-questions.md` (in Serbian: all questions by app segment, with the requested deliverables), plus `regulatory.md`, `product-spec.md` §5–7 and `business-model.md` §2.
2. Obtain a written memo answering every P0 question, with citations.
3. Obtain a tax opinion on all *(porez)* questions in `legal-questions.md` §3.3 (LQ-43 to LQ-46 and LQ-69). P0 items come first; LQ-46 and LQ-69 may be deferred.
4. Read in full and summarise into `regulatory.md` the laws listed as "identified but not yet read" in regulatory.md §1.
5. Confirm with the Securities Commission / Ministry of Finance (formal inquiry if needed): whether an entry in the APR Registar faktoringa (or an APR certificate) is sufficient evidence of an approval, and how quickly new Commission approvals are recorded there (LQ-09); the CEF timeline and planned API and intermediary access (LQ-18).
6. Confirm SEF API key third-party usage terms (LQ-21), possibly via a Ministry of Finance inquiry.
7. Interview ≥ 5 factors (including ≥ 1 bank) about: fee acceptance (BQ-01), CDD reliance (LQ-29), contract template (BQ-09), required data/documents, RSD vs. EUR (BQ-02).
8. Interview ≥ 10 target sellers about: SEF usage, typical invoice sizes and tenors, willingness to share an API key, confidentiality concerns (BQ-06).
9. Draft (by the lawyers), as listed in `legal-questions.md` §1:
   - Terms of Use (sellers and factors);
   - in the Terms for factors: confidentiality and the fee obligation (no separate agreement with factors, ADR-022, LQ-72);
   - seller pristupnica;
   - seller declaration texts;
   - Privacy Notice, records of processing and DPIA input;
   - consent to KYB sharing and to the use of the SEF API key;
   - notice to factors on the off-platform double-assignment risk (LQ-20).

# Business Rules
- No P0 item may be closed by assumption. Each answer needs a source and a named professional.
- If an answer contradicts an Accepted ADR, open a superseding ADR before any code changes.

# Technical Requirements
None. Outputs are documentation updates.

# API Changes
None directly. Answers may change [docs/api.md](../docs/api.md) (e.g. the signature step).

# Database Changes
None directly.

# Security Considerations
Share the docs with external counsel under an NDA. Do not share any real customer data.

# Tests
Not applicable. Review checklist: every LQ marked P0 has answer, source, lawyer and date.

# Acceptance Criteria
- [ ] All P0 legal questions answered in writing and recorded in `legal-questions.md`.
- [ ] `regulatory.md` updated: confidence levels revised, new requirements added (REG-xx), wrong items corrected.
- [ ] `decisions.md`: ADR-004, 007, 009, 015, 016, 018, 020 confirmed or superseded.
- [ ] All legal texts from requirement 9 drafted and versioned in `docs/legal-texts/`.
- [ ] BQ-01 to BQ-13 answered or explicitly deferred with an owner.
- [ ] Go/no-go decision recorded for the MVP model.

# Out of Scope
Licence applications (only if counsel says one is needed; that would be a new task), CEF integration build, marketing.
