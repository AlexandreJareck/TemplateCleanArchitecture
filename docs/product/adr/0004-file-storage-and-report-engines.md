# ADR-0004: File storage and report engines

- Status: Accepted (Phase 23, 2026-10-18); packages and versions verified in Phase 30
- Decision owners: D23-5, D23-6, D23-7

## Context

The product needs attachments (requisitions, quotes, receipts) and generated documents: requisition, quote-comparison and purchase-order PDFs; budget and supplier XLSX workbooks (RPT-009). Repository policy forbids unapproved licenses and requires vendor types behind Application ports.

## Decision

1. **Storage:** Application port `IFileStorage` (put stream, open read stream, delete, exists) with provider-neutral keys. Infrastructure adapter uses Azure Blob-compatible APIs: Azurite locally (pinned image) and Azure Blob Storage in Azure (D23-7, never provisioned without authorization). Private containers only. PostgreSQL stores `StoredFile` metadata; bytes never enter Domain or database columns (FIL-001).
2. **Downloads:** streamed through authorized API endpoints (FIL-003); storage URLs are not part of any contract.
3. **PDF:** server-side HTML/CSS templates (versioned assets, no user-supplied templates, RPT-008) rendered to PDF by Playwright headless Chromium in the Worker (D23-5). Fonts are embedded from licensed files recorded in the license inventory. QuestPDF is an optional alternative adapter only after explicit license approval.
4. **XLSX:** ClosedXML (MIT) behind an `ISpreadsheetWriter`-style port (D23-6); Open XML SDK is the low-level fallback. Formula injection is neutralized (RPT-007).
5. Report generation runs in the Worker from a parameter snapshot and as-of instant (RPT-003); jobs are durable and idempotent (RPT-001, RPT-005).
6. A malware-scan port exists with a no-op adapter (FIL-006); no protection is claimed.

## Alternatives

- **Database BLOBs:** simple but bloats backups and the database. Rejected.
- **Local filesystem:** not portable to Azure. Rejected.
- **EPPlus:** commercial license (Polyform). Rejected unless licensed.
- **PDFsharp/MigraDoc or QuestPDF as default:** code-first layouts are harder to share with HTML previews; QuestPDF license terms require approval. Not default.
- **External rendering service:** sends data outside the system. Rejected.

## Consequences

- The Worker image needs a supported browser runtime, fonts and a non-root configuration (spike SP-4).
- Tests assert PDF/XLSX semantics (text, headings, tables, cell types), not byte equality.
- Phase 30 re-verifies versions, licenses and advisories before installing any package.
