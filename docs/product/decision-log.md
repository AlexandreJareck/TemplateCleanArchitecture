# Decision log

All D23 decisions were presented with their recommended defaults on 2026-10-18 and the owner explicitly answered "accept all recommended" for D23-1 through D23-18. No default was adopted silently. Decisions taken by the blueprint itself (B23-*) are design choices that refine an owner decision without changing it; they can be changed by a later owner decision recorded here.

## Owner decisions (2026-10-18)

| ID | Decision | Alternatives considered | Rationale | Consequences | Deferred |
|---|---|---|---|---|---|
| D23-1 | One organization per installation; no multi-tenancy in the first release ([ADR-0006](adr/0006-single-organization-scope.md)) | Tenant column per row; database per tenant | Academic scope, simpler authorization and data ownership, no tenant-isolation test burden | No `TenantId` anywhere; organization settings are a singleton; installation = organization | Multi-tenancy (would require a new ADR and a migration plan) |
| D23-2 | Angular TypeScript SPA under `Client/Template.WebApp` ([ADR-0005](adr/0005-frontend-stack-and-clean-architecture.md)) | React, Blazor, server-rendered MVC | Owner preference, typed SPA, strong CLI tooling, fits the generated-client approach | Phase 26A decides tool versions and libraries; the folder lives in this repository unless D26A-1 changes it | Toolchain choices (Phase 26A) |
| D23-3 | Buyer-entered supplier quotes; no supplier portal initially | Supplier portal with external accounts; email parsing | Removes external identity, supplier onboarding and public exposure | Every quote records `enteredBy`, source (`Email`, `Phone`, `Document`, `Other`) and attachment metadata | Supplier portal |
| D23-4 | Reserve budget on requisition submission; confirm on final approval; release on rejection, cancellation, withdrawal or expiry | Reserve at approval; reserve at PO issue | Prevents approving work that has no budget; early visibility of commitments | Submission fails when funds are insufficient (BUD-005); one active reservation per requisition (BUD-008) | Encumbrance accounting, actual spend |
| D23-5 | PDF from HTML/CSS templates rendered through Playwright; optional QuestPDF adapter only after license approval ([ADR-0004](adr/0004-file-storage-and-report-engines.md)) | QuestPDF, PDFsharp/MigraDoc, commercial services | Permissive licenses, template reuse, semantic tests | Worker image must carry a supported headless browser; Phase 30 verifies license and runtime | QuestPDF adapter |
| D23-6 | XLSX with ClosedXML; Open XML SDK as the low-level alternative | EPPlus (commercial license), CSV only | MIT license, productive API | Formula-injection handling is mandatory (RPT-007) | — |
| D23-7 | Azurite locally and Azure Blob Storage in Azure, behind an Application port | Local filesystem, MinIO/S3, database BLOBs | Same API locally and in Azure, private containers | `IFileStorage` port; bytes never in PostgreSQL or Domain (FIL-001) | Malware scanning (extension point only) |
| D23-8 | RabbitMQ Worker + transactional Outbox/Inbox; Durable Functions only for long-running workflow ([ADR-0002](adr/0002-separate-worker-host.md), [ADR-0003](adr/0003-durable-functions-boundary.md)) | Azure Service Bus, Hangfire/Quartz, Durable for everything | Reuses the verified RabbitMQ adapter; keeps Durable to the approval process | Three composition roots: WebApi, Worker, WorkflowFunctions | Service Bus migration |
| D23-9 | Local implementation and deployable IaC; no real Azure provisioning without separate authorization | Deploy a demo environment | No cloud cost or credential handling in this program | Phase 33 validates IaC syntax only | Azure deployment |
| D23-10 | English code, contracts and technical docs; pt-BR user-facing labels and localization resources | All Portuguese; all English | Matches repository language rules | API `code` values, rule IDs, permissions and event names are English; `*.pt-BR` resources hold UI text | English UI (structure is ready) |
| D23-11 | Short-lived access token in memory plus rotating refresh token in a Secure, HttpOnly cookie, with an explicit CSRF design | Tokens in localStorage; BFF with server session | No script-readable long-lived credential; keeps the existing JWT API | Phase 24 implements refresh families and CSRF (SEC-* rules, [api-guidelines.md](api-guidelines.md#7-browser-session-and-csrf)) | Backend-for-frontend, MFA |
| D23-12 | Frontend Clean Architecture: framework-free Domain/Application, generated-client adapters in Infrastructure, Angular in Presentation/Bootstrap, automated boundary enforcement | Feature folders only; NgRx-centric | Testable client logic and enforceable boundaries | Architecture tests and lint rules are mandatory gates from Phase 26B | — |
| D23-13 | Versioned amount-based approval levels; the requester cannot give final approval to their own requisition; every bypass is forbidden unless a separate waiver rule explicitly allows it | Single approver; free-form workflow designer | Credible maker-checker control without a rules engine | APR-* rules; approval policy version is pinned per approval instance | Category/cost-center specific chains |
| D23-14 | Require three invited eligible suppliers above an approved threshold; allow a documented insufficient-quotation/direct-purchase waiver with dedicated permission and reason | Always require three quotes; no waivers | Realistic competition with an auditable escape hatch | SRC-* rules and waiver types `DirectPurchase`, `LowValueDirectPurchase`, `InsufficientQuotations`, `NonLeadingSelection` | — |
| D23-15 | Award the whole RFQ to one supplier in the first release; split awards are deferred | Line-level split awards | Simple PO creation (one award → one PO) | A quote must cover every RFQ line to be awardable (SRC-014) | Split awards, partial-coverage awards |
| D23-16 | Only Active suppliers may be newly invited, selected or used for a purchase order; suspension preserves history and blocks new commitments | Warnings only | Real supplier-risk control | SUP-003, SRC-003, SRC-016, PUR-003 | — |
| D23-17 | Allocation by active fiscal period, cost center and currency; no silent overspend or cross-currency reservation; explicit decimal precision and rounding at approved calculation boundaries | Global budget; automatic FX on reservations | Deterministic and testable budget control | BUD-* and CALC rules in [business-rules.md](business-rules.md#calculation-rules-calc) | FX revaluation of commitments |
| D23-18 | Default zero over-receipt tolerance; approved tolerance is configurable; corrections are compensating records with reason rather than destructive edits | Free editing of receipts | Auditability | RCV-* rules; `ReceivingPolicy` v1 tolerance 0 % | Returns to supplier |

## Blueprint decisions (refinements, 2026-10-18)

| ID | Decision | Refines | Rationale |
|---|---|---|---|
| B23-1 | Approval is an **award approval**: one approval instance per requisition, started when the buyer submits an award (competitive or direct). There is no separate pre-sourcing requisition approval. | D23-4, D23-13 | Matches the roadmap sequence (comparison → approval → PO) and D23-4 (confirm on final approval). |
| B23-2 | Besides D23-13, the same user cannot decide two levels of the same approval instance, and the buyer who submitted the award cannot give the final approval (APR-007, APR-008). | D23-13 | Standard maker-checker; adjustable only by a new owner decision. |
| B23-3 | Direct purchases use the same RFQ aggregate with `SourcingMethod = Direct` and a waiver, so quote, award, approval and PO handling are uniform. | D23-14 | Avoids a parallel path that would duplicate rules. |
| B23-4 | Organization base currency is BRL (installation setting); cross-currency quotes are normalized with a persisted PTAX selling-rate snapshot or a permission-protected manual rate. | D23-17 | Deterministic comparison; never a live rate. |
| B23-5 | The organization time zone is `America/Sao_Paulo`; all instants are stored in UTC; dates such as needed-by and fiscal boundaries are local dates in the organization time zone. | D23-17 | Fiscal periods and deadlines need one calendar. |
| B23-6 | Late quotes are rejected; the buyer may extend the RFQ deadline before it passes (reason required). No late-quote waiver. | D23-14 | Smaller rule surface. |
| B23-7 | Rejection is terminal for the requisition; the requester can copy it into a new draft. Withdrawal (before an RFQ is published) returns it to Draft with a new revision. | D23-4 | Keeps approvals immutable. |
| B23-8 | New procurement endpoints return plain resource representations and a `PagedResult` envelope, not `BaseResult`; existing v1/v2 sample endpoints are unchanged. | API compatibility | See [api-guidelines.md](api-guidelines.md). |
| B23-9 | Roles group permissions; authorization checks permissions only; no role is a business-rule bypass (including Administrator). | D23-13 | Required by the Phase 24 handoff. |
| B23-10 | Seeded policy values in this blueprint are policy version 1 and are owner-adjustable data; a change creates a new version and never alters in-flight instances. | D23-13, D23-14, D23-17, D23-18 | Versioned policy requirement. |

## Deferrals

| Item | Reason | Earliest owner |
|---|---|---|
| Split awards and partial-coverage awards | D23-15 | Post-release |
| Supplier portal, email parsing | D23-3 | Post-release |
| Purchase-order amendments (change orders) | Not required by the release definition; a cancelled PO plus a new award covers corrections before receipt | Post-release |
| Returns to supplier, inventory, invoices, payments, tax, NFe, three-way match | Roadmap exclusions | Out of program |
| FX revaluation of committed budget | D23-17 | Post-release |
| Holiday/business-day calendars | Durations are calendar hours in policy v1 | Post-release |
| Category- or cost-center-specific approval chains | D23-13 amount-based only | Post-release |
| Malware scanning of attachments | Extension point only (FIL-006) | Post-release |
| Sample Product/Person/Order removal | Requires replacement and a separate sunset decision ([api-guidelines.md](api-guidelines.md#10-compatibility-and-legacy-samples)) | Separate approval |
| Multi-tenancy, MFA, external identity providers, BFF | D23-1, D23-11 | Post-release |

## Spikes for later phases

| ID | Question | Phase | Exit criterion |
|---|---|---|---|
| SP-1 | PostgreSQL row versioning: `xmin` system column versus an explicit `version` column for ETags | 24 | Chosen mechanism proven by a 412 functional test and a PostgreSQL concurrency test |
| SP-2 | Gapless purchase-order numbering under concurrency (counter row lock) | 32 | Concurrency test with parallel issues produces unique, gapless committed numbers |
| SP-3 | Durable Functions local provider (Azurite storage provider vs. Durable Task Scheduler emulator) and the supported isolated-worker TFM | 31 | ADR addendum with current official sources |
| SP-4 | Playwright browser in the Worker container (image size, fonts, non-root) | 30 | Worker image renders a PDF in an isolated Compose run |
| SP-5 | BCB PTAX endpoint semantics (which bulletin, weekends/holidays fallback) | 25/29 | Fake-server contract tests from current official documentation |
| SP-6 | Angular localization runtime option (D26A-10) | 26A | Decision recorded in Phase 26A |
