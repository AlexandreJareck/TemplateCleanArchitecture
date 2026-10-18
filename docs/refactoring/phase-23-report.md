# Phase 23 report — Product blueprint and architecture decisions

Specification: `docs/refactoring/handoff-phase-23.md` (executed through `docs/refactoring/execute-phase-23.md`) on 2026-10-18. Outcome: **complete**. Documentation and design only; no Phase 24 implementation was started.

## 1. Decision summary

The owner was shown D23-1 through D23-18 with their recommended defaults and answered explicitly "accept all recommended" on 2026-10-18. Full records with alternatives, rationale, consequences and deferrals: `docs/product/decision-log.md`.

| ID | Recorded decision |
|---|---|
| D23-1 | One organization per installation; no multi-tenancy |
| D23-2 | Angular TypeScript SPA under `Client/Template.WebApp` |
| D23-3 | Buyer-entered supplier quotes; no supplier portal |
| D23-4 | Reserve on submission, confirm on final approval, release on rejection/cancellation/expiry (and withdrawal) |
| D23-5 | PDF from HTML/CSS through Playwright; QuestPDF only after license approval |
| D23-6 | XLSX with ClosedXML; Open XML SDK as fallback |
| D23-7 | Azurite locally, Azure Blob in Azure, behind an Application port |
| D23-8 | RabbitMQ Worker + Outbox/Inbox; Durable Functions only for long-running workflow |
| D23-9 | Local implementation plus deployable IaC; no Azure provisioning without authorization |
| D23-10 | English code/contracts/technical docs; pt-BR UI labels and resources |
| D23-11 | In-memory short-lived access token; rotating refresh token in a Secure HttpOnly cookie; explicit CSRF design |
| D23-12 | Frontend Clean Architecture with automated boundary enforcement |
| D23-13 | Versioned amount-based approval levels; no final self-approval; bypass only through explicit waiver rules |
| D23-14 | Three invited eligible suppliers above an approved threshold; insufficient-quotation/direct-purchase waivers with permission and reason |
| D23-15 | Whole-RFQ award to one supplier; split awards deferred |
| D23-16 | Only Active suppliers for new invitations, awards and POs; suspension preserves history |
| D23-17 | Allocation by active fiscal period, cost center and currency; no silent overspend or cross-currency reservation; explicit precision/rounding |
| D23-18 | Zero default over-receipt tolerance (configurable); compensating corrections with reason |

Blueprint refinements B23-1 to B23-10 (award approval model, additional maker-checker constraints, direct purchase as an RFQ method, BRL base currency, `America/Sao_Paulo` time zone, late-quote rejection, terminal rejection with copy-to-draft, plain v2 representations for new endpoints, permission-only authorization without role bypass, policy v1 seeds) are recorded in the same log as design choices that refine, not change, the owner decisions.

## 2. Product boundary

Single-organization purchase lifecycle from requisition to receipt and closure, with budget control, competitive sourcing, multi-level award approval, purchase orders, partial receiving, asynchronous PDF/XLSX reports and a complete audit trail through the UI. Excluded: multi-tenancy, supplier portal, invoices/payments/tax/NFe, inventory, returns, PO amendments, split awards, generic workflow designer, microservices, Service Bus, Azure provisioning, API v1 removal, compliance certification (`docs/product/product-vision.md`).

## 3. Architecture

- Modular monolith in the existing Domain/Application/Infrastructure layers with eleven modules plus Access and Audit, module-prefixed namespaces and tables, references by ID plus snapshots, architecture tests for module boundaries (ADR-0001).
- Composition roots: WebApi, `Template.Worker` (Outbox/Inbox, ADR-0002), `Template.WorkflowFunctions` (approval orchestration and recurring triggers only, ADR-0003).
- PostgreSQL application database is the business source of truth; Durable history is process state; blob storage holds bytes only; external providers are advisory snapshots (ADR-0004, ADR-0007).
- Frontend: Angular with `Domain <- Application <- Infrastructure / Presentation <- Bootstrap`, generated client confined to Infrastructure, backend authority, explicit state ownership, no global store by default (ADR-0005).
- Single organization (ADR-0006).

