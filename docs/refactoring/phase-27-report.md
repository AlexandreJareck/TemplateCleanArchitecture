# Phase 27 report — Purchase requisitions and budget control

Specification: `docs/refactoring/handoff-phase-27.md`, executed through `docs/refactoring/execute-phase-27.md` on 2026-10-20.

## Release decision

**Ready.** Every Phase 27 functional, persistence, concurrency, contract, architecture, browser, backend and frontend gate passed. The first frontend audit found three advisories published after Phase 26 against the transitive development dependency `vite` 8.3.0 (pinned exactly by `@angular/build` 22.2.3, the latest stable 22.x; dev-server `launch-editor` on Windows): GHSA-587h-7wmj-h5pm (high), GHSA-698v-4r3x-3vcv and GHSA-r825-w8mm-5pxw (moderate), patched in `vite` ≥ 8.3.3. With the owner's approval the phase added an exact pnpm override `vite: 8.3.3` in `pnpm-workspace.yaml` (first patched release, published 2026-10-06; same MIT license, Node engines and peer ranges as 8.3.0; no other package changed in the lock) and repeated every frontend gate: frozen offline install, `pnpm validate`, licenses, `pnpm audit:security` (no known vulnerabilities) and Playwright 6/6 (D27-19). The override is removed once `@angular/build` depends on a patched `vite`.

### Demonstrated end-to-end journey

1. A `BudgetController` creates fiscal period `FY…` (Planned), opens it, and allocates 1,000.00 BRL to a cost center.
2. A `Requester` creates a draft (number `REQ-{yyyy}-{nnnnnn}`), adds a line (`3 × 33.3333` → server total `100.00`; client preview shows the same value labelled unofficial), sees own-draft availability (BUD-012) and submits with `If-Match` and `Idempotency-Key`.
3. Submission and reservation commit atomically (REQ-005): status `Submitted`, snapshot revision 1, reservation `Reserved` 100.00, availability 900.00, two audit records with the request correlation; the timeline shows the reservation and the state change.
4. Cancellation with a reason releases the reservation exactly once (`Released`, reason `Cancelled`); availability returns to 1,000.00; a second cancel answers 409 `invalid-transition`; the requester can copy the cancelled requisition into a new draft (REQ-012).

The same journey runs as HTTP functional tests (`ProcurementFunctionalTests`), as handler tests against port fakes, on real PostgreSQL 16.15 (`RequisitionBudgetPostgreSqlTests`) and in Chromium against the contract fixture (`e2e/requisitions.spec.ts`).

## Preconditions and starting state

- HEAD `51036da6b39b7aea848d05e2aba1219646f2bcdc` on `codex/phase-26-frontend`, clean tree. Phases 23–25 and 26A–26C are `Ready` and the umbrella Phase 26 report is reconciled.
- Baseline before the change (from the Phase 25/26 gates, unchanged at HEAD): architecture 59, unit 427, integration 189, PostgreSQL 25, functional 322, Vitest 30, Playwright 4; OpenAPI = `docs/refactoring/baseline-phase-25`; last migration `20261018135425_Phase25MasterData`.
- No stop condition applied: money/rounding (CALC-001..003), fiscal periods (BUD-001), attachment limits (`FilePolicy` v1), permission ownership (REQ-001/008/011, permissions §3) and concurrency (BUD-006, SP-1) are defined in the Phase 23 blueprint. D23-4 (reserve on submission, confirm on final approval, release on rejection/cancellation/withdrawal/expiry) and the requisition state machine (workflows §1) are approved.

## Model and state table

