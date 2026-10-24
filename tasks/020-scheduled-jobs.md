# 020 — Scheduled Jobs: Expiry & SEF Monitoring

# Goal
Time-based transitions and external-status monitoring run reliably, once per cluster, and are audited.

# Context
[database.md](../docs/database.md) §4 (application locks); INV-R4, INV-A2; [integrations.md](../docs/integrations.md) §1.

# Requirements
Jobs in `Platform.Worker`, each guarded by an application lock (`sp_getapplock`), with metrics (last run, items processed, failures):

| Job | Schedule | Action |
|-----|----------|--------|
| Listing expiry | every 5 min | Open listings past `closesAt` → Expired; receivable → Verified; Active offers → Superseded |
| Offer expiry | every 5 min | Active offers past `validUntil` → Expired |
| Signature deadline | every 15 min | Deals not Signed past `signatureDeadline` → Cancelled (`SignatureDeadlineMissed`); receivable → Verified |
| Maturity ineligibility | daily 00:30 Europe/Belgrade | Verified/Listed receivables with maturity − today < `minDaysAtListing` → Ineligible (listing → Withdrawn by system) |
| SEF monitoring | daily + on demand | Re-poll SEF status for receivables in Verified, Listed, Reserved, Assigned, Funded (until Closed). On cancel/storno/credit note: before `Assigned` → Ineligible (+ listing withdrawn / deal cancelled); at or after `Assigned` → Hold `FraudInvestigation` on the deal + alert to both parties and compliance |
| Pending verification retry | every 15 min | Re-run SEF-dependent checks for receivables stuck in PendingVerification |
| Authorization expiry | daily | `reverifyBy` passed → Expired + events (13/14 consumers act); 14-day reminder events |
| Funding follow-up | daily | Deals `FundingReported` > N business days without confirmation → Operations alert; `Signed` past funding deadline → alert both parties |
| Outcome overdue | daily | `Funded` deals past maturity + 30 days without outcome → reminder to factor |
| KYB periodic review | daily | Orgs past `nextReviewAt` → review case opened |
| Sanctions re-screen | daily after list import | Re-screen approved orgs, BOs and debtors with active deals; new hits → compliance alert + Hold for confirmed matches |

# Business Rules
Every system-initiated transition is audited with `actor_type = System` and a reason code.

# Technical Requirements
Jobs are idempotent and process in batches of ≤ 100 with per-item transactions; failures on one item do not stop the batch.

# API Changes
Admin `GET /admin/jobs` (status), `POST /admin/jobs/{name}/run` 🔐 (Operations).

# Database Changes
`platform.job_runs`.

# Security Considerations
SEF calls use each org's key; a missing or invalid key → org alert, and the receivables are flagged "monitoring unavailable" (blocks listing/acceptance via INV-R4 freshness).

# Tests
Time-travel tests with a fake `TimeProvider` for each job; two worker instances → each job runs once; SEF storno after Assigned → Hold, not state rollback.

# Acceptance Criteria
- [ ] All jobs implemented with tests and metrics.
- [ ] Admin can view job status and trigger runs.

# Follow-ups from the phase 2–3 review (2026-10-24)
- Delete quarantined blobs whose document row was rolled back (an upload whose request ended in 5xx leaves an orphan blob).
- When SEF shows a listed receivable as no longer approved (re-check job or acceptance), close or suspend its Open listing and supersede its offers; today only the receivable becomes Ineligible.
- The daily sanctions-list import (UN and EU XML, OFAC SDN; APML as CSV) of task 007 and the re-screening of approved organizations.
- Call `DealStepService.CancelIfSignatureDeadlinePassedAsync` for deals past their signature deadline.

# Out of Scope
CEF monitoring (future).
