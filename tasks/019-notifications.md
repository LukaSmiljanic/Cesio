# 019 — Notifications

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
- [ ] All product-spec §8 events wired (checklist in the PR).
- [ ] Preferences respected for optional types.

# Out of Scope
SMS, push notifications, debtor notifications.
