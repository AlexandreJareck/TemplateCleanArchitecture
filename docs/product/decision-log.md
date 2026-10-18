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
| B23-11 | Approval policy v1 uses 120 h step duration and a 72 h reminder (calendar hours), instead of the first draft's 72 h / 48 h, so a step opened on a Friday does not escalate over the weekend. Approved by the owner on 2026-10-18. | D23-13 | No business-day calendar in the first release. |

## Phase 24 implementation decisions (2026-10-18)

Design choices made while implementing D23-11 and SEC-001..010 in Phase 24. They refine the blueprint without changing an owner decision; details and evidence are in `docs/refactoring/phase-24-report.md`.

| ID | Decision | Refines | Rationale |
|---|---|---|---|
| D24-1 | `POST /api/v2/auth/sessions` verifies credentials without signing in and **does not rotate the security stamp**; the legacy `authenticate`/`start` routes keep rotating it on every sign-in, which revokes v2 families at their next refresh and rejects their access tokens immediately. | SEC-001, SEC-006 | Several concurrent sessions (SEC-010) are impossible if every login invalidates the others; the legacy behavior is preserved deliberately. |
| D24-2 | Session families, refresh-token hashes, audit records and idempotency records live in the **application database**; Identity keeps users, roles and role claims. | AUD-001, ADR-0001 | Session changes and their audit records commit together, and later procurement changes can share the same audit table in one transaction. Password-change revocation crosses databases, so it is enforced twice: by the stamp binding and by an explicit revoke-all after the Identity commit. |
| D24-3 | The CSRF HMAC key is derived with HKDF-SHA256 from `JwtSettings:Key` (info `template.session.csrf.v1`); **no new secret** is introduced. | NFR-SEC-1, api-guidelines §7 | Key separation without another secret to provision; rotating the signing key also rotates every CSRF binding. |
| D24-4 | Access tokens issued by a session carry the registered `sid` claim (family ID); bearer validation rejects them as soon as the family is revoked or expired. Legacy tokens have no `sid` and are unaffected. | SEC-006 | Logout and revoke-all take effect immediately instead of after the 15-minute token lifetime. |
| D24-5 | Login answers `200 OK` (credential exchange, like an OAuth token endpoint), not `201`. | api-guidelines §2 | No addressable session resource is returned. |
| D24-6 | SEC-007 uses `System.Threading.RateLimiting` fixed-window partitions inside an action filter on the two endpoints (login: IP + normalized user name; refresh: IP), with SHA-256 partition keys. | SEC-007 | The user-name partition needs the bound body; no other endpoint is affected. |
| D24-7 | The stable `code` extension is added only to Problem Details of Phase 24+ endpoints (marker attribute); legacy v1/v2 bodies stay byte-compatible. `ErrorCode` gains values 6–11 (appended). | api-guidelines §5, §10 | Contract compatibility. |
| D24-8 | **SP-1 closed:** explicit `bigint Version` concurrency token on `VersionedAggregateRoot`; a guard unique index (`AccessRefreshTokens(FamilyId, Sequence)`) violated by a concurrent writer is translated to the same `ConcurrencyConflictException`. | SP-1 | Proven by the PostgreSQL concurrency tests and the 412/428 functional test. |
| D24-9 | The seeded default user receives **no** role; roles are seeded with their permissions only when missing, and the legacy `Admin` role grants no permission. | B23-9, B23-10 | The default user's password is a committed sample value; assigning roles is Phase 25 user management. |
| D24-10 | `Idempotency-Key`, `If-Match`/`ETag` and `202 Accepted` are reusable primitives exercised by a test-only fixture controller; the first production adopters arrive with Phase 25 resources. Session endpoints never use idempotency storage (it would persist tokens). | api-guidelines §4, §6, §8 | Avoids speculative production endpoints. |

## Deferrals

The owner confirmed every deferral on 2026-10-18: Procurement Hub is an academic study project, and these items stay outside Phases 23–33. The recommendation column records how each one would be approached if it is ever reopened, which needs a new owner decision.

