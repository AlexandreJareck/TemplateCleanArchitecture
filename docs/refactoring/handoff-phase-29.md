# Phase 29 Execution Handoff — Requests for Quotation and Supplier Quotes

## Mission

Implement sourcing: quotation rounds created from submitted requisitions, invited suppliers, buyer-entered supplier quotes, commercial terms, currency normalization, deterministic comparison, selection recommendation, notifications, v2 APIs, events, and matching Angular workflows.

## Required context and skills

Read instructions, the Phase 23 glossary/workflows/permissions/event catalog, Phase 25 supplier/currency adapters, Phase 27 requisitions, Phase 28 async guarantees, frontend conventions, API baseline, and tests. Use `api-contract-change`, `ef-migration`, `integration-adapter`, `architecture-boundary-check`, and `dotnet-change-verification` as triggered.

## Preconditions

- Phases 23–28 are complete and green.
- Confirm approved quotation states, required quote fields, invitation policy, currency/base-date rule, tie-breaking, selection authority, and whether partial quotes are permitted.
- Record HEAD/dirty tree and baselines.
- Stop if price comparison rules would require unstated tax/fiscal logic. Taxes may be captured as explicit quoted amounts, but no Brazilian tax engine is in scope.

## Domain and persistence

Implement approved aggregates/value objects for:

- `RequestForQuotation`: requisition snapshot/reference, round number, invited suppliers, issue/deadline, status and history;
- `SupplierQuote`: supplier, validity, currency, line offers, freight, explicit taxes/discounts, delivery estimate, payment terms, notes/attachments metadata, revision and status;
- comparison result/snapshot with base currency, exchange-rate source/date, normalized totals, coverage, warnings, ranking and buyer recommendation.

Protect invariants through behavior methods, immutable historical snapshots, optimistic concurrency, permissions, and additive PostgreSQL migrations. Closing a round freezes accepted quote revisions. Supplier/master-data changes must not rewrite history.

Enforce the approved sourcing rules:

- only an eligible requisition and Active suppliers may enter a new RFQ; invitation records snapshot supplier identity/status evidence used at that time;
- publishing requires a future deadline, complete scope, and the configured minimum invitations unless an authorized waiver is recorded;
- quotes must be attributable to supplier, revision, source, entered-by user and entry time; withdrawn/superseded revisions remain in history;
- normal closing occurs after the deadline; an authorized early close requires a reason; late quotes are rejected or explicitly recorded as an approved exception according to Phase 23;
- comparison includes only eligible, valid, comparable quote revisions and records missing coverage instead of treating missing lines as zero;
- ranking uses one documented deterministic formula and tie-breaker, while selection remains a human decision;
- selecting with fewer valid quotations than policy requires, selecting a non-leading quote, or using direct purchase requires the specific waiver permission and reason;
- the first release awards the whole RFQ to one supplier unless D23-15 was explicitly changed before implementation.

## Application and API

- Add commands/queries/validators for create/publish/extend/cancel/close RFQ, invite/remove supplier while allowed, enter/revise/withdraw quote, compare, and recommend/select.
- Apply Outbox events for RFQ published/closed, quote recorded, comparison ready, and selection recommended as approved.
- Use the Phase 25 currency port with a persisted rate snapshot; unavailable rates produce an explicit incomplete comparison and manual fallback, not invented values.
- Queue invitation/reminder notification commands through the Worker; use a fake/local notification adapter until Phase 33.
- Add v2 list/detail/action endpoints with idempotency, concurrency, permissions, filters, Problem Details, cancellation, and OpenAPI tests.
- Do not expose supplier-private quote details to unauthorized roles.

## Frontend

Implement this slice through frontend Domain/Application/Infrastructure/Presentation boundaries. Application use cases consume sourcing ports; Infrastructure maps generated RFQ/quote DTOs and errors; Angular Presentation owns the work queue, RFQ wizard from an eligible requisition, invited-supplier management, quote entry/revision, deadline/status display, comparison table with raw and normalized totals, warnings, recommendation/selection confirmation, history, conflict recovery, accessibility, and responsive tables. No Presentation code imports the generated client directly.

The browser may format and preview values but may not calculate authoritative rankings or exchange conversions.

## Out of scope

- Public supplier portal, email reply parsing, e-signature, auction/bidding, automated award, purchase order creation, Durable approval, report files, invoice/payment/tax engine, or AI recommendations.

## Tests and validation

Add a rule-to-test matrix for every applicable `SRC-*` rule, including standard competition and each approved waiver. Add domain property/boundary tests for money, quantity, deadlines, revisions, coverage, eligibility and deterministic ranking; handler tests; PostgreSQL state/concurrency/snapshot/query tests; fake BCB timeout/rate/date tests; Outbox/Rabbit Worker notification tests; functional permission/contract/idempotency tests; frontend unit/E2E journeys; architecture mutations; OpenAPI/generated-client drift.

Run sequential backend/frontend gates, PostgreSQL/Rabbit real suites, external fake-server tests, coverage, audits, secret scan, Compose, Graphify, OpenAPI, and `git diff --check`.

## Acceptance criteria

- A buyer can create and close an RFQ, capture comparable quotes, and select a recommendation with an auditable snapshot.
- Currency conversion is reproducible from stored provenance and never guessed.
- Concurrent revisions/deadline transitions are safe.
- Events and notifications are durable/idempotent.
- Permissions prevent unauthorized quote visibility/actions.
- Required gates pass or the phase is `Not ready`.

## Report and final response

Create `docs/refactoring/phase-29-report.md` covering domain/states, comparison formula and examples, schema/migration, contracts/events, BCB behavior, screens, tests/counts, OpenAPI, rollback, residual risks and gates. Lead with readiness and the demonstrated sourcing journey.

Do not commit/push/merge, change branches/worktrees, open a PR, contact real suppliers, or delete/alter material resources without authorization.