## 4. Deliverables

| File | Content |
|---|---|
| `docs/product/README.md` | Index and how later phases use the blueprint |
| `docs/product/product-vision.md` | Personas, problems, goals, non-goals, release boundary, scenarios SC-01..SC-09 |
| `docs/product/ubiquitous-language.md` | Glossary with pt-BR label examples |
| `docs/product/domain-model.md` | Modules, shared kernel value objects, aggregates, invariants, lifecycles, cross-module references, legacy sample isolation |
| `docs/product/business-rules.md` | 141 stable rule IDs (POL, CALC, SEC, AUD, ORG, SUP, CAT, INT, BUD, REQ, SRC, APR, PUR, RCV, FIL, RPT) with class, policy v1 values, permission, waiver, audit action, failure code/status, planned tests and phase; waiver catalog; scenario traceability |
| `docs/product/workflows.md` | Transition tables (actor, guard, result, event, audit, idempotency) and Mermaid diagrams for requisition, RFQ, approval, PO, receipt, report job, master data/budget; scenario walkthroughs |
| `docs/product/permissions.md` | Permission catalog, seeded role matrix, separation-of-duty constraints SOD-1..8, authorization test strategy |
| `docs/product/api-guidelines.md` | Routes, representations, Money as string, paging/filter/sort, ETag/If-Match (412/428), Problem Details codes, Idempotency-Key, session/CSRF, 202 jobs, file download, compatibility and sample-endpoint policy |
| `docs/product/frontend-information-architecture.md` | Routes and screens per phase, navigation, role entry points, screen states, WCAG 2.2 AA and responsive scope, layer map and rules, ports/errors, state ownership, architecture tests |
| `docs/product/non-functional-requirements.md` | Security, audit, availability/degradation, performance budgets with dataset, retention, RPO/RTO, observability, localization, privacy classification |
| `docs/product/event-catalog.md` | Domain vs integration events, envelope, 34 integration event types with producers, consumers, business keys and phases, cross-process commands |
| `docs/product/decision-log.md` | D23-1..18, B23-1..10, deferrals, spikes SP-1..6 |
| `docs/product/adr/0001`–`0007` + `README.md` | Modular monolith, Worker, Durable boundary, file/report engines, frontend stack/Clean Architecture, single organization, external enrichment |
| `docs/refactoring/phases-23-33-product-roadmap.md` | Status line and a "Phase 23 outcome" section with the refinements that affect later phases (no phase removed or reordered) |
| `AGENTS.md` | Pointer to `docs/product/` and Phase 23 execution status |

## 5. Review passes

### Pass 1 — Product consistency

Checked every use case against the glossary, every transition against the rule catalog and permissions, and every scenario against the transition tables.

| Finding | Resolution |
|---|---|
| SC-06 required a `NonLeadingSelection` waiver to award rank 2 after the rank 1 supplier was suspended, punishing a compliant buyer | SRC-018 now compares against the best-ranked *awardable* quote; SC-06 walkthrough updated |
| SC-09 implied the requester could never approve level 1, while D23-13 forbids only the *final* approval | SC-09 now specifies a level-1-only award (level 1 is final); APR-005 kept literal to D23-13, extra constraints are B23-2 |
| Issued-PO cancellation actor differed between the requisition table (Buyer/ProcurementManager) and the PO table (ProcurementManager) | Aligned to ProcurementManager; buyer cancels drafts only (PUR-008, permissions note) |
| Workflow events `ComparisonReady.v1` and `DiscrepancyRecorded.v1` were not in the event catalog | Folded into `rfq.closed` (comparison status) and `receipt.posted` (`hasDiscrepancy`); naming map added to workflows |
| Competition threshold is in BRL but a requisition may be in another currency | SRC-005 converts with CALC-004 and treats a missing rate conservatively as above threshold |
| Budget insufficiency at final approval would leave an approved award without funds | Reservation is adjusted at award submission (BUD-009), so final approval only commits (BUD-010) and cannot fail on funds |

