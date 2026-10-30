# Decision Log (ADRs)

Status: **Accepted** (build on it) · **Proposed** (default until confirmed; flagged in tasks) · **Open** (must be decided before the dependent task starts).
Changing an Accepted decision requires a new ADR that supersedes it. AI agents must not change decisions silently.

| ID | Title | Status | Depends on |
|----|-------|--------|-----------|
| ADR-001 | Modular monolith (.NET, PostgreSQL, React, Azure) | Accepted (database superseded by ADR-021, hosting by ADR-023) | — |
| ADR-002 | ASP.NET Core minimal APIs with endpoint groups per module | Accepted | — |
| ADR-003 | Schema per module, sequential GUID keys (originally UUIDv7, amended by ADR-021), EF Core per-module DbContext | Accepted | — |
| ADR-004 | Platform never receives, holds or routes deal funds | Accepted (legal confirmation pending) | LQ-06 |
| ADR-005 | Own identity (ASP.NET Core Identity), mandatory MFA, BFF cookie | Accepted | — |
| ADR-006 | Offer acceptance as one cross-module DB transaction | Accepted | — |
| ADR-007 | Factor-paid success fee = 15% of the factor's offered earnings (discount `F − P`, not face value; product owner 2026-10-22, BQ-01 decided); configurable share/min/cap/payer ([financial-model.md](financial-model.md)) | Accepted (LQ-02, LQ-03: working assumption 2026-10-22, legal confirmation pending) | LQ-04, LQ-43, LQ-44, LQ-74 |
| ADR-008 | Money = decimal + ISO currency; rounding half away from zero at fee calculation | Accepted | — |
| ADR-009 | MVP limited to RSD, SEF e-invoices, full assignment, domestic private-sector debtors | Proposed | BQ-02, BQ-03 |
| ADR-010 | Frontend component library: shadcn/ui + Tailwind + TanStack Table (see [ux-design.md](ux-design.md) §8) | Accepted (product owner 2026-10-22) | — |
| ADR-011 | PAdES/QES validation approach (library vs. QTSP validation service): validation behind `ISignatureValidator`; production validator chosen by a spike with Serbian QES samples (candidate: the EU DSS validation service in a sidecar) | Proposed (2026-10-23; the spike needs QES samples from at least two Serbian QTSPs) | LQ-24, LQ-61, LQ-75; until accepted, no deal can become Signed outside development |
| ADR-012 | Invoice and Receivable are separate entities | Accepted | — |
| ADR-013 | Offers state a purchase price and are immutable | Accepted | — |
| ADR-014 | Holds are an overlay, not lifecycle states | Accepted | — |
| ADR-015 | Deal includes a factor CDD gate before contract | Accepted | LQ-29 |
| ADR-016 | Azure EU region hosting | Superseded by ADR-023 | LQ-35 |
| ADR-017 | Audit: same-transaction writes, hash chain, daily digests (read-only files, copied off the server since ADR-023) | Accepted | — |
| ADR-018 | Contract = factor's own agreement, signed externally with QES, validated by the platform | Proposed | LQ-24, LQ-27 |
| ADR-019 | Production launch waits for an operational CEF; build and test everything before that | Proposed | CEF bylaw/API, LQ-17, LQ-18 |
| ADR-021 | SQL Server (Azure SQL Database) instead of PostgreSQL | Accepted (Azure SQL replaced by SQL Server on the own server, ADR-023) | — |
| ADR-020 | Minimal seller onboarding: one shared account per seller, PIB claimed by a valid SEF key, pristupnica, NBS-verified payout account, APR-email confirmations | Proposed | LQ-21, LQ-26, LQ-32, BQ-13 |
| ADR-022 | No separate agreement with factors: factors register and accept the platform's Terms of Use for factors | Accepted (LQ-72, LQ-26: working assumption 2026-10-22, legal confirmation pending) | LQ-73, LQ-74, LQ-36, LQ-65 |
| ADR-023 | Own server (on-premises or VPS) with Docker instead of Azure: secrets encrypted in the database, files on disk, ClamAV, SMTP, SQL Server Express | Accepted (product owner, 2026-10-30) | LQ-35 |

## ADR-001 Modular monolith
**Context:** small team, low transaction volume, high correctness needs. **Decision:** one deployable unit with strict module boundaries enforced by architecture tests. **Consequences:** simple ops and atomic transactions; modules can be extracted later if a real need appears.

## ADR-004 No handling of deal funds
**Context:** handling third-party funds likely requires a payment-services licence (LQ-06) and adds AML exposure. **Decision:** the factor pays the seller directly; the platform records the factor's report and the seller's confirmation. **Consequences:** the platform cannot guarantee settlement. Funding discrepancies are disputes between the parties. The platform's fee is collected by a separate invoice, so there is credit risk on fees.

