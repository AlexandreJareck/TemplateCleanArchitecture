# Phase 28 report — Reliable asynchronous execution

Specification: `docs/refactoring/handoff-phase-28.md`, executed through `docs/refactoring/execute-phase-28.md` on 2026-10-21.

## Release decision

**Ready.** Procurement state changes and their versioned integration events now commit in one PostgreSQL transaction. A separately deployable Worker leases Outbox rows, publishes persistent RabbitMQ messages with publisher confirms, and applies local projection effects through a durable PostgreSQL Inbox. Transport is **at-least-once**, not exactly-once; the demonstrated guarantee is effectively-once for the protected same-database effect.

The legacy order-v1 endpoints, serializer, queues, in-memory deduplication path and WebApi registration remain unchanged. The Phase 27 v1/v2 OpenAPI documents are byte-equal after line-ending normalization and structurally equal.

## Preconditions and scope

- Starting HEAD: `219c19c36f92f934aa9867782b22f882b18865ce`, clean worktree.
- Phases 23–27 were `Ready`; the Phase 23 event catalog and ADRs 0001–0003 were treated as authoritative.
- Existing package and image versions were retained: RabbitMQ.Client 7.2.2, RabbitMQ 4.3.6, PostgreSQL 16.15 and .NET SDK/runtime 10.0.402/10.0.13. No version research or package upgrade was needed.
- RFQ, notifications, reports, files, workflow orchestration, service-bus alternatives and legacy order-v1 migration remain out of scope.
- No public HTTP operation, response, status, authorization rule or OpenAPI schema changed.

## Architecture and project graph

```text
Domain <- Application <- Persistence ----\
                      <- Messaging -------+-> Template.Worker

Domain <- Application <- Infrastructure adapters -> Template.WebApi
```

`Template.Worker` is a thin composition root. It references Application, Persistence and Messaging only; it contains lifecycle, health and Outbox scheduling, not procurement business rules. Application owns provider-neutral event, Outbox, publisher, Inbox and protected-effect ports. Persistence owns database mechanics and SQL. Messaging owns RabbitMQ topology and protocol behavior.

The WebApi does not register the procurement consumer or dispatcher. Its procurement transitions stage Outbox envelopes through `ProcurementWriter` before the existing unit-of-work commit. The Worker calls the same Application-owned integration handler; RabbitMQ routing and retry code contain no eligibility, budget, requisition or authorization policy.

## Integration-event contract

Envelope v1 fields are `eventId`, `eventType`, `eventVersion`, `occurredAtUtc`, `correlationId`, optional `causationId`, `aggregateType`, `aggregateId`, `aggregateVersion` and a minimal `payload`. JSON is camel-case, compact and UTF-8. `eventId` is stable across Outbox retries and broker redelivery and is the Inbox idempotency key together with the consumer name.

Supported Phase 28 event types:

| Event type | Aggregate | Minimal payload purpose |
|---|---|---|
| `procurement.requisition.submitted` | `PurchaseRequisition` | number, revision, cost center, total and needed-by |
| `procurement.requisition.withdrawn` | `PurchaseRequisition` | requisition and revision |
| `procurement.requisition.cancelled` | `PurchaseRequisition` | requisition, revision and actor ID |
| `procurement.requisition.expired` | `PurchaseRequisition` | requisition and revision |
| `procurement.budget.reservation-created` | `BudgetReservation` | reservation, requisition, allocation and amount |
| `procurement.budget.reservation-committed` | `BudgetReservation` | reservation, requisition and amount |
| `procurement.budget.reservation-released` | `BudgetReservation` | reservation, requisition, released amount and reason code |

The payloads contain no credentials, tokens, full entities, purpose/description text or unnecessary personal data. Unit tests pin the submitted-event bytes and round-trip behavior.

## PostgreSQL schema and migration

Migration `20261021010433_Phase28ReliableMessaging` was generated with `dotnet-ef` 10.0.13. Its reviewed SQL is additive: three new tables and seven indexes, with no existing-table alteration, backfill or destructive `Up` operation.

| Table | Purpose and important constraints/indexes |
|---|---|
| `MessagingOutbox` | Immutable envelope plus attempt, next-attempt, lease owner/expiry, dispatched, poison and safe failure-code metadata; primary key `EventId`; claim, retention and `(AggregateId, CreatedAtUtc, EventId)` ordering indexes |
| `MessagingInbox` | Durable `(ConsumerName, EventId)` primary key with received/completed timestamps; duplicates cannot acquire the effect transaction |
| `MessagingProcurementProjections` | Demonstrated protected local effect keyed by consumer, aggregate and event type; stores last event/version, count and update time |

