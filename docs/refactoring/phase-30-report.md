# Phase 30 report — Files and reporting platform

Specification: `docs/refactoring/handoff-phase-30.md`, executed through `docs/refactoring/execute-phase-30.md` on 2026-10-23.

## Release decision

**Ready.** Every gate is green on the final workspace. The second full gate passed 21 of 22 steps; the remaining step, the Compose baseline, passed 135/135 when rerun alone after a one-line Worker Dockerfile fix (details below). No threshold was lowered and no test was skipped.

A requester asks for an approved report and gets `202 Accepted` at once; the separately deployed Worker claims the durable job with a leased row lock, builds the content from server-owned immutable sources at the request's as-of instant, renders it (headless Chromium for PDF, ClosedXML for XLSX), writes the bytes to a private Azure Blob-compatible container (Azurite locally) with a server-computed SHA-256, verifies them by reading them back and only then marks the file available and the job ready in one commit. The browser polls with bounded back-off and downloads through the API; no storage URL, key or credential ever leaves the server. Failures expose a safe catalogued code and never touch procurement state.

Gate iterations (no threshold was lowered):

1. The first full gate (2026-10-23 14:12–14:45) failed 3 of 22 steps:
   - `06e-compose-baseline` 124/135: every report attempt in the real Worker failed with `InvalidOperationException` because the Worker composition root had no `IPermissionReader` (the Identity adapter, which provides it, is not composed by the Worker), so `ProcurementWriter` could not be resolved. Fixed with a system-actor `WorkerPermissions` and a new `Tests/Template.Worker.Tests` that composes the real Worker host and resolves and runs both report use cases; a controlled mutation (registration removed) makes it fail with the original error. Two snapshot checks also failed because the report section deliberately grants synthetic roles; the restart idempotency checks now compare with the state after that section.
   - `07b-coverage`: Application branch coverage 85.08% (< 90%). New edge tests (status labels, parameter edge values, view states, guard paths, failure classification, lease-loss paths, incomplete comparisons with manual rate and waiver, submitted awards, several currencies, withdrawals after the as-of instant) raised it to 90.39%; one of them found a real defect: a Ready job past its retention was shown as `Expired` without the `retry` action — the representation now derives its actions from the displayed status.
   - `06-test-postgresql` 60/61: the Phase 27 test `T_BUD_001_b_OverlappingPeriods_CreatedConcurrently_OnlyOneWins` is intermittent (about one run in six). With three concurrent writers the winner can wait for a second PostgreSQL `deadlock_timeout` (1 s) before committing, longer than the 0.1 s observation window of the deadlock-loser re-evaluation (D27-18). The bounded window now covers about 3 s on that rare path only; the assertion now reports the losers' codes.
2. The second full gate (2026-10-23 18:21–18:50) passed 21 of 22 steps; `06e-compose-baseline` was 133/135. The XLSX job, the storage outage and the recovery passed, but every PDF attempt in the real Worker container failed with `Win32Exception` (`report.unexpected`, then `report.attempts-exhausted`): `dotnet publish` writes the Playwright Node driver with owner-only execute permission (`rwxr--r--`, root), and the Worker runs as the non-root `app` user, so the driver process could not start. The in-process suites could not see this because they run the driver as the developer. Fix: the Worker Dockerfile also applies `chmod -R a+rX` to `/app/.playwright` (D30-18; the process stays non-root). After rebuilding the image, `node --version` runs as UID 1654. The Compose baseline was then rerun alone on the final workspace (2026-10-23 18:50–18:56): **135/135** (`p14-baseline-20261023185027`). The PDF rendered by the container (`report-requisition.pdf`, 31,850 bytes, `%PDF-1.4`) embeds `Roboto-Regular` and `Roboto-Bold`. This Dockerfile line is the only change after the second gate; it affects only the Worker image, which the rerun covers end to end.

## Preconditions and starting state

- HEAD `fe4b471` (`main`, Phase 29 merged through pull request 30), clean tree; branch `refactor/phase-30-files-reports`. Phases 23–29 are `Ready`; Phase 29 gate 20/20 green.
- Baseline before the change: architecture 68, unit 626, integration 233, PostgreSQL 56, RabbitMQ 27, Elasticsearch 13, Redis 23, functional 359, Vitest 56, Playwright 8; OpenAPI = `docs/refactoring/baseline-phase-29`; last migration `20261023025916_Phase29SourcingQuotations`; package graph and licenses as recorded in the Phase 29 report.
- D23-5 (HTML/CSS → PDF with Playwright), D23-6 (ClosedXML) and D23-7 (Azurite/Azure Blob behind a port) are approved; ADR-0004 accepted.