| Aggregate (module) | State | Key fields | Invariants enforced in the aggregate |
|---|---|---|---|
| `FiscalPeriod` (Budgeting) | `Planned → Open → Closed` | unique code, local start/end, opened/closed instants | BUD-001 transitions and ordered range; dates editable only while `Planned`; BUD-011 close only without `Reserved` funds |
| `BudgetAllocation` (Budgeting) | `Active → Closed` | period, cost center, currency, amount, reserved, committed, amount history, reservations | BUD-002 positive minor-unit amount, active cost center, supported currency, period not closed; BUD-003 reasoned change never below reserved + committed; CALC-003 availability |
| `BudgetReservation` (inside the allocation) | `Reserved → Committed`, `Reserved/Committed → Released` | requisition, revision, amount, release reason, immutable change history | BUD-005 amount ≤ available; BUD-007 idempotent reserve per (requisition, revision) and exactly-once release with reason; BUD-008 one active reservation per requisition |
| `PurchaseRequisition` (Requisitions) | `Draft → Submitted → Draft` (withdraw), `Draft/Submitted(/InSourcing) → Cancelled`, `Submitted/InSourcing → Expired`; later states reserved for Phases 29–32 | number, requester, cost-center snapshot, purpose, needed-by, currency, lines, attachments, revision, submissions, state changes, active reservation | REQ-002 line validation and policy limit; REQ-003/CALC-002 server totals; REQ-004 guards reported together; REQ-006 immutable submitted content with snapshot; REQ-007 withdraw with new revision; REQ-008 reasoned cancellation by authority; REQ-009 expiry after needed-by + 30 days; REQ-010 discard only if never submitted; REQ-012 copy from terminal; FIL-002/004 metadata policy |

Every transition appends `RequisitionStateChange` (sequence, from/to, revision, actor or system, UTC from `IClock`, reason, reason code, correlation, event type) and raises a domain event (`procurement.requisition.*`, `procurement.budget.reservation-*`). Public setters do not exist (architecture-tested).

## Schema and migration

Additive migration `20261020215624_Phase27PurchaseRequisitionsBudget` (application database; Identity unchanged), generated with `dotnet-ef` 10.0.13 installed only in the session scratchpad. Reviewed SQL: 12 `CREATE TABLE`, indexes and checks on new tables only, one hand-written `ALTER TABLE … ADD CONSTRAINT "EX_BudgetFiscalPeriods_NoOverlap" EXCLUDE USING gist (daterange("StartDate","EndDate",'[]') WITH &&)` (its `Down` drops it); no existing table is altered, no backfill. Designer and snapshot are tool-generated.

| Table | Purpose and constraints |
|---|---|
| `BudgetFiscalPeriods` | unique `Code`; `CK_…_DateRange`; exclusion constraint against overlap |
| `BudgetAllocations` | unique (period, cost center, currency); `CK_…_Amount` (> 0); `CK_…_Totals` (reserved, committed ≥ 0 and their sum ≤ amount); restricted FKs to periods and cost centers |
| `BudgetAllocationAmountChanges` | append-only history; unique (allocation, sequence) guard |
| `BudgetReservations` | partial unique `IX_BudgetReservations_RequisitionId_Active` (`Status IN ('Reserved','Committed')`); `CK_…_Amount`; restricted FK to requisitions |
| `BudgetReservationChanges` | immutable monetary history; unique (reservation, sequence) guard |
| `PurchaseRequisitions` | unique `Number`; requester/cost-center/department/status indexes; checks on total and revision; restricted FK to cost centers |
| `PurchaseRequisitionLines` | unique (requisition, line number) guard; positive quantity and price checks; restricted FKs to catalog item, category, unit |
| `PurchaseRequisitionAttachments` | metadata only; length 1..10,485,760 check |
| `PurchaseRequisitionStateChanges` | append-only; unique (requisition, sequence) guard |
| `PurchaseRequisitionSubmissions` / `…SubmissionLines` | immutable snapshot per revision; unique (requisition, revision) guard |
| `PurchaseRequisitionNumberCounters` | per-year counter for numbers |

The unit of work translates the new business keys (period code, overlap exclusion — SQLSTATE 23P01 —, allocation key) to `UniqueConstraintViolationException` and the history/line/submission guards plus the BUD-008 partial index to `ConcurrencyConflictException` (412). The ORG-004 lock adapter now queries allocations (D27-15).

## Atomicity and concurrency proof (PostgreSQL 16.15, never EF InMemory)

