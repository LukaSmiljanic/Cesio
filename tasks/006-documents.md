# 006 — Secure Documents

# Goal
A shared document service for uploads and downloads (KYB evidence, invoice attachments, contracts) with malware scanning, hashing and authorization-checked access.

# Context
[security.md](../docs/security.md) §4; [architecture.md](../docs/architecture.md) §2 (files on disk, ClamAV since ADR-023).

# Requirements
- `documents` schema: `stored_documents` (id, owner_org_id, uploaded_by, purpose enum, content_type, size, sha256, blob_key, scan_status `Pending|Clean|Infected|Failed`, created_at).
- `POST /documents` multipart: allow-list PDF, XML, PNG, JPEG; magic-byte check; max 20 MB; random key (column `blob_key`); stored in the `quarantine` folder (a container until ADR-023).
- Scan: an `IMalwareScanner` abstraction. Production: ClamAV over the clamd protocol (ADR-023, replaced Defender for Storage); no verdict → stays in quarantine. Local: a fake scanner (EICAR test string → Infected).
- `GET /documents/{id}/content`: authorization delegated to the owning module via `IDocumentAccessPolicy` (purpose-specific: e.g. a listing document is readable by the seller, staff and factors that can see the listing). Streams the file. Only `Clean` documents can be downloaded.
- Optional PDF watermark on download for purpose `ListingAttachment` (factor org name + timestamp).
- Documents are immutable; "replace" = new document + reference change in the owning module.

# Business Rules
Documents referenced by deals, KYB decisions or audit events can never be deleted.

# Technical Requirements
Files on the server's disk under `Storage:Root` (ADR-023; Azure Blob storage until 2026-10-30).

# API Changes
`POST /documents`, `GET /documents/{id}`, `GET /documents/{id}/content`.

# Database Changes
`documents.stored_documents`.

# Security Considerations
No public containers; filename sanitisation; Content-Disposition attachment; `X-Content-Type-Options: nosniff`. Every download is written to `audit.access_log`.

# Tests
Wrong magic bytes rejected; EICAR → Infected and not downloadable; unauthorized org → 404; hash stored and correct.

# Acceptance Criteria
- [x] Upload → scan → download works locally with the fake scanner (`DocumentTests`; CI on `main`, 2026-10-22, then with Azurite; since ADR-023 on disk). Downloads are streamed (no SAS); the PDF watermark is not built yet (optional).
- [x] Access policy hook is used (no direct download bypass).
- [x] Access log entries are written.

# Out of Scope
Document previews, OCR, e-signature validation (016).
