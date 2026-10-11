# Phase 9 Execution Handoff

Read and fully execute Phase 9 of this repository's refactoring plan.

Main objective:
Isolate the entire RabbitMQ integration behind Application-owned ports, move publishers, consumers, hosted services, configuration, topology, and vendor-dependent code into Infrastructure, and migrate the `RabbitMQ.Client` client from 6.8.1 to a stable and compatible 7.x release.

Do not merely analyze or propose a plan: implement, test, validate, and document Phase 9 until its acceptance criteria are met.

## Required context

The repository is located at:

`C:\Users\alexa\source\repos\TemplateCleanArchNET`

Before changing any code, read the following in full:

- `AGENTS.md`
- `docs/phase-2-workspace.md`
- `docs/phase-3-implementation-plan.md`
- `docs/refactoring/phases-0-5-report.md`
- `docs/refactoring/phases-6-7-report.md`
- `docs/refactoring/phase-8-report.md`
- Any folder-specific `AGENTS.md` files governing files you modify
- The applicable repository skills, especially:
  - `integration-adapter`
  - `architecture-boundary-check`
  - `dotnet-change-verification`
  - `package-upgrade`
  - `api-contract-change`, only if an HTTP contract genuinely needs to change

Treat the following as the approved API baseline:

- `docs/refactoring/baseline-phase-6/`

Before starting, confirm from the repository's actual state that Phase 8 is complete, with:

- Application free of Redis dependencies or concrete implementation details;
- Phase 8 tests passing;
- the Phase 8 architecture exception removed;
- the HTTP contract preserved;
- the Phase 8 report updated with actual results.

If Phase 8 is not genuinely complete, do not mix the two phases. Objectively record the blocker and stop the Phase 9 execution.

The working tree contains pre-existing changes. Preserve them in full. Do not use reset, checkout, clean, stash, or any other destructive command.

## Current state to characterize

The current implementation includes, among other things:

- `RabbitMQ.Client` referenced directly by `Template.Application`;
- `RabbitMqService` implemented in Application;
- `IRabbitMqService` exposing synchronous operations;
- consumers derived from `BackgroundService` inside Application;
- connection and channel creation inside publishers and consumers;
- exchange, queue, and binding declarations spread across services;
- message serialization inside the concrete implementation;
- hosted services registered by `AddApplicationLayer`;
- v1 endpoints in `MessageController`;
- fanout, direct, and topic exchanges;
- manual acknowledgements;
- a temporary Phase 9 exception in the architecture tests.

Do not assume this list is complete. Before implementing, create an inventory of the actual:

- publishers;
- consumers;
- exchanges;
- exchange types;
- queues;
- bindings;
- routing keys;
- payloads;
- message properties;
- durability;
- persistence;
- acknowledgements;
- failure behavior;
- connection and channel lifecycle;
- DI registrations;
- configuration;
- existing tests;
- HTTP contracts that trigger publishing.

Record this inventory in the final report.

## Phase 9 scope

Implement the requirements defined under “Phase 9 — Move RabbitMQ behind messaging ports and upgrade client” in `docs/phase-3-implementation-plan.md`:

1. Characterize the existing direct, fanout, and topic exchanges, as well as queues, bindings, routing keys, payloads, and acknowledgements.
2. Define application event contracts and ports for publishing and processing.
3. Move hosted services and all RabbitMQ-specific code into Infrastructure.
4. Migrate to the asynchronous APIs of `RabbitMQ.Client` 7.x.
5. Correctly copy or process `ReadOnlyMemory<byte>` payloads before the handler returns.
6. Add broker-backed contract tests for publishing, consumption, retry, poison messages, reconnection, and shutdown.

## Ports and boundaries

Define Application-owned ports with use-case-oriented semantics.

The ports must not expose:

- `IConnection`;
- `IChannel`;
- `IModel`;
- `BasicDeliverEventArgs`;
- `ReadOnlyMemory<byte>` received from the client;
- `RabbitMQ.Client` properties or types;
- vendor-specific configuration names;
- serialization details;
- types belonging to Infrastructure.

Prefer business-oriented names over technology-oriented names. The publishing port should not be called `IRabbitMqService` if its actual purpose is to publish order events.

Application may own:

- event contracts;
- messaging commands or DTOs;
- publishing ports;
- ports for handlers of received messages;
- business logic required for processing;
- abstract policies that are part of the use case.

Infrastructure must own:

- publisher implementation;
- connection and channel creation and management;
- transport serialization and deserialization;
- exchange, queue, and binding declarations;
- consumers and hosted services;
- acknowledgements and negative acknowledgements;
- technical retry;
- poison-message handling;
- reconnection;
- graceful shutdown;
- RabbitMQ-specific configuration;
- broker-related health/readiness behavior, if it is already part of the adopted design.