## ADR-006 Atomic acceptance
**Context:** acceptance changes the offer, listing, receivable and deal; partial failure could double-sell. **Decision:** a single application service in Deals coordinates the write contracts of Marketplace and Receivables on one shared connection/transaction, with row locks and filtered unique indexes. **Consequences:** a deliberate, documented exception to "modules communicate via events".

## ADR-008 Money
`decimal` in C#, `decimal(19,4)` in SQL Server, currency always paired (`Money` value object); arithmetic across currencies throws; results are rounded to minor units only at defined points (fee calculation, display). No floats, enforced by a CI check.

## ADR-009 MVP scope limits
**Why:** SEF gives machine-verifiable invoices with buyer acceptance and a future CEF link; RSD avoids FX legal questions (LQ-50); full assignment keeps INV-R1 simple. **Revisit** after legal review and pilot feedback.

## ADR-011 Signature validation (Proposed)
The platform validates PAdES signatures and never signs. Validation sits behind `ISignatureValidator` (task 016): integrity; the signed original revision equals the agreed contract hash; a qualified certificate; a chain to a trust-store root (Serbian QTSPs, staff-maintained in `deals.trusted_certificates`); revocation at signing time (OCSP/CRL with a timeout, unavailable → fail closed); and the signer named as a legal representative of the party (matching rule: LQ-75; who qualifies: LQ-61). **Options:** (a) iText 9 + BouncyCastle in process (commercial licence for closed source); (b) the EU DSS validation service as a sidecar container (open source, LGPL, REST, LTV support); (c) a QTSP validation API (per-call cost, external dependency). **Proposal:** (b), confirmed by a spike against smart-card and cloud QES from at least two Serbian QTSPs. **Until accepted:** development uses a fake validator; every other environment uses a validator that always answers "unavailable", so no deal becomes Signed there.

## ADR-012 Invoice ≠ Receivable
See [domain-model.md](domain-model.md) §2. Enables partial assignment and bundles later without remodelling invoices.

## ADR-013 Price-based immutable offers
Factors state the purchase price; the platform derives rates for display only. Immutability gives a clean audit and prevents last-second edits; changes are withdraw + resubmit.

## ADR-014 Holds as overlay
Disputes, compliance holds and fraud investigations can occur in any state; modelling them as states would multiply transitions. A Hold blocks forward transitions per a policy table.

## ADR-015 Factor CDD gate
Factors are AML obligors and must do their own CDD. The deal cannot proceed to contract until the factor confirms CDD (or declines → cancel without penalty).

## ADR-017 Audit
See [audit.md](audit.md).

## ADR-018 Contract handling
The MVP does not generate legal contracts: the factor uploads its own agreement populated with the deal terms, both parties confirm a terms checklist (parties, receivable, face value, price, recourse), and sign with a QES. The platform validates the signatures. A platform template is a future option (LQ-27).

## ADR-019 Launch gated on the CEF
**Context:** until the Central Factoring Register (CEF) operates, there is no reliable way to detect an invoice assigned off-platform. SEF status normally stays `Approved` after an off-platform assignment, and the SEF factoring fields (`vatNumberFactoringCompany`, `factoringContractNumber`) are not documented well enough to rely on (LQ-22; see [integrations.md](integrations.md) §1.1). The platform's own registry only protects deals made on the platform (REG-05). **Decision (product owner, 2026-10-13):** develop and test the full platform against the SEF demo and fakes now. **Do not launch production until the CEF is operational** and a CEF lookup (already assigned → block) runs before listing and before signing. **Consequences:**
- The `CefNotOperationalGateway` / `NotApplicable` path is for development, testing and demos only, not for production.
- A task for the CEF integration (lookup + registration) is written once the CEF bylaw and its API are published. Until then, `ICefGateway` stays the integration seam.
- Revisit `cef.blockFundingUntilRegistered` (task 017) with LQ-17 when the CEF rules are known.
- Launch timing depends on the CEF (statutory deadline ≈ June 2027) and can slip with it. A closed pilot before the CEF would need a new ADR and an answer to LQ-20.

## ADR-021 SQL Server instead of PostgreSQL
**Context (product owner, 2026-10-19):** the product owner prefers Microsoft SQL Server; it fits the .NET/Azure stack and the team's tooling. No code exists yet, so the switch costs only documentation. Supersedes the database part of ADR-001 and amends the key format of ADR-003.