| Test | Evidence |
|---|---|
| `T_BUD_006_a_TwentyParallelSubmissions_OnANearlyEmptyAllocation_NeverOverReserve` | 20 requisitions of 100.00 submitted simultaneously (barrier) against 250.00: exactly 2 succeed, 18 answer `budget.insufficient` (never 412 or an over-reservation); stored reserved 200.00, available 50.00, 2 active reservations, 2 reservation audits |
| `SC_08_ConcurrentSubmissionsOfTheSameRequisition_OneWins_TheOtherIs412_AndOneReservationExists` | one success, the other `concurrency.version-mismatch`; one reservation and one snapshot |
| `T_BUD_007_a_And_T_REQ_008_b_ConcurrentCancellations_ReleaseExactlyOnce` | 3 parallel cancels: one success, two 412; reservation released once (2 history rows), availability restored, one release audit |
| `T_REQ_005_a_FailedSubmission_RollsBack_AndLeavesTheDraftUnchanged` | insufficient funds: requisition stays `Draft` with the same version, no snapshot, no reservation, no audit |
| `T_AUD_001_a_SubmissionReservationSnapshotAndAudit_CommitTogether` | submission, reservation (with correlation), snapshot (4-decimal unit price preserved, total 246.91) and both audits committed together |
| `T_REQ_007_b_WithdrawThenResubmit_…` | revision 1 reservation `Released (Withdrawn)`, revision 2 `Reserved`; reserved total correct |
| `T_BUD_011_CloseRacingASubmission_…` | close and submit raced: exactly one succeeds; never a reservation inside a closed period (observed outcome recorded in test output) |
| `T_BUD_004_b_ClosedPeriod_RejectsNewReservations` | closed period → `budget.no-open-allocation` |
| `T_BUD_001_b_OverlappingPeriods_CreatedConcurrently_OnlyOneWins` | concurrent overlapping creations: one success, others `budget.period-overlap` via the exclusion constraint |
| `T_BUD_002_a_AllocationsAreUnique…_UnderConcurrency` | one success, others `budget.allocation-duplicate` |
| `T_BUD_008_a_TheDatabaseRejectsASecondActiveReservation…` | direct insert violates `IX_BudgetReservations_RequisitionId_Active` |
| `T_CALC_003_TheDatabaseNeverAllowsNegativeAvailability` | direct update violates `CK_BudgetAllocations_Totals` |
| `SC_08_ConcurrentDraftLineEdits_OneWins_TheOtherIs412` | one line added, the other 412 (line-number guard) |
| `RequisitionNumbers_AreUnique_UnderConcurrentDraftCreation` | 10 parallel drafts → 10 distinct numbers |
| `Migration_RollsBackToPhase25_AndReappliesIdempotently` | `Down` to Phase 25 removes only the 12 tables; re-`Up` and a second run are no-ops; exclusion constraint restored |
| `RepresentativeQueryPlans_UseTheExpectedIndexes` | `EXPLAIN` (seq scan disabled to show index usability on tiny data): allocation key → `Index Scan using "IX_BudgetAllocations_FiscalPeriodId_CostCenterId_Currency"`; requester list → `Index Scan Backward using "IX_PurchaseRequisitions_RequesterId_CreatedAtUtc"`; active reservation → `Index Scan using "IX_BudgetReservations_RequisitionId_Active"` |

## API contract (v2)

29 new operations, all additive (OpenAPI diff against `baseline-phase-25`: v1 identical; v2 0 removed, 0 changed, +29 operations, +25 schemas). New baseline: `docs/refactoring/baseline-phase-27/openapi-{v1,v2}.json`.

| Resource | Operations | Authorization |
|---|---|---|
| `fiscal-periods` | `GET` list/detail; `POST` create; `PUT {id}`; `POST {id}/open`, `{id}/close` | `budget.read` / `budget.periods.manage` |
| `budget-allocations` | `GET` list, `availability`, `{id}`, `{id}/reservations`; `POST` create; `POST {id}/amount-changes`, `{id}/close` | `budget.read` / `budget.allocations.manage` |
| `purchase-requisitions` | `GET` list (`scope=mine\|visible`), `{id}`, `{id}/history`, `{id}/budget-availability`; `POST` create; `PUT {id}`; `DELETE {id}`; `POST/PUT/DELETE {id}/lines[/{lineNumber}]`; `POST/DELETE {id}/attachments[/{attachmentId}]`; `POST {id}/submit`, `/withdraw`, `/cancel`, `/copy` | reads and cancel: authenticated + use-case authorization (REQ-011/008); writes: `requisitions.create`; submit/withdraw: `requisitions.submit` (own) |

