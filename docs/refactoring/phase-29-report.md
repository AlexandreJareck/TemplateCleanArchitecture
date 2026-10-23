# Phase 29 report — Requests for quotation and supplier quotes

Specification: `docs/refactoring/handoff-phase-29.md`, executed through `docs/refactoring/execute-phase-29.md` on 2026-10-22/23.

## Release decision

**Ready.** Every Phase 29 backend, persistence, broker, fake-provider, contract, architecture, coverage, frontend, browser, audit, Compose, Graphify and diff gate passed. A buyer can create a quotation round from a submitted requisition, invite Active suppliers, record comparable buyer-entered quotes with full provenance, close the round, review a deterministic comparison with persisted exchange-rate provenance and submit the selection for approval with an auditable snapshot, an adjusted budget reservation and durable events. Currency conversion is never guessed: a missing PTAX rate yields an explicit `Incomplete` comparison and a permission-protected manual rate. No supplier portal, tax engine, real provider call or supplier contact was introduced.

One gate iteration failed and was fixed without lowering any threshold: the first full run (2026-10-23 00:52–01:15) passed 19 of 20 steps but `07b-coverage` reported Domain 97.73% line (< 99%) and Application 86.54% branch (< 90%). Additional domain and handler tests (guard paths, CALC-007-b, BUD-009 guards, 403/404/412 for every command, list validation, expiry cascade, envelope validation, award visibility) raised them to 99.75% and 90.14%; the complete gate was then rerun green.

### Demonstrated sourcing journey

1. A requester's requisition `REQ-…` (18 × 1,000.00 = 18,000.00 BRL, above the 5,000.00 BRL threshold) is `Submitted` with an 18,000.00 BRL reservation.
2. A `Buyer` starts a competitive round from the sourcing work queue: `RFQ-{yyyy}-{nnnnnn}`, round 1, `SourcingPolicy` v1 pinned, the requisition submission snapshot copied as immutable lines (SRC-001/002/005).
3. The buyer invites three `Active` suppliers (identity and status snapshotted, SRC-003) and publishes with a deadline inside the 24 h–30 day window (SRC-004/005); the requisition becomes `InSourcing` and `procurement.rfq.published` is staged in the Outbox with the invited supplier IDs; the Worker notification consumer records one local delivery per supplier (SRC-022).
4. The buyer records three quotes received by e-mail (source, received instant, entered-by and entry time, D23-3); totals are server-computed (CALC-002).
5. The buyer closes the round (early, with reason, `sourcing.rfqs.close-early`); revisions freeze and comparison 1 is computed: ranks 1–3 by normalized total with the documented tie-breakers (CALC-005), the rank 1 revision is the advisory recommendation.
6. The buyer submits the recommended quote: award `PendingApproval` (16,200.00 BRL), reservation adjusted down from 18,000.00 to 16,200.00 (BUD-009), requisition `PendingApproval`, `procurement.award.submitted` and `procurement.budget.reservation-adjusted` in the same commit (SRC-019), audit records for the award, the requisition transition and the adjustment.

The same journey runs as handler tests against port fakes (`SC_01_BuyerJourney…`), as HTTP functional tests on the self-contained Test host (`SC_01_BuyerCreatesAndClosesAnRfq…`), on real PostgreSQL 16.15 (`SC_01_JourneyPersisted…`, with a USD quote, a PTAX snapshot and supplier changes after close) and in Chromium against the contract fixture (`e2e/sourcing.spec.ts`, including a 412 recovery and the server-requested `NonLeadingSelection` waiver).

## Preconditions and starting state

- HEAD `74ee1453091b8ff425e81264fd7e3aae1a9da225` on `main`, clean tree. Phases 23–28 are `Ready` (Phase 28 gate 19/19 green on 2026-10-21 at this HEAD).
- Baseline before the change: architecture 64, unit 540, integration 223, PostgreSQL 49, RabbitMQ 25, Elasticsearch 13, Redis 23, functional 352, Vitest 45, Playwright 6; OpenAPI = `docs/refactoring/baseline-phase-27`; last migration `20261021010433_Phase28ReliableMessaging`.
- Approved rules confirmed in the Phase 23 blueprint before implementation: RFQ and quote states (workflows §2), required quote fields (domain-model §3), invitation policy and minimums (SRC-003/005/007, `SourcingPolicy` v1), currency and base-date rule (CALC-004, SP-5: PTAX selling rate of the close local date or the latest earlier bulletin), tie-breaking (CALC-005), selection authority and waivers (SRC-016..018, waiver catalog), late quotes rejected (B23-6), whole-RFQ award (D23-15: partial quotes may be recorded but are never awardable).
- No stop condition applied: taxes are only an explicit `TaxesIncluded` quoted amount (CALC-002), never computed; no Brazilian tax engine was needed.
- Real suppliers were never contacted; every supplier, quote and rate in tests is synthetic; the BCB provider stays disabled in every committed profile and is replaced by fakes in tests.