Automated check (script in the session scratchpad, not committed): every rule ID referenced anywhere in `docs/product/` and the roadmap is defined in `business-rules.md` (141 defined, 141 referenced, 0 undefined).

### Pass 2 — Architecture and safety

| Topic | Where it is explicit |
|---|---|
| Dependency direction (backend and frontend) | ADR-0001, ADR-0005, frontend IA §4, domain-model §1 |
| Source of truth | ADR-0002/0003/0004, APR-016, NFR-REC-4 |
| Idempotency | API guidelines §6, BUD-007, APR-001/014, PUR-001/006, RPT-001, INT-003, every transition row |
| Concurrency | API guidelines §4 (ETag/If-Match, 412/428), BUD-006 row lock, APR-014, SP-1, SP-2 |
| Security and secrets | SEC-001..010, NFR-SEC-1..8, CSRF/cookie design, no tokens/keys in docs |
| Privacy and data ownership | NFR §9, SUP-006, SRC-021, AUD-003, event payload rule |
| Rollback | NFR-REC-1..4; additive migrations; this phase changes no runtime artifact |
| Authorization vs business rule separation | permissions §1, SEC-008, B23-9, waiver catalog |

Findings resolved: the user lookup and user/role administration API had no phase (moved to Phase 25 because master data needs manager/head assignment; recorded in the roadmap outcome section). No contradiction found with `docs/refactoring/api-v1-lifecycle.md`: all procurement endpoints are additive v2 operations, v1 and sample v2 endpoints are unchanged, and sample removal requires replacement plus a separate sunset decision (api-guidelines §10).

### Pass 3 — Executability (Phases 24–33)

| Phase | Inputs the handoff requires | Provided by |
|---|---|---|
| 24 | Permission matrix, session transport, idempotency/concurrency/paging conventions, rate limiting, audit events | permissions.md, api-guidelines §3–§7, SEC-*, AUD-* |
| 25 | Names, identifiers, required fields, supplier lifecycle, permissions, retention, enrichment rules | domain-model §3, ORG/SUP/CAT/INT, workflows §7, ADR-0007, NFR §5 |
| 26A–26C | Frontend IA, layers, state ownership, session behavior, screens for master data | frontend-information-architecture.md, ADR-0005, api-guidelines §7 |
| 27 | Rounding, fiscal period, attachment limits, permission ownership, concurrency, state machine | CALC-001..003, BUD-*, REQ-*, FIL-002, workflows §1 |
| 28 | Event catalog with producers, consumers, versions, idempotency keys, sensitive fields | event-catalog.md, ADR-0002, INT-003/004 |
| 29 | Quote states, required fields, invitation policy, base-date rule, tie-breaking, selection authority, partial quotes | SRC-*, CALC-004/005/007, workflows §2 |
| 30 | D23-5..7, file/report rules, report catalog | ADR-0004, FIL-*, RPT-* |
| 31 | Policy, steps, thresholds, resolution, SoD, deadlines, delegation, escalation, compensation | APR-*, ApprovalPolicy v1, ADR-0003, workflows §3 |
| 32 | Numbering, snapshot fields, cancellation, tolerances, service receipts, discrepancies, closure, amendments | PUR-*, RCV-*, CALC-006/008/009, deferrals (amendments excluded) |
| 33 | Performance thresholds, retention, recovery objectives, scheduler boundary, scenarios | NFR, ADR-0003, SC-01..SC-09 traceability |