The `Down` migration drops only those three Phase 28 tables. Application and Identity databases remain separate. Both WebApi and Worker may apply migrations at startup; the Worker disables seed data, avoiding duplicate seeding while EF's PostgreSQL migration lock serializes schema work.

## Outbox dispatch

- Claims use a short PostgreSQL transaction, `FOR UPDATE SKIP LOCKED`, a bounded batch and an expiring owner lease.
- A candidate is blocked while an earlier undispatched, non-poison event for the same aggregate exists, providing per-aggregate dispatch order. No global order is promised.
- A successful broker confirm precedes `DispatchedAtUtc`. A crash in between can republish the same event; Inbox idempotency makes that window safe.
- Failures clear the lease, increment the durable attempt count and schedule bounded exponential backoff. The configured maximum marks the row poisoned rather than silently deleting it.
- Only confirmed rows older than the configured retention period are removed, in bounded batches. Pending and poison rows remain inspectable.
- Logs use event IDs, types, attempt counts and exception type/failure code only; payload and credentials are not logged.

## Inbox and consumption

The Worker declares one shared competing-consumer queue. On delivery it validates content type, contract marker, envelope version/type and the equality of AMQP `message_id` with `eventId`. The Application handler then invokes one persistence operation that:

1. inserts the `(consumer, eventId)` Inbox key with `ON CONFLICT DO NOTHING`;
2. applies the projection effect;
3. completes the Inbox row;
4. commits all three in one transaction.

A duplicate returns `Duplicate` and is acknowledged without repeating the protected effect. A handler failure rolls the transaction back, so retry remains possible. Invalid/incompatible contracts go directly to the dead-letter exchange with a reason header. Other failures are republished with a durable attempt header and publisher confirm, then the original is acknowledged; the final failed attempt is copied with confirm to the DLQ before acknowledgement.

On shutdown the consumer cancels new deliveries and gives in-flight work a bounded drain period. Work interrupted by host cancellation remains unacknowledged and is redelivered by RabbitMQ.

## RabbitMQ topology

| Entity | Type/durability | Binding or use |
|---|---|---|
| `procurement.events` | durable topic exchange | publication exchange; routing key is the event type |
| `procurement.projections.v1` | durable shared queue | `procurement.#`; competing Worker consumers |
| `procurement.events.dead-letter` | durable direct exchange | confirmed poison/invalid copy |
| `procurement.projections.v1.dead-letter` | durable queue | routing key `procurement.projections.v1` |

AMQP messages are persistent and carry `content_type=application/json`, `type=procurement.integration-event.v1`, `message_id=eventId`, correlation ID and timestamp. The topology is additive and does not modify the legacy order exchanges, queues, routing keys or dead-letter queues.

## Delivery guarantees

| Boundary | Guarantee |
|---|---|
| business transaction → Outbox | atomic in one PostgreSQL transaction; commit contains both or neither |
| Outbox → RabbitMQ | at-least-once with persistent messages and publisher confirms |
| order | per aggregate at Outbox claim/dispatch; no global or cross-aggregate ordering |
| RabbitMQ → consumer | at-least-once; unacknowledged work is redelivered |
| consumer → same-database protected effect | effectively-once through atomic Inbox key + effect + completion |
| external effects | no exactly-once claim; future adapters must provide their own idempotency contract |

## Failure and recovery matrix