## Domain model and states

| Element | States / content | Invariants enforced in the aggregate |
|---|---|---|
| `RequestForQuotation` (Sourcing) | `Draft → Published → Closed → AwardPending`, `Draft/Published/Closed → Cancelled` (buyer, SRC-020), any non-terminal → `Cancelled` by requisition cascade; `Awarded` reserved for Phase 31 | SRC-001/002 snapshot lines, round number and pinned policy; SRC-004 deadline window; SRC-005 competitive minimum by threshold (non-BRL estimate converted with the creation-date PTAX snapshot, unknown = above); SRC-006 Direct = one invitation + the threshold-matching waiver; SRC-007 invitation add/remove windows; SRC-011 extension; SRC-012 close (reason before the deadline) freezing revisions; SRC-013 manual rate; SRC-016..018 award eligibility and waivers; append-only history |
| `RfqInvitation` | active / removed (row kept) | Active suppliers only; supplier legal name, tax ID, country and status snapshot at invitation (SUP-004) |
| `SupplierQuote` → `QuoteRevision` | `Current → Superseded` (revise), `Current → Withdrawn` (reason) | SRC-008 invited supplier, received ≤ now and ≤ deadline; SRC-009 immutable revisions n+1; SRC-010 valid until ≥ deadline local date; CALC-002 totals; 4-decimal prices, 3-decimal quantities, minor-unit freight/discount/taxes; attachment metadata (FIL-002) |
| `RfqComparison` → `ComparisonEntry` | `Complete` / `Incomplete`; sequence 1 at close, n+1 per manual rate | SRC-014 validity reasons; CALC-004 normalization with persisted snapshots; CALC-005 ranking; warnings; recommendation = rank 1 of a complete comparison |
| `RfqExchangeRate` | `Estimate` / `Comparison`, source `BcbPtax` / `Manual` | positive, ≤ 6 decimals; provider, quote date, retrieval instant; manual rate with entering user, reason and waiver |
| `RfqWaiver` | append-only | type, rule ID, actor, permission, 10–500 character reason (D29-3), scope (award or rate) |
| `RfqAward` | `PendingApproval` (Phase 29), `Cancelled` (cascade); approval states for Phase 31 | one pending award; frozen quote-currency, base-currency and requisition-currency amounts (CALC-007) |
| `PurchaseRequisition` (extended) | `Submitted → InSourcing` (publish), `InSourcing → Submitted` (RFQ cancelled), `InSourcing → PendingApproval` (award) | award snapshot: award ID, supplier ID/name, amount in requisition currency |
| `BudgetAllocation` (extended) | reservation `Adjusted` operation | BUD-009: increase ≤ available under the allocation row lock, decrease returns the difference, idempotent per award |

## Comparison formula and examples

```
valid(rev)       = latest revision is Current ∧ received ≤ deadline ∧ validUntil ≥ close local date
                   ∧ supplier Active at close ∧ every RFQ line offered with quantity ≥ requested     (SRC-014)
normalized(rev)  = total                                  if currency = base (BRL)
                 = round(total × rate[currency], 2)       with the persisted snapshot             (CALC-004)
                 = missing → comparison Incomplete        never guessed                           (SRC-013)
rank             = dense 1..n over valid normalized entries ordered by
                   normalized ↑, deliveryDays ↑, receivedAt ↑, legalName (ordinal) ↑, supplierId ↑  (CALC-005, D29-5)
award (req. cur) = awarded                                if quote currency = requisition currency
                 = round(round(awarded × rate quote→BRL) ÷ rate req→BRL)                           (CALC-007)
```

