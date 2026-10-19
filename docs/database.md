# Database Design (PostgreSQL)

Target: PostgreSQL 16+ (Azure Database for PostgreSQL Flexible Server). Access via EF Core + Npgsql, with migrations kept per module.

## 1. Conventions
- **Schemas per module:** `identity`, `orgs`, `compliance`, `receivables`, `marketplace`, `deals`, `fees`, `documents`, `notifications`, `audit`, `platform` (config, outbox, idempotency). A module never writes another module's schema; cross-schema foreign keys are allowed **only** to stable reference rows (organization, user, receivable IDs) to protect integrity.
- **Keys:** `uuid` primary keys generated in the application as **UUIDv7** (time-ordered, index-friendly). Human-readable references (`DL-2026-000123`) come from per-type sequences for support and documents.
- **Money:** `numeric(19,4)` + `char(3)` currency column next to **every** amount, with a `CHECK (currency IN ('RSD','EUR'))` domain. No `float`/`real`/`double precision`/`money` types anywhere (a CI check greps migrations).
- **Time:** `timestamptz` (UTC) for instants; `date` for business dates (issue, maturity, value date).
- **Standard columns:** `created_at`, `created_by`, `updated_at`, `updated_by`, `version` (integer, optimistic concurrency, incremented by EF).
- **Enums:** stored as `text` with `CHECK` constraints (easier migrations than PG enums).
- **Deletion:** no hard deletes of business or financial rows. Soft delete (`deleted_at`) **only** for drafts, notification preferences and unsubmitted documents. Financial tables (`deals.*`, `fees.*`, `marketplace.offers`, `audit.*`) get `REVOKE UPDATE, DELETE` for columns/rows that must be immutable, via a dedicated DB role, plus triggers rejecting updates to immutable columns.

## 2. Core tables (abridged DDL)