| Failure | Expected state and recovery | Evidence |
|---|---|---|
| business transaction rolls back | business row and Outbox row are both absent | PostgreSQL atomic rollback test |
| Worker is stopped | WebApi remains operational; committed Outbox accumulates | Compose independent-stop check and functional Test host without broker services |
| two dispatchers race | leases are disjoint; earlier same-aggregate event blocks later event | real PostgreSQL two-dispatcher test |
| dispatcher crashes while leased | another Worker claims after lease expiry | real PostgreSQL expired-lease test |
| crash after broker confirm, before DB acknowledgement | duplicate publish is possible and safe; Inbox effect remains once | confirmed publish + concurrent duplicate Inbox tests |
| duplicate broker delivery | handler is invoked, durable Inbox skips repeated effect | real RabbitMQ duplicate test and PostgreSQL eight-way duplicate test |
| transient consumer failure | bounded durable retry, then ACK after successful effect | real RabbitMQ retry test |
| poison or incompatible message | confirmed copy to dedicated DLQ with sanitized reason, then ACK | real RabbitMQ DLQ test |
| two Worker consumers | shared queue distributes work; protected effects remain unique | real RabbitMQ two-consumer test |
| RabbitMQ unavailable | Outbox remains pending; Worker reconnects and drains it after recovery | isolated Compose outage/recovery check |
| PostgreSQL unavailable | Worker liveness stays healthy, readiness reports unavailable; readiness recovers after database restart | isolated Compose outage/recovery check |
| shutdown during a delivery | no ACK; durable queue receives the interrupted delivery again | real RabbitMQ graceful-stop test |
| old confirmed records | bounded retention deletes only rows older than cutoff | real PostgreSQL retention test |

## Compose and operations

Compose adds `worker` without a fixed container name or published port, so instances can scale independently. It waits for healthy PostgreSQL and RabbitMQ, receives credentials only through environment interpolation, exposes internal `/health/live` and `/health/ready`, and uses the same pinned .NET images as WebApi. Liveness is process-only. Readiness requires PostgreSQL and reports broker loss as degraded while allowing durable backlog accumulation.

The isolated baseline creates a new project and volumes, uses synthetic credentials, never attaches user volumes, ends with `stop`, and never deletes resources. Its Phase 28 checks cover clean concurrent startup/migration, independent WebApi/Worker stop and restart, broker outage/backlog/recovery into the Inbox, PostgreSQL outage/readiness/recovery, persistence across full-stack restart, and graceful shutdown. Retained projects are listed in the gate evidence for optional owner-directed cleanup; none was removed.

## Compatibility and rollback

- Public API rollback: none required; OpenAPI is unchanged from Phase 27.
- Application rollback: stop the Worker and deploy the previous WebApi. Phase 28 Outbox rows remain inert and inspectable.
- Schema rollback: only after the Worker and every Phase 28 writer are stopped, migrate down one application-database migration. This removes Phase 28 Outbox/Inbox/projection data, so export or retain it when replay/audit is required.
- Broker rollback: stop Phase 28 publishers/consumers, then remove the additive procurement topology only under an explicit operator-approved procedure. This phase did not delete queues or volumes.
- Legacy compatibility: `AddMessagingInfrastructure`, order-v1 HTTP behavior, wire bytes and in-memory deduplication remain in place until a separately approved migration.

## Verification evidence

| Gate | Result |
|---|---|
| restore | solution filter restored with SDK 10.0.402; vulnerability metadata refreshed |
| WebApi build | 0 warnings, 0 errors |
| Worker build | 0 warnings, 0 errors |
| architecture | 64/64 plus deterministic boundary scan |
| unit | 540/540 |
| in-memory/adapter integration | 223/223 |
| PostgreSQL 16.15 | 49/49 |
| RabbitMQ 4.3.6 | 25/25 |
| Elasticsearch 9.5.5 | 13/13 |
| Redis 8.10.2 | 23/23 |
| functional HTTP | 352/352 |
| Compose | 116/116 in `p14-baseline-20261021132601`; resources stopped and retained |
| OpenAPI | v1/v2 byte-equal after newline normalization and structurally equal to `baseline-phase-27` |
| packages/licenses/security | expected Worker graph expansion (283 production/471 test entries), 155-package license inventory with 0 exceptions outside policy, no vulnerable package; only test-only xUnit 2.9.3 remains deprecated |
| secrets | committed settings, Compose and evidence scan green; no value printed |
| source hygiene | discovery and coverage policy green (Application 97.97/90.08, Messaging 70.08/81.55 line/branch); Graphify 10,688 nodes/26,948 edges/414 communities; `git diff --check` green |

The full gate ran from 13:20 to 13:46 America/Sao_Paulo on 2026-10-21; every one of its 19 steps exited 0. Generated evidence remains under the git-ignored `artifacts/validation` tree.

## Deliberate limits

- The projection is the first protected local consumer effect; email, files, reports, RFQ and other later-phase effects are not implemented.
- Replay is operationally a reset/requeue action requiring an explicit procedure; there is no public replay endpoint.
- Broker health is reachability/degradation, not an exactly-once or end-to-end freshness signal.
- Poison rows and dead-letter messages are retained for inspection; automatic destructive cleanup is intentionally absent.
