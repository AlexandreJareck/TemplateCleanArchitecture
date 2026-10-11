# Phase 17 Execution Handoff

Read and fully execute Phase 17 of this repository's refactoring plan.

Main objective:
Upgrade and pin the RabbitMQ management server independently after the Phase 9 client adapter is stable. Research and follow the officially supported RabbitMQ 4.x upgrade sequence, preserve plugins, feature flags, definitions, topology, durable state, and v1 message contracts, rehearse the upgrade on isolated copied state, and prove publish/consume, acknowledgement, retry, poison-message, reconnect, restart, shutdown, and recovery behavior with an executable rollback path.

Do not merely analyze or produce a plan: research, implement, rehearse, test, validate, and document Phase 17 until all acceptance criteria are met.

## Required context

The authoritative repository is:

`C:\Users\alexa\source\repos\TemplateCleanArchNET`

Before changing any file, read in full:

- `AGENTS.md`;
- `docs/phase-2-workspace.md`;
- `docs/phase-3-implementation-plan.md`;
- `docs/refactoring/phases-0-5-report.md`;
- `docs/refactoring/phases-6-7-report.md`;
- `docs/refactoring/phase-8-report.md`;
- `docs/refactoring/phase-9-report.md`;
- all subsequent phase reports through the latest phase actually executed, including `docs/refactoring/phase-14-report.md`;
- `docs/refactoring/handoff-phase-9.md` for the client adapter, message contract, topology, retry, and delivery decisions;
- `docs/refactoring/handoff-phase-14.md` for the reproducible Compose baseline;
- reports for Phases 15 and 16 if those phases were executed in the accumulated sequence;
- every folder-specific `AGENTS.md` governing files you modify;
- the applicable repository skills, especially:
  - `package-upgrade`, because the RabbitMQ server image changes;
  - `integration-adapter`, because the existing adapter and availability policy must be validated against the target broker;
  - `dotnet-change-verification` if any .NET, project, solution, or build file changes;
  - `architecture-boundary-check` if project references, package references, or layer placement changes;
  - `api-contract-change` only if an observable HTTP contract could change.

Treat `docs/refactoring/baseline-phase-6/` as the approved API baseline unless a later, explicitly approved phase report records a replacement baseline.

## Prerequisites

Phase 17 depends on Phases 9, 13, and 14.

Before editing, verify from actual code, reports, tests, Compose configuration, and live isolated behavior that:

- Phase 9 is complete: Application owns the versioned order event and messaging ports; RabbitMQ-specific code, topology, publisher, consumers, serializer, retries, dead-lettering, reconnection, and hosted services reside in `Template.Infrastructure.Messaging`; `RabbitMQ.Client` is on the documented supported 7.x line; and the broker-backed contract suite passes;
- Phase 13 is complete: broker availability/readiness, logging, correlation, redaction, and shutdown behavior are explicitly classified and tested;
- Phase 14 is complete: `docs/refactoring/phase-14-report.md` contains actual pinned-image, clean-volume Compose, health, restart, persistence, and rollback evidence, with no mutable image tag remaining;
- the current broker image, digest, Compose configuration, plugin configuration, health check, volumes, and application settings match the Phase 14 report;
- no previously adopted phase is missing from this checkout;
- if Phases 15 and 16 were executed in sequence, their accumulated state and reports are present and must remain untouched;
- the latest documented validation checkpoint matches the current repository state;
- this is the authoritative checkout, not an outdated branch or worktree.

If a prerequisite or accumulated prior-phase state is missing, stop and report the blocker. Do not recreate the missing work inside Phase 17 and do not selectively copy files from another checkout.

## Checkout and working-tree safety

Work directly in the authoritative checkout above.

The working tree may contain pre-existing user or concurrent-agent changes. Inventory and preserve them in full. Do not overwrite, normalize, or reformat unrelated files.

Do not:

- run `git reset`, `git checkout --`, `git clean`, or `git stash`;
- perform a destructive rebase or discard working-tree changes;
- commit, push, merge, change branches, create another worktree, or open a pull request without explicit authorization;
- delete or overwrite RabbitMQ volumes, definitions, queues, exchanges, bindings, policies, users, vhosts, feature-flag state, persisted messages, logs, backups, or other material broker state;
- run an upgrade against the user's only broker/data copy;
- start an older broker against data files written or upgraded by a newer incompatible broker.