Every change of an existing aggregate requires `If-Match` (428/412 with `version`); every creating/transition `POST` requires `Idempotency-Key` (replay with `Idempotent-Replayed: true`). Problem Details carry `code`, `ruleId`, `traceId` and, where applicable, `currentState`, `available`/`requested`/`currency` (BUD-005) or `fields` (REQ-004). New stable codes: `budget.period-overlap`, `budget.period-code-duplicate`, `budget.period-closed`, `budget.period-has-open-reservations`, `budget.allocation-duplicate`, `budget.allocation-has-open-reservations`, `budget.below-reserved`, `budget.no-open-allocation`, `budget.insufficient`, `budget.reservation-exists`, `requisition.not-submittable`, `requisition.immutable`, `file.too-large` (413), `file.type-not-allowed` (415), `file.limit-reached`, `file.immutable`. Representations are immutable DTO snapshots with server-computed totals and `allowedActions`; entities are never exposed. Decisions D27-1..19 are in `docs/product/decision-log.md`.

## Screens (Angular)

| Route | Capability |
|---|---|
| `/budget/periods` | list; create, open and close (confirmation, no reason) for `budget.periods.manage` |
| `/budget/allocations` | list with server totals and period filter; create allocation |
| `/budget/allocations/:id` | totals, amount history, reservations with release reasons, reasoned amount change and close |
| `/requisitions/mine`, `/requisitions/team` | server paging, status/number filters, responsive cards; team route needs `read-team` or `read-all` |
| `/requisitions/new`, `/requisitions/:id/edit` | header form, catalog or free lines with live line/total preview (unofficial) next to the server total, attachment metadata, own-draft availability with insufficient-funds warning, submission confirmation with summary, discard |
| `/requisitions/:id` | detail, lines, attachments, availability for `budget.read`, timeline of state and reservation changes, cancellation/rejection/expiry reason display, withdraw, reasoned cancel, copy |

Layering is enforced (`Domain ← Application ← Infrastructure / Presentation ← Bootstrap`): the preview arithmetic is framework-free (`domain/requisition-preview.ts`, scaled `BigInt`), ports and use cases live in `application/procurement`, the generated-client adapter in `infrastructure/api/adapters/procurement-api-adapter.ts`; Presentation never imports generated DTOs (dependency-cruiser and five negative mutations). Conflicts (412/409) show the stable code, trace and an explicit reload; tables become labelled cards on narrow screens; pt-BR and English catalogs have identical keys.

## Tests and counts

| Suite | Before | After | Phase 27 additions |
|---|---|---|---|
| Architecture | 59 | 63 | module boundaries for Budgeting/Requisitions, raw SQL only in Persistence, reservations changed only by requisition transitions, no public setters, controlled mutations, 15 convention controllers |
| Unit | 427 | 531 | Budgeting and requisition domain tests (mock-free), requisition and budget handler tests against port fakes |
| Integration (EF InMemory) | 189 | 196 | adapter round trips, filtered reservation load, logical transaction, visibility filters, ORG-004 lock, number allocator, organization scope |
| PostgreSQL 16.15 | 25 | 43 | 18 tests listed above |
| Functional (HTTP) | 322 | 352 | 30 new: HTTP contract journeys plus the 10-role permission matrix, anonymous 401 and SoD checks |
| Vitest | 30 | 45 | preview arithmetic, use cases, adapter mapping/headers/errors, guard any-of, reasonless confirmation |
| Playwright + axe | 4 | 6 | happy path with 412 resolution, submission, timeline and reasoned cancellation; insufficient budget, mobile cards; zero axe violations on editor and list |

### Rule-to-test matrix