| Example | Result | Test |
|---|---|---|
| USD 105.00 × 5.4321 | 570.37 BRL (570.3705 rounded away from zero) | `T_CALC_004_a_And_T_SRC_013…` (U) |
| 18 × 20.00 USD × 5.4321 | 1,955.56 BRL; reservation adjusted up from 100.00 | `T_CALC_004_c_FakeBcbRate…` (H), functional manual-rate test |
| 1,500.00 / 7 days vs 1,500.00 / 3 days vs 1,350.00 / 9 days | ranks 1,350.00 → 1,500.00 (3 d) → 1,500.00 (7 d) | `T_CALC_005_a…` (U) |
| equal totals, days and receipt: `Charlie`, `alpha`, `Bravo` | `Bravo`, `Charlie`, `alpha` (ordinal) in any input order | `T_CALC_005_a…` (U) |
| 5,000.00 vs 5,000.01 BRL estimate | below / above the exclusive threshold | `T_SRC_005_a_ThresholdIsExclusive` (U) |
| USD 900.00 estimate × 5.4321 | 4,888.89 BRL: below the threshold; without a rate: above | `T_SRC_005_a_NonBaseEstimate…` (U) |

## Schema and migration

Additive migration `20261023025916_Phase29SourcingQuotations` (application database; Identity unchanged), generated with `dotnet-ef` 10.0.13 installed only in the session scratchpad. Reviewed operations: 15 `CreateTable`, 7 nullable `AddColumn` on existing tables (`PurchaseRequisitions.ActiveRfqId/AwardId/AwardedSupplierId/AwardedSupplierName/AwardAmount`, `BudgetReservations.LastAwardId`, `BudgetReservationChanges.AwardId`), no alteration of existing columns, no backfill; `Down` drops only those tables and columns. Designer and snapshot are tool-generated.

| Table | Purpose and constraints |
|---|---|
| `SourcingRfqs` | unique `Number`; partial unique `IX_SourcingRfqs_RequisitionId_Active` (`Status <> 'Cancelled'`, SRC-001, business key → `rfq.already-active`); unique (requisition, round); (status, deadline) for the close sweep; restricted FK to requisitions |
| `SourcingRfqLines` | requisition snapshot lines; unique (rfq, line) |
| `SourcingRfqInvitations` | supplier snapshot; unique (rfq, sequence); partial unique active (rfq, supplier); restricted FK to suppliers |
| `SourcingQuotes` / `SourcingQuoteRevisions` / `SourcingQuoteLines` / `SourcingQuoteAttachments` | unique (rfq, supplier), (quote, revision), (revision, line); amount and delivery-day checks |
| `SourcingWaivers`, `SourcingExchangeRates` | append-only; rate > 0 check, 18,6 precision |
| `SourcingComparisons` / `SourcingComparisonEntries` | unique (rfq, sequence) and (comparison, revision) |
| `SourcingAwards` | partial unique `IX_SourcingAwards_RfqId_Pending`; amount checks; restricted FKs to revision and comparison |
| `SourcingRfqHistory` | append-only; unique (rfq, sequence) |
| `SourcingRfqNumberCounters` | per-year counter (gaps allowed) |
| `MessagingNotificationDeliveries` | local notification adapter; unique (event, recipient, template) |

The unit of work translates the history/invitation/revision/comparison/award guards to `ConcurrencyConflictException` (412) and the active-RFQ index to `UniqueConstraintViolationException` (`rfq.already-active`).

## API contract (v2)

16 new operations under `/api/v2/requests-for-quotation`, all additive (OpenAPI against `baseline-phase-27`: v1 identical; v2 0 removed, 0 operations changed, +16 operations, +30 schemas, `RequisitionView` gains the optional `activeRfqId` and `award`). New baseline: `docs/refactoring/baseline-phase-29/openapi-{v1,v2}.json`; `compose-baseline.ps1` and the Orval input now point to it.

| Operation | Permission | Rules |
|---|---|---|
| `GET /`, `GET /{id}`, `GET /{id}/history` | `sourcing.rfqs.manage` or `sourcing.quotes.read` (use case); commercial details only with `sourcing.quotes.read` | SRC-021 |
| `POST /` | `sourcing.rfqs.manage` | SRC-001/002/005 |
| `POST /{id}/invitations`, `DELETE /{id}/invitations/{supplierId}` | `sourcing.rfqs.manage` | SRC-003/007 |
| `POST /{id}/publish` | `sourcing.rfqs.manage` + waiver permission for Direct | SRC-004/005/006 |
| `POST /{id}/extend-deadline`, `POST /{id}/reminders` | `sourcing.rfqs.manage` | SRC-011/022 |
| `POST /{id}/quotes`, `/quotes/{quoteId}/revisions`, `/quotes/{quoteId}/withdraw` | `sourcing.quotes.enter` | SRC-008/009/010 |
| `POST /{id}/close` | `sourcing.rfqs.manage` (+ `close-early` before the deadline) | SRC-012/013/014/015 |
| `POST /{id}/cancel` | `sourcing.rfqs.manage` | SRC-020 |
| `POST /{id}/exchange-rates` | `sourcing.rates.manual` | SRC-013 |
| `POST /{id}/award` | `sourcing.awards.submit` + waiver permissions when required | SRC-016..019, BUD-009 |