Domain must remain free of any messaging, hosting, transport, or RabbitMQ dependency.

WebApi must remain only the composition root and HTTP layer.

## Event contracts

Analyze the current use of `Template.Domain.Order.Order` as a transport payload.

Do not turn a domain entity into an integration contract for convenience. Define an explicit and versionable event contract when necessary, preserving the currently observable external fields and behavior.

Define and document:

- logical event name;
- contract version;
- required fields;
- policy for unknown fields;
- read compatibility;
- serialization format;
- encoding;
- message identifier;
- correlation identifier, if one exists;
- timestamp, only when there is an appropriate time source and a concrete need;
- applicable routing key;
- content type;
- handling of invalid or incompatible payloads.

Do not invent metadata without a demonstrated need. If a new technical field is introduced, justify and test it.

Preserve compatibility with currently published messages whenever technically safe. If an incompatibility is unavoidable, clearly document the transition and rollback strategy.

## Topology

Centralize topology ownership and declaration in Infrastructure.

Initially preserve the following unless a problem is demonstrated:

- existing exchange names;
- fanout, direct, and topic types;
- existing queue names;
- existing bindings;
- existing routing keys;
- durability;
- message persistence;
- observable semantics of the existing endpoints.

Declarations must be idempotent and compatible with the current broker.

Do not rename exchanges, queues, or routing keys merely for aesthetic preference.

If the current topology is unsafe or incompatible with Phase 9 requirements, document:

- the problem;
- the minimum necessary change;
- impact on existing messages;
- migration path;
- rollback path.

## Asynchronous publishing

Replace synchronous operations with asynchronous APIs compatible with `RabbitMQ.Client` 7.x.

Ensure:

- `CancellationToken` propagation;
- safe connection reuse;
- correct channel lifecycle;
- no creation of a new connection for every message unless supported by a demonstrated technical justification;
- explicit handling of broker unavailability;
- publisher confirms when necessary to support the documented guarantee;
- structured logging without sensitive content;
- correct asynchronous resource disposal;
- no blocking synchronous operations hidden inside asynchronous methods.

Clearly define what a publisher's return value means. Do not report success to the caller with a stronger guarantee than the implementation actually provides.

## Consumption, acknowledgements, and delivery

Implement consumers in Infrastructure using the asynchronous 7.x APIs.

Define and test the policy for:

- ack only after successful processing;
- nack/requeue after a transient failure;
- retry limits;
- invalid messages;
- incompatible messages;
- poison messages;
- redelivery;
- duplicates;
- reconnection;
- cancellation;
- shutdown.

Phase 9 must demonstrate **at-least-once** delivery behavior. Therefore, handlers must tolerate duplicate messages or have an explicit idempotency strategy.

Do not claim “exactly once.”

If the current use case only logs the received message and no idempotent business operation exists, implement the smallest testable abstraction and clearly document the limitation. Do not invent business persistence or a permanent inbox without an approved need.

A message must not be acknowledged before its payload has been safely copied or processed. In callbacks that receive `ReadOnlyMemory<byte>`, copy or deserialize the content before the handler returns, according to the guarantees of the 7.x client.

## Retry and poison messages

Define an explicit and bounded policy.

The policy must prevent:

- infinite requeue;
- hot redelivery loops;
- silent loss;
- acknowledging an unprocessed message;
- unbounded logging;
- exposure of the complete payload in logs.

If the strategy uses a dead-letter exchange or dead-letter queue:

- declare the topology idempotently;
- use explicit and documented names;
- test the routing;
- preserve the previous topology whenever possible;
- document how to inspect and republish messages;
- do not automatically delete messages or queues.

If another strategy is adopted, justify how it satisfies the retry and poison-message tests.

## Availability and startup

The application must have an explicit policy for RabbitMQ availability and unavailability.

Ensure that:

- the Test environment continues to start without a real RabbitMQ instance;
- functional tests remain deterministic;
- real publishers and consumers can be replaced by fake/no-op adapters in tests;
- broker failure does not prevent unrelated endpoints from starting, according to the policy adopted by the project;
- unavailability is observable without leaking credentials;
- reconnection is bounded and respects cancellation;
- hosted-service constructors do not open external connections;
- connections are opened during the appropriate asynchronous lifecycle stage;
- shutdown stops new consumption, cancels consumers, and correctly disposes resources;
- the host can shut down within a reasonable time even when the broker is unavailable.

Do not indiscriminately hide programming errors. Distinguish broker operational failures, cancellation, and code defects.

## Configuration and security

Move `RabbitMqSettings` and other broker-specific options into Infrastructure unless there is a strong architectural justification for another location.

Use typed options and configuration validation.

Preserve currently used external configuration names whenever possible.

Do not copy real credential values into documentation, tests, logs, or new files.

Remove sensitive defaults from source code when this can be done compatibly. Use placeholders, environment variables, user secrets, or external configuration.