## Dependency and license research (verified 2026-10-23 on nuget.org, GitHub and MCR)

| Package / asset | Version | License | Scope | Notes |
|---|---|---|---|---|
| `Microsoft.Playwright` | 1.64.1 (latest) | MIT | Reporting (Worker only) | netstandard2.0; ships the Playwright Node driver; browser revision 1248 = the same Chromium the frontend Playwright 1.64.1 uses |
| Chromium headless shell | revision 1248 | BSD-3-Clause (Chromium) + bundled notices | Worker image / developer cache | installed by the package's own driver (`install --with-deps --only-shell chromium`); approved by D23-5 |
| `ClosedXML` | 0.105.1 (latest) | MIT | Reporting | deps `ClosedXML.Parser` 2.0.0 (MIT), `DocumentFormat.OpenXml` 3.1.1 (MIT, lowest in its range), `ExcelNumberFormat` 1.1.0 (MIT), `RBush.Signed` 4.0.0 (MIT) |
| `SixLabors.Fonts` | **1.0.1 (pinned)** | Apache-2.0 | transitive of ClosedXML | ClosedXML accepts `[1.0.0, 3.0.0)`; 2.x and later use the Six Labors Split License, which is outside the policy, so the version is pinned centrally (D30-9) |
| `Azure.Storage.Blobs` | 12.30.1 (latest) | MIT | Files | deps `Azure.Core` 1.60.0, `Azure.Storage.Common` 12.29.0, `System.IO.Hashing`, `Microsoft.Identity.Client` 4.84.2 (all MIT) and `Apache.Arrow` 22.1.0 |
| `Apache.Arrow` | 22.1.0 | Apache-2.0 (declared as a license file) | transitive | the file is the Apache-2.0 text plus the Apache-2.0 FlatBuffers notice; `scripts/license-inventory.ps1` now accepts it only when the file's SHA-256 matches the reviewed text (`f49cc7be…c570`) — no new license admitted |
| `PdfPig` | 0.1.16 (latest stable) | Apache-2.0 | test only (`PrivateAssets=all`) | PDF text/page/font assertions |
| Azurite image | `mcr.microsoft.com/azure-storage/azurite:3.37.0@sha256:830430c1…71fd5` | MIT | Compose / tests | latest release (2026-08-26) |
| Roboto Regular/Bold | 2.138 (googlefonts GitHub release, `roboto-unhinted.zip` SHA-256 `70f64c71…2a728`) | Apache-2.0 (license file committed as `Assets/Fonts/LICENSE-Roboto.txt`) | embedded in every PDF | font SHA-256: Regular `f3edb805…24c6`, Bold `2ca2b3bf…06c8`; later Roboto releases and Noto/Liberation are OFL, which the allowlists do not contain (D30-8) |

QuestPDF, PDFsharp/MigraDoc, EPPlus, commercial or external rendering services and unapproved native binaries were not used. No license conflict required an owner decision.

## Architecture

```
WebApi  ── POST /report-jobs ──► RequestReportCommand ──► ReportJob (Pending) + Outbox report.requested + audit
                                                         │
Worker  ── ReportWorkerService (lanes) ──► ReportJobProcessor
           1. lock due job FOR UPDATE SKIP LOCKED, Claim (lease 5 min) and commit
           2. IReportBuilder (Application) builds ReportDocument / WorkbookDocument from immutable history at AsOfUtc
           3. IPdfRenderer (Playwright/Chromium) or ISpreadsheetWriter (ClosedXML) → temp file, signature and size check
           4. StoredFile Pending committed → IFileStorage.WriteAsync (streamed, SHA-256, create-only) → read-back digest
           5. StoredFile Available + ReportJob Ready + Outbox report.ready + audits in one commit
        ── ReportMaintenance (every 15 min): Ready past 7 days → Expired; FIL-005 cleanup deletes bytes, marks Deleted
WebApi  ── GET /report-jobs/{id}/content ──► authorization (RPT-006) → IFileStorage.OpenReadAsync → streamed attachment
Notification consumer ── report.ready / report.failed ──► one local in-app delivery for the requester (D30-11)
```