## Phase 17 scope

Implement the requirements under “Phase 17 — Upgrade RabbitMQ server independently” in `docs/phase-3-implementation-plan.md`:

1. Re-query the officially supported RabbitMQ broker upgrade sequence and feature-flag requirements.
2. Export definitions and inventory queues, exchanges, bindings, policies, plugins, feature flags, and persistence expectations.
3. Rehearse the supported version sequence on an isolated broker using copied definitions and, where officially supported, copied broker data or another supported message-preservation strategy.
4. Pin the target RabbitMQ management image to an explicit compatible version/digest and enable only required plugins explicitly.
5. Run publish/consume, ack, retry, poison-message, reconnect, restart, shutdown, and recovery tests.

This phase changes the RabbitMQ server only. Do not redesign message contracts, topology, Application ports, handler semantics, or unrelated containers.

## Current-state inventory before editing

Treat the actual post-Phase-14 accumulated state as authoritative. Inventory and record at least:

- RabbitMQ Compose service, management image tag/digest, runtime RabbitMQ version, bundled Erlang/OTP version, architecture, command, hostname/node name, environment-key names, health check, dependencies, ports, networks, configs, restart policy, log/data volumes, and resource limits;
- cluster/node identity, cluster membership, node type, running nodes, alarms, partitions, listeners, memory/disk thresholds, and shutdown behavior;
- enabled plugins, plugin versions, and the source of `enabled_plugins` configuration;
- feature flags and their states;
- vhosts, users, permissions, topic permissions, policies, operator policies, limits, parameters, and runtime parameters, without exposing credentials or password hashes;
- exchanges, types, durability, auto-delete/internal settings, alternate exchanges, and arguments;
- queues, types, durability, exclusivity, auto-delete settings, arguments, leader/member placement where applicable, message counts, unacknowledged counts, consumer counts, and dead-letter configuration;
- bindings, routing keys, and arguments;
- definitions export/import mechanism and contents with secrets redacted;
- persistence/data-store layout, durable queue expectations, message persistence properties, and recovery behavior;
- current `RabbitMqSettings` keys and Compose environment-variable mappings by name only;
- `OrderMessagingTopology`, exchange/queue/binding declarations, retry/dead-letter topology, and application startup/recovery declarations;
- `OrderPlacedEvent` v1 fields, serialization, content type, encoding, headers, message ID, routing keys, and byte-compatibility tests;
- publisher-confirm semantics and the meaning of publish success;
- acknowledgement, nack/requeue, retry, poison-message, idempotency, duplicate, reconnect, and shutdown policies;
- current RabbitMQ.Client and related package versions;
- broker-backed test image/version/digest and every existing broker test;
- health/readiness classification established in Phase 13;
- known external publishers/consumers within the user-provided scope.

At the authoring checkpoint, the Phase 9 implementation used `RabbitMQ.Client` 7.2.2, a versioned `OrderPlacedEvent` v1 contract, confirm-based publishing, manual ack after successful processing, bounded retries, dead-letter queues, bounded reconnection, and in-process idempotency. The topology included fanout, direct, and topic exchanges plus durable queues and dead-letter paths; the Phase 9 report also noted non-durable exchanges and a broker image that originally used `rabbitmq:latest`. Confirm the actual post-Phase-14 state instead of assuming these details are unchanged.

Record a sanitized inventory in `docs/refactoring/phase-17-report.md`.

## Version, Erlang, and upgrade-path research

RabbitMQ server and Erlang compatibility information is time-sensitive. Do not select versions from memory or use the historical Phase 2 table as current authorization.

Before editing the image reference, consult current official primary sources, including as applicable:

- RabbitMQ release information and supported-version policy;
- official upgrade guides for the exact source and target versions;
- required intermediate-version rules and supported upgrade hops;
- feature-flag compatibility and enablement guidance;
- RabbitMQ backup/restore and definitions import/export guidance;
- queue-type, metadata-store, schema, and data-directory upgrade notes;
- rolling-upgrade and blue/green migration guidance;
- message transfer, federation, shovel, draining, and quorum-queue guidance when relevant;
- plugin compatibility and management-plugin documentation;
- RabbitMQ/Erlang/OTP compatibility matrix;
- official management container image tags, manifests, and bundled Erlang versions;
- RabbitMQ .NET client compatibility documentation;
- security advisories for RabbitMQ, Erlang, and the selected image.

