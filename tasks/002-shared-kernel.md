# 002 — Shared Kernel: Money & Identifiers

# Goal
Implement the value objects that every module depends on, with exhaustive tests.

# Context
[domain-model.md](../docs/domain-model.md) §4; ADR-008; [financial-model.md](../docs/financial-model.md) §5.

# Requirements
- `Currency` (ISO code, minor units; allow-list RSD, EUR).
- `Money` (decimal amount + currency): `Add`, `Subtract`, `Multiply(decimal)`, `Round()` with `MidpointRounding.AwayFromZero` to minor units, comparisons; cross-currency operations throw `CurrencyMismatchException`; parse/format from/to a string decimal (invariant culture) for the API.
- `Pib` (9 digits, ISO 7064 mod 11,10 check digit), `MaticniBroj` (8 digits; format validation, plus a check digit if one is confirmed to apply; mark `TODO(task-000)` otherwise), `SerbianBankAccountNumber` (18 digits = 3 bank + 13 account + 2 control, mod 97 check; accepts input with dashes or short forms, normalising zero padding of the middle part).
- `BusinessDayCalendar`: weekends + a holiday table (Serbian public holidays including Orthodox Easter dates loaded as data, not computed); `AddBusinessDays`, `IsBusinessDay`; "today" in Europe/Belgrade.
- `Result<T>`/error type conventions; strongly typed IDs (`OrganizationId`, `ReceivableId`, …) wrapping UUIDv7 (generator included).
- System.Text.Json converters for `Money` (string amount) and the IDs.

# Business Rules
- A money amount never uses floating point; JSON numbers for money are rejected (strings only).
- Rounding happens only when `Round()` is called explicitly.

# Technical Requirements
No external dependencies beyond the BCL (UUIDv7: use `Guid.CreateVersion7()`).

# API Changes
None (converters registered for later use).

# Database Changes
None (EF value converters come in 003).

# Security Considerations
Validation types must reject over-long input (DoS) and non-ASCII digits.

# Tests
Property-based or table tests: Money arithmetic and rounding (0.005 → 0.01, −0.005 → −0.01), currency mismatch, parsing of "97000.00"/"97000"/"9.7e4" (reject), PIB valid/invalid samples (use real-format test numbers, not real companies), account number checksum samples, business-day addition across holidays and year end.

# Acceptance Criteria
- [x] 100% branch coverage on the kernel value objects.
- [x] No `double`/`float` in the kernel.
- [x] JSON round trip of Money preserves scale.

# Out of Scope
FX conversion, persistence mapping. The holiday dates themselves: the calendar takes non-working days as input and ships no dates (product owner, 2026-10-19); where the real table is stored and maintained is still to be assigned to a task.