Layering: Domain `Files` and `Reporting` modules (metadata and lifecycle only, no bytes, streams or storage keys; architecture-tested); Application ports, document models, builders and use cases (no vendor package); `Infrastructure.Files` (Azure Blob SDK) composed by WebApi and Worker; `Infrastructure.Reporting` (Playwright, ClosedXML, template and fonts) composed only by the Worker — the WebApi never references it (architecture-tested).

## Domain model and states

| Element | States / content | Invariants |
|---|---|---|
| `ReportJob` (Reporting) | `Pending → Running → Ready → Expired`; `Running → Pending` (transient failure with attempts left, or graceful release); `Running → Failed`; `Pending → Cancelled` | catalog type (RPT-009); parameter snapshot ≤ 4,000 characters with a 64-hex fingerprint; `AsOfUtc` ≤ request instant; pinned template and `ReportPolicy` versions; claim only when due or the lease expired; a stale claim without attempts left fails `report.attempts-exhausted`; progress renews the lease; completion only by the current claimant and idempotent for the same file; failure codes only from the catalogue (RPT-005); cancel only Pending |
| `StoredFile` (Files) | `Pending → Available → Deleted`, `Pending → Deleted` | owner type/ID, sanitized name (last path segment, control/reserved/path characters replaced, 1–255), media type, `Confidential`/`Internal`; `Available` needs length > 0 within the size policy and a lowercase 64-hex SHA-256; the same digest again is a no-op, another one a conflict; cleanup selection after 24 h (pending) or at expiry (available) (FIL-005) |
| `ReportPolicy` v1 | 3 attempts, back-off 30 s doubling, retention 7 days, lease 5 min, 10,000 rows | D30-2 |
| `FileRetentionPolicy` v1 | pending 24 h, report outputs 7 days, 50 MB | FIL-005 |

## Schema and migration

Additive migration `20261023161337_Phase30FilesReports` (application database; Identity unchanged), generated with `dotnet-ef` 10.0.13 installed only in the session scratchpad and reviewed:

| Table | Content and constraints |
|---|---|
| `FilesStoredFiles` | metadata only (no `bytea`, URL or storage-key column); checks `Length > 0` and "Available ⇒ length and 64-character digest"; indexes (owner type, owner), (status, created), (status, expiry) |
| `ReportingReportJobs` | parameter snapshot as `jsonb`, fingerprint `char(64)`; checks attempts 0–10, progress 0–100, "Ready ⇒ output file"; FK to `FilesStoredFiles` (restrict); partial unique index `IX_ReportingReportJobs_Active` on (requester, type, fingerprint) where status is `Pending`/`Running`; queue indexes (status, next attempt), (status, lease expiry), (status, expiry), (requester, requested) |

Two `CreateTable` and nine `CreateIndex` operations, no change to existing tables, no backfill; `Down` drops only the two tables. Rehearsed on PostgreSQL 16.15 by `Migration_IsAdditive_HasNoByteColumns_AndRollsBackToPhase29_ThenReapplies`.

## Storage policy

- One private container (`FileStorageSettings:ContainerName`, default `template-files`) created with `PublicAccessType.None`; anonymous blob reads and listings are rejected (proven against real Azurite).
- Blob names `v1/{fileId:N}` are derived from the metadata ID inside the adapter (D30-3); nothing stores or returns them.
- Writes are streamed, create-only (`If-None-Match: *`), hashed while streamed, bounded by the size policy (an oversized write throws `FileContentTooLargeException` and leaves no blob), retried at most `MaxRetries` (default 2) within `OperationTimeout` (default 30 s); caller cancellation is never reported as unavailability.
- Every output is read back and re-hashed before it becomes Available; generated outputs must match their signature (`%PDF-`, `PK\x03\x04`).
- Downloads stream from storage through the API (no SAS or delegated URL, D30-10).
- Malware scanning: `IMalwareScanner` with a no-op adapter (`NotScanned`); an `Infected` verdict would fail the attempt with `report.output-invalid`. **No malware protection is claimed** (FIL-006).
- Retention: report outputs 7 days, then the job is `Expired` and the retention pass deletes the bytes and marks the metadata `Deleted`; pending orphans of interrupted writes are deleted after 24 h. Business-record attachments will follow the owner's retention when uploads arrive (D30-13).
- Disabled storage (`Enabled=false` outside Test) is an always-unavailable adapter: attempts retry with `report.storage-unavailable`, downloads answer 503, procurement is unaffected; readiness reports the dependency as optional/Degraded.