Select only a stable, supported RabbitMQ 4.x management image compatible with the current client and required plugins. Verify the exact bundled Erlang/OTP version and image architecture.

Record:

- source and target RabbitMQ versions;
- source and target image tags/digests;
- bundled Erlang/OTP versions;
- verification date;
- direct official source links;
- supported upgrade sequence and any required intermediate hop;
- feature-flag prerequisites and post-upgrade implications;
- plugin compatibility;
- client compatibility;
- data/message-preservation method;
- rollback constraints.

Use an explicit tag and immutable digest when available. Do not introduce `latest`, floating major/minor tags, preview releases, unofficial images, or an unsupported Erlang/RabbitMQ combination.

If Phase 14 already pinned the same currently supported target patch, do not manufacture an image change. Still execute the definitions, feature-flag, persistence, restart, recovery, and rollback validation required by Phase 17.

Do not update `RabbitMQ.Client` merely because a newer version exists. If the current client is proven incompatible with the selected supported broker, update only the smallest coherent supported client/package family using official sources, preserve the Phase 9 port/contract semantics, document breaking changes, and run direct/transitive vulnerability audits. Do not update unrelated packages.

## Definitions and secret handling

Export definitions before any upgrade rehearsal and verify that the export is readable and complete for the intended recovery method.

Definitions must account for, as applicable:

- vhosts;
- users and permissions;
- topic permissions;
- policies and operator policies;
- limits and parameters;
- exchanges;
- queues;
- bindings;
- runtime parameters relevant to enabled plugins.

Definitions do not contain queued message bodies. Never claim that a definitions export backs up persisted messages.

Treat exported definitions as sensitive because they may contain user records, password hashes, endpoints, or plugin parameters. Store rehearsal artifacts outside version control or in an ignored secure location, restrict access appropriately, redact report excerpts, and never print credentials, password hashes, Erlang cookies, connection strings, tokens, complete payloads, or personal data.

Verify import into an isolated compatible broker and compare the resulting topology semantically. Do not import definitions into a user or production-like broker without explicit authorization.

## Feature flags

Inventory every feature flag and its state before the upgrade.

For the exact source-to-target path, determine from official guidance:

- which flags must be enabled before leaving the source version;
- which flags are enabled automatically or remain optional;
- which flags become required in the target;
- whether enabling a flag is irreversible for downgrade purposes;
- whether every node must support a flag before enablement;
- whether any plugin adds feature flags;
- how feature-flag state is preserved or restored.

Enable only the flags required by the supported sequence or explicitly adopted target behavior. Perform irreversible flag changes only in the isolated rehearsal/copy until the cutover is authorized and rollback implications are understood.

Do not enable every available flag blindly. Do not disable or edit feature-flag state files manually. Do not claim rollback to an older broker is possible after an incompatible flag/schema change; rollback must use the preserved old environment.

## Topology and message-contract compatibility

Preserve the Phase 9 topology and external message contract unless a target incompatibility is objectively demonstrated.

Do not silently change:

- exchange names or types;
- exchange durability, auto-delete, internal, or alternate-exchange behavior;
- queue names, types, durability, exclusivity, or auto-delete behavior;
- routing keys or bindings;
- dead-letter exchange/queue names and routing;
- retry headers or delivery-attempt semantics;
- publisher-confirm behavior;
- message persistence properties;
- `OrderPlacedEvent` v1 JSON shape, encoding, content type, headers, message ID, or compatibility policy;
- acknowledgement timing;
- at-least-once delivery semantics;
- duplicate/idempotency behavior.

Declarations must remain idempotent on the target broker. If the current non-durable exchange behavior is intentional compatibility from Phase 9, test redeclaration/recovery after restart; do not change it merely for aesthetics.

If the target rejects an existing declaration or policy, document the exact incompatibility, affected messages/clients, minimum supported change, migration path, and rollback impact. Stop for approval before making a public contract or destructive topology change.

## Durable data and message preservation