Every change requires `If-Match` (428/412 with `version`); every POST requires `Idempotency-Key` (replay with `Idempotent-Replayed: true`). New stable codes: `rfq.requisition-not-eligible`, `rfq.already-active`, `rfq.deadline-invalid`, `rfq.insufficient-invitations`, `rfq.deadline-passed`, `rfq.supplier-already-invited`, `rfq.direct-single-supplier`, `rfq.no-pending-suppliers`, `rfq.rate-not-needed`, `waiver.required`, `waiver.permission-required` (403), `quote.late`, `quote.supplier-not-invited`, `quote.immutable`, `quote.exists`, `quote.expired`, `award.not-eligible`, `award.pending-exists`, `award.insufficient-quotations`, `award.non-leading-requires-waiver`, `award.comparison-incomplete`. Representations carry server-computed `allowedActions`, `version`, raw and normalized totals, rate provenance and waivers.

## Events and notifications

| Event (v1) | Payload (minimal, no supplier contact or quote text) |
|---|---|
| `procurement.rfq.published` | rfqId, number, requisitionId, deadline, invitedSupplierIds, sourcingMethod |
| `procurement.rfq.deadline-extended` | rfqId, previousDeadline, newDeadline, invitedSupplierIds |
| `procurement.rfq.reminder-requested` (new, D29-6) | rfqId, deadline, supplierIds |
| `procurement.rfq.closed` | rfqId, closedAt, validQuoteCount, comparisonStatus |
| `procurement.rfq.cancelled` | rfqId, requisitionId, reasonCode |
| `procurement.quote.recorded` | rfqId, quoteId, revision, supplierId |
| `procurement.award.submitted` | awardId, rfqId, requisitionId, supplierId, normalizedTotal, requisitionCurrencyTotal, policyRef |
| `procurement.budget.reservation-adjusted` | reservationId, requisitionId, awardId, previousAmount, newAmount |

All are staged in the Outbox in the business transaction (INT-004) and dispatched by the Phase 28 Worker. The Worker now runs two consumers: the Phase 28 projection (`procurement.projections.v1`) and the sourcing notification consumer (`procurement.notifications.v1`, bound to published/extended/reminder events, own DLQ). The notification use case plans one request per invited supplier and template and the local adapter records `MessagingNotificationDeliveries` atomically with the Inbox key, so a redelivered or duplicate-published event never queues a second notification; notification failure never changes RFQ state (SRC-022). Retries target only the failing queue (D29-9). No e-mail is sent before Phase 33.

## BCB PTAX behavior

The Phase 25 port and SP-5 resolver are consumed by `SourcingExchangeRates`: one PTAX range request for `[close date − 7, close date]`, the selling rate of the latest closing bulletin on or before the close local date, persisted as a `BcbPtax` snapshot with provider, quote date and retrieval instant and reused by the award (the provider is never called again). Timeout, 429, 5xx, schema mismatch, empty windows, rejection and disabled providers all yield "no rate": the comparison is `Incomplete`, no award is possible and a `ProcurementManager` may record a manual rate with the `ManualExchangeRate` waiver. The provider stays disabled in every committed profile and in the Test host; tests use the loopback fake server (`T_CALC_004_Sourcing_UsesTheClosingSellingRate…`, `T_SRC_013_a_ProviderFailureOrNoBulletin…`, `T_SRC_013_a_ProviderTimeout…`) and counting fakes.

## Screens (Angular)

