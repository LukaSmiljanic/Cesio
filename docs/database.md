# Database Design (SQL Server)

Target: Microsoft SQL Server 2022+ compatible, hosted as **Azure SQL Database** (ADR-021). Local development and tests use the SQL Server Developer edition in Docker. Access via EF Core (`Microsoft.EntityFrameworkCore.SqlServer`), with migrations kept per module.

## 1. Conventions
- **Schemas per module:** `auth` (identity; `identity` is a reserved word in T-SQL), `orgs`, `compliance`, `receivables`, `marketplace`, `deals`, `fees`, `documents`, `notifications`, `audit`, `platform` (config, outbox, processed events, idempotency). A module never writes another module's schema; cross-schema foreign keys are allowed **only** to stable reference rows (organization, user, receivable IDs) to protect integrity.
- **Keys:** `uniqueidentifier` primary keys generated in the application as **sequential GUIDs in SQL Server byte order** (e.g. EF Core's sequential GUID generator). A plain UUIDv7 is *not* ordered the way SQL Server sorts `uniqueidentifier` and would fragment the clustered index (ADR-021). Human-readable references (`DL-2026-000123`) come from per-type sequences for support and documents.
- **Money:** `decimal(19,4)` + `char(3)` currency column next to **every** amount, with a `CHECK (currency IN ('RSD','EUR'))` constraint. No `float`/`real`/`money`/`smallmoney` types anywhere (a CI check greps migrations).
- **Time:** `datetimeoffset(3)` (stored in UTC) for instants; `date` for business dates (issue, maturity, value date).
- **Standard columns:** `created_at`, `created_by`, `updated_at`, `updated_by`, `version` (integer, optimistic concurrency, incremented by EF).
- **Strings and enums:** `nvarchar(n)`; enums as `varchar(40)`/`nvarchar(200)` with `CHECK` constraints. Never the deprecated `text`/`ntext` types. Lengths in this document are indicative; migrations fix them. JSON is stored as `nvarchar(max)` with `CHECK (ISJSON(col) = 1)`.
- **Naming:** snake_case table and column names (kept for consistency with the docs); avoid T-SQL reserved words as names (`key` → `idem_key`/`param_key`).
- **Filtered unique indexes** replace PostgreSQL partial indexes. Their predicate allows only simple comparisons joined by `AND` (no `NOT IN`, no `OR`), and they need the standard ANSI `SET` options (EF Core's defaults). A unique index treats `NULL`s as equal, so nullable unique columns are filtered with `IS NOT NULL`.
- **Deletion:** no hard deletes of business or financial rows. Soft delete (`deleted_at`) **only** for drafts, notification preferences and unsubmitted documents. Immutability (ADR-021):
  - **Fully append-only tables** (`audit.*`, `deals.deal_state_history`, `deals.signatures`, `deals.contract_documents`, `deals.funding_reports`, `deals.funding_confirmations`, `receivables.verification_checks`, `receivables.seller_declarations`, `receivables.sef_status_history`, `deals.cdd_decisions`, `deals.terms_confirmations`, `deals.debtor_notifications`, `deals.outcomes`): `DENY UPDATE, DELETE` to `app_rw` + an INSTEAD OF UPDATE, DELETE trigger that throws.
  - **Tables whose state changes but some columns are fixed** (`marketplace.offers`: price, recourse, terms and the fee snapshot `fee_schedule_version`, `platform_fee`, `seller_fee`, `vat_pending`; only `state` and the once-written `rejection_reason` change; `deals.deals`: parties, amounts, recourse, terms, fee snapshot and acceptance; `fees.fee_charges`: amounts, payer): column-level `DENY UPDATE (col, …)` to `app_rw` + an AFTER UPDATE trigger with `IF UPDATE(col) THROW`.
  - **Staff-maintained reference data** (`deals.trusted_certificates`): rows are never deleted; only `deactivated_at`, `deactivated_by` and `version` change (deactivation, audited).
  - **Versioned configuration** (`fees.fee_schedules`): an AFTER UPDATE trigger allows only the one-time approval (`approved_by`, `approved_at`) and an INSTEAD OF DELETE trigger throws. The seed version is created by the system user (all zeros) and approved by a distinct system approver ID, so the four-eyes CHECK holds for it too.
  - **EF Core:** every table with a trigger is declared with `ToTable(t => t.HasTrigger("…"))` in its mapping. Otherwise EF Core's `OUTPUT` clause fails on tables with triggers.

## 2. Core tables (abridged DDL)

```sql
-- orgs
CREATE TABLE orgs.organizations (
  id uniqueidentifier PRIMARY KEY,
  org_type nvarchar(200) NOT NULL CHECK (org_type IN ('Seller','Factor')),
  legal_name nvarchar(200) NOT NULL,
  pib char(9) NOT NULL,
  mb  char(8),                       -- known after the APR prefill
  claimed_at datetimeoffset(3),            -- set by a valid SEF key (sellers) or at invitation (factors), ADR-020
  official_email nvarchar(200),               -- from APR, recorded by compliance
  onboarding_status nvarchar(200) NOT NULL,
  risk_rating nvarchar(200),
  created_at datetimeoffset(3) NOT NULL, version int NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX ux_org_pib_claimed ON orgs.organizations(pib) WHERE claimed_at IS NOT NULL;
CREATE UNIQUE INDEX ux_org_mb_claimed  ON orgs.organizations(mb)  WHERE claimed_at IS NOT NULL AND mb IS NOT NULL;
CREATE TABLE orgs.memberships (
  id uniqueidentifier PRIMARY KEY,
  org_id uniqueidentifier NOT NULL REFERENCES orgs.organizations(id),
  user_id uniqueidentifier NOT NULL REFERENCES auth.users(id),
  role varchar(40) NOT NULL DEFAULT 'Owner',  -- MVP: Owner only (ADR-020)
  is_signatory bit NOT NULL DEFAULT 0,
  status nvarchar(200) NOT NULL,
  UNIQUE (org_id, user_id)
);
CREATE TABLE orgs.bank_accounts (
  id uniqueidentifier PRIMARY KEY,
  org_id uniqueidentifier NOT NULL REFERENCES orgs.organizations(id),
  account_number char(18) NOT NULL,
  currency char(3) NOT NULL,
  verification_status nvarchar(200) NOT NULL,
  evidence_document_id uniqueidentifier,
  UNIQUE (org_id, account_number, currency)
);

CREATE TABLE orgs.pristupnice (
  id uniqueidentifier PRIMARY KEY, org_id uniqueidentifier NOT NULL REFERENCES orgs.organizations(id),
  generated_document_id uniqueidentifier NOT NULL, generated_sha256 char(64) NOT NULL,
  signed_document_id uniqueidentifier, method nvarchar(200),            -- Qes | ScannedConfirmedByEmail
  signer_name nvarchar(200), matched_representative_id uniqueidentifier, poa_document_id uniqueidentifier,
  status nvarchar(200) NOT NULL,                            -- Generated | Uploaded | EmailConfirmed | Accepted | Rejected
  decided_by uniqueidentifier, decided_at datetimeoffset(3), version int NOT NULL DEFAULT 0
);
CREATE TABLE orgs.confirmation_tokens (
  id uniqueidentifier PRIMARY KEY, org_id uniqueidentifier NOT NULL REFERENCES orgs.organizations(id),
  purpose nvarchar(200) NOT NULL,                           -- Pristupnica | PayoutAccountChange | SefKeyChange | MfaReset
  subject_id uniqueidentifier, token_hash char(64) NOT NULL UNIQUE,
  expires_at datetimeoffset(3) NOT NULL, used_at datetimeoffset(3)
);
-- auth.mfa_reset_requests (user_id, org_id, approved_by_kind Email|Owner, approved_at, executed_by, executed_at, status)

-- compliance
CREATE TABLE compliance.factor_authorizations (
  id uniqueidentifier PRIMARY KEY,
  org_id uniqueidentifier NOT NULL REFERENCES orgs.organizations(id),
  kind nvarchar(200) NOT NULL, issuing_authority nvarchar(200) NOT NULL,
  decision_number nvarchar(200), decision_date date,
  evidence_document_id uniqueidentifier, source_snapshot_document_id uniqueidentifier,
  official_source_url nvarchar(max),
  verification_method nvarchar(200),          -- OfficialRegisterLookup | AuthorityConfirmationLetter
  status nvarchar(200) NOT NULL, verified_by uniqueidentifier, verified_at datetimeoffset(3), reverify_by date,
  version int NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX ux_factor_auth_active ON compliance.factor_authorizations(org_id)
  WHERE status = 'Verified';
ALTER TABLE compliance.factor_authorizations ADD CONSTRAINT ck_factor_auth_decision
  CHECK (kind = 'Bank' OR (decision_number IS NOT NULL AND decision_date IS NOT NULL AND evidence_document_id IS NOT NULL));
CREATE TABLE compliance.holds (
  id uniqueidentifier PRIMARY KEY, subject_type nvarchar(200) NOT NULL, subject_id uniqueidentifier NOT NULL,
  kind nvarchar(200) NOT NULL, reason nvarchar(max) NOT NULL,
  placed_by uniqueidentifier NOT NULL, placed_at datetimeoffset(3) NOT NULL,
  lifted_by uniqueidentifier, lifted_at datetimeoffset(3)
);
CREATE INDEX ix_holds_active ON compliance.holds(subject_type, subject_id) WHERE lifted_at IS NULL;

-- receivables
CREATE TABLE receivables.debtors (
  id uniqueidentifier PRIMARY KEY, pib char(9) NOT NULL UNIQUE, mb char(8), legal_name nvarchar(200) NOT NULL,
  status_snapshot nvarchar(max) /* JSON, CHECK (ISJSON(...) = 1) */, updated_at datetimeoffset(3) NOT NULL
);
CREATE TABLE receivables.invoices (
  id uniqueidentifier PRIMARY KEY,
  seller_org_id uniqueidentifier NOT NULL REFERENCES orgs.organizations(id),
  debtor_id uniqueidentifier NOT NULL REFERENCES receivables.debtors(id),
  sef_invoice_id nvarchar(200) NOT NULL,
  invoice_number nvarchar(200) NOT NULL,
  issue_date date NOT NULL, due_date date NOT NULL,
  currency char(3) NOT NULL,
  net_amount decimal(19,4) NOT NULL, vat_amount decimal(19,4) NOT NULL, gross_amount decimal(19,4) NOT NULL,
  ubl_document_id uniqueidentifier NOT NULL, ubl_sha256 char(64) NOT NULL,
  sef_status nvarchar(200) NOT NULL, sef_status_checked_at datetimeoffset(3) NOT NULL,
  CHECK (gross_amount = net_amount + vat_amount),
  CHECK (due_date >= issue_date),
  UNIQUE (sef_invoice_id),
  UNIQUE (seller_org_id, invoice_number, issue_date)
);
CREATE TABLE receivables.receivables (
  id uniqueidentifier PRIMARY KEY,
  invoice_id uniqueidentifier NOT NULL REFERENCES receivables.invoices(id),
  seller_org_id uniqueidentifier NOT NULL, debtor_id uniqueidentifier NOT NULL,
  face_value decimal(19,4) NOT NULL CHECK (face_value > 0), currency char(3) NOT NULL,
  maturity_date date NOT NULL,
  state nvarchar(200) NOT NULL,
  fingerprint char(64) NOT NULL,
  version int NOT NULL DEFAULT 0
);
-- MVP: one receivable per invoice (full assignment). Excludes Rejected/Cancelled so a corrected resubmission is possible.
CREATE UNIQUE INDEX ux_receivable_per_invoice ON receivables.receivables(invoice_id)
  WHERE state <> 'Rejected' AND state <> 'Cancelled';
CREATE INDEX ix_receivable_fingerprint ON receivables.receivables(fingerprint);
CREATE TABLE receivables.verification_checks (... append-only ...);
CREATE TABLE receivables.seller_declarations (... append-only ...);
CREATE TABLE receivables.sef_status_history (
  id uniqueidentifier PRIMARY KEY, invoice_id uniqueidentifier NOT NULL, status nvarchar(200) NOT NULL,
  observed_at datetimeoffset(3) NOT NULL, raw nvarchar(max) /* JSON, CHECK (ISJSON(...) = 1) */ NOT NULL
);

-- marketplace
CREATE TABLE marketplace.listings (
  id uniqueidentifier PRIMARY KEY, receivable_id uniqueidentifier NOT NULL REFERENCES receivables.receivables(id),
  seller_org_id uniqueidentifier NOT NULL, state nvarchar(200) NOT NULL,
  opens_at datetimeoffset(3) NOT NULL, closes_at datetimeoffset(3) NOT NULL,
  audience_type nvarchar(200) NOT NULL, reserve_price decimal(19,4), currency char(3) NOT NULL,
  -- snapshots at opening (task 012): the feed filters and the related-party rule need no other schema
  face_value decimal(19,4) NOT NULL, maturity_date date NOT NULL, seller_pib char(9) NOT NULL, debtor_pib char(9) NOT NULL,
  suspended_reason nvarchar(400),
  version int NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX ux_listing_open_per_receivable ON marketplace.listings(receivable_id) WHERE state = 'Open';
CREATE TABLE marketplace.listing_audience (listing_id uniqueidentifier, factor_org_id uniqueidentifier, mode nvarchar(200) CHECK (mode IN ('Include','Exclude')),
  PRIMARY KEY (listing_id, factor_org_id));
CREATE TABLE marketplace.offers (
  id uniqueidentifier PRIMARY KEY, listing_id uniqueidentifier NOT NULL REFERENCES marketplace.listings(id),
  factor_org_id uniqueidentifier NOT NULL,
  purchase_price decimal(19,4) NOT NULL CHECK (purchase_price > 0), currency char(3) NOT NULL,
  recourse_type nvarchar(200) NOT NULL, funding_deadline_bdays int NOT NULL CHECK (funding_deadline_bdays BETWEEN 1 AND 30),
  valid_until datetimeoffset(3) NOT NULL, conditions_text nvarchar(1000),
  fee_schedule_version int NOT NULL, platform_fee decimal(19,4) NOT NULL, seller_fee decimal(19,4) NOT NULL /* the seller's part of platform_fee */, vat_pending bit NOT NULL,
  rejection_reason nvarchar(400),
  state nvarchar(200) NOT NULL, created_by uniqueidentifier NOT NULL, created_at datetimeoffset(3) NOT NULL,
  version int NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX ux_offer_active_per_factor ON marketplace.offers(listing_id, factor_org_id) WHERE state = 'Active';
CREATE UNIQUE INDEX ux_offer_accepted_per_listing ON marketplace.offers(listing_id) WHERE state = 'Accepted';

-- deals
CREATE TABLE deals.deals (
  id uniqueidentifier PRIMARY KEY, reference nvarchar(200) NOT NULL UNIQUE,
  receivable_id uniqueidentifier NOT NULL REFERENCES receivables.receivables(id),
  listing_id uniqueidentifier NOT NULL, offer_id uniqueidentifier NOT NULL UNIQUE REFERENCES marketplace.offers(id),
  seller_org_id uniqueidentifier NOT NULL, factor_org_id uniqueidentifier NOT NULL,
  face_value decimal(19,4) NOT NULL, purchase_price decimal(19,4) NOT NULL, currency char(3) NOT NULL,
  recourse_type nvarchar(200) NOT NULL, state nvarchar(200) NOT NULL,
  signature_deadline datetimeoffset(3) NOT NULL, funding_deadline date,
  -- terms and fee snapshot copied from the accepted offer (task 014); fixed by a trigger and a column DENY
  funding_deadline_bdays int NOT NULL, conditions_text nvarchar(1000), fee_schedule_version int NOT NULL,
  platform_fee decimal(19,4) NOT NULL, seller_fee decimal(19,4) NOT NULL, vat_pending bit NOT NULL,
  accepted_by uniqueidentifier NOT NULL, accepted_at datetimeoffset(3) NOT NULL, accepted_step_up_at datetimeoffset(3) NOT NULL,
  version int NOT NULL DEFAULT 0,
  CHECK (purchase_price < face_value)
);
-- INV-R1: the core anti-double-financing guarantee
CREATE UNIQUE INDEX ux_deal_live_per_receivable ON deals.deals(receivable_id)
  WHERE state <> 'Cancelled' AND state <> 'Terminated';
CREATE TABLE deals.deal_state_history (id uniqueidentifier PRIMARY KEY, deal_id uniqueidentifier NOT NULL, from_state nvarchar(200), to_state nvarchar(200) NOT NULL,
  actor_user_id uniqueidentifier, at datetimeoffset(3) NOT NULL, reason nvarchar(max));
-- task 015: append-only step records; deals.deals.agreed_contract_sha256 char(64) is set once, when both parties confirmed the same agreement
CREATE TABLE deals.cdd_decisions (id uniqueidentifier PRIMARY KEY, deal_id uniqueidentifier NOT NULL UNIQUE, approved bit NOT NULL, reason nvarchar(400),
  decided_by uniqueidentifier NOT NULL, decided_at datetimeoffset(3) NOT NULL, step_up_at datetimeoffset(3) NOT NULL);
CREATE TABLE deals.contract_documents (id uniqueidentifier PRIMARY KEY, deal_id uniqueidentifier NOT NULL, document_id uniqueidentifier NOT NULL,
  sha256 char(64) NOT NULL, uploaded_by uniqueidentifier NOT NULL, uploaded_at datetimeoffset(3) NOT NULL);
CREATE TABLE deals.terms_confirmations (id uniqueidentifier PRIMARY KEY, deal_id uniqueidentifier NOT NULL, contract_document_id uniqueidentifier NOT NULL,
  sha256 char(64) NOT NULL, party_org_id uniqueidentifier NOT NULL, confirmed_by uniqueidentifier NOT NULL, confirmed_at datetimeoffset(3) NOT NULL,
  step_up_at datetimeoffset(3) NOT NULL, UNIQUE (contract_document_id, party_org_id));
-- task 016: validated signatures (append-only evidence) and the staff-maintained trust store
CREATE TABLE deals.signatures (id uniqueidentifier PRIMARY KEY, deal_id uniqueidentifier NOT NULL, document_id uniqueidentifier NOT NULL, party varchar(10) /* Seller, Factor or NULL */,
  signer_name nvarchar(200) NOT NULL, certificate_subject nvarchar(400) NOT NULL, certificate_issuer nvarchar(400) NOT NULL, certificate_serial varchar(100) NOT NULL,
  signing_time datetimeoffset(3), result varchar(40) NOT NULL /* Valid or the first failed rule */, report nvarchar(max) /* JSON */ NOT NULL,
  uploaded_by uniqueidentifier NOT NULL, validated_at datetimeoffset(3) NOT NULL);
CREATE TABLE deals.trusted_certificates (id uniqueidentifier PRIMARY KEY, name nvarchar(200) NOT NULL, sha256_thumbprint char(64) NOT NULL UNIQUE,
  certificate nvarchar(max) NOT NULL, added_by uniqueidentifier NOT NULL, added_at datetimeoffset(3) NOT NULL, deactivated_at datetimeoffset(3), deactivated_by uniqueidentifier, version int NOT NULL);
-- task 017: evidence after signing; all append-only except the CEF record (status set once, Pending → Registered) and the dispute status
CREATE TABLE deals.cef_registrations (deal_id uniqueidentifier PRIMARY KEY, status varchar(20) NOT NULL /* NotApplicable, Pending, Registered */, note nvarchar(400),
  reference nvarchar(100), evidence_document_id uniqueidentifier, recorded_by uniqueidentifier, created_at datetimeoffset(3) NOT NULL, registered_at datetimeoffset(3), version int NOT NULL);
CREATE TABLE deals.debtor_notifications (id uniqueidentifier PRIMARY KEY, deal_id uniqueidentifier NOT NULL, channel varchar(20) NOT NULL, sent_at datetimeoffset(3) NOT NULL,
  evidence_document_id uniqueidentifier NOT NULL, recorded_by_org_id uniqueidentifier NOT NULL, recorded_by uniqueidentifier NOT NULL, recorded_at datetimeoffset(3) NOT NULL);
CREATE TABLE deals.funding_reports (id uniqueidentifier PRIMARY KEY, deal_id uniqueidentifier NOT NULL UNIQUE, amount decimal(19,4) NOT NULL, currency char(3) NOT NULL,
  value_date date NOT NULL, payment_reference nvarchar(100) NOT NULL, payee_bank_account_id uniqueidentifier NOT NULL, payee_account_number char(18) NOT NULL,
  reported_by uniqueidentifier NOT NULL, reported_at datetimeoffset(3) NOT NULL, step_up_at datetimeoffset(3) NOT NULL);
CREATE TABLE deals.funding_confirmations (id uniqueidentifier PRIMARY KEY, deal_id uniqueidentifier NOT NULL, received bit NOT NULL, reason nvarchar(400),
  confirmed_by uniqueidentifier NOT NULL, confirmed_at datetimeoffset(3) NOT NULL, step_up_at datetimeoffset(3) NOT NULL);
CREATE TABLE deals.outcomes (id uniqueidentifier PRIMARY KEY, deal_id uniqueidentifier NOT NULL UNIQUE, type varchar(20) NOT NULL, date date NOT NULL,
  amount_collected decimal(19,4), reported_by uniqueidentifier NOT NULL, reported_at datetimeoffset(3) NOT NULL);
-- minimal in task 017 (an Open dispute holds the deal); the workflow comes with task 021
CREATE TABLE deals.disputes (id uniqueidentifier PRIMARY KEY, deal_id uniqueidentifier NOT NULL, kind varchar(40) NOT NULL, status varchar(20) NOT NULL, reason nvarchar(1000),
  opened_by uniqueidentifier, opened_at datetimeoffset(3) NOT NULL, version int NOT NULL);

-- fees
CREATE TABLE fees.fee_schedules (version int PRIMARY KEY, definition nvarchar(max) /* JSON, CHECK (ISJSON(...) = 1) */ NOT NULL, effective_from datetimeoffset(3) NOT NULL,
  created_at datetimeoffset(3) NOT NULL, approved_by uniqueidentifier CHECK (approved_by <> created_by) /* four-eyes; set once */, approved_at datetimeoffset(3),
  created_by uniqueidentifier NOT NULL);
CREATE TABLE fees.fee_charges (
  id uniqueidentifier PRIMARY KEY, deal_id uniqueidentifier NOT NULL REFERENCES deals.deals(id),
  payer_org_id uniqueidentifier NOT NULL, kind nvarchar(200) NOT NULL CHECK (kind IN ('Charge','Reversal')),
  amount decimal(19,4) NOT NULL, vat_amount decimal(19,4) NOT NULL, currency char(3) NOT NULL,
  status nvarchar(200) NOT NULL, due_at date, external_invoice_ref nvarchar(200), reverses_id uniqueidentifier REFERENCES fees.fee_charges(id),
  created_at datetimeoffset(3) NOT NULL
);
CREATE UNIQUE INDEX ux_fee_charge_once ON fees.fee_charges(deal_id, payer_org_id) WHERE kind = 'Charge';

-- platform
CREATE TABLE platform.idempotency_keys (
  idem_key nvarchar(200) NOT NULL, org_id uniqueidentifier NOT NULL, user_id uniqueidentifier NOT NULL, endpoint nvarchar(200) NOT NULL,
  request_sha256 char(64) NOT NULL, response_status int, response_body nvarchar(max) /* JSON, CHECK (ISJSON(...) = 1) */,
  created_at datetimeoffset(3) NOT NULL, expires_at datetimeoffset(3) NOT NULL,
  PRIMARY KEY (org_id, endpoint, idem_key)  -- org_id = the active organization, or the user ID for commands outside one (api.md §1)
);
CREATE TABLE platform.outbox (id uniqueidentifier PRIMARY KEY, type nvarchar(200) NOT NULL, payload nvarchar(max) /* JSON, CHECK (ISJSON(...) = 1) */ NOT NULL, occurred_at datetimeoffset(3) NOT NULL,
  next_attempt_at datetimeoffset(3) NOT NULL /* backoff and claim lease */, processed_at datetimeoffset(3), dead_lettered_at datetimeoffset(3),
  attempts int NOT NULL DEFAULT 0, last_error nvarchar(max) /* exception type only, no message (security.md §8) */);
CREATE INDEX ix_outbox_pending ON platform.outbox(next_attempt_at) WHERE processed_at IS NULL AND dead_lettered_at IS NULL;
CREATE TABLE platform.processed_events (event_id uniqueidentifier, consumer nvarchar(200), processed_at datetimeoffset(3) NOT NULL,
  PRIMARY KEY (event_id, consumer)); -- written in the consumer's transaction, so redelivery does not repeat side effects
CREATE TABLE platform.business_parameters (param_key nvarchar(200), version int, value nvarchar(max) /* JSON value, scalars allowed: CHECK (ISJSON(value, VALUE) = 1) */ NOT NULL, effective_from datetimeoffset(3) NOT NULL,
  created_by uniqueidentifier NOT NULL, PRIMARY KEY (param_key, version));
```

Audit tables: see [audit.md](audit.md) §4.

## 3. Duplicate invoice detection (layered)
1. `UNIQUE (sef_invoice_id)` blocks the same SEF document twice, across all sellers.
2. `UNIQUE (seller_org_id, invoice_number, issue_date)` blocks the same seller re-entering a number.
3. **Fingerprint** across sellers (debtor PIB + gross + currency + issue date + due date) → manual review flag, not a block (legitimate identical invoices exist).
4. Verifying the seller as the SEF issuer (seller PIB = SEF supplier PIB) blocks invoices imported by a non-issuer.
5. Future: a CEF lookup blocks invoices assigned off-platform.

## 4. Concurrency & transaction boundaries
- **Optimistic concurrency** (`version`) on every aggregate. A conflict returns HTTP 409, and the client re-fetches.
- **Offer acceptance** = one transaction: `SELECT … WITH (UPDLOCK, ROWLOCK)` on the listing and receivable rows (pessimistic locking at the hottest point), update offer/others/listing/receivable, insert deal + outbox + audit. The filtered unique indexes are the last line of defence: even a code bug cannot create two live deals.
- Isolation level: `READ COMMITTED` with `READ_COMMITTED_SNAPSHOT ON` (the Azure SQL default: readers don't block writers) + explicit `UPDLOCK` row locks + unique indexes (we do not rely on `SERIALIZABLE` retries).
- **Outbox** rows are written in the same transaction as the state change. A background dispatcher claims batches with `WITH (UPDLOCK, READPAST, ROWLOCK)` and publishes them (at-least-once; consumers are idempotent by event ID).
- **Idempotency:** every financial or state-changing POST requires an `Idempotency-Key` header (except the sign-in, account-recovery and session endpoints `/auth/*`, `/me/sessions`, `/me/step-up-check`, which are rate limited instead; product owner 2026-10-22, api.md §1). The row is inserted within the same transaction as the command. Replay with the same key and body hash → stored response; same key with a different body → 422. Retention: 30 days.
- Scheduled jobs (expiry of listings and offers, SEF re-polling, authorization expiry) run as hosted services. An application lock per job (`sp_getapplock` with `@LockOwner = 'Session'`, on a connection held open for the whole run) ensures a single runner across instances.

## 5. Indexing (beyond constraints)
- `marketplace.listings (state, closes_at)` for the marketplace feed; `receivables.receivables (seller_org_id, state)`; `deals.deals (seller_org_id, state)`, `(factor_org_id, state)`; `receivables.invoices (debtor_id)` for exposure queries; `audit.audit_events (entity_type, entity_id, occurred_at)`.

## 6. Data protection in the DB
- Azure-managed encryption at rest. Highly sensitive fields (SEF API keys) are **not** stored in the DB: they go to Key Vault, and the DB holds only the secret name.
- Personal data (BO date of birth, representatives) lives in `compliance` and `orgs` only. Read access goes through the application role; reporting replicas exclude these columns.
- Separate DB roles: `app_rw` (no DDL, no UPDATE/DELETE on immutable tables), `migrator` (DDL, used only in the pipeline), `readonly_reporting` (no grants until the reporting scope is defined in task 022; never `compliance`/`orgs` personal-data columns or `platform.idempotency_keys`). Platform.Migrator creates the roles and grants, so the pipeline runs it with a principal that may manage roles (e.g. `db_owner`); the API and worker connect as members of `app_rw` (task 024).

## 7. Backups & retention
- Point-in-time restore for 35 days (Azure SQL maximum) + monthly long-term retention backups. Restores are tested quarterly.
- Retention periods per record class follow LQ-47; until it is answered, **nothing financial or compliance-related is purged**.