Preserve definitions and durable messages as separate concerns.

Inventory which queues/messages are expected to survive restart and upgrade. For isolated validation, create synthetic, non-sensitive sentinel messages with known IDs and persistence properties, leave selected messages ready/unconsumed, and verify them after the supported upgrade/restart path without exposing payload contents.

Choose a preservation strategy based on official guidance and the actual topology, such as a supported in-place version sequence performed on a verified copy, a blue/green broker with an officially supported transfer/drain mechanism, or another supported backup/recovery approach. Do not improvise a live data-directory copy or assume that copying a volume while the broker is running is consistent.

If using federation, shovel, draining, mirroring, or another transfer mechanism:

- verify plugin/version support;
- scope it to the isolated rehearsal;
- prevent loops and duplicate routing;
- account for acknowledgements and publisher confirms;
- define how in-flight and unacknowledged deliveries are handled;
- compare message counts and sentinel IDs;
- remove only test-owned temporary transfer configuration after verification;
- do not claim exactly-once transfer.

If real/user-owned broker data or messages are required, stop and obtain explicit authorization before reading, copying, exporting, draining, mirroring, consuming, republishing, or migrating them. If only synthetic data is used, state that clearly and do not claim real-message migration compatibility.

Never discard queues or messages to make the target start. Never purge a queue, delete a vhost, or recreate a queue under incompatible arguments without explicit authorization.

## Isolated upgrade rehearsal

Rehearse the supported sequence in a parallel isolated environment before changing the default Compose image.

Use:

- the exact Phase 14 source image and a verified source-data copy or reconstructed source topology;
- a unique project/environment name;
- distinct container/node names, hostnames, networks, ports, and volumes;
- synthetic credentials supplied outside version control;
- copied definitions and synthetic messages/data;
- bounded waits and deterministic health probes;
- no connection to user/production-like brokers.

Never run old and target brokers against the same data volume. Never allow two active consumers to process the same user queue unintentionally.

The rehearsal must demonstrate:

1. The source broker starts and matches the recorded source version/digest.
2. Definitions/topology import or source reconstruction is complete.
3. Required plugins and feature flags are in the expected source state.
4. Persistent sentinel messages exist in representative durable queues.
5. The officially supported upgrade hop or transfer sequence is followed exactly.
6. The target starts on the selected RabbitMQ/Erlang combination and becomes healthy.
7. Required target feature flags and plugins are correct.
8. Exchanges, queues, bindings, policies, permissions, arguments, and dead-letter paths match expectations.
9. Persistent sentinel messages and message properties remain available without silent loss.
10. Application publisher/consumer contract tests pass.
11. Broker and application restart preserve/recover expected durable state.
12. Rollback is demonstrated by switching the application back to the untouched source broker, not by downgrading target data.

Keep the source environment, old image reference, definitions, data backup/copy, and migration artifacts after acceptance. Deletion requires explicit authorization.

## Plugins and management image

Use the official RabbitMQ management image selected through the compatibility research.

Enable only the plugins actually required by the repository and the supported migration/recovery method. At minimum, verify the management plugin and every plugin referenced by definitions, policies, runtime parameters, or the rehearsal transfer strategy.

Keep plugin enablement explicit and reproducible. Verify plugin startup, version compatibility, management API availability, and persistence across restart.

Do not add delayed-message exchange, federation, shovel, Prometheus, tracing, or other plugins as permanent dependencies unless they already exist or are explicitly required and approved. A temporary rehearsal plugin must not silently become a production dependency.

## Broker behavior tests

Run the Phase 9 broker-backed contract suite against the selected target and extend it only where the server upgrade creates a coverage gap.

Cover at least:

### Topology and routing

- idempotent topology declaration;
- fanout routing to all expected queues;
- direct routing for matching and non-matching keys;
- topic routing for matching and non-matching patterns;
- dead-letter exchange/queue declaration and routing;
- topology recovery after broker restart;
- definitions export/import parity.

### Publishing and persistence

- publisher confirms;
- persistent message properties;
- returned/nacked/unroutable behavior according to the existing contract;
- publish cancellation and timeout;
- broker unavailable on initial publish;
- publish recovery after broker returns;
- persistent sentinel survival across restart and the rehearsed upgrade path.