Never log:

- passwords;
- connection strings;
- tokens;
- complete payloads;
- personal data;
- sensitive headers.

## RabbitMQ.Client upgrade

The currently recorded version is `RabbitMQ.Client` 6.8.1.

Before changing the version:

1. Consult current official sources:
   - official RabbitMQ .NET client documentation;
   - official repository and releases;
   - official NuGet page;
   - official 7.x migration guide;
   - compatibility matrix with the current broker, when available.
2. Select a stable and compatible 7.x release.
3. Do not choose a version from memory.
4. Record the following in the report:
   - previous version;
   - selected version;
   - verification date;
   - official sources;
   - relevant breaking changes;
   - API changes implemented;
   - package rollback procedure.
5. Update the related package family coherently, if applicable.
6. Run a direct and transitive vulnerability audit.

Do not upgrade the RabbitMQ server in this phase.

The broker upgrade and pinning belong to Phase 17. Do not change the container's major version, broker feature flags, volumes, or persisted-data format.

If integration tests require a RabbitMQ image:

- use an explicit version compatible with the current environment;
- do not use a mutable tag;
- verify the version using official sources;
- use an isolated and disposable environment;
- do not reuse user volumes;
- do not turn this test into an upgrade of the development or production broker.

## HTTP compatibility

Phase 9 must not change the public HTTP contract.

In particular, preserve the v1 `MessageController` endpoints:

- routes;
- methods;
- body and query parameters;
- status codes;
- textual response content;
- v1 deprecation;
- existing authentication and authorization;
- published OpenAPI.

Internal methods may become asynchronous, but the observable HTTP contract must remain compatible.

Capture OpenAPI again and compare it with:

`docs/refactoring/baseline-phase-6/`

The expected result is exact equality.

If any difference appears, investigate it. Do not silently update the baseline.

## Required tests

Add or update tests proportionally to the implemented design.

### Unit tests

Cover at least:

1. The controller or use case calls the correct port for fanout.
2. The controller or use case calls the correct port for direct.
3. The controller or use case calls the correct port for topic.
4. Routing keys are propagated correctly.
5. `CancellationToken` is propagated.
6. Port errors follow the documented policy.
7. Event contracts are mapped correctly.
8. Consumer handlers handle duplicates according to the selected strategy.
9. Invalid or incompatible payloads are not treated as successful.
10. No unit test depends on a real broker.

### Architecture tests

Prove, using positive tests and controlled mutations when appropriate, that:

- Domain does not reference RabbitMQ or hosting;
- Application does not reference `RabbitMQ.Client`;
- Application contains no integration `BackgroundService`;
- Application does not create connections, channels, consumers, or topology;
- Application contains no RabbitMQ-specific settings;
- Infrastructure implements Application-owned ports;
- messaging hosted services reside in Infrastructure;
- WebApi only composes services;
- the Phase 8 and Phase 9 exceptions do not return;
- exceptions still reserved for Elasticsearch/Phase 10 remain unchanged.

Remove the temporary Phase 9 exceptions from:

`Tests/Template.Architecture.Tests/ArchitectureRulesTests.cs`

only after the implementation is genuinely clean and the tests prove it.

### Tests with a real broker

Create or use a separate, opt-in integration profile backed by an isolated and disposable RabbitMQ instance.

Cover at least:

1. Idempotent topology declaration.
2. Fanout publication and consumption.
3. Direct publication and consumption.
4. Topic publication and consumption.
5. Matching routing key.
6. Non-matching routing key.
7. Relevant message persistence/properties.
8. Ack after success.
9. Redelivery after a transient failure.
10. Retry limit.
11. Poison-message routing or handling.
12. No infinite requeue.
13. Duplicate message and idempotent behavior.
14. Invalid payload.
15. Incompatible contract.
16. Initial broker unavailability.
17. Reconnection after the broker returns.
18. Cancellation.
19. Graceful shutdown.
20. Connection, channel, and consumer release.

Tests must not depend on arbitrary timing when a deterministic alternative exists. Use bounded timeouts and useful diagnostics to prevent hung tests.

Do not report broker tests as passing unless they were actually executed.

### Functional tests

Keep functional tests independent of real RabbitMQ through a fake or no-op adapter.

Cover the existing v1 endpoints and prove that they:

- continue to start in the Test environment;
- preserve methods, routes, parameters, and responses;
- call the expected port;
- respect cancellation;
- do not start real hosted services;
- allow unrelated endpoints to continue working without a broker.

## Feature flag and rollback

The Phase 9 plan requires a safe rollback capability during validation.

Implement the smallest reversible strategy that makes it possible to:

- disable real publishers and consumers through configuration;
- start the application without the broker;
- select a fake/no-op implementation in Test;
- revert the adapter or client version without changing Application;
- preserve the existing event contracts and topology.

