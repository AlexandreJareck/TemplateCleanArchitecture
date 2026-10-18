# ADR-0001: Modular monolith for procurement capabilities

- Status: Accepted (Phase 23, 2026-10-18)
- Decision owners: repository owner (roadmap), D23-1, D23-8

## Context

Procurement Hub adds eleven business modules (Organization, Suppliers, Catalog, Budgeting, Requisitions, Sourcing, Approvals, Purchasing, Receiving, Reporting, Integrations) to an existing Clean Architecture solution with one Domain project, one Application project, several Infrastructure adapters and one WebApi composition root. Several rules must be atomic across modules (submission + budget reservation, final approval + commitment). The team is academic and small; microservices are an explicit roadmap non-goal.

## Decision

1. Keep one deployable business core: `Template.Domain` and `Template.Application` hold all modules as namespaces (`Template.Domain.<Module>`, `Template.Application.Features.<Module>`); Persistence keeps one application `DbContext` with per-module configurations and module-prefixed tables.
2. Modules reference other modules' aggregates by ID plus immutable snapshots; cross-module atomic changes run in one Application use case and one unit of work; asynchronous reactions use integration events through the Outbox.
3. Architecture tests (existing `Template.Architecture.Tests`) gain rules: module Domain namespaces may not reference other modules' Domain types except the shared kernel; Application features may call another module only through that module's public Application interfaces/ports.
4. Composition roots: `Template.WebApi` (HTTP), `Template.Worker` (ADR-0002), `Template.WorkflowFunctions` (ADR-0003). Each references Application and only the Infrastructure adapters it needs.
5. The dependency direction `Domain <- Application <- Infrastructure` with composition roots outside is unchanged.

## Alternatives

- **Per-module projects now** (`Template.Domain.Budgeting`, ...): stronger compile-time isolation but many projects, slower builds and premature boundaries. Deferred: a module may be extracted to its own project later when tests show coupling pressure.
- **Microservices**: independent deployment at the cost of distributed transactions, which conflict with BUD-006/REQ-005 atomicity. Rejected (roadmap non-goal).

## Consequences

- Single PostgreSQL transaction for cross-module invariants; simpler consistency proofs.
- Module discipline depends on namespace rules and architecture tests, not project references; those tests are mandatory gates.
- Later extraction remains possible because modules communicate by ID, ports and versioned events.