### Consumption and delivery

- ack only after successful handling;
- transient failure and redelivery;
- bounded retry;
- poison-message dead-lettering;
- invalid payload and incompatible contract handling;
- no infinite requeue/hot loop;
- duplicate delivery/idempotency behavior;
- `ReadOnlyMemory<byte>` payload safety;
- prefetch and consumer behavior;
- cancellation and in-flight delivery handling;
- graceful consumer shutdown;
- consumer/channel/connection release.

### Recovery and availability

- host startup with broker available;
- host startup with broker unavailable according to policy;
- bounded reconnect;
- recovery after broker restart;
- recovery after network interruption in the isolated test environment;
- health/readiness behavior for healthy, unavailable, timeout, and recovered states;
- non-messaging endpoints remain available according to the adopted fail-soft policy;
- broker and WebApi shutdown complete within bounded time.

Do not report exactly-once delivery. Preserve and demonstrate at-least-once semantics.

Avoid arbitrary sleeps when a deterministic signal or bounded polling probe exists. Capture useful sanitized diagnostics for failures without logging payloads or credentials.

## Cutover and rollback

Do not switch the default Compose broker image/configuration until the isolated rehearsal and contract suites pass.

Before cutover, record:

- source and target image references/digests;
- source and target RabbitMQ/Erlang versions;
- source and target node/container/volume identifiers;
- source feature-flag and plugin state;
- verified definitions artifact and source-data recovery point;
- message preservation/drain strategy;
- expected write/publish pause, if any;
- handling of in-flight/unacknowledged deliveries;
- objective go/no-go criteria;
- exact application configuration switch;
- health and recovery checks;
- timed rollback procedure.

Perform the switch as one coherent configuration change. Do not run two competing consumer sets against the same queues unless the migration design explicitly requires it and tests the consequences.

Rollback means reconnecting the application to the preserved old broker/environment or a verified compatible restore. It must not require starting an older broker against target-upgraded data or reversing incompatible feature flags in place.

Measure rollback timing in the isolated rehearsal and state its limitations. Do not present synthetic timing as a production recovery guarantee.

Do not alter a remote or production-like broker without explicit authorization for that exact environment.

## Configuration, security, and observability

Preserve the external `RabbitMqSettings` configuration contract unless a proven Compose wiring defect must be corrected.

Validate:

- host, port, username, password, virtual host if used, timeouts, retry settings, prefetch, enablement, and consumer enablement are wired to the intended target by key name;
- credentials are supplied through environment variables, user secrets, or another approved external source;
- no credential-bearing fallback is introduced;
- TLS policy is unchanged unless the target requires an approved change;
- logs, health output, management commands, reports, and test failures omit credentials, connection strings, cookies, password hashes, authorization headers, and full payloads;
- broker unavailability remains observable without log storms;
- health checks are bounded and non-mutating.

Do not weaken authentication, use default credentials, expose management ports beyond the existing intended environment, or log complete definitions.

## Public HTTP and OpenAPI compatibility

Phase 17 must not change the public HTTP contract.

Preserve:

- the existing v1 message endpoint routes, methods, body/query parameters, status codes, textual responses, content types, and deprecation state;
- all v1/v2 unrelated endpoints;
- authentication and authorization behavior;
- health routes and semantics established in prior phases;
- published OpenAPI.

Internal broker recovery behavior may change only to follow supported server semantics while preserving the documented public policy.

Capture v1 and v2 OpenAPI after target validation and compare exactly with the approved baseline. Investigate every semantic difference. Do not silently update the baseline.

If target compatibility requires a public message-contract or HTTP change, stop and present the evidence, compatibility impact, transition, and rollback choices before implementing it.

## Required automated checks

Add or update tests proportionally to the server change.

### Definitions and topology checks

Automate or deterministically verify:

- definitions export succeeds;
- sensitive fields are not committed or printed;
- isolated import succeeds;
- exchanges, queues, bindings, policies, permissions, arguments, plugins, and feature flags match the expected sanitized inventory;
- target declarations remain idempotent;
- no queue, binding, or dead-letter path silently disappears.

### Upgrade and persistence checks

Automate or script the isolated supported version sequence, including source/target version assertions, feature-flag gates, persistent sentinel messages, restart persistence, target health, and switch-back to the source environment.

