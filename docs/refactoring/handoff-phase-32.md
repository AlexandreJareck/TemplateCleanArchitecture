# Phase 32 Execution Handoff — Purchase Orders and Receiving

## Mission

Complete the core procurement lifecycle. Implement approved purchase-order creation and issue, immutable commercial snapshots, numbering, PDF generation, supplier notification, cancellation, partial/full receiving, discrepancies, completion, integration events, v2 APIs, and Angular screens.

## Required context and skills

Read root/nested instructions, Phase 23 purchasing/receiving models and permissions, Phases 27–31 reports, quote selection/approval outcomes, Outbox/Inbox, reporting/storage platform, frontend conventions, API baseline, and tests. Use `api-contract-change`, `ef-migration`, `integration-adapter` for notification/document boundaries, `architecture-boundary-check`, and `dotnet-change-verification`.

## Preconditions

- Phases 23–31 are complete and green.
- Confirm purchase-order numbering scope, immutable snapshot fields, cancellation policy, over/under-receipt tolerances, service-receipt behavior, discrepancy permissions, closure rule, and whether amendments are excluded or versioned.
- Record HEAD/dirty tree and baselines.
- Stop if invoicing, payment, tax, NFe, stock valuation, or warehouse requirements are introduced without a separate approved scope.

## Domain and persistence

Implement approved forms of:

- `PurchaseOrder`: number, approved source decision, supplier/buyer/address/contact snapshots, currency, lines, commercial terms, totals, issued/cancelled/completed history, version;
- `PurchaseOrderLine`: quote/requisition references and immutable description, quantity, unit, price, explicit adjustments, promised delivery and received totals;
- `Receipt`: receiver, date, location/reference, line quantities, accepted/rejected or discrepancy details, notes/attachments metadata, status and history.

Only an approved and still-valid selection may create a purchase order. Creation/number allocation must be idempotent and concurrency-safe in PostgreSQL. Issued commercial snapshots do not drift with master-data edits. Receiving cannot exceed approved tolerance silently. Completion is derived from explicit rules and produces an audit/event trail.

Enforce the approved purchasing and receiving rules:

- a purchase order is created once from one approved award and cannot silently change supplier, currency, price, quantity, terms, or delivery commitments;
- issuance requires an Active supplier, valid approval, confirmed budget, complete commercial snapshot, and permission; failure to generate/send a document does not unissue or duplicate the order;
- cancellation requires reason and permission and is rejected after accepted receipt unless Phase 23 defines a narrower compensating process;
- receipts reference an issued, non-cancelled order; quantities are positive and cannot exceed remaining quantity plus the approved tolerance;
- receiver identity, receipt time, location/reference, accepted/discrepant quantities, and evidence metadata are recorded; discrepancy resolution is permission-protected;
- posted receipts are not destructively edited or deleted: correction creates an auditable compensating record and recalculates remaining/completion state;
- completion occurs only when every line satisfies the approved received/accepted rule; under-delivery closure, if supported, is an explicit permissioned exception with reason.

Create additive migrations, constraints/indexes and reviewed SQL. Preserve historic records through status/versioning rather than destructive delete.

## Application, messaging, reports, and API

- Add focused commands/queries/validators, repositories/ports, permissions and result mappings.
- Publish versioned order issued/cancelled, receipt recorded/corrected if approved, discrepancy detected, and order completed events through Outbox.
- Queue purchase-order PDF generation through Phase 30 and supplier notification through the Worker; retries are idempotent and cannot issue multiple order numbers.
- Store the exact issued document file reference and template/version provenance.
- Add v2 endpoints for order list/detail/issue/cancel/document and receipt create/list/detail/actions with idempotency, concurrency, cancellation, Problem Details, filters, and permissions.
- Do not expose storage or broker implementation details.

## Frontend

Implement this slice through frontend Domain/Application/Infrastructure/Presentation boundaries, keeping generated purchasing/receiving DTOs inside Infrastructure and server rules authoritative. Implement approved-order queue, purchase-order detail/timeline/document download, issue/cancel confirmation, receiving work queue, partial receipt editor, discrepancy capture, remaining quantity display, completion status, conflict/stale action handling, permission behavior, accessibility, and E2E tests.

## Out of scope

- Supplier portal, electronic signature, invoice/NFe, three-way matching, accounts payable, payments, inventory valuation, warehouse management, returns, amendments beyond the approved minimal rule, or external ERP write integration.

## Tests and validation

Add a rule-to-test matrix for every applicable `PUR-*` and `RCV-*` rule. Add domain tests for eligibility, numbering, immutable snapshots, receipt arithmetic/tolerances, compensating corrections, cancellation/completion and under-delivery exception; application tests; PostgreSQL concurrency/idempotency/rollback/migration tests; Rabbit Outbox/Inbox and Worker recovery tests; report semantic/visual tests for purchase-order PDF; functional contract/permission tests; frontend unit/E2E full journey; architecture mutations; OpenAPI/generated-client drift.

Run sequential backend/frontend gates, PostgreSQL/Rabbit/Azurite/report real integration, isolated Compose restart/recovery, coverage, audits, secret scan, Graphify, OpenAPI, and `git diff --check`.

## Acceptance criteria

- An approved selection produces exactly one numbered purchase order with an immutable auditable snapshot.
- Document generation/notification failure is visible and retryable without duplicating the order.
- Partial and final receipts calculate remaining quantities correctly and enforce tolerances/concurrency.
- Cancellation/completion transitions and events execute exactly once.
- The complete requisition-to-receipt path works through API and UI.
- Required gates pass; otherwise readiness is denied.

## Report and final response

Create `docs/refactoring/phase-32-report.md` with domain/states, numbering decision, schema/migration, snapshots, contracts/events, report/notification behavior, UI, full-journey tests, real-service evidence, rollback, risks, and gates. Lead with readiness and the proven lifecycle.

Do not commit/push/merge, change branches/worktrees, open a PR, contact real suppliers, or delete/alter data/resources without explicit authorization.