| Item | Reason | Recommendation if reopened | Existing preparation |
|---|---|---|---|
| Split awards and partial-coverage awards | D23-15 | Reopen only on real demand; introduce award lines and one PO per awarded supplier | Award references one quote revision |
| Supplier portal, email parsing | D23-3 | Largest security-surface increase (external accounts); keep out | Quotes record `Source`; a portal would be a new source |
| Purchase-order amendments (change orders) | Not required by the release definition; a cancelled PO plus a new award covers corrections before receipt | Versioned PO revisions: increases re-run approval, decreases release budget | Immutable PO snapshot and version |
| Returns to supplier, inventory, invoices, payments, tax, NFe, three-way match | Roadmap exclusions | Integrate with an ERP instead of building | Integration events are the outbound seam |
| FX revaluation of committed budget | D23-17 | Keep the commitment fixed by the recorded rate | Exchange-rate snapshots are stored |
| Holiday/business-day calendars | Durations are calendar hours in policy v1 | Keep calendar hours; B23-11 lengthens step durations instead | Durations are policy data |
| Category- or cost-center-specific approval chains | D23-13 amount-based only | Add an optional dimension to policy levels, never a rules engine | Policies are versioned and pinned per instance |
| Malware scanning of attachments | Extension point only (FIL-006) | Optional ClamAV container adapter (Phase 33 at the earliest, separately approved) | Scan port with a no-op adapter |
| Sample Product/Person/Order removal and API v1 sunset | Requires replacement and a separate sunset decision ([api-guidelines.md](api-guidelines.md#10-compatibility-and-legacy-samples)) | A dedicated post-release sunset phase: announcement, window, new contract baseline | Compatibility policy is written |
| Multi-tenancy | D23-1 | Do not pursue | — |
| MFA | D23-11 | First post-release candidate: TOTP through ASP.NET Core Identity | — |
| External identity providers (for example Entra ID) | D23-11 | Optional post-release, only with a real corporate scenario | — |
| Backend-for-frontend | D23-11 | Not needed: same-origin access with the HttpOnly refresh cookie and CSRF already covers the risk | — |

## Spikes for later phases

The recommendation is the approved starting hypothesis (owner, 2026-10-18). The phase still verifies it with the exit criterion and current official sources, and records any change in this log.

| ID | Question | Phase | Recommended starting point | Exit criterion |
|---|---|---|---|---|
| SP-1 | PostgreSQL row versioning: `xmin` system column versus an explicit `version` column for ETags | 24 | Explicit `bigint` `version` column incremented by the aggregate on every change, including child-entity changes, mapped as the concurrency token. `xmin` changes only when the root row changes, is a wrapping 32-bit value, is PostgreSQL-specific and is not emulated by EF InMemory. | Chosen mechanism proven by a 412 functional test and a PostgreSQL concurrency test |
| SP-2 | Gapless purchase-order numbering under concurrency | 32 | Per-year counter table updated with `UPDATE ... SET last_value = last_value + 1 RETURNING` inside the issue transaction; the row lock serializes issues and a rollback returns the number. No PostgreSQL `SEQUENCE` (it leaves gaps on rollback). | Concurrency test with parallel issues produces unique, gapless committed numbers |
| SP-3 | Durable Functions local provider and the supported isolated-worker TFM | 31 | Default Azure Storage provider on Azurite (already present from Phase 30); Durable Task Scheduler emulator as the alternative if local reliability or diagnostics require it. TFM from the official isolated-worker support matrix; may differ from WebApi. | ADR addendum with current official sources |
| SP-4 | Playwright browser in the Worker container | 30 | Install Chromium only with its minimal dependencies; embed versioned OFL fonts recorded in the license inventory; non-root; image pinned by digest. If the image grows too large (around 1 GB), publish the same Worker code as a report image and a lean messaging image. | Worker image renders a PDF in an isolated Compose run |
| SP-5 | BCB PTAX endpoint semantics | 25/29 | Selling rate of the closing PTAX bulletin of the latest business day on or before the RFQ close date, from the BCB public data API; step back day by day for up to 7 days; beyond that the comparison is `Incomplete` and SRC-013 applies. | Fake-server contract tests from current official documentation |
| SP-6 | Angular localization runtime option | 26A | `@angular/localize` (first-party, build-time per locale) because only pt-BR is required now; Transloco only if runtime language switching becomes a requirement. | Decision recorded in Phase 26A (D26A-10) |