Definitions-only testing is insufficient because definitions do not contain messages.

### Architecture and functional checks

Prove that:

- Domain and Application remain free of RabbitMQ vendor types and hosted-service plumbing;
- messaging implementation remains in Infrastructure;
- WebApi remains the composition root;
- the v1 message contract remains byte/behavior compatible;
- normal functional tests use fakes/no-op adapters and do not require a live broker;
- broker unavailability does not take down unrelated endpoints according to policy;
- no prior architecture exception returns;
- the public HTTP/OpenAPI contract is unchanged.

## Validation

Run restore, build, and tests sequentially because this checkout's shared intermediate outputs contend under parallel execution.

Use the pinned SDK:

`C:\Users\alexa\.dotnet\dotnet.exe`

Start with the smallest RabbitMQ adapter, broker-backed, Compose, and functional checks.

At the end, run the repository validation script:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-phase-validation.ps1
```

Confirm and record the actual results of every applicable gate, including:

- restore;
- WebApi build with warning/error count;
- architecture tests;
- unit tests;
- adapter integration tests without a broker;
- broker-backed contract tests against the selected target;
- isolated source-to-target upgrade/transfer rehearsal;
- definitions export/import parity;
- feature-flag and plugin checks;
- persistent-message/restart checks;
- initial unavailability, reconnect, recovery, and shutdown tests;
- EF InMemory and PostgreSQL integration tests;
- Redis and Elasticsearch real-service suites for their current adopted versions;
- functional tests;
- direct and transitive NuGet vulnerability audit;
- `docker compose config -q`;
- full isolated Compose smoke/restart validation where required by the Phase 14 baseline;
- OpenAPI capture and exact comparison;
- timed rollback/switch-back rehearsal;
- `git diff --check`.

Because this phase changes infrastructure topology/configuration, regenerate `graphify-out` only when repository rules or structural source changes require it. Do not hand-edit generated Graphify output. If Graphify does not model the Compose-only change and no structural code change occurred, record why regeneration was not required.

If Docker, the official RabbitMQ image, required broker tooling, or another dependency is unavailable, do not report the corresponding gate as passed. Record the command attempted, sanitized result, observed cause, and unverified criteria. Do not silently reduce message-preservation, feature-flag, or recovery coverage to obtain a green result.

## Package, image, and security audit

After the authorized changes:

- verify the target runtime RabbitMQ and Erlang/OTP versions;
- verify the management image digest and architecture;
- confirm no mutable RabbitMQ image tag remains;
- verify enabled plugins and feature flags;
- inspect direct and transitive NuGet package graphs;
- run the vulnerability audit;
- confirm no stale or unintended broker/client dependency was introduced;
- confirm no unrelated package family or container image changed;
- verify `.env` remains ignored/untracked and `.env.example` contains placeholders only;
- ensure definitions, backups, logs, reports, and artifacts contain no credentials, hashes, cookies, or payloads.

## Scope boundaries

Do not perform work from adjacent or later phases:

- do not redesign message contracts or introduce a v2 event;
- do not rename or cosmetically redesign topology;
- do not replace RabbitMQ or introduce Kafka, MassTransit, NServiceBus, or another messaging framework;
- do not change Application ports or handler business semantics without a proven server-compatibility defect;
- do not change the delivery guarantee from at-least-once or claim exactly-once;
- do not add a permanent inbox/outbox or distributed deduplication store as unrelated work;
- do not upgrade PostgreSQL, Redis, Elasticsearch, Kibana, Redis Insight, test tooling, or VSIX;
- do not implement Phase 18 Redis Insight work;
- do not change public HTTP contracts or remove API v1;
- do not create EF migrations without a real relational schema delta;
- do not rewrite applied migrations or hand-edit migration designer/snapshot files;
- do not purge queues, delete definitions, remove messages, or delete broker volumes/backups;
- do not enable unrelated plugins or feature flags;
- do not perform unrelated package, code, formatting, or documentation cleanup.

## Acceptance criteria

Phase 17 may be considered complete only when:

- Phases 9, 13, and 14 and the authoritative accumulated checkout were verified;
- current official RabbitMQ/Erlang sources were consulted and recorded;
- the exact supported source-to-target RabbitMQ 4.x upgrade path was identified and followed;
- a stable supported management image with explicit tag/digest and compatible Erlang/OTP was selected;
- source definitions, topology, plugins, policies, feature flags, persistence, and message expectations were inventoried;
- definitions were exported securely and imported successfully into an isolated compatible broker;
- required feature flags were handled according to official guidance;
- required plugins were enabled explicitly and no unrelated permanent plugin was introduced;
- the Phase 9 exchange/queue/binding/dead-letter topology remains correct and idempotent;
- the `OrderPlacedEvent` v1 contract and public message endpoint behavior remain compatible;
- persistent synthetic sentinel messages survive the rehearsed supported upgrade/restart path without silent loss;
- publish/consume, publisher confirms, ack-after-success, bounded retry, poison-message dead-lettering, duplicates, reconnect, restart, shutdown, and recovery tests pass;
- at-least-once behavior remains demonstrated and exactly-once is not claimed;
- WebApi handles broker availability/unavailability according to the adopted policy;
- broker health is correct on the pinned target;
- restart preserves expected durable state and topology recovery works;
- timed rollback to the untouched source broker was demonstrated without data-directory downgrade;
- old definitions, source data/volume backup, source image reference, and rollback configuration remain available;
- the public HTTP/OpenAPI contract remains unchanged;
- no queues, definitions, or persisted messages were silently discarded;
- all required validation gates actually pass;
- no Phase 18+ work was implemented.

If only synthetic data/messages were available, explicitly state that real broker-data migration compatibility remains unverified. Do not infer it from synthetic coverage.

## Documentation and delivery

Create:

`docs/refactoring/phase-17-report.md`

The report must include:

- an executive summary;
- proof of prerequisite and authoritative-checkout verification;
- the pre-change broker/image/configuration inventory;
- sanitized topology, vhost, policy, plugin, feature-flag, persistence, and message inventory;
- source/target RabbitMQ, Erlang/OTP, image, and digest matrix;
- verification date and direct official source links;
- the officially supported upgrade sequence and intermediate hops;
- feature-flag decisions and downgrade implications;
- plugin compatibility and explicit enablement;
- definitions export/import method, secure artifact location, and parity result;
- message/data preservation strategy;
- whether copied real data or synthetic data/messages were used;
- persistent sentinel-message and restart results;
- Phase 9 message-contract and topology compatibility results;
- publish/consume, confirms, ack, retry, poison-message, duplicate, reconnect, shutdown, and recovery results;
- Compose and health changes;
- cutover decision and exact configuration switch;
- timed rollback/switch-back procedure and result;
- exact retained source containers, volumes, definitions, images, and backup artifacts;
- packages/images changed and confirmation unrelated families were untouched;
- tests added or updated;
- commands actually executed and sanitized actual results;
- final test counts;
- OpenAPI comparison result;
- vulnerability-audit result;
- Graphify result or documented reason it was not required;
- residual risks and known limitations;
- gates not executed or blocked;
- explicit confirmation that no queue, definition, persisted message, volume, or material data was deleted;
- explicit confirmation that no Phase 18+ work was implemented.

Update `AGENTS.md`, `.env.example`, setup/upgrade/rollback instructions, and validation scripts only with configuration-key names, commands, and results actually verified. Never include secret values or full definitions.

Upon completion, present:

1. An objective implementation summary.
2. The main files, images, packages, plugins, and configuration changed.
3. The selected RabbitMQ/Erlang versions/digest and official compatibility sources.
4. The supported upgrade sequence and feature-flag decisions.
5. Definitions/topology and durable-message preservation results.
6. Publish/consume, ack, retry, poison-message, duplicate, reconnect, restart, shutdown, and recovery results.
7. Compose health and WebApi availability results.
8. Full validation results with actual counts.
9. OpenAPI, vulnerability-audit, and Graphify results.
10. Pending/blocked gates and real-data limitations.
11. The timed rollback procedure and exact retained source resources.
12. Confirmation that no queue, definition, persisted message, volume, or material data was deleted.
13. Confirmation that Phase 18 was not started.

Do not commit, push, merge, change branches, create another worktree, delete broker resources/data, alter a production-like environment, or open a pull request without explicit authorization.
