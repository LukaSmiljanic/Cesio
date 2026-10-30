# Operational Runbooks

Task 023. Security model: [security.md](security.md). Deployment: task 024 and the `cesio-deploy` skill. Nothing here resolves a legal question; legal deadlines and duties are open in [legal-questions.md](legal-questions.md).

## 1. Incident response
**Severity.** SEV1: confirmed access to another organization's data, money redirected or at risk (payout account changed by an attacker), audit chain broken, secrets leaked. SEV2: suspected one of these, outage of acceptance or signing, SEF integration failing for all organizations. SEV3: everything else.

**Roles.** Incident lead (the on-call engineer until handed over), a ComplianceOfficer for anything touching a deal, an organization or personal data, the product owner for outward communication.

**Steps.**
1. *Open*: record time, reporter, first symptoms in the incident log (a ticket); keep one timeline from here on.
2. *Contain*:
   - account takeover: a ComplianceOfficer places a FraudInvestigation hold on the organization (`POST /admin/holds`); the real user signs out all sessions (`/me/sessions`) and changes the password, and MFA is reset through the MFA reset flow (`/admin/mfa-resets`). Staff cannot force a sign-out yet; the fallback is a data protection key rotation (§2), which signs everyone out;
   - payout account: the 72 h cool-down blocks a new account; a ComplianceOfficer rejects it and places a hold on the affected deals;
   - leaked secret: rotate it now (§2), then look at its use;
   - broad compromise: stop the API and worker on the server (`docker compose stop api worker`), the one allowed manual change, recorded in the incident; everything else through the pipeline.
3. *Preserve evidence*: export the audit log for the window (`GET /admin/audit/export`, step-up), keep application logs (`docker compose logs`) and the server's system and SSH logs; never edit or delete audit or financial rows.
4. *Assess*: which organizations, deals, documents and people were affected (audit search by entity and actor).
5. *Notify*: who must be told and by when (the data protection authority, affected people, NBS, factors) is open, **LQ-64**; until it is answered the product owner decides with the lawyer, and the decision and its time are written in the incident log.
6. *Recover*: fix through the normal CI pipeline; lift holds only by a ComplianceOfficer with a reason.
7. *Close*: a short post-incident review within 5 business days (cause, what worked, follow-up tasks with numbers).

## 2. Key rotation
| Secret | Where | Rotation | Steps |
|--------|-------|----------|-------|
| SEF API key (per organization) | `platform.secrets`, encrypted with data protection (ADR-023) | When the seller rotates it in SEF, on suspicion, or when the person who knew it leaves | The seller's owner enters the new key under SEF connection (step-up); the platform tests it against SEF and overwrites the secret; the seller then revokes the old key in SEF. Platform staff never see or type the key. |
| ASP.NET data protection keys (cookies, anti-forgery, confirmation links) | Volume `keys` on the server, encrypted with the certificate `secrets/dataprotection.pfx` | Keys: automatic every 90 days (framework default). Certificate: before it expires or on suspicion | Keys on suspicion: revoke the key ring entries; every user is signed out and outstanding confirmation links stop working; announce it. Certificate: create a new `.pfx`, decrypt the stored SEF keys with the old one and re-enter them (or have the sellers re-enter them), then replace the file and restart through the pipeline. Never delete the old certificate before that. |
| SQL Server logins (`sa`, `cesio_app`) | `.env` on the server | Yearly and on suspicion | Change `APP_DB_PASSWORD` in `.env` and redeploy: the migrator rotates the `cesio_app` password before the apps start. `sa`: `ALTER LOGIN sa WITH PASSWORD`, then update `MSSQL_SA_PASSWORD`. |
| Email provider, screening, signature provider credentials | `.env` on the server (SMTP), read at start | Per the provider's policy, at least yearly | Put the new value in `.env`, restart through the pipeline, verify a test message or call, disable the old credential at the provider. |

## 3. Backup restore drill
Backups (ADR-023, deploy/README.md): a nightly full `BACKUP DATABASE` with transaction log backups in between, the `files` volume, the `keys` volume, the certificate and `.env`, all copied off the server; the daily audit digests are also kept off the server where they cannot be overwritten. Retention follows database.md §7. Quarterly (task 024), once LQ-86 is answered: until then no production data is copied into staging. The drill proves that a restore is complete and that the audit chain still verifies.

Steps:
1. Restore the production database to a new database in the staging environment at a chosen time.
2. Run the audit chain verification against it.
3. Spot-check five deals: state, fee charges, document hashes against the restored files, and that a stored SEF key decrypts with the restored keys and certificate.
4. Record the result below.
5. Delete the restored copy. It holds production personal data.

| Date | Restore point | Time to restore | Audit chain | Spot checks | By | Notes |
|------|---------------|-----------------|-------------|-------------|----|-------|
| — | — | — | — | — | — | Not run yet: no production environment exists. The first drill is part of the launch checklist (task 024). |

## 4. Rate-limit review (2026-10-24)
| Endpoints | Partition | Limit | Notes |
|-----------|-----------|-------|-------|
| `/auth/*`, `/me/step-up-check`, public `POST /confirmations/{token}` | client IP | 20 per minute (`Auth:RateLimitPerMinute`) | The client IP must come from the trusted proxy's forwarded header, or all users share one partition behind the proxy: `ForwardedHeaders:KnownNetworks` trusts only the compose network of Caddy (ADR-023). |
| `/marketplace/*` (factor feed and listing details) | user | 120 per minute | Anti-scraping. Each listing view is also limited by audience control. |
| `/documents/*` | user | 120 per minute | Downloads are access-logged. |
| SEF ownership claim | user | 10 per hour | A valid SEF key is needed anyway. |
| Payout bank account additions | user | 10 per hour | Together with the 72 h cool-down. |
| `/deals/export`, `/receivables/export`, `/admin/audit/export` | none | — | Accepted: they return only the caller's own data (or staff data with step-up) and every call is access-logged. |
| All other authenticated endpoints | none | — | Accepted for the MVP: every call needs a session, the organization context and the resource checks; idempotency keys stop replays of commands. Alerts on spikes of 403/404 (security.md §8) cover enumeration. |

The rejection status is 429.

## 5. Scan record
| Date | Scan | Result |
|------|------|--------|
| 2026-10-24 | `dotnet list package --vulnerable --include-transitive` | No vulnerable packages |
| 2026-10-24 | `npm audit` (web) | 0 vulnerabilities |
| — | Container image and configuration scans (Trivy) | Not run yet: in the deploy workflow since ADR-023, no deployment yet |
| — | External penetration test | Not engaged yet (the product owner engages the firm). No production launch with open critical or high findings. |
