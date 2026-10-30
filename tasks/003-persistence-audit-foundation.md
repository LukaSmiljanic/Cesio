# 003 — Persistence, Outbox, Idempotency, Audit Foundation

# Goal
Provide the shared infrastructure that guarantees atomic state change + audit + events, and safe retries.

# Context
[database.md](../docs/database.md) §1, §4; [audit.md](../docs/audit.md); [architecture.md](../docs/architecture.md) §5; ADR-003, ADR-006, ADR-017.

# Requirements
1. EF Core + SQL Server conventions (ADR-021): snake_case, schema per module (`auth`, not `identity`), `decimal(19,4)` for decimals, `datetimeoffset(3)`, sequential GUID keys in SQL Server byte order, `version` concurrency token, Money mapped as two columns (amount + currency), strongly typed ID converters.
2. A `UnitOfWork` that lets several module DbContexts share **one** `SqlConnection` + transaction (needed for ADR-006).
3. `platform` schema: `outbox`, `processed_events`, `idempotency_keys`, `business_parameters` tables (DDL in database.md).
4. Outbox: `IOutbox.Add(event)` inside the transaction; a worker dispatcher claiming batches with `WITH (UPDLOCK, READPAST, ROWLOCK)`, at-least-once delivery, retry with backoff, dead-letter after N attempts (visible in logs and metrics); consumers keyed by `(event_id, consumer)` in a `processed_events` table for idempotency.
5. Idempotency filter for endpoints marked `.RequireIdempotency()`: header required (400 if missing), body SHA-256, stored response replay, 422 on body mismatch, row inserted in the same transaction as the command; 30-day expiry with a cleanup job.
6. `audit` schema + `IAuditWriter` exactly as in audit.md §2/§4: hash chain under an application lock (`sp_getapplock`, owner `Transaction`), an INSTEAD OF trigger blocking UPDATE/DELETE (declared in the EF mapping with `HasTrigger`), DB grants (`app_rw` INSERT/SELECT only; TRUNCATE impossible without ALTER). Separate `access_log` table.
7. Daily chain-verification job + digest written as a read-only file under `Storage:Root` (ADR-023; copied off the server, task 024).
8. Migration strategy: per-module migrations folder; a migration runner console command used by CI/CD with the `migrator` role.
9. Application-lock helper (`sp_getapplock`, owner `Session`, dedicated open connection) for singleton jobs.
10. `/health/ready` includes the DB.

# Business Rules
- If the audit write fails, the business transaction fails.
- Audit and outbox rows are written in the same transaction as the state change.

# Technical Requirements
Testcontainers SQL Server (`Testcontainers.MsSql`) integration test base class (fresh DB per test class, migrations applied).

# API Changes
None (filters and infrastructure only).

# Database Changes
Schemas `platform`, `audit`; roles `app_rw`, `migrator`, `readonly_reporting` (created by Platform.Migrator; the application login also by the migrator, ADR-023, task 024).

# Security Considerations
Audit `changes` masking helper (account numbers, emails partially masked). Idempotency keys are scoped by org (or by user outside an organization, task 005) + endpoint so a key cannot replay another tenant's or user's response.

# Tests
- Outbox: event delivered once to an idempotent consumer despite a simulated crash after publish.
- Idempotency: replay returns the same body; a different body → 422; concurrent duplicate requests → one execution.
- Audit: UPDATE/DELETE with the app role fails; chain verification detects a tampered row; a business rollback leaves no audit row.

# Acceptance Criteria
- [x] All tests above green in CI with Testcontainers (CI on `main`, 2026-10-22).
- [x] Sample command in a test module demonstrates state change + audit + outbox in one transaction (`tests/Platform.Integration.Tests/SampleCommandTests.cs`).

# Out of Scope
Business tables; The off-site copy of the digests (024).