**Decision:** SQL Server 2022+ compatible; **Azure SQL Database** (General Purpose, zone-redundant in production) in Azure (*superseded by ADR-023: SQL Server Express in a container on our own server*); SQL Server Developer edition in Docker for local work; `Testcontainers.MsSql` for integration tests; EF Core SQL Server provider.

**Translation rules** (details in [database.md](database.md)):
- Partial unique indexes → **filtered unique indexes**; predicates use only simple comparisons with `AND` (INV-R1: `state <> 'Cancelled' AND state <> 'Terminated'`). Nullable unique columns are filtered with `IS NOT NULL`, because SQL Server treats `NULL`s as equal in unique indexes.
- `numeric(19,4)` → `decimal(19,4)`; `timestamptz` → `datetimeoffset(3)` (UTC); `jsonb` → `nvarchar(max)` + `ISJSON` check; `text` → `nvarchar(n)`; `inet` → `varchar(45)`; arrays → JSON or child tables (membership roles → `role` + `is_signatory`).
- Keys: `uniqueidentifier`, generated as **sequential GUIDs in SQL Server byte order** (a plain UUIDv7 would fragment the clustered index).
- `SELECT … FOR UPDATE` → `WITH (UPDLOCK, ROWLOCK)`; outbox `FOR UPDATE SKIP LOCKED` → `WITH (UPDLOCK, READPAST, ROWLOCK)`; advisory locks → `sp_getapplock`; `READ_COMMITTED_SNAPSHOT ON`.
- Reserved words: schema `identity` → `auth`; column `key` → `idem_key` / `param_key`.
- Immutability: append-only tables get `DENY UPDATE, DELETE` + INSTEAD OF triggers; tables with changing state get column-level `DENY UPDATE` + AFTER UPDATE triggers on fixed columns; every trigger is declared in the EF mapping (`HasTrigger`); TRUNCATE requires ALTER, which the app role lacks.
- Application locks: owner `Transaction` for the audit hash chain, owner `Session` (dedicated connection) for singleton jobs.

**Consequences:** the integration-test container is heavier (≈ 1.5 GB image, slower start); (the Azure consequences no longer apply since ADR-023: the application signs in with a SQL login).

## ADR-020 Minimal seller onboarding
**Context (product owner, 2026-10-19):** the MVP should start with as little friction as possible and add complexity later. A market check ([market-analysis.md](market-analysis.md)) shows that a comparable marketplace (PlatiMe, Croatia) asks sellers only for company data. It relies on the e-invoice channel, debtor confirmation, contractual warranties and payment to the company's own account, not on a heavy identity check. On Cesio only licensed factors buy, and they run their own CDD and check who signs the agreement (ADR-015, ADR-018).

**Decision:**
1. **One account per seller organization** in the MVP. The account represents the company, and the company is liable for every action taken through it (Terms of Use, pristupnica; enforceability pending LQ-26). The company decides who uses it (e.g. its accountant). MFA is TOTP so the account can be used on several devices; passkeys are optional. The membership table stays; the business parameter `orgs.maxSellerMembers` (default 1) enforces the limit.
2. **Factor organizations may have several users**, because banks cannot share credentials (BQ-13).
3. **Roles collapse to Owner + a Signatory flag.** Every member is Owner. Signatory requires a link to an APR legal representative or a verified power of attorney. A seller's single account becomes Signatory when its pristupnica is accepted. Admin, Operator and Viewer are post-MVP.
4. **A PIB is claimed by a valid SEF key, not by typing it.** Entering a PIB creates a private Draft claim that blocks nobody and expires after 7 days. The first claim whose SEF key returns invoices with supplier PIB = the claimed PIB wins; other drafts for that PIB are closed with "already registered, ask its administrator". Two different valid SEF users for one PIB → compliance resolves it through the APR legal representative.
5. **Pristupnica:** one document signed by an APR legal representative (or an attorney): it confirms company data and beneficial owners, accepts the Terms, authorizes the account, and consents to KYB sharing and to the platform's use of the SEF key. The MVP accepts a QES-signed PDF, or a scanned hand-signed copy confirmed through the company's official email recorded in APR. The required signature level is LQ-32.
6. **The payout account must be listed under the organization's PIB/MB in the NBS account register** (snapshot mandatory; a bank letter is only a supplement).
7. **Sensitive changes go through the company, not the account:** changing the payout account or the SEF key, and MFA recovery without recovery codes, need confirmation via the official email recorded in APR (for a factor organization with several users, MFA recovery can instead be approved by another Owner) and notify the security recipients. A **72 h** cooling period applies to a changed payout account (no funding) and after an MFA reset (no step-up actions).
8. **A QES is not an onboarding step.** It is needed when the factoring agreement is signed by a person authorized for the seller (level per LQ-24, ADR-018).

