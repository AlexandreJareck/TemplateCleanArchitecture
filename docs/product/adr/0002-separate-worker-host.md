# ADR-0002: Separate Worker host with transactional Outbox and Inbox

- Status: Accepted (Phase 23, 2026-10-18); implemented in Phase 28
- Decision owners: D23-8

## Context

The WebApi currently hosts the sample `RabbitMqOrderConsumerService` and publishes directly to RabbitMQ with publisher confirms; deduplication is in memory per process. Procurement needs events that are never lost after a committed business change, consumers that apply effects once despite redelivery, and background work (notifications, reports, PO drafts, projections, sweeps) that must not run inside HTTP request processing or block WebApi scaling.

## Decision

1. Create `Src/Workers/Template.Worker` (.NET generic host, same TFM as WebApi) as a composition root referencing Application and the needed Infrastructure adapters.
2. Business changes write integration events to an Outbox table in the same PostgreSQL transaction (INT-004). The Worker dispatcher claims Outbox batches safely across instances (row locking with skip-locked semantics or leases), publishes with confirms, marks them dispatched and retries with bounded backoff.
3. Consumers record (consumer name, event ID) in a PostgreSQL Inbox inside the same transaction as their effect (INT-003). Delivery is at-least-once; effects are effectively-once.
4. Consumers call Application use cases; no business rule lives in a consumer, routing key, retry policy or topology declaration.
5. The sample order messaging endpoints and their in-memory deduplication remain unchanged until a separately approved migration.
6. Hangfire/Quartz are not introduced; recurring triggers follow ADR-0003.

## Alternatives

- **Keep consumers in WebApi**: simpler, but couples HTTP scaling and shutdown to background processing. Rejected.
- **Azure Service Bus**: managed broker, but replaces the verified RabbitMQ 4.3.6 stack and conflicts with local-first scope. Rejected for this program.
- **Publish after commit without Outbox**: loses events on crash. Rejected.

## Consequences

- New tables (Outbox, Inbox, idempotency) and cleanup jobs (retention in [non-functional-requirements.md](../non-functional-requirements.md#5-data-retention)).
- HTTP success no longer depends on immediate broker availability.
- Worker has its own health endpoints, Compose service and real-broker tests.