If retaining both old and new adapters simultaneously introduces risk or duplicate consumption, do not run them in parallel. Document a safe alternative for rollback through configuration and coherent package/code reversion.

Never allow two competing consumers to process the same queue during validation unless this is intentional and tested.

## Scope boundaries

Do not perform work from subsequent phases:

- do not upgrade the RabbitMQ server;
- do not change RabbitMQ volumes;
- do not enable feature flags for a new broker version;
- do not redesign the entire event architecture;
- do not implement microservices;
- do not introduce Kafka, MassTransit, NServiceBus, or another messaging framework;
- do not refactor Elasticsearch;
- do not upgrade Elasticsearch;
- do not change Redis;
- do not modify HTTP contracts without a demonstrated need;
- do not create EF migrations without a real schema delta;
- do not manually edit migration designer or snapshot files;
- do not perform unrelated cosmetic modernization;
- do not perform work from Phases 10+.

## Validation

Run commands sequentially because parallel builds compete for the same `obj` directories.

Use the pinned SDK:

`C:\Users\alexa\.dotnet\dotnet.exe`

Start with the smallest tests related to the changes.

At the end, run the existing complete validation gate:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-phase-validation.ps1
```

Confirm at least:

- restore;
- WebApi build without warnings;
- architecture tests;
- unit tests;
- EF InMemory integration tests;
- real PostgreSQL integration tests;
- functional tests;
- RabbitMQ adapter-specific tests;
- opt-in tests with a real broker;
- direct and transitive NuGet vulnerability audit;
- `docker compose config -q`;
- OpenAPI capture and comparison;
- `git diff --check`.

Because this involves structural changes, regenerate `graphify-out` exclusively through the official Graphify command. Do not edit `graphify-out` manually.

If Docker, RabbitMQ, or another external service is unavailable, do not report the corresponding gate as passed. Record precisely:

- the command attempted;
- the result;
- the observed cause;
- which criteria remain unverified.

Do not silently reduce coverage to obtain a green gate.

## Acceptance criteria

Phase 9 may only be considered complete when:

- Application does not reference `RabbitMQ.Client`;
- Application has no concrete RabbitMQ publishers;
- Application contains no consumers derived from `BackgroundService`;
- Application contains no RabbitMQ-specific settings;
- publishers and handlers depend only on Application ports;
- vendor-specific code resides in Infrastructure;
- WebApi remains the composition root;
- publishing and consumption use asynchronous 7.x client APIs;
- `CancellationToken` is propagated;
- the current topology is inventoried and covered by tests;
- event contracts are explicit and versionable;
- ack occurs only after successful processing;
- retry is bounded;
- poison messages do not enter an infinite requeue loop;
- at-least-once behavior is demonstrated;
- duplicates have a documented and tested strategy;
- reconnection and graceful shutdown are tested;
- the application starts with the broker available and unavailable according to policy;
- functional tests do not depend on a real broker;
- the HTTP contract and OpenAPI remain unchanged;
- the Phase 9 architecture exceptions have been removed;
- Phase 10 exceptions remain;
- all suites unrelated to the broker remain green;
- no Phase 10+ work was started.

## Documentation and delivery

Create:

`docs/refactoring/phase-9-report.md`

The report must include:

- a summary of the adopted design;
- the previous inventory of publishers, consumers, and topology;
- ports created in Application;
- event contracts defined;
- adapters and hosted services created in Infrastructure;
- configuration and DI registrations;
- message format and version;
- exchanges, queues, bindings, and routing keys;
- ack/nack policy;
- retry policy;
- poison-message handling;
- idempotency strategy;
- the delivery guarantee actually implemented;
- reconnection policy;
- startup and shutdown behavior;
- behavior when the broker is unavailable;
- previous and new `RabbitMQ.Client` versions;
- official sources consulted;
- breaking changes handled;
- tests added;
- commands executed and actual results;
- final test counts;
- OpenAPI comparison result;
- vulnerability-audit result;
- residual risks;
- rollback procedure;
- gates not executed;
- explicit confirmation that the RabbitMQ server was not upgraded;
- explicit confirmation that no Phase 10+ work was implemented.

Update the root `AGENTS.md` only with results that were actually executed and confirmed.

Upon completion, present:

1. An objective implementation summary.
2. The main files changed.
3. Ports, adapters, and contracts created.
4. Topology preserved or justified changes.
5. The selected client version and official sources.
6. Tests and validations with actual results.
7. Pending work or gates not executed.
8. Risks and rollback.
9. Confirmation that the HTTP contract was preserved.
10. Confirmation that the Phase 9 architecture exceptions were removed.
11. Confirmation that the broker was not upgraded.
12. Confirmation that Phases 10+ were not started.

Do not commit, push, merge, change branches, or open a pull request without explicit authorization.
