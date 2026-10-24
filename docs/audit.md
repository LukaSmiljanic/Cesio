# Audit Model

Goal: for any receivable, deal, organization or fee, we can reconstruct **who did what, when, from where, in which state, and why**, and prove that the record has not been altered.

## 1. Principles
1. **Write in the same DB transaction** as the business change. If the audit write fails, the business change fails.
2. **Append-only.** The application DB role has `INSERT, SELECT` only on `audit.*`. Updates and deletes are denied, and an INSTEAD OF trigger raises on any attempt (EF mapping declares it with `HasTrigger`).
3. **Tamper-evident.** Each event stores `hash = SHA-256(prev_hash || canonical_json(event))` in a single chain (serialised by an application lock, `sp_getapplock` with `@LockOwner = 'Transaction'`, so the lock is held until the event commits; volume is low). A daily job verifies the chain and writes a signed daily digest to **immutable (WORM) Azure Blob storage** with a time-based retention policy.
4. **Business intent, not only row diffs.** Events are named actions (`OfferAccepted`), not just `UPDATE deals`.
5. **Financial records are never deleted.** Corrections are new records (reversals, compensating events).

## 2. Event schema
| Field | Notes |
|-------|-------|
| `id` | sequential GUID (ADR-021) |
| `sequence` | bigint, gap-free per chain |
| `occurred_at` | datetimeoffset (server clock, UTC) |
| `actor_type` | `User | PlatformStaff | System | Integration` |
| `actor_user_id` | nullable for System |
| `actor_org_id` | organization context of the actor (nullable for staff/system) |
| `on_behalf_of_org_id` | affected organization(s) |
| `action` | e.g. `Receivable.Submitted`, `Offer.Accepted` |
| `entity_type`, `entity_id` | primary subject |
| `related` | JSON array of `{type,id}` (e.g. the deal, listing, offer) |
| `previous_state`, `new_state` | for lifecycle entities |
| `changes` | JSON field-level diff for configured fields (amounts, bank account, roles); sensitive values are masked |
| `reason` | free text / reason code where the action needs one |
| `correlation_id` | request trace ID |
| `idempotency_key` | for commands |
| `ip_address`, `user_agent`, `device_id` | for user actions, subject to LQ-38; truncated or hashed where required |
| `mfa_step_up_at` | for step-up actions |
| `evidence_document_ids` | linked documents (hashes are in the documents table) |
| `prev_hash`, `hash` | chain |

## 3. Mandatory audited actions (minimum)
- **Identity:** login success/failure, MFA enrolment/change, password reset, session revoke, lockout.
- **Organizations:** created, submitted, approved/rejected/suspended, claim created/won/closed (`Organization.ClaimWon`, `Organization.ClaimClosed`), compliance alert on a second valid SEF key for a claimed PIB (`Compliance.Alert`), pristupnica generated/uploaded/email-confirmed/accepted/rejected, confirmation sent/used/expired (`Confirmation.*`), official email recorded, member invited/added/removed, Signatory flag changed, MFA reset requested/approved/executed (`User.MfaReset*`), SEF key change requested, bank account added/verified/changed, SEF key set/rotated/invalidated, terms accepted.
- **Compliance:** KYB case opened/decided, screening run/result/resolution, factor authorization submitted/verified (both approvers)/expired/revoked, hold placed/lifted, risk rating changed.
- **Receivables:** imported (with UBL hash), declaration accepted, submitted, every verification check, verified/rejected (reviewer), became ineligible, SEF status change observed, cancelled.
- **Marketplace:** listing opened/withdrawn/expired/suspended/closed; listing viewed and document downloaded by a factor (access log); offer submitted/withdrawn/rejected/expired/superseded/accepted.
- **Deals:** every state transition; CDD confirmation; contract uploaded (hash); terms checklist confirmed; signature validation results; CEF registration recorded; debtor notification recorded; funding reported; funding confirmed or disputed; outcome recorded; cancellation/termination with reason.
- **Fees:** schedule version created and approved (four-eyes), charge created/invoiced/paid, reversal proposed and approved (four-eyes), fee export downloaded.
- **Platform:** business parameter changes, signature trust store certificate added/deactivated (`TrustStore.CertificateAdded`, `TrustStore.CertificateDeactivated`), admin role grants, impersonation (**not allowed**; listed so that an attempt is logged), data exports.

## 4. Storage
```sql
CREATE TABLE audit.audit_events (
  id uniqueidentifier PRIMARY KEY,
  sequence bigint NOT NULL UNIQUE,
  occurred_at datetimeoffset(3) NOT NULL,
  actor_type nvarchar(200) NOT NULL, actor_user_id uniqueidentifier, actor_org_id uniqueidentifier,
  on_behalf_of_org_id uniqueidentifier,
  action nvarchar(200) NOT NULL, entity_type nvarchar(200) NOT NULL, entity_id uniqueidentifier NOT NULL,
  related nvarchar(max), previous_state nvarchar(200), new_state nvarchar(200), changes nvarchar(max), reason nvarchar(max),
  correlation_id nvarchar(200) NOT NULL, idempotency_key nvarchar(200),
  ip_address varchar(45), user_agent nvarchar(max), mfa_step_up_at datetimeoffset(3),
  evidence_document_ids nvarchar(max),
  prev_hash char(64) NOT NULL, hash char(64) NOT NULL
);
CREATE INDEX ix_audit_entity ON audit.audit_events(entity_type, entity_id, occurred_at);
CREATE INDEX ix_audit_org ON audit.audit_events(on_behalf_of_org_id, occurred_at);
-- grants: app_rw INSERT, SELECT only (DENY UPDATE, DELETE); INSTEAD OF UPDATE/DELETE trigger raises an error;
-- TRUNCATE needs ALTER permission, which app_rw does not have
-- related, changes, evidence_document_ids: JSON with CHECK (ISJSON(...) = 1)
CREATE TABLE audit.access_log (...)  -- high-volume reads (listing views, downloads), same append-only rules, no hash chain
```
Partition by month (SQL Server table partitioning) when volume requires it. Retention ≥ 10 years pending LQ-47. Archive older partitions to WORM Blob (Parquet/JSONL with digests) rather than delete.

## 5. Access to audit data
- Organizations see the audit history of **their own** entities (with staff identities shown as "Platform compliance").
- `Auditor` and `ComplianceOfficer` see everything; exports are themselves audited.
- An audit extract per deal (PDF/JSON bundle with hashes) is available on request and supports dispute handling.

## 6. Tests
- Every command handler test asserts the expected audit event (action, entity, states).
- An integration test verifies that UPDATE/DELETE on `audit_events` fails with the app role.
- A chain verification test detects a modified row.