## Templates and reports

| Report | Template | Content and as-of rule | Parameters |
|---|---|---|---|
| `RequisitionPdf` | `requisition-pdf.v1` | latest submitted revision at `AsOfUtc` from its immutable snapshot (or the unchanged draft, else `report.source-changed`), status and timeline at `AsOfUtc`, lines with server totals (CALC-002), attachment metadata, requisition policy version | `requisitionId` (REQ-011 visibility) |
| `QuoteComparisonPdf` | `quote-comparison-pdf.v1` | the comparison sequence pinned at request (immutable CALC-004/005 entries with supplier snapshot names), RFQ line snapshot, persisted rates (PTAX or manual with reason), waivers and awards decided by `AsOfUtc`, advisory recommendation, sourcing policy version | `rfqId` (`sourcing.quotes.read`); 422 `report.source-not-available` before a comparison exists |
| `BudgetConsumptionXlsx` | `budget-consumption-xlsx.v1` | allocations of one fiscal period reconstructed at `AsOfUtc` (amount history, each reservation's last change; available = amount − reserved − committed, CALC-003); sheets Resumo / Reservas / Movimentos; totals only for a single currency | `fiscalPeriodId`, optional `currency`, `costCenterId` (`budget.read`) |
| `SupplierQuotesXlsx` | `supplier-quotes-xlsx.v1` | quote revisions received in a local-date window (≤ 366 days) with invitation snapshots, persisted comparison outcome and selection; metrics per supplier: invitations, responses, response rate, ranked comparisons, average rank, first places, submitted selections, average delivery days | `from`, `to`, optional `supplierId` (`sourcing.quotes.read` + `suppliers.read`) |

Every report states generation and as-of instants with the IANA time zone, filters, currency, the report policy version, the template version and the job ID; the domain policy versions (requisition, sourcing) are shown where they apply (RPT-004). Language pt-BR (D30-7). `PurchaseOrderPdf` arrives with Phase 32 on this platform.

PDF safety: values are HTML-encoded into a fixed skeleton generated in code (no template language), `Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; font-src data:`, JavaScript disabled, offline context, every request aborted, fonts as data URIs (body and footer), tagged PDF with outline, A4 with page numbers (D30-8). XLSX safety: no formula is ever written, quote prefix on `= + - @ \t \r` text, control characters removed, explicit widths (no font measurement), workbook package validated on save (D30-9).

## API contract (v2)

| Operation | Authorization | Notes |
|---|---|---|
| `POST /api/v2/report-jobs` | `reports.request` + data permission (RPT-002) + `Idempotency-Key` | `202` with the job, `Location: /api/v2/report-jobs/{id}`, `Retry-After: 2`, `ETag`; an identical active request returns the existing job (also 202) |
| `GET /api/v2/report-jobs` | `reports.request` (`scope=mine`) or `reports.read-all` (`scope=all`) | paged, `status`, `reportType`, `sort` |
| `GET /api/v2/report-jobs/types` | authenticated | catalog with required permissions, parameters and `available` |
| `GET /api/v2/report-jobs/{id}` | requester (with the data permission) or `reports.read-all`; otherwise 404 | status, progress, attempts, safe failure code, `contentUrl` only while downloadable, `allowedActions` |
| `POST /api/v2/report-jobs/{id}/cancel` | requester, `If-Match` | Pending only (409 otherwise), 428/412 |
| `GET /api/v2/report-jobs/{id}/content` | as status | attachment with sanitized name, exact media type, `nosniff`, `private, no-store`; 409 `report.not-ready`, 410 `report.expired`, 503 `file.unavailable`, 404 |

New stable codes: `report.type-unknown` (400), `report.source-not-available` (422), `report.not-ready` (409), `report.expired` (410), `file.unavailable` (503). OpenAPI: v1 identical to `baseline-phase-29`; v2 adds exactly these 6 operations and 5 schemas (`ReportJobView`, `ReportJobViewPagedResult`, `ReportParametersView`, `ReportTypeView`, `RequestReportBody`); no existing operation or schema changed. New baseline `docs/refactoring/baseline-phase-30`; the frontend client was regenerated from it (7 additive generated files).

## Events and notifications

`procurement.report.requested`, `procurement.report.ready` and `procurement.report.failed` v1 (aggregate `ReportJob`; payload job ID, type, requester and, when failed, the safe code; no file ID, name or storage detail). They are staged in the Outbox in the same commit as the state change. The notification queue additionally binds `report.ready`/`report.failed`; the planner queues one local in-app delivery for the requester (`RecipientType = User`). Proven once-only over real RabbitMQ 4.3.6 with a duplicate publish.

## Screens (Angular)

`/reports` (guard `reports.request`, nav entry): request form showing only the types the server marks available with the parameters of the chosen type (fiscal periods from the budget API), the user's jobs with status badge, progress bar, attempts, safe failure message, expiry, as-of, and the server-allowed actions (download, cancel, generate again); bounded back-off polling (2 s doubling to 30 s, "still running" after 10 minutes); a polite live region announces status changes; downloads fetch the API content path (validated pattern) as a Blob and save it through a temporary object URL (Infrastructure `BrowserFileSaver`); Blob Problem Details are parsed so `report.expired` (new error kind `gone`) and `file.unavailable` are shown. Deep links "Gerar PDF da requisição" (requisition detail) and "Gerar mapa comparativo (PDF)" (RFQ detail when a comparison exists). Generated DTOs stay in Infrastructure; no storage URL or secret reaches browser configuration (dependency-cruiser rules unchanged and green).

## Security review (STRIDE, NFR-SEC-8)

| Threat | Control | Evidence |
|---|---|---|
| Spoofing a requester | bearer/session authentication on every action; `reports.request` policy; data permission re-checked on every read | permission matrix (F), `T_RPT_002_*`, `T_RPT_006_a` |
| Tampering with templates or content | no template/markup/script input (architecture rule), HTML encoding, CSP, JS off, offline; XLSX without formulas | `T_RPT_008_*` (I, A), `T_RPT_007_a` (I), E2E |
| Tampering with stored bytes | create-only writes, server SHA-256, read-back verification, signature check | `T_FIL_002_d`, Azurite `T_FIL_002_g` |
| Repudiation | audits `report.requested/ready/failed/cancelled/expired`, `file.stored/expired/downloaded` (confidential) with allow-listed IDs only | `T_RPT_001_a`, `T_FIL_003_a`, functional audit count |
| Information disclosure | other users get 404 (no existence leak); private container; no storage URL/key in API, events, logs or browser; logs carry IDs, types, codes and sizes only | `T_RPT_006_a`, Azurite `T_FIL_003_e/f`, `T_RPT_005_f` |
| Denial of service | bounded rows/size/time, 3 attempts, lease, bounded render concurrency, retries with back-off; storage/browser outages degrade, never block procurement | `T_RPT_005_*`, `T_NFR_PERF_6_a`, Compose outage checks |
| Elevation of privilege | Worker runs as system actor without permissions and non-root; WebApi cannot render; renderer only in the Worker image | `WorkerCompositionTests`, architecture rules, Dockerfile |

## Tests and counts

| Suite | Before | After | Phase 30 additions |
|---|---|---|---|
| Architecture | 68 | 71 | Files/Reporting module boundaries and no public setters; vendor types confined to their adapters and the WebApi without the renderer; no bytes/keys in the domain, no template input, encoding/CSP/JS-off present; controlled mutations; controller count 17; composition roots and Worker references |
| Unit | 626 | 717 | `ReportJobTests` (domain, mock-free), `ReportJobHandlerTests`, `ReportProcessorTests`, `ReportEdgeCaseTests`, `ReportBuilderEdgeTests` (port fakes), report envelope mapping |
| Integration | 233 | 256 | real Chromium PDF (PdfPig: pages, text, headings, escaped markup, fonts, footer, tagged), browser crash/timeout/launch failure/cancellation, ClosedXML injection/types/formats/no `<f>`, 10,000-row bound, the four reports from real builders and EF repositories, storage adapters and registration, reporting repositories |
| PostgreSQL 16.15 | 56 | 61 | migration additive + rollback, concurrent identical requests → one job, four concurrent workers process 12 jobs once each with one verified output, crashed lease recovered, retention pass |
| RabbitMQ 4.3.6 | 27 | 28 | report.ready notifies the requester once (duplicate publish), report.requested reaches only the projection |
| Azurite 3.37.0 (new) | — | 6 | streamed write/read-back digest, create-only, size limit without leftovers, private container, wrong key, outage/recovery with data after restart, cancellation |
| Worker composition (new) | — | 1 | real Worker host resolves and runs the report use cases |
| Elasticsearch / Redis | 13 / 23 | 13 / 23 | unchanged |
| Functional (HTTP) | 359 | 362 | 202 journey with replay/reuse, in-process Worker attempt, download headers, visibility, 503 and 410; data permissions, catalog, validation, idempotency key; workbook, cancel 428/412/404, retry; permission matrix +4 report endpoints |
| Vitest | 56 | 67 | polling policy, parameters, retry, failure keys; adapter mapping, headers, download path guard, Blob Problem Details, catalog filter |
| Playwright + axe | 8 | 11 | request → queued → running → ready → download; failed/expired/retry/410 on mobile; no report center without permission; zero axe violations |

### Coverage (gate step `07b-coverage`, second gate)

| Module | Line % | Branch % | Ratchet (line / branch / sources) |
|---|---:|---:|---|
| Domain | 99.72 | 93.65 | 99 / — / 44 |
| Application | 97.78 | 90.39 | 96 / 90 / 121 |
| Infrastructure.Files (new) | 76.96 | 79.73 | 75 / 78 / 5 |
| Infrastructure.Reporting (new) | 93.92 | 88.97 | 92 / 87 / 4 |
| Infrastructure.Persistence | 81.00 | 69.41 | 65 / 55 / 33 |
| Infrastructure.Messaging | 70.01 | 80.19 | 58 / 65 / 15 |
| WebApi | 76.79 | 61.54 | 67 / 50 / 68 |

`Tests/test-coverage-policy.json` adds the two new modules at `floor(measured − 1)` (Files 75/78, Reporting 92/87) and raises the source minimums (Domain 44, Application 121, Persistence 33, WebApi 68), so the new files cannot drop out of measurement unnoticed. No existing threshold was lowered. The Files adapter's remaining lines are the real-Blob paths that only the opt-in Azurite suite (6/6, not instrumented) exercises.

### Rule-to-test matrix

| Rule | Tests |
|---|---|
| FIL-001 | `T_FIL_001_a` (U), `Phase30_Files_Domain_Holds_Metadata_Only…` (A), `Migration_…HasNoByteColumns…` (P), `T_FIL_001_b/c` (I, Azurite) |
| FIL-002 | `T_FIL_002_a..d`, `T_FIL_002_h/i` (U), `T_FIL_002_e/f` (I), `T_FIL_002_g` (Azurite) |
| FIL-003 | `T_FIL_003_a/b` (H), `T_FIL_003_c/d` (I), `T_FIL_003_e..h` (Azurite), `T_RPT_001_f` (F), E2E download |
| FIL-005 | `T_FIL_005_a` (U), `T_RPT_005_i`, `T_RPT_006_b` (H), `T_FIL_005_b` (P), repository selection (I) |
| FIL-006 | `T_FIL_006_a` (I), infected verdict in `T_FIL_002_d` (H) |
| RPT-001 | `T_RPT_001_a..d`, `T_RPT_001_h` (H), `T_RPT_001_e` (P), `T_RPT_001_f/g` (F), E2E |
| RPT-002 | `T_RPT_002_a/b/d` (H), `T_RPT_002_c` (F), permission matrix (F), E2E permission |
| RPT-003 | `T_RPT_003_a` (U), `T_RPT_003_a..l` (H), four reports from repositories (I) |
| RPT-004 | `T_RPT_004_a/c` (U/H), `T_RPT_004_b` (I, PDF/XLSX semantic assertions) |
| RPT-005 | `T_RPT_005_a..e` (U), `T_RPT_005_f..s` (H), `T_RPT_005_l/m` (I), `T_RPT_005_n/o` (P), `T_RPT_005_p` (W), Compose restart/outage |
| RPT-006 | `T_RPT_006_a..c` (U/H), `T_RPT_001_f` (F, 410), E2E expired |
| RPT-007 | `T_RPT_007_a` (I), injection payloads in `T_RPT_004_b` (I), `T_RPT_003_e` (H) |
| RPT-008 | `T_RPT_008_a..c` (I/U), `Phase30_…No_Templates` (A), `T_RPT_002_c` (F, `template` member ignored) |
| RPT-009 | `T_RPT_009_a` (U/H), `T_RPT_002_c` (F), E2E catalog |
| NFR-PERF-6 | `T_NFR_PERF_6_a` (10,000 rows), requisition PDF render < 15 s in `T_RPT_004_b` (I) |

## Rendered evidence and performance bounds

Samples produced by `T_RPT_004_b` and the adapter tests (synthetic data) in `artifacts/validation/phase30/samples/` (git-ignored): `requisicao-REQ-2027-000042.pdf`, `comparacao-RFQ-2027-000007-c1.pdf`, `orcamento-FY27-20270301-1000.xlsx`, `cotacoes-fornecedores-20270201-20270301.xlsx`, `adapter-escaping.pdf`, `adapter-injection.xlsx`. The PDFs were reviewed page by page: embedded Roboto, context block, encoded markup and formula-like text shown literally, repeated table headers, page numbers. Visual QA found amounts and dates wrapping inside cells of the 12-column ranking table; the template now keeps numeric and date cells on one line and uses a compact style for tables with 9 or more columns (re-reviewed). Bounds: requisition/comparison PDFs render in about 1–3 s on this machine (limit 15 s), a 10,000-row workbook is written well under the 30 s limit, rendering timeout 60 s, 2 concurrent renders and 1 job lane per Worker by default.

## Real services and Compose

`scripts/compose-baseline.ps1` (final run `p14-baseline-20261023185027`, isolated project, clean volumes, synthetic credentials, ends with `stop`, deletes nothing): **135/135** checks, up from 116. The run uses Azurite 3.37.0 with `--skipApiVersionCheck` and an account read from the per-run environment, and the Worker image with the Chromium headless shell. The 19 new checks:

- the exact Phase 30 OpenAPI;
- private container and file-storage readiness (`Degraded` only when unavailable);
- synthetic roles granted, fiscal period and requisition created through the API;
- XLSX `202` → `Ready` → streamed download (`PK` signature), metadata in PostgreSQL (`Available`, 64-hex digest) and bytes only in Azurite;
- requisition PDF `Pending` while the Worker is stopped, then `Ready` after restart, rendered by the container's Chromium, and a streamed download (`%PDF` signature);
- storage outage: download `503 file.unavailable`, attempts retried with `report.storage-unavailable`, procurement API still operating;
- recovery: the job completes on attempt 2;
- after a full stack stop/start, the earlier output still downloads from the `azurite-data` volume.

The restart idempotency snapshot is taken after the report section, because that section deliberately adds synthetic data. The evidence folder keeps the downloaded samples and the WebApi/Worker log tails. The JWT key is absent from all evidence.

## Rollback and retention

- API: additive; previous clients are unaffected. Rolling back removes the 6 operations.
- Application: deploy the Phase 29 WebApi/Worker after stopping report writers. Outbox rows of the three report event types would be dead-lettered by a Phase 29 consumer as unsupported, so drain or retain them first.
- Schema: `dotnet ef database update 20261023025916_Phase29SourcingQuotations` drops both tables (loss of job history and file metadata — export first when needed); rehearsed on PostgreSQL 16.15.
- Storage: the Azurite volume `azurite-data` and any container content are kept; delete blobs only with an operator-approved procedure (the retention pass already removes expired outputs). No blob, volume or data was deleted outside synthetic test resources.
- Broker: two additional bindings on the notification queue; remove only with an approved procedure.
- Frontend: `/reports` and the deep links are additive.
- Retention: report outputs 7 days; pending orphans 24 h; job rows are kept (Expired) for audit.

## Residual risks and deliberate limits

- Attachment byte upload (`POST /api/v2/files`) is not implemented (D30-13); requisition and quote attachments stay metadata-only.
- No malware scanning is performed (no-op hook only).
- No scheduler: the retention pass runs inside the Worker every 15 minutes; scheduled report subscriptions, e-mail and PO PDF are later phases.
- Azurite needs `--skipApiVersionCheck` with Azure.Storage.Blobs 12.30.1 (D30-12); Azure Blob Storage itself was not provisioned or tested.
- Reports are pt-BR only (D30-7); cost-center labels in the budget workbook use current master data (amounts never do).
- The Worker image downloads the Chromium headless shell from the Playwright CDN during the image build (revision pinned by the package version, not by a digest).
- The phase-29 flaky test (D27-18 window) is mitigated, not eliminated: the window now covers about 3 s.
- Frontend tooling on this machine: Node 24.22.0 from the official zip (SHA-256 verified against `nodejs.org/dist/v24.22.0/SHASUMS256.txt`) and pnpm 12.14.0 via Corepack in the session scratchpad; with `core.autocrlf=true` formatting was checked with `prettier --check . --end-of-line auto`. No frontend package or lockfile changed.

## Gates

Second full gate `powershell -ExecutionPolicy Bypass -File .\scripts\run-phase-validation.ps1 -ComposeBaseline` (2026-10-23 18:21:46–18:50:15, SDK 10.0.402), then the Compose baseline alone after the Dockerfile fix:

| Step | Result |
|---|---|
| `00-git-status`, `01-restore`, `02-build-webapi`, `02b-build-worker` | exit 0; builds 0 warnings, 0 errors |
| `03-test-architecture` | 71/71 |
| `04-test-unit` | 717/717 |
| `05-test-integration` | 256/256 (real headless Chromium 1248 and ClosedXML) |
| `06-test-postgresql` | 61/61 on `postgres:16.15` (the previously flaky `T_BUD_001_b` also passed 8/8 full-suite runs in isolation) |
| `06b-test-rabbitmq` | 28/28 on `rabbitmq:4.3.6-management` |
| `06c-test-elasticsearch` | 13/13 on `elasticsearch:9.5.5` |
| `06f-test-redis` | 23/23 on `redis:8.10.2` |
| `06q-test-worker` (new) | 1/1 |
| `06p-test-azurite` (new) | 6/6 on `azurite:3.37.0` |
| `06e-compose-baseline` | 133/135 in the gate; **135/135** in the rerun after the Dockerfile fix (see Release decision) |
| `07-test-functional` | 362/362 |
| `07b-coverage` | OK (coverage table above; 717 / 256 / 362 discovered = executed = passed) |
| `08-vulnerable-packages` | no vulnerable packages |
| `09-docker-compose-config` | OK with `.env.example` placeholders only; every image pinned `version@sha256` |
| `10-capture-openapi` | v1 = `baseline-phase-30` = `baseline-phase-29`; v2 = `baseline-phase-30` (+6 operations, +5 schemas) |
| `11-graphify` | 13,126 nodes, 34,650 edges, 468 communities |
| `12-git-diff-check` | exit 0 |
| `13-secret-scan` | OK: 7 settings files, 8 Compose/env template files, compromised key absent from 10,914 files |

Separate checks on the final workspace:

- `scripts/license-inventory.ps1 -Label phase30`: 178 packages, 0 outside the policy (the transitive `System.Security.Cryptography.ProtectedData` 4.5.0 had no SPDX expression, so it is pinned centrally to the MIT 10.0.13; Apache.Arrow is accepted by its verified license-file SHA-256).
- `scripts/package-graph-snapshot.ps1 -Label phase30 -CompareWith phase29`: 372 production / 677 test entries. The 89 production and 206 test differences all come from the two new adapter projects, the two new test projects and their packages; no existing project's package changed.
- `dotnet list … --deprecated`: only the xunit 2.9.3 family (unchanged).
- Frontend (pinned Node 24.22.0, pnpm 12.14.0): Vitest 67/67, Playwright 11/11 with axe (0 violations), `pnpm validate` green (lint, type check, Prettier with `--end-of-line auto`, dependency-cruiser without violations and its mutations rejected, Orval output clean against `baseline-phase-30`), licenses 458 approved, `pnpm audit` without known vulnerabilities.
- VSIX and the older infrastructure rehearsals were not rerun (out of scope, unchanged).

Decisions D30-1..D30-18 are recorded in `docs/product/decision-log.md`; D30-15..18 come from the gate iterations (Worker system-actor permissions, the widened D27-18 observation window, the ProtectedData pin, and the driver permissions in the Worker image).
