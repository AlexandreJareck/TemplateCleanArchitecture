# Phase 28 Execution Handoff — Reliable Asynchronous Execution

## Mission

Turn the existing demonstration messaging adapter into business-safe asynchronous infrastructure. Create a separately deployable Worker, transactional Outbox, durable Inbox, and versioned procurement events; prove atomic publication, idempotent consumption, retries, dead-letter, restart, and multi-worker behavior.

## Required context and skills

Read root/nested instructions, Phase 9/13/17 reports, Phase 23 event catalog/ADRs, Phase 27 report, current RabbitMQ adapter/topology/serializer/consumer tests, persistence transactions, Compose, health/observability, and validation scripts. Use `integration-adapter`, `ef-migration`, `architecture-boundary-check`, `api-contract-change` only if HTTP changes, and `dotnet-change-verification`.

## Preconditions

- Phases 23–27 are complete and green.
- The Phase 23 event catalog identifies producer, consumer, schema version, idempotency key, sensitive fields, and delivery semantics.
- Record HEAD/dirty tree and baseline package/architecture/test/topology/wire-format evidence.
- Verify current RabbitMQ.Client/server support from official sources before package/image changes; upgrades are not implicit scope.

## Target structure

Create `Src/Workers/Template.Worker` (exact path/name may follow the approved ADR) as a composition root referencing Application and required Infrastructure adapters. It owns business message consumers, outbox dispatch, report/notification/projection handlers added later, health/lifecycle, and graceful shutdown.

The WebApi publishes business state plus Outbox records in one PostgreSQL transaction. It must not directly perform critical broker publication after commit. Preserve legacy v1 messaging endpoints and their documented behavior until separately deprecated; isolate their compatibility path rather than silently changing responses.

The Worker is an execution host, not a second business layer. Consumers invoke Application use cases that enforce the same Phase 23 rule IDs, permissions, policy versions, state transitions, and idempotency semantics as synchronous entry points. Do not reimplement eligibility, budget, sourcing, approval, purchasing, or receiving decisions in RabbitMQ handlers, routing keys, retry policies, or topology declarations.

## Outbox

- Application/domain emits provider-neutral events.
- Persistence stores immutable versioned envelopes atomically with business changes.
- A dispatcher claims batches safely across multiple workers, publishes with confirms, records success, and retries failures with bounded backoff.
- Define ordering scope, retention, poison handling, replay, clock, correlation/causation IDs, and observability.
- A crash after broker confirmation but before database acknowledgement must be safe through consumer idempotency.

## Inbox and consumers

- Replace in-memory deduplication for new business handlers with PostgreSQL Inbox records keyed by consumer/subscription plus message ID.
- Inbox acquisition, business side effect, and completion are atomic where the same database is involved.
- Concurrent delivery and process restart execute the effect once; failed processing remains retryable.
- Preserve invalid/incompatible payload classification, bounded retry, dead-letter reason, correlation scope, cancellation, and graceful drain.
- Do not claim global exactly-once delivery; document at-least-once transport with effectively-once idempotent effects.

## Events in scope

Add only events needed by implemented capabilities, for example requisition submitted/cancelled and budget reservation created/released. Keep schemas minimal, stable, versioned, byte-tested where compatibility matters, and free of credentials, full entities, and unnecessary personal data.

## Compose and operations

Add the Worker to isolated Compose with validated settings, health, dependency ordering, no public port unless needed for health, and separate scaling. Prove WebApi operation when Worker is temporarily unavailable, backlog recovery, two-worker contention, broker outage/recovery, PostgreSQL outage, restart, shutdown, and dead-letter inspection. Never attach rehearsals to user volumes.

## Out of scope

- RFQ/quote implementation, reports, email provider, Durable Functions, generic scheduling, Azure Service Bus, or microservices;
- changing the public v1 order contract;
- deleting the old in-memory store until every remaining legacy consumer has an approved migration.

## Tests and validation

Add unit tests for envelope/dispatch policy; PostgreSQL tests for outbox/inbox atomicity, claims, concurrency, crash windows, retention; RabbitMQ real-service tests for confirms, duplicates, retries, dead-letter, recovery, two workers and shutdown; functional tests proving HTTP success is not coupled to immediate broker availability where approved; architecture mutations; Compose baseline and OpenAPI comparison.

Run sequential restore/build/tests, all affected real-service suites, discovery/coverage, package/license/vulnerability audits, secret scan, Compose smoke/restart, Graphify, OpenAPI, and `git diff --check`.

## Acceptance criteria

- A committed business change cannot lose its integration event.
- Duplicate delivery or worker restart does not repeat a protected side effect.
- Synchronous and asynchronous entry paths produce the same business outcome for the same rule/policy version; no business policy exists only in a consumer.
- WebApi and Worker deploy/stop independently and recover backlog safely.
- Topology and event versions are documented and tested.
- Failure/retry/dead-letter state is observable without sensitive payload logging.
- Required gates pass; otherwise report `Not ready`.

## Report and final response

Create `docs/refactoring/phase-28-report.md` with architecture, project graph, schema, delivery guarantees, failure matrix, topology/contracts, tests/counts, real broker/PostgreSQL/Compose evidence, migration and rollback, retained compatibility paths, and gates. Lead with readiness and reliability guarantees.

Do not commit/push/merge, change branches/worktrees, open a PR, delete queues/volumes/data, or alter user environments without explicit authorization.