| Rule | Tests |
|---|---|
| CALC-001 | `T_CALC_001_a/b/c` (U) |
| CALC-002 | `T_CALC_002_a` (U), `T_CALC_002_c_ClientSentTotals_AreIgnored` (F), `requisition-preview.spec.ts` |
| CALC-003 | `T_CALC_003_a` (U), `T_CALC_003_TheDatabaseNeverAllowsNegativeAvailability` (P) |
| BUD-001 | `T_BUD_001_a` (U), `T_BUD_001_b` (H, P, F) |
| BUD-002 | `T_BUD_002_a` (U, H, P) |
| BUD-003 | `T_BUD_003_a` (U, H), `T_BUD_003_…` (F) |
| BUD-004 | `T_BUD_004_a` (U), `T_BUD_004_b/c` (H), `T_BUD_004_b` (P) |
| BUD-005 | `T_BUD_005_a` (U), `T_BUD_005_b` (H, F), E2E insufficient journey |
| BUD-006 | `T_BUD_006_a` (P, 20 parallel submissions) |
| BUD-007 | `T_BUD_007_a` (U, every reason), `T_BUD_007_a_And_T_REQ_008_b` (P) |
| BUD-008 | `T_BUD_007_a_And_T_BUD_008_a` (U), `T_BUD_008_a` (P) |
| BUD-009/010 | Domain `Commit` path tested in `T_CALC_003_a`; triggers in Phases 29/31 |
| BUD-011 | `T_BUD_011_a` (U, H), close-versus-submit race (P), period close 409 (F) |
| BUD-012 | `T_BUD_012_a` (H, F) |
| REQ-001 | `T_REQ_001_a/b` (H, F) |
| REQ-002 | `T_REQ_002_a` (U, H), `T_REQ_002_b` (F) |
| REQ-003 | `T_REQ_002_a_And_T_REQ_003` (U), E2E server total |
| REQ-004 | `T_REQ_004_a` (U, H, F), `T_ORG_003_b` (H) |
| REQ-005 | `T_REQ_005_a` (P), `T_REQ_005_b` (H, F) |
| REQ-006 | `T_REQ_006_a` (U) |
| REQ-007 | `T_REQ_007_a` (U), `T_REQ_007_b` (H, P), withdraw/copy (F) |
| REQ-008 | `T_REQ_008_a` (U, H, F), `T_REQ_008_b` (H, P, F), E2E reasoned cancel |
| REQ-009 | `T_REQ_009_a` (U, H with fake clock); scheduler `-b` (W) in Phase 33 |
| REQ-010 | `T_REQ_010_a` (U, P, F) |
| REQ-011 | `T_REQ_011_a` (H, F) |
| REQ-012 | `T_REQ_012_a` (U), copy (H, F) |
| FIL-002 | `T_FIL_002_a` (U), `T_FIL_002_b` (F) |
| FIL-004 | `T_FIL_004_a` (U), attachments (H) |
| AUD-001/003 | `T_AUD_001_a` (P), audit assertions in handler tests, proxy audit without free text (F) |
| SEC-008 / permissions §5 | `ProcurementPermissionMatrixTests` (F) |
| SC-08 | `SC_08_*` (P), 412/428 (F, E2E) |

Rejection (APR-009) and its release path: the requisition cannot reach `PendingApproval` before Phases 29/31, so `Rejected` release is proven at the allocation level (`T_BUD_007_a` with reason `Rejected`, same code path as cancellation) and the UI renders rejection reasons from the timeline (`closingReason`, Vitest).

## Gates

Executed on 2026-10-20 with the pinned SDK 10.0.402, sequentially; logs in the git-ignored `artifacts/validation/` (gate run 19:47–19:59 America/Sao_Paulo, then reruns after the fixes below).