Hidden prerequisites found and recorded: between Phase 29 and Phase 31 awarded requisitions stay `PendingApproval` (expected, roadmap outcome section); expiry and RFQ-close sweeps are use cases in Phases 27/29 and get recurring triggers in Phase 33; spikes SP-1..SP-6 are assigned to their phases. Each later handoff already demands a rule-to-test matrix; the planned test IDs (`T-<RULE>-<letter>`) give it a starting point.

## 6. Ambiguities, deferrals and spikes

- Ambiguities resolved by recorded blueprint choices: B23-1..B23-10 (decision log). Policy values (thresholds 5,000/10,000/100,000 BRL, 72 h steps, 30-day expiry grace, 24 h response window, file limits, retention periods, performance budgets) are seeded v1 defaults chosen by the blueprint and owner-adjustable as data.
- Retention periods are assumptions, not legal advice.
- Deferrals: split awards, supplier portal, PO amendments, returns/inventory/invoicing, FX revaluation of commitments, business-day calendars, category-specific approval chains, malware scanning, sample endpoint removal, multi-tenancy/MFA/BFF.
- Spikes: SP-1 (row version mechanism, Phase 24), SP-2 (gapless PO numbering, Phase 32), SP-3 (Durable local provider and TFM, Phase 31), SP-4 (Playwright in the Worker image, Phase 30), SP-5 (BCB PTAX semantics, Phases 25/29), SP-6 (Angular localization runtime, Phase 26A).

## 7. Validation performed

| Check | Result |
|---|---|
| Phase 22 precondition: `docs/refactoring/phase-22-report.md` decision | `Ready` (PR #20 and D6 record PR #22 merged on 2026-10-17/18) |
| Relative link and heading-anchor check over `docs/product/**`, the roadmap and this report (Python script in the session scratchpad) | 0 broken links/anchors |
| Rule-ID reference check (same script) | 141 defined, 0 undefined references |
| `git diff --check` | Exit 0 (only LF→CRLF normalization notices) |
| `powershell -ExecutionPolicy Bypass -File .\scripts\secret-scan.ps1` | `secret-scan: OK` (5 settings files, 8 Compose/env templates, fingerprint searched in 431 files) |
| Markdown linter | Not available in this checkout (no `markdownlint`); skipped |
| Build/test gates | Not run: no code, project, package, configuration or runtime file changed (handoff: do not run the full gate unless a concurrent change requires it; none exists) |

Repository facts referenced by the blueprint were verified by search: `BaseApiController` route `api/v{version:apiVersion}/[controller]`; v1/v2 sample routes; `ApiResultMapper` Problem Details with `traceId`; `ErrorCategory`/`ErrorCode` values; `PagedResponse<T>`/`PaginationRequestParameter`; `CorsExtensions` using `AllowAnyOrigin`; `ApplicationUser`/`ApplicationRole` with no refresh-token store; `DefaultRoles` seeding only `Admin`; `SupportedCultures` `pt-BR`, `en`, `fa`; no concurrency token or idempotency mechanism in current code.

## 8. Working-tree inventory

- Start: branch `docs/phase-22-d6-history-rewrite` at `f44a5d9`, `git status --short` empty; that branch was already squash-merged into `main` (PR #22, `3edb8e3`). Per the owner's standing instruction that every phase ends in a PR, work moved to a new branch `refactor/fase-23-product-blueprint` created from an up-to-date `main` at `3edb8e3`; the working tree was clean, so nothing was carried or discarded.
- No concurrent changes were present. The legacy worktree `.claude/worktrees/fase-8-redis-adapter-f4fda8` was not touched.
- Changed files: `AGENTS.md`, `docs/refactoring/phases-23-33-product-roadmap.md`; new: `docs/product/**` (20 files), this report.

## 9. Confirmation of scope

No C#, TypeScript, project, package, schema, migration, configuration, Compose, image, runtime or script file changed. No external API was called, no cloud or Docker resource was created or removed, no data was read or migrated, and API v1 and every existing endpoint are unchanged. No Phase 24 work began.
