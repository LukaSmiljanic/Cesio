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
| Offer expiry | every 5 min | Active offers past `validUntil` → Expired; a reminder to the factor when its offer expires within 24 h (product-spec §8 "offer expiring") |
| Signature deadline | every 15 min | Deals not Signed past `signatureDeadline` → Cancelled (`SignatureDeadlineMissed`); receivable → Verified; a reminder to both parties when the deadline is within 24 h (product-spec §8) |
| Maturity ineligibility | daily (an interval from the worker's start; a fixed 00:30 Europe/Belgrade time is a follow-up) | Verified/Listed receivables with maturity − today < `minDaysAtListing` → Ineligible (listing → Withdrawn by system) |
| SEF monitoring | daily + on demand | Re-poll SEF status for receivables in Verified, Listed, Reserved, Assigned, Funded (until Closed). On cancel/storno/credit note: before `Assigned` → Ineligible (+ listing withdrawn / deal cancelled); at or after `Assigned` → Hold `FraudInvestigation` on the deal + alert to both parties and compliance |
| Pending verification retry | every 15 min | Re-run SEF-dependent checks for receivables stuck in PendingVerification |
| Authorization expiry | daily | Expiry done in task 008 (`FactorAuthorizationExpiryJob`, now the job `compliance-factor-authorization-expiry`). Done here: reminders to the factor 14 and 7 days (factoring company) or 7 and 2 days (bank) before `reverifyBy` (product-spec §8), the compliance re-check alert 14 days (factoring company) or 7 days (bank) before (compliance.md §4), and the consumers of `FactorAuthorizationRevoked/Expired` (the factor's Active offers expire, its deals before Signed get a hold) |
| Funding follow-up | daily | Deals `FundingReported` > N business days without confirmation → Operations alert; `Signed` past funding deadline → alert both parties |
| Outcome overdue | daily | `Funded` deals past maturity + 30 days without outcome → reminder to factor |
| KYB periodic review | daily | Orgs past `nextReviewAt` → review case opened |
| Sanctions re-screen | daily after list import | Re-screen approved orgs, BOs and debtors with active deals; new hits → compliance alert + Hold for confirmed matches |

Notifications (task 019): a job that finds a reminder or a SEF status change publishes `OrganizationNotificationRequested` (type `reminder` or `sef_status_changed`); task 019 delivers it.

# Business Rules
Every system-initiated transition is audited with `actor_type = System` and a reason code.

Implementation notes (2026-10-24, simplest options): modules register their jobs (`AddJob(name, interval, work)`) in every host; the worker runs each on its interval, `JobRunner` takes a session application lock per job (`job:{name}`, a second instance skips) and records every run in `platform.job_runs` (status, items, exception type); `POST /admin/jobs/{name}/run` runs a job in the request (audit actor = the Operations user). Job names: `marketplace-listing-expiry`, `marketplace-offer-expiry` (5 min), `deals-signature-deadline`, `receivables-pending-verification-retry` (15 min), `receivables-maturity-ineligibility`, `receivables-sef-monitoring`, `deals-funding-follow-up` (`deals.fundingConfirmationAlertBusinessDays`, 5), `deals-outcome-overdue`, `compliance-factor-authorization-expiry`, `compliance-factor-authorization-reminders`, `compliance-kyb-periodic-review`, `compliance-sanctions-rescreen`, `orgs-draft-expiry`, `documents-orphan-blob-cleanup` (daily), `notifications-email` (1 min), `notifications-digest` (hourly). A storno before assignment: a Verified or Listed receivable becomes Ineligible (`ReceivableBecameIneligible` → Marketplace withdraws its listing); a Reserved one is left to Deals, which cancels the deal (`sef_status_withdrawn`) and then marks the receivable Ineligible; at or after assignment the deal gets a FraudInvestigation hold and a compliance alert, the receivable keeps its state. `FactorAuthorizationRevoked/Expired` expire the factor's Active offers and put a ComplianceHold on its deals before Signed. Reminders are deduplicated by a key per subject and threshold. Operations and compliance alerts are audit events (`Operations.Alert`, `Compliance.Alert`) until an alert queue exists. A periodic KYB review opens a case while the organization stays Approved; approving it does not move the organization again. "Daily 00:30 Europe/Belgrade" is an interval of one day from the worker's start. Not built: the daily download and XML parsing of the UN, EU and OFAC lists (the lists are still imported as CSV by an officer, task 007); re-screening of debtors with active deals; the "monitoring unavailable" flag (a stale SEF status already blocks listing and acceptance, INV-R4); a fake `TimeProvider` for every job (the tests shift the clock for the services or the timestamps).

# Technical Requirements
Jobs are idempotent and process in batches of ≤ 100 with per-item transactions; failures on one item do not stop the batch.

# API Changes
Admin `GET /admin/jobs` (status), `POST /admin/jobs/{name}/run` 🔐 (Operations, PlatformAdmin).

# Database Changes
`platform.job_runs`.

# Security Considerations
SEF calls use each org's key; a missing or invalid key → org alert (the key is marked Invalid and the security recipients are told); the "monitoring unavailable" flag is deferred, because a stale SEF status already blocks listing and acceptance (INV-R4 freshness).

# Tests
Time-travel tests (a shifted clock for the job's services, or timestamps moved back; a fake `TimeProvider` for every job is deferred); two worker instances → each job runs once; SEF storno after Assigned → Hold, not state rollback.

# Acceptance Criteria
- [ ] All jobs implemented with tests and metrics (all but the XML list import and the debtor re-screen; see the notes).
- [x] Admin can view job status and trigger runs.

# Follow-ups from the phase 2–3 review (2026-10-24)
- Delete quarantined blobs whose document row was rolled back (an upload whose request ended in 5xx leaves an orphan blob). Done: `documents-orphan-blob-cleanup`.
- When SEF shows a listed receivable as no longer approved (re-check job or acceptance), close or suspend its Open listing and supersede its offers; today only the receivable becomes Ineligible. Done for the jobs: `ReceivableBecameIneligible` withdraws the listing.
- The daily sanctions-list import (UN and EU XML, OFAC SDN; APML as CSV) of task 007 and the re-screening of approved organizations.
- Call `DealStepService.CancelIfSignatureDeadlinePassedAsync` for deals past their signature deadline. Done: `deals-signature-deadline`.

# Follow-ups from the phase 4 review (2026-10-24)
Fixed: the scheduler survives any failing job; manual runs execute in their own scope and are audited (`Job.RunTriggered`, `job_runs.triggered_by`); SEF monitoring samples randomly (an unreachable SEF keeps the old check time, which must not look fresh); the verification retry isolates items; the funding alert is written once per report on the first business day after the confirmation was due (Belgrade dates); the authorization reminder sends only the smallest reached threshold; the orphan cleanup walks every quarantined blob. Left open:
- The compliance re-check alert is written only on the threshold day (a day the job does not run is missed); an alert queue with dedupe would fix it.
- A system hold on a deal or receivable has no organization id in its audit event (the hold service does not know the parties).
- The pending SEF key stays in the secret store after its link expired (it is overwritten by the next request).

# Out of Scope
CEF monitoring (future).