| Route | Capability |
|---|---|
| `/sourcing` | buyer work queue of `Submitted` requisitions with competitive/direct start, RFQ list with status filter, responsive cards |
| `/sourcing/:id` | summary (status, method, round, threshold, minimum invitations, deadline, policy), snapshot lines, invitations with supplier snapshots and add/remove, publication with deadline and Direct waiver reason, extension and reminders, quote entry/revision/withdrawal with a labelled unofficial preview, close/early close with reason, server comparison table (rank, raw and normalized totals, rate, delivery, coverage, validity reasons, recommendation, warnings), manual rate, award confirmation with the waiver reasons the server asks for, rate provenance, waivers, history, cancellation, Problem Details with reload on 412/409 |
| `/requisitions/:id` | awarded supplier and RFQ link (SRC-021) |

Layering stays `Domain ← Application ← Infrastructure / Presentation ← Bootstrap`: `application/sourcing` holds types, the `SourcingPort` and state/permission helpers; `infrastructure/api/adapters/sourcing-api-adapter.ts` maps the regenerated Orval client; Presentation never imports generated code (dependency-cruiser). The browser never ranks or converts: an architecture rule rejects ranking and rate arithmetic in `Client/Template.WebApp/src/app` (`Phase29_Browser_Never_Calculates_Authoritative_Ranking_Or_Conversion`, with controlled mutations). pt-BR and English catalogs have identical keys.

## Tests and counts

| Suite | Before | After | Phase 29 additions |
|---|---|---|---|
| Architecture | 64 | 68 | Sourcing module boundaries/no public setters/no provider code, reservation adjustment only in the award use case, browser never ranks or converts, controlled mutations; controller count 16 |
| Unit | 540 | 626 | `RequestForQuotationTests` and `SourcingEdgeCaseTests` (mock-free domain), `SourcingHandlerTests` and `SourcingHandlerEdgeCaseTests` (port fakes incl. fake BCB), Phase 29 envelope mapping |
| Integration (EF InMemory / adapters) | 223 | 233 | sourcing repository round trip, filters, due-for-close, RFQ numbers, in-memory notification Inbox, PTAX fake-server sourcing tests (closing rate, 503/429/empty/malformed, timeout), processor consumer routing and per-queue DLQ |
| PostgreSQL 16.15 | 49 | 56 | additive migration + rollback to Phase 28, persisted journey with PTAX snapshot reuse and supplier changes after close, concurrent RFQ creation, quote racing the close, concurrent awards, failed-award rollback, concurrent notification duplicates |
| RabbitMQ 4.3.6 | 25 | 27 | RFQ published reaches projection and notification consumers once each (duplicate publish); notification failures retry only their queue and dead-letter there |
| Elasticsearch 9.5.5 / Redis 8.10.2 | 13 / 23 | 13 / 23 | unchanged |
| Functional (HTTP) | 352 | 359 | buyer journey with Outbox assertions, incomplete comparison + manual rate, Direct waiver permission, suspended supplier / uninvited / future quote, 428/412/idempotency/`rfq.already-active`, SRC-021 visibility, RFQ cancel and round 2; permission matrix extended with 16 sourcing endpoints |
| Vitest | 45 | 56 | sourcing use cases, adapter mapping/headers/errors |
| Playwright + axe | 6 | 8 | buyer journey (queue → invite → publish → quotes with 412 recovery → early close → comparison → non-leading waiver → award), incomplete comparison and mobile cards; zero axe violations |

Coverage (merged unit + integration + functional, Cobertura): Domain 99.75% line, Application 97.48% line / 90.14% branch, Messaging 70.01/80.19, Persistence 80.18/67.83, WebApi 76.62/60.83; every module passes `Tests/test-coverage-policy.json`, whose `minimumSources` were raised to the measured counts (nothing lowered).

### Rule-to-test matrix