```sql
-- orgs
CREATE TABLE orgs.organizations (
  id uuid PRIMARY KEY,
  org_type text NOT NULL CHECK (org_type IN ('Seller','Factor')),
  legal_name text NOT NULL,
  pib char(9) NOT NULL,
  mb  char(8),                       -- known after the APR prefill
  claimed_at timestamptz,            -- set by a valid SEF key (sellers) or at invitation (factors), ADR-020
  official_email text,               -- from APR, recorded by compliance
  onboarding_status text NOT NULL,
  risk_rating text,
  created_at timestamptz NOT NULL, version int NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX ux_org_pib_claimed ON orgs.organizations(pib) WHERE claimed_at IS NOT NULL;
CREATE UNIQUE INDEX ux_org_mb_claimed  ON orgs.organizations(mb)  WHERE claimed_at IS NOT NULL;
CREATE TABLE orgs.memberships (
  id uuid PRIMARY KEY,
  org_id uuid NOT NULL REFERENCES orgs.organizations(id),
  user_id uuid NOT NULL REFERENCES identity.users(id),
  roles text[] NOT NULL,
  status text NOT NULL,
  UNIQUE (org_id, user_id)
);
CREATE TABLE orgs.bank_accounts (
  id uuid PRIMARY KEY,
  org_id uuid NOT NULL REFERENCES orgs.organizations(id),
  account_number char(18) NOT NULL,
  currency char(3) NOT NULL,
  verification_status text NOT NULL,
  evidence_document_id uuid,
  UNIQUE (org_id, account_number, currency)
);

CREATE TABLE orgs.pristupnice (
  id uuid PRIMARY KEY, org_id uuid NOT NULL REFERENCES orgs.organizations(id),
  generated_document_id uuid NOT NULL, generated_sha256 char(64) NOT NULL,
  signed_document_id uuid, method text,            -- Qes | ScannedConfirmedByEmail
  signer_name text, matched_representative_id uuid, poa_document_id uuid,
  status text NOT NULL,                            -- Generated | Uploaded | EmailConfirmed | Accepted | Rejected
  decided_by uuid, decided_at timestamptz, version int NOT NULL DEFAULT 0
);
CREATE TABLE orgs.confirmation_tokens (
  id uuid PRIMARY KEY, org_id uuid NOT NULL REFERENCES orgs.organizations(id),
  purpose text NOT NULL,                           -- Pristupnica | PayoutAccountChange | SefKeyChange | MfaReset
  subject_id uuid, token_hash char(64) NOT NULL UNIQUE,
  expires_at timestamptz NOT NULL, used_at timestamptz
);
-- identity.mfa_reset_requests (user_id, org_id, approved_by_kind Email|Owner, approved_at, executed_by, executed_at, status)

-- compliance
CREATE TABLE compliance.factor_authorizations (
  id uuid PRIMARY KEY,
  org_id uuid NOT NULL REFERENCES orgs.organizations(id),
  kind text NOT NULL, issuing_authority text NOT NULL,
  decision_number text, decision_date date,
  evidence_document_id uuid, source_snapshot_document_id uuid,
  official_source_url text,
  verification_method text,          -- OfficialRegisterLookup | AuthorityConfirmationLetter
  status text NOT NULL, verified_by uuid, verified_at timestamptz, reverify_by date,
  version int NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX ux_factor_auth_active ON compliance.factor_authorizations(org_id)
  WHERE status = 'Verified';
ALTER TABLE compliance.factor_authorizations ADD CONSTRAINT ck_factor_auth_decision
  CHECK (kind = 'Bank' OR (decision_number IS NOT NULL AND decision_date IS NOT NULL AND evidence_document_id IS NOT NULL));
CREATE TABLE compliance.holds (
  id uuid PRIMARY KEY, subject_type text NOT NULL, subject_id uuid NOT NULL,
  kind text NOT NULL, reason text NOT NULL,
  placed_by uuid NOT NULL, placed_at timestamptz NOT NULL,
  lifted_by uuid, lifted_at timestamptz
);
CREATE INDEX ix_holds_active ON compliance.holds(subject_type, subject_id) WHERE lifted_at IS NULL;

-- receivables
CREATE TABLE receivables.debtors (
  id uuid PRIMARY KEY, pib char(9) NOT NULL UNIQUE, mb char(8), legal_name text NOT NULL,
  status_snapshot jsonb, updated_at timestamptz NOT NULL
);
CREATE TABLE receivables.invoices (
  id uuid PRIMARY KEY,
  seller_org_id uuid NOT NULL REFERENCES orgs.organizations(id),
  debtor_id uuid NOT NULL REFERENCES receivables.debtors(id),
  sef_invoice_id text NOT NULL,
  invoice_number text NOT NULL,
  issue_date date NOT NULL, due_date date NOT NULL,
  currency char(3) NOT NULL,
  net_amount numeric(19,4) NOT NULL, vat_amount numeric(19,4) NOT NULL, gross_amount numeric(19,4) NOT NULL,
  ubl_document_id uuid NOT NULL, ubl_sha256 char(64) NOT NULL,
  sef_status text NOT NULL, sef_status_checked_at timestamptz NOT NULL,
  CHECK (gross_amount = net_amount + vat_amount),
  CHECK (due_date >= issue_date),
  UNIQUE (sef_invoice_id),
  UNIQUE (seller_org_id, invoice_number, issue_date)
);
CREATE TABLE receivables.receivables (
  id uuid PRIMARY KEY,
  invoice_id uuid NOT NULL REFERENCES receivables.invoices(id),
  seller_org_id uuid NOT NULL, debtor_id uuid NOT NULL,
  face_value numeric(19,4) NOT NULL CHECK (face_value > 0), currency char(3) NOT NULL,
  maturity_date date NOT NULL,
  state text NOT NULL,
  fingerprint char(64) NOT NULL,
  version int NOT NULL DEFAULT 0
);
-- MVP: one receivable per invoice (full assignment). Excludes Rejected/Cancelled so a corrected resubmission is possible.
CREATE UNIQUE INDEX ux_receivable_per_invoice ON receivables.receivables(invoice_id)
  WHERE state NOT IN ('Rejected','Cancelled');
CREATE INDEX ix_receivable_fingerprint ON receivables.receivables(fingerprint);
CREATE TABLE receivables.verification_checks (... append-only ...);
CREATE TABLE receivables.seller_declarations (... append-only ...);
CREATE TABLE receivables.sef_status_history (
  id uuid PRIMARY KEY, invoice_id uuid NOT NULL, status text NOT NULL,
  observed_at timestamptz NOT NULL, raw jsonb NOT NULL
);

-- marketplace
CREATE TABLE marketplace.listings (
  id uuid PRIMARY KEY, receivable_id uuid NOT NULL REFERENCES receivables.receivables(id),
  seller_org_id uuid NOT NULL, state text NOT NULL,
  opens_at timestamptz NOT NULL, closes_at timestamptz NOT NULL,
  audience_type text NOT NULL, reserve_price numeric(19,4), currency char(3) NOT NULL,
  version int NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX ux_listing_open_per_receivable ON marketplace.listings(receivable_id) WHERE state = 'Open';
CREATE TABLE marketplace.listing_audience (listing_id uuid, factor_org_id uuid, mode text CHECK (mode IN ('Include','Exclude')),
  PRIMARY KEY (listing_id, factor_org_id));
CREATE TABLE marketplace.offers (
  id uuid PRIMARY KEY, listing_id uuid NOT NULL REFERENCES marketplace.listings(id),
  factor_org_id uuid NOT NULL,
  purchase_price numeric(19,4) NOT NULL CHECK (purchase_price > 0), currency char(3) NOT NULL,
  recourse_type text NOT NULL, funding_deadline_bdays int NOT NULL CHECK (funding_deadline_bdays BETWEEN 1 AND 30),
  valid_until timestamptz NOT NULL, conditions_text varchar(1000),
  fee_schedule_version int NOT NULL, platform_fee numeric(19,4) NOT NULL,
  state text NOT NULL, created_by uuid NOT NULL, created_at timestamptz NOT NULL,
  version int NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX ux_offer_active_per_factor ON marketplace.offers(listing_id, factor_org_id) WHERE state = 'Active';
CREATE UNIQUE INDEX ux_offer_accepted_per_listing ON marketplace.offers(listing_id) WHERE state = 'Accepted';

-- deals
CREATE TABLE deals.deals (
  id uuid PRIMARY KEY, reference text NOT NULL UNIQUE,
  receivable_id uuid NOT NULL REFERENCES receivables.receivables(id),
  listing_id uuid NOT NULL, offer_id uuid NOT NULL UNIQUE REFERENCES marketplace.offers(id),
  seller_org_id uuid NOT NULL, factor_org_id uuid NOT NULL,
  face_value numeric(19,4) NOT NULL, purchase_price numeric(19,4) NOT NULL, currency char(3) NOT NULL,
  recourse_type text NOT NULL, state text NOT NULL,
  signature_deadline timestamptz NOT NULL, funding_deadline date,
  version int NOT NULL DEFAULT 0,
  CHECK (purchase_price < face_value)
);
-- INV-R1: the core anti-double-financing guarantee
CREATE UNIQUE INDEX ux_deal_live_per_receivable ON deals.deals(receivable_id)
  WHERE state NOT IN ('Cancelled','Terminated');
CREATE TABLE deals.deal_state_history (id uuid PRIMARY KEY, deal_id uuid NOT NULL, from_state text, to_state text NOT NULL,
  actor_user_id uuid, at timestamptz NOT NULL, reason text);
CREATE TABLE deals.signatures (...), deals.contract_documents (...), deals.cef_registrations (...),
             deals.debtor_notifications (...), deals.funding_reports (...), deals.funding_confirmations (...),
             deals.outcomes (... UNIQUE (deal_id) ...);
CREATE TABLE deals.disputes (...);

-- fees
CREATE TABLE fees.fee_schedules (version int PRIMARY KEY, definition jsonb NOT NULL, effective_from timestamptz NOT NULL,
  created_by uuid NOT NULL);
CREATE TABLE fees.fee_charges (
  id uuid PRIMARY KEY, deal_id uuid NOT NULL REFERENCES deals.deals(id),
  payer_org_id uuid NOT NULL, kind text NOT NULL CHECK (kind IN ('Charge','Reversal')),
  amount numeric(19,4) NOT NULL, vat_amount numeric(19,4) NOT NULL, currency char(3) NOT NULL,
  status text NOT NULL, due_at date, external_invoice_ref text, reverses_id uuid REFERENCES fees.fee_charges(id),
  created_at timestamptz NOT NULL
);
CREATE UNIQUE INDEX ux_fee_charge_once ON fees.fee_charges(deal_id, payer_org_id) WHERE kind = 'Charge';

-- platform
CREATE TABLE platform.idempotency_keys (
  key text NOT NULL, org_id uuid NOT NULL, user_id uuid NOT NULL, endpoint text NOT NULL,
  request_sha256 char(64) NOT NULL, response_status int, response_body jsonb,
  created_at timestamptz NOT NULL, expires_at timestamptz NOT NULL,
  PRIMARY KEY (org_id, endpoint, key)
);
CREATE TABLE platform.outbox (id uuid PRIMARY KEY, type text NOT NULL, payload jsonb NOT NULL, occurred_at timestamptz NOT NULL,
  processed_at timestamptz, attempts int NOT NULL DEFAULT 0, last_error text);
CREATE INDEX ix_outbox_pending ON platform.outbox(occurred_at) WHERE processed_at IS NULL;
CREATE TABLE platform.business_parameters (key text, version int, value jsonb NOT NULL, effective_from timestamptz NOT NULL,
  created_by uuid NOT NULL, PRIMARY KEY (key, version));
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
- **Offer acceptance** = one transaction: `SELECT … FOR UPDATE` on listing and receivable rows (pessimistic locking at the hottest point), update offer/others/listing/receivable, insert deal + outbox + audit. The partial unique indexes are the last line of defence: even a code bug cannot create two live deals.
- Isolation level: `READ COMMITTED` + explicit row locks + unique indexes (we do not rely on `SERIALIZABLE` retries).
- **Outbox** rows are written in the same transaction as the state change. A background dispatcher publishes them (at-least-once; consumers are idempotent by event ID).
- **Idempotency:** every financial or state-changing POST requires an `Idempotency-Key` header. The row is inserted within the same transaction as the command. Replay with the same key and body hash → stored response; same key with a different body → 422. Retention: 30 days.
- Scheduled jobs (expiry of listings and offers, SEF re-polling, authorization expiry) run as hosted services. A Postgres advisory lock per job ensures a single runner across instances.

## 5. Indexing (beyond constraints)
- `marketplace.listings (state, closes_at)` for the marketplace feed; `receivables.receivables (seller_org_id, state)`; `deals.deals (seller_org_id, state)`, `(factor_org_id, state)`; `receivables.invoices (debtor_id)` for exposure queries; `audit.audit_events (entity_type, entity_id, occurred_at)`.

## 6. Data protection in the DB
- Azure-managed encryption at rest. Highly sensitive fields (SEF API keys) are **not** stored in the DB: they go to Key Vault, and the DB holds only the secret name.
- Personal data (BO date of birth, representatives) lives in `compliance` and `orgs` only. Read access goes through the application role; reporting replicas exclude these columns.
- Separate DB roles: `app_rw` (no DDL, no UPDATE/DELETE on immutable tables), `migrator` (DDL, used only in the pipeline), `readonly_reporting`.

## 7. Backups & retention
- PITR backups for 35 days (Azure maximum) + monthly long-term backups. Restores are tested quarterly.
- Retention periods per record class follow LQ-47; until it is answered, **nothing financial or compliance-related is purged**.