| Gate | Result |
|---|---|
| `dotnet restore` / WebApi build | Succeeded, 0 warnings, 0 errors |
| Architecture | 63/63 (Phase 27: +4 rules with controlled mutations; convention controllers 15) |
| Unit | 531/531 |
| Integration (EF InMemory) | 196/196 |
| PostgreSQL 16.15 (Docker) | 43/43; the Phase 27 concurrency tests also repeated 8× without failure |
| RabbitMQ / Elasticsearch / Redis (Docker) | gate steps exit 0 (unaffected suites, rerun as part of the gate) |
| Functional | 352/352 |
| Coverage (`coverage-report.ps1`) | OK: Domain 99.52% line; Application 97.97% line / 90.71% branch; every other module at or above its minimum; `minimumSources` ratcheted to the measured counts, nothing lowered |
| `dotnet list … --vulnerable --include-transitive` | No vulnerable NuGet package |
| `dotnet list … --deprecated --include-transitive` | Only the known xunit 2.9.3 family (test-only, deferred) |
| `docker compose --env-file .env.example config -q` | Succeeded |
| OpenAPI capture | v1 identical to Phase 25; v2 additive only (+29 operations, +25 schemas); recapture identical to `baseline-phase-27` |
| Migration SQL review | `dotnet ef migrations script` Phase 25 → 27: 12 `CREATE TABLE`, 23 indexes, 1 exclusion constraint, 0 changes to existing tables, 0 drops |
| Frontend `pnpm validate` | Passed: format, lint, type-check, Vitest 19 files / 45 tests, dependency graph 163 modules / 322 dependencies with 0 violations, 5/5 negative mutations rejected, 121 generated files match the new baseline, production build (initial 412.07 kB raw / 99.11 kB estimated transfer; lazy route chunks 2.8–5.8 kB transfer; the known non-blocking 36-selector critical-CSS warning remains) |
| Frontend `pnpm install --frozen-lockfile --offline` | Passed after the `vite` override (lock diff: `vite` 8.3.0 → 8.3.3 only; previous lock kept in `artifacts/validation/phase27/pnpm-lock.before-vite-override.yaml`) |
| Frontend licenses | 458 records approved (1 documented metadata exception) |
| Frontend security audit | First run: 3 advisories in transitive dev dependency `vite` 8.3.0; after the exact override to 8.3.3: **No known vulnerabilities found** |
| Playwright + axe | 6/6 Chromium journeys (Chromium installed by the pinned Playwright into git-ignored `artifacts/validation/phase27/ms-playwright`); zero axe violations on dashboard, requisition editor and mobile list |
| Graphify | `graphify update . --force`: 10,274 nodes, 25,811 edges, 417 communities |
| Secret scan | OK (5 settings files, 8 Compose/env templates, compromised fingerprint absent) |
| `git diff --check` | Exit 0 (only LF/CRLF notices) |

### Findings fixed during the gates

- **Deadlock under concurrent overlapping period creation.** The first gate run caught PostgreSQL `40P01 deadlock detected` when three overlapping periods were inserted at once (the exclusion constraint makes concurrent inserters wait on each other). The unit of work now translates `40P01`/`40001` into `ConcurrencyConflictException`; period and allocation creation then re-evaluate against the winner and answer `budget.period-overlap`, `budget.period-code-duplicate` or `budget.allocation-duplicate` (D27-18). Covered by the PostgreSQL test (repeated 8×) and by handler tests that simulate the lost race.
- **Coverage ratchet.** The first measurement put Domain at 98.73% and Application at 94.25%/83.39%, below the policy. Edge-case domain and handler tests (missing resources, stale versions, filters, validators, header/line edits, deadlock re-evaluation) restored 99.52% and 97.97%/90.71% without lowering any threshold. A stale functional/integration build had also skewed the first numbers; the final measurement used a fresh build of the whole solution filter.
- **Accessibility.** axe flagged `landmark-unique` because a table region repeated its section's label; the table regions now have distinct names.

## Rollback

Source rollback is the reversion of the Phase 27 files. Schema rollback: `dotnet ef database update 20261018135425_Phase25MasterData` (application database) drops the 12 Phase 27 tables and the exclusion constraint — **only with authorization**, because it discards budget and requisition data; it was rehearsed on disposable PostgreSQL. Without schema rollback, Phase 26 code ignores the new tables. The frontend rollback is the reversion of `Client/Template.WebApp` sources and the regenerated client back to `baseline-phase-25`; the `vite` override rollback is removing the `overrides` block from `pnpm-workspace.yaml` and restoring the previous lock (reintroduces the advisories). No data, volume or external resource was altered or deleted.

## Risks and limitations

- The `vite` 8.3.3 override must be revisited at each Angular update (remove it when `@angular/build` ships a patched `vite`; D26A-14 monthly review).
- Row locks and constraints are PostgreSQL semantics; EF InMemory (functional host) uses a logical transaction, so concurrency is proven only by the PostgreSQL suite.
- Domain events are logged and counted, not delivered; the Outbox (Phase 28) will publish them.
- Expiry has no scheduler yet (Phase 33); rejection, award adjustment and commit triggers arrive with Phases 29/31.
- Attachment bytes, malware scanning and downloads remain Phase 30.
- E2E journeys run against a contract fixture, not a live host (as in Phase 26); manual browser/screen-reader matrix stays a Phase 33 release item.
- The `dotnet-ef` tool and the Playwright Chromium cache live in the scratchpad and git-ignored `artifacts/validation/phase27`.

No commit, push, merge, branch/worktree change, PR, migration rewrite or data deletion was performed by the execution itself.