| Rule | Tests |
|---|---|
| SRC-001 | `T_SRC_001_a_CreateCopies…` (U), `T_SRC_001_a_OnlySubmittedRequisitions…` (H), `T_SRC_001_b_ConcurrentRfqCreation…` (P), `T_API_412_428_And_Idempotency…` (F, `rfq.already-active`) |
| SRC-002 | `T_SRC_001_a_And_T_SRC_002_a…` (U), PostgreSQL journey reload |
| SRC-003 | `T_SRC_003_a…`, `T_SRC_003_b_SupplierSuspendedAfterInvitation…` (U), `T_SRC_003_b_SuspendedSupplier…` (F) |
| SRC-004 | `T_SRC_004_a_DeadlineMustFallInsideTheResponseWindow` (U), journey 400 `rfq.deadline-invalid` (F) |
| SRC-005 | `T_SRC_005_a_ThresholdIsExclusive`, `T_SRC_005_a_NonBaseEstimate…`, `T_SRC_005_b…` (U), SC_01 (H, F) |
| SRC-006 | `T_SRC_006_a…` (U), `T_SRC_006_b_BuyerCannotUseTheDirectPurchaseWaiver…`, `T_SRC_006_c_LowValueDirectPurchase…` (H), `T_SRC_006_b_BuyerCannotPublish…` (F) |
| SRC-007 | `T_SRC_007_a…` (U) |
| SRC-008 | `T_SRC_008_a_QuoteDeadlineBoundary_AndProvenance` (U), `T_SRC_003_b…` (F, uninvited/future) |
| SRC-009 | `T_SRC_009_a…` (U) |
| SRC-010 | `T_SRC_010_a…`, `T_SRC_016_a_InvalidExpiredOrIneligible…` (U) |
| SRC-011 | `T_SRC_011_a…` (U) |
| SRC-012 | `T_SRC_012_a…` (U), `T_SRC_012_EarlyClose…`, `T_SRC_012_b_SystemCloseAtTheDeadline_IsIdempotent` (H), `T_SRC_012_QuoteRacingTheClose…` (P), E2E early close |
| SRC-013 / CALC-004 | `T_CALC_004_a_And_T_SRC_013…`, `T_CALC_004_c…` (U), `T_SRC_013_a_FakeBcbDown…`, `T_SRC_013_EveryProviderFailure…`, `T_CALC_004_c_FakeBcbRate…` (H), PTAX fake-server tests (I), `SC_01_JourneyPersisted…` (P), `T_SRC_013_b…` (F), E2E incomplete comparison |
| SRC-014 | `T_SRC_014_a_EachInvalidReason…`, `T_SRC_017_ZeroValidQuotes…` (U) |
| SRC-015 / CALC-005 | `T_CALC_005_a_EachTieBreakerLevel_AndT_CALC_005_b_DeterminismAcrossInputOrder` (U), SC_01 ranks (H, F), E2E table |
| SRC-016 | `T_SRC_016_a…` (U), `T_SRC_016_b_ConcurrentAwards…` (P), `SC_01_JourneyPersisted…` (P, suspended supplier blocks) |
| SRC-017 | `T_SRC_017_a…` (U), `T_SRC_017_b_InsufficientQuotations_NeedsTheManagerWaiver` (H) |
| SRC-018 | `T_SRC_018_a…` (U, including SC-06 suspended rank 1), E2E non-leading waiver |
| SRC-019 / BUD-009 / CALC-007 | SC_01 (H, F), `T_BUD_009_a_AdjustmentUpAndDown…`, `T_BUD_009_a_AnAwardAboveAvailability…` (H), `T_SRC_019_a_FailedAward_RollsBackEverything`, `T_SRC_016_b…` (P), `T_CALC_004_c…` (U, BRL requisition / USD award) |
| SRC-020 | `T_SRC_020_a…` (U), `T_SRC_020_b…` (H, F) |
| SRC-021 | `T_SRC_021_a…` (H, F), permission matrix (F) |
| SRC-022 | `T_SRC_022_a_ReminderTargets…` (U), `T_SRC_022_a_NotificationPlanner…` (H), notification Inbox (I, P), RabbitMQ notification routing/retry/DLQ (W) |
| REQ-007/008 cascades | `T_REQ_007_WithdrawingTheRequisition…`, `T_REQ_008_b_CancellingTheRequisition…` (H) |
| INT-003/004 | Outbox assertions in SC_01 (F, P), `T_SRC_019_a…` (P, rollback leaves no event), notification duplicates (P, W) |
| AUD-001/003 | audit assertions in SC_01 (H, P), waiver audits with `waiverId` (H) |
| SUP-004 snapshot stability | `SupplierChangesAfterInvitation_NeverRewriteTheSnapshot` (U), `SC_01_JourneyPersisted…` (P) |

Architecture: Sourcing joins the module-boundary rule, has no public setters and no provider code (`Phase29_Sourcing_Aggregate…`), the reservation adjustment lives only in the award use case (`Phase29_Award_Reservation_Adjustment…`), the browser rule above, and `Phase29_Controlled_Mutations_Are_Detected` proves every new rule rejects a synthetic violation.

## Rollback