**Consequences:** audit identifies the account, not the person (IP and device are logged). Multi-user seller accounts, more roles and eID login are later additions. **Assumption to verify:** the APR record of every company contains an email address for official correspondence; if it is missing, fall back to a letter to the registered seat.

## ADR-022 No separate agreement with factors
**Context (product owner, 2026-10-22):** onboarding factors should be as simple as onboarding sellers. Product-spec §6.2 previously required a framework agreement between each factor and the platform before access.

**Decision:** a factor's relationship with the platform rests only on **registration plus acceptance of the platform's Terms of Use for factors** (versioned, accepted on behalf of the factor; who may accept and in what form is pending LQ-73). There is no framework or participation agreement. The Terms for factors cover the platform rules, the fee obligation, confidentiality of listing data, liability, data protection and disputes. The only agreement in a deal remains the factoring agreement between seller and factor (ADR-018), to which the platform is not a party. Authorization verification (task 008) is unchanged.

**Open:** whether Terms alone are legally sufficient towards factors, especially banks (fee obligation, confidentiality, liability, and outsourcing rules that may require a written contract), is LQ-72 (with LQ-26, LQ-36, LQ-65). Who may accept the Terms for a factor and in what form is LQ-73; whether later changes to the Terms (including the fee) bind a factor is LQ-74. If the answer requires a written contract, a superseding ADR adds it to factor onboarding.

**Status (product owner, 2026-10-22):** Accepted on the working assumption that LQ-72 and LQ-26 are answered positively (Terms alone are sufficient and click acceptance is valid). Both LQs stay open for the lawyer; LQ-73 and LQ-74 are not covered by the assumption.

## ADR-023 Own server instead of Azure
**Context (product owner, 2026-10-30):** the platform will not run on Azure. Production runs on a Linux server with Docker, either the company's own or a rented one (VPS); which one is not decided yet. ADR-016 (Azure EU region) was only proposed, and the Azure design of task 024 (Bicep, managed identities, Key Vault, Blob, Defender for Storage, Azure Communication Services, Application Insights) was never deployed.

**Decision:**
1. **One Docker host per environment** (staging, production), started with `docker compose`: API, worker, a one-off migrator, SQL Server, ClamAV and a reverse proxy (Caddy) that serves the SPA, terminates HTTPS and forwards `/api` to the API. No Kubernetes (CLAUDE.md).
2. **Secrets** (sellers' SEF keys) are stored **encrypted in the database** (`platform.secrets`) with ASP.NET Core data protection. The data protection keys live in a directory on the server, and outside Development they are encrypted with an X.509 certificate. Database backups therefore contain only ciphertext; the certificate is kept and backed up separately.
3. **Files** (documents, signed contracts, audit digests) are stored **on the server's disk** under `Storage:Root`, with quarantine and clean folders as before. Audit digests are written as read-only files. Immutability (WORM) is now an operations measure: digests are copied off the server daily (see runbooks.md).
4. **Malware scanning** uses **ClamAV** (`clamd`, INSTREAM protocol, no new package). Without a scanner verdict a file stays quarantined (fail closed).
5. **Email** goes through any SMTP server with a login (`Email:UserName`, password from an environment variable).
6. **Database:** SQL Server in a container, **Express edition** for the MVP (10 GB per database; Standard later without code changes). The API and worker sign in with a SQL login that is a member of `app_rw`. The migrator runs with an administrative login and creates or rotates the application login.
7. **Logs** go to standard output as JSON (collected by Docker); a log server (Seq or Grafana Loki through OpenTelemetry) can be added later.
8. **Deployment** stays through the CI pipeline: GitHub Actions builds the image, pushes it to GitHub Container Registry and deploys over SSH after the environment's approval (staging after CI on main, production on a `v*` tag).

**Consequences:**
- No Azure packages remain: Azure.Storage.Blobs, Azure.Security.KeyVault.Secrets and Testcontainers.Azurite are removed. Azure.Identity remains only as a transitive dependency of the SQL client. No new package is added.
- The operator becomes responsible for the server: patches, firewall, HTTPS certificates (Caddy renews them automatically), backups of the database, the files and the data-protection certificate, and restore drills.
- File immutability and off-site copies are weaker than Azure's built-in WORM and geo-redundancy, and depend on the backup routine.
- Data can stay in Serbia, which may change LQ-35 (transfer of personal data abroad). LQ-35 stays open until the lawyer answers.
- ADR-016 is superseded. ADR-021 stays (SQL Server) without Azure SQL.

