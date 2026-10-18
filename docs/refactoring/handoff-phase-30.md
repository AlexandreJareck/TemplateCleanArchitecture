# Phase 30 Execution Handoff — Files and Reporting Platform

## Mission

Implement a reusable, asynchronous file and reporting platform. Store file metadata in PostgreSQL and bytes in blob-compatible storage; generate approved PDF and XLSX reports through the Worker; expose secure job/status/download APIs; and deliver a frontend report center.

## Required context and skills

Read instructions, Phase 23 file/report/security decisions, Phases 26–29 reports, Worker/outbox/inbox design, current Compose/security/health/observability, coverage/license policies, and relevant frontend/backend tests.

Use `package-upgrade` for every new report/browser/storage package family, `integration-adapter` for blob/PDF/XLSX adapters, `api-contract-change`, `ef-migration`, `architecture-boundary-check`, and `dotnet-change-verification`. Verify current official versions, supported TFMs, transitive advisories, container/runtime requirements, and licenses on the execution date.

## Preconditions and license hard stops

- Phases 23–29 are complete and green.
- D23-5 through D23-7 are approved.
- Default recommendation is HTML/CSS to PDF with Playwright and XLSX with ClosedXML. If current licenses or support conflict with repository policy, stop and present alternatives; do not silently accept a new license exception.
- QuestPDF, PDFsharp/MigraDoc, EPPlus, commercial browser services, or unapproved native binaries require explicit license/architecture approval.
- Record HEAD/dirty tree and baseline package/license/image/test evidence.

## Application model and ports

Implement provider-neutral contracts for:

- `StoredFile`: owner/resource, type, sanitized name, media type, length, digest, storage key, creation/expiry, classification and status;
- `ReportJob`: report type, requested-by, parameters snapshot, Pending/Running/Ready/Failed/Expired/Cancelled state, progress metadata, output file and safe failure code;
- file storage, report generation, template rendering, and job queue/use cases.

Never expose physical storage paths, container credentials, or permanent public URLs as domain/API contracts. Authorize every request and download against the owning resource/permission.

## Storage adapter

- Use Azurite locally and Azure Blob-compatible APIs behind an Infrastructure adapter.
- Stream uploads/downloads; enforce approved size/type/count limits, normalized filenames, digest verification, cancellation, timeout, and safe overwrite policy.
- Use private containers. Prefer authenticated proxy downloads or bounded delegated URLs only if explicitly approved.
- Define orphan cleanup, retention, expiry and malware-scanning extension point. Do not claim malware protection unless implemented and tested.
- Add isolated Azurite Compose/Testcontainers validation without touching user storage.

## Report execution

- `POST` report request returns `202 Accepted`, job ID and status location; repeated idempotent requests do not create uncontrolled duplicates.
- Worker claims a report job durably, renders from a stable query/snapshot, stores bytes, marks Ready, and publishes completion/failure events.
- Retries cannot create multiple active outputs or corrupt state.
- Never accept arbitrary executable template code or unsanitized HTML from API users.
- Embed fonts/assets reproducibly and record their licenses.
- Validate spreadsheet formula-injection handling for user-controlled cells.

Initial reports:

- purchase requisition PDF;
- quote-comparison PDF;
- budget allocation/consumption XLSX;
- supplier quote/performance data XLSX using only currently available metrics.

Purchase-order PDF is added in Phase 32 on this platform.

Every report must state its effective date/time, timezone, filter/policy context, currency and generation timestamp where relevant. Financial/operational totals come from server-owned queries or immutable snapshots and use the same approved calculation rules as the transactional use case. A report must not silently recompute historical outcomes with today's supplier status, exchange rate, approval policy, or master data.

## Frontend

Implement the report slice through the established frontend layers: Application owns report-request/status/download use cases and polling policy, Infrastructure adapts the generated report API and browser download mechanism, and Presentation owns the report center and feedback. Implement report request actions, status/progress polling with bounded backoff, completed download, failure/retry display, permission behavior, expired state, and accessible feedback. Do not let generated DTOs escape Infrastructure or place storage secrets/raw blob URLs in browser configuration.

## Tests and validation

Use a rule-to-test matrix for applicable `RPT-*` rules. Use semantic and visual assertions, not byte equality for generated documents. Verify PDF page count/text/headings/tables/fonts and render representative pages for visual QA; verify XLSX workbook/sheets/cell types/styles/formulas/locale and openability. Test historical as-of consistency, policy/snapshot provenance, large bounded data, cancellation, retries, duplicates, storage outage, browser crash, unsafe filenames/content, formula injection, unauthorized download, expiry, and cleanup selection.

Run package/license/vulnerability audits, sequential backend/frontend tests, PostgreSQL/Rabbit/Azurite real integration, Worker/Compose restart, functional/OpenAPI/generated client, E2E, coverage, secret scan, image provenance, Graphify, and `git diff --check`.

## Out of scope

- Editable report designer, arbitrary user templates, scheduled report subscriptions, email attachments, public file sharing, OCR, digital signatures, invoice/NFe, or purchase-order implementation.

## Acceptance criteria

- Report requests are durable, idempotent, asynchronous, authorized, observable, and recover after restart.
- PDF/XLSX outputs are semantically and visually verified with approved licenses.
- File bytes never transit into Domain or database columns.
- Unauthorized users cannot infer or download files.
- Storage/report failure does not corrupt procurement state.
- Required gates pass or readiness is denied.

## Report and final response

Create `docs/refactoring/phase-30-report.md` with dependency/license research, architecture, schema, storage policy, templates/reports, security tests, rendered evidence, performance bounds, real-service/Compose results, rollback/retention, risks, and gates. Lead with readiness and links to local sanitized artifacts only.

Do not commit/push/merge, change branches/worktrees, open a PR, provision Azure, or delete blobs/volumes/data without explicit authorization.