- Public API: additive only; previous clients are unaffected. Rolling back the WebApi removes the 16 operations and the two optional `RequisitionView` properties.
- Application: stop the Worker and deploy the Phase 28 WebApi/Worker; Phase 29 Outbox rows of the new event types would be dead-lettered as unsupported by a Phase 28 consumer, so drain or retain them before rolling back the Worker.
- Schema: after stopping every Phase 29 writer, `dotnet ef database update 20261021010433_Phase28ReliableMessaging` drops the 15 tables and 7 columns (data loss of RFQs, quotes, comparisons, awards and notification deliveries; export them first when needed). Rehearsed by `Migration_IsAdditive_AndRollsBackToPhase28_ThenReappliesIdempotently` on PostgreSQL 16.15.
- Broker: the notification queue and DLQ are additive; remove them only with an operator-approved procedure. Nothing was deleted by this phase.
- Frontend: the `/sourcing` routes and nav entry are additive; the previous build keeps working against the new API.

## Residual risks and deliberate limits

- Approval (Phase 31) is not implemented: awards stay `PendingApproval` and `procurement.award.submitted` has no consumer yet; award withdrawal/invalidation paths (APR-012) arrive with it.
- No scheduler: automatic close at the deadline (`CloseDueRfqsCommand`) and scheduled reminders are triggered from Phase 33; buyers close manually after the deadline meanwhile.
- Notifications are durable intent records only (no e-mail, no supplier contact resolution) until the Phase 33 Mailpit adapter.
- Concurrent quote entry on one RFQ serializes by version (one buyer retries after 412); acceptable for buyer-entered quotes.
- PTAX is disabled in every committed profile; with it disabled any cross-currency RFQ needs a manual rate.
- Frontend tooling: this machine had Node 22.20; the pinned Node 24.22.0 was taken from the official `nodejs.org` zip (SHA-256 verified against `SHASUMS256.txt`) and pnpm 12.14.0 through Corepack, both in the session scratchpad, and Playwright's Chromium for 1.64.1 was installed into the user cache. The checkout uses `core.autocrlf=true`, so `pnpm format:check` reports CRLF in untouched files; content formatting was verified with `prettier --check . --end-of-line auto`. No package or lockfile changed.

## Gates

Final full gate `scripts/run-phase-validation.ps1 -ComposeBaseline` on 2026-10-23 01:24–01:40 (America/Sao_Paulo): all 20 steps exited 0.

| Gate | Result |
|---|---|
| restore / WebApi build / Worker build | succeeded; 0 warnings, 0 errors |
| architecture | 68/68 |
| unit | 626/626 |
| integration (EF InMemory, adapters, fake servers) | 233/233 |
| PostgreSQL 16.15 | 56/56 |
| RabbitMQ 4.3.6 | 27/27 |
| Elasticsearch 9.5.5 | 13/13 |
| Redis 8.10.2 | 23/23 |
| Compose baseline | 116/116 in `p14-baseline-20261023012912`; resources stopped and retained |
| functional HTTP | 359/359 |
| coverage | OK (see above); discovered = executed = passed for 626 unit, 233 integration and 359 functional tests (adapter 3.1.5) |
| vulnerable packages | none; deprecated only the test-only xUnit 2.9.3 family |
| package graph / licenses | 283 production / 471 test entries, unchanged from Phase 28; 155 packages, 0 outside policy |
| Compose config | `docker compose --env-file .env.example config -q` succeeded |
| OpenAPI | captured v1/v2 identical to `docs/refactoring/baseline-phase-29` after newline normalization; v1 unchanged from Phase 27; v2 additive |
| Graphify | 11,920 nodes, 31,038 edges, 455 communities |
| `git diff --check` / secret scan | exit 0 / OK (compromised key absent from 10,758 files) |
| frontend (Node 24.22.0, pnpm 12.14.0) | Prettier content check (`--end-of-line auto`), ESLint, typecheck, Vitest 56/56, dependency-cruiser 170 modules / 343 dependencies with no violation, 5/5 architecture mutations rejected, API drift 153 generated files match, production build, licenses 458 records approved, `pnpm audit` no known vulnerabilities, Playwright 8/8 |

The older infrastructure upgrade rehearsals (Phases 11 and 15–18) and the VSIX validation were not rerun: Phase 29 did not change their scope. Nothing was committed, pushed or merged; no branch or worktree was changed; no material resource was deleted.
