# 019 — Notifications

**Blocked by (information only):** LQ-63 (notices), LQ-66 (service vs marketing messages; the digest is an optional type).

# Goal
Users receive in-app and email notifications for the events listed in the product spec, with per-user preferences for non-mandatory types.

# Context
[product-spec.md](../docs/product-spec.md) §8; [integrations.md](../docs/integrations.md) §8; [security.md](../docs/security.md) §2 (security notifications).

# Requirements
- `notifications` schema: notifications (recipient user, org, type, payload JSON, read_at), preferences (user, type, channel, enabled), delivery attempts.
- Outbox event consumers map domain events → recipients (by org role) → notifications. Mandatory types (security, deal step required, funding, holds) cannot be disabled.
- Email: `IEmailSender` (ACS Email in Azure, smtp4dev locally); templates in Serbian Latin + English (user language preference); **no confidential details** in emails (no amounts, no debtor names; only "action required on deal DL-…" + a link).
- In-app: `GET /notifications` (cursor), `POST /notifications/{id}/read`, unread count; a UI bell + list; a preferences page.
- Factor digest: "new listings matching your filters" at most hourly (appetite filters stored per factor org; simple amount/tenor ranges).
- Retry email sending (3x backoff); failures visible in admin integration health.

# Business Rules
Recipients are resolved at send time from current memberships (removed members get nothing).

Implementation notes (2026-10-24, simplest options): recipients are all active members (every member is Owner in the MVP); security notifications also go to the official email; mandatory types are security, deal steps, offer accepted, funding, deal cancelled, holds, KYB decisions and factor authorization changes; modules without a domain event publish `OrganizationNotificationRequested` through the outbox; emails are sent by a worker job every minute with three attempts (after 1, 5 and 25 minutes), failures are listed at `GET /admin/notification-failures`; the new-listing digest is an hourly job over notifications marked `Digest`; appetite filters live in `marketplace.factor_appetite` and the marketplace decides which factors match (the same visibility rule as the feed); the email language is a user setting (sr default).

Event checklist (product-spec §8): verification result ✔ (receivable verified/rejected), KYB decision ✔ (approved, rejected, needs info), new matching listing ✔ (digest), offer received ✔, withdrawn ✔, accepted ✔, superseded ✔, deal step required ✔ (deal created, signed), funding reported ✔ / confirmed ✔, deal cancelled ✔, fee statement issued ✔ (charge invoiced), hold placed/lifted ✔ (organization holds), security ✔ (member added, Signatory changed, SEF key change and invalidation, payout account added/verified/deactivated, also to the factors of open deals). Task 020's jobs publish the rest as `OrganizationNotificationRequested` (type `reminder` or `sef_status_changed`): authorization expiring ✔, offer expiring ✔, signature deadline approaching ✔, outcome overdue ✔, SEF status changed ✔.

# Technical Requirements
Idempotent consumers (event ID + recipient unique).

# API Changes
As listed + `GET/PUT /notification-preferences`, `PUT /organizations/{id}/factor-appetite`.

# Database Changes
`notifications.*`; `marketplace.factor_appetite`.

# Security Considerations
Links point to the app (authentication required); no PII in email subjects; SPF/DKIM/DMARC configured in 024.

# Tests
Event → correct recipients by role; mandatory types ignore preferences; duplicate events → one notification; email content contains no amounts or debtor names (snapshot test).

# Acceptance Criteria
- [x] All product-spec §8 events wired (checklist above).
- [x] Preferences respected for optional types.

# Follow-ups from the phase 4 review (2026-10-24)
Fixed: factor appetite amounts are `Money` (string amounts, minor units checked), with currency and range CHECKs and an audited before → after change. Left for task 024: one configured `Auth:PublicBaseUrl` checked at startup outside Development (email links fall back to localhost today).

# Out of Scope
SMS, push notifications, debtor notifications.
