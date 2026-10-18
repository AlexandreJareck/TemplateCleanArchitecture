# Phase 27 Execution Handoff — Purchase Requisitions and Budget Control

## Mission

Implement the first complete procurement vertical slice: budget allocations and reservations plus purchase requisitions with lines, attachments metadata, controlled lifecycle, audit history, optimistic concurrency, v2 API, and Angular user experience.

## Required context and skills

Read all instructions, the Phase 23 domain/workflow/permission/API decisions, Phase 24–26 reports, master-data model, persistence conventions, frontend architecture, API baseline, and test suites. Use `ef-migration`, `api-contract-change`, `architecture-boundary-check`, and `dotnet-change-verification` as applicable.

## Preconditions

- Phases 23–25 and 26A–26C are complete and green, with the umbrella Phase 26 report reconciled.
- D23-4 budget reservation timing and requisition state machine are approved.
- Record HEAD/dirty tree and baseline OpenAPI, tests, coverage, package graph, and current migrations.
- Stop if money/currency rounding, fiscal period, attachment limits, permission ownership, or concurrency semantics remain undefined.

## Domain model

Implement the approved forms of:

- `BudgetAllocation` by cost center, period, currency, amount, active/closed state;
- `BudgetReservation` with owner requisition, reserved/confirmed/released state and immutable monetary history;
- `PurchaseRequisition` aggregate with requester, department/cost center, purpose, needed-by date, currency, lines, attachments metadata, totals, version, and status history;
- requisition line with catalog reference when present plus immutable description, quantity, unit, estimated unit price, and classification snapshot.

Enforce transitions such as Draft, Submitted, UnderSourcing/UnderApproval as approved, Approved, Rejected, Cancelled, and Closed. Do not expose public setters as a substitute for transition methods. Every transition records actor, time through `IClock`, previous/new state, reason, correlation, and emitted domain event.

Budget reservation and transition persistence must be atomic. Define deterministic oversubscription and concurrency behavior under PostgreSQL. Do not rely on EF InMemory to prove it.

Enforce the approved corporate-style rule set without adding a generic rules engine:

- allocations belong to one active fiscal period, cost center, and currency; closed periods reject new reservations;
- requested quantities and prices are positive, totals use the approved decimal precision/rounding, and the server recalculates every total;
- only the requester or an explicitly authorized proxy may edit a Draft; submitted content becomes an immutable snapshot unless an approved return-to-draft transition creates a new revision;
- submission requires at least one valid line, active organizational/master-data references, purpose, needed-by date, and sufficient available budget;
- one requisition owns at most one active reservation; reserve/confirm/release operations are idempotent and never make availability negative;
- cancellation/rejection/expiry requires the approved reason semantics and releases funds exactly once; no submitted business history is hard-deleted;
- overrides, if Phase 23 approved any, use a dedicated permission and waiver record rather than a generic administrator bypass.

## Application, persistence, and API

- Add focused use cases, validators, repositories/ports, policies, and result categories.
- Add additive migrations with constraints/indexes and reviewed PostgreSQL SQL.
- Implement v2 endpoints for draft CRUD, line/attachment metadata, submit, cancel, list/filter, detail, history, budget availability/allocation administration, and reservation visibility according to permissions.
- Apply Phase 24 idempotency and concurrency preconditions to transition commands.
- Return immutable DTO snapshots; never expose entities.
- Add audit-safe structured logs and metrics/events without line descriptions or sensitive attachments in logs.
- File bytes are out of scope until Phase 30; accept only validated attachment metadata or an explicit placeholder contract approved in Phase 23.

## Frontend

Implement the slice through the established frontend Clean Architecture: framework-free requisition/budget client models and Application use cases where needed, API adapters and transport mapping in Infrastructure, and Angular pages/forms in Presentation. Add budget administration, my requisitions, team requisitions (permission-gated), draft editor with line calculations, submission confirmation, detail/timeline, budget availability, cancellation/rejection display, conflict resolution, validation, and responsive/accessibility tests. Presentation must not import generated DTOs directly. The UI may preview totals but the server is authoritative.

## Out of scope

- RFQ/quotes, final approval orchestration, Worker/outbox, file upload bytes, reports, purchase orders, receipts, supplier portal, invoices/payments;
- generic workflow engine or multi-currency conversion;
- removal of legacy Product/Person/Order APIs.

## Tests and validation

Cover every applicable `BUD-*` and `REQ-*` rule with a rule-to-test matrix: invariants/transitions with mock-free domain tests; policy and authorization with handler tests; PostgreSQL atomic reservation, closed-period rejection, oversubscription, uniqueness, optimistic concurrency, transaction rollback, migration/idempotency and representative query plans; functional API contracts, permissions, waivers, idempotency and 409/412; frontend unit/E2E happy and negative journeys; architecture mutations; OpenAPI/generated-client drift.

Run sequential backend/frontend gates, PostgreSQL real integration, coverage, audits, secret scan, Compose config, Graphify after structural changes, OpenAPI comparison, E2E, and `git diff --check`.

## Acceptance criteria

- A user can create and submit a requisition with lines against available budget.
- Concurrent submissions cannot over-reserve a budget or overwrite a requisition silently.
- Rejection/cancellation releases reservations exactly once.
- Full state/audit history is queryable under permissions.
- Backend and frontend enforce the approved contract; server totals and transitions are authoritative.
- All required gates pass or the phase is `Not ready`.

## Report and final response

Create `docs/refactoring/phase-27-report.md` with model/state table, schema/migration, atomicity proof, contracts, screens, tests/counts, query/concurrency evidence, OpenAPI delta, rollback, risks, and gates. Lead with readiness and the demonstrated end-to-end journey.

Do not commit/push/merge, change branches/worktrees, open a PR, rewrite migrations, or delete/alter material data without authorization.
