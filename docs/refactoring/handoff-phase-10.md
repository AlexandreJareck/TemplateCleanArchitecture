# Phase 10 Execution Handoff

Read and fully execute Phase 10 of this repository's refactoring plan.

Main objective:
Separate the person search/indexing capability from the logging infrastructure, remove all Elasticsearch dependencies and concrete implementation details from `Template.Application`, and place the search integration behind an Application-owned port and an adapter implemented in Infrastructure.

This phase must preserve the current behavior of the Elasticsearch client and server on the 8.x line. Do not perform the Elasticsearch 9 migration, because it belongs to Phase 11.

Do not merely analyze or propose a plan: implement, test, validate, and document Phase 10 until all acceptance criteria are met.

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
- `docs/refactoring/phase-9-report.md`
- Any folder-specific `AGENTS.md` files governing files you modify
- The applicable repository skills, especially:
  - `integration-adapter`
  - `architecture-boundary-check`
  - `dotnet-change-verification`
  - `api-contract-change`, only if an HTTP contract genuinely needs to change
  - `package-upgrade`, only if a package version genuinely needs to change

Treat the following as the approved API baseline:

- `docs/refactoring/baseline-phase-6/`

## Prerequisites

Before starting, confirm from the repository's actual state that Phases 8 and 9 are complete.

For Phase 8, confirm at least:

- Application is free of Redis dependencies or concrete implementation details;
- the Redis adapter is located in Infrastructure;
- Phase 8 tests are passing;
- the Phase 8 architecture exception has been removed;
- the Phase 8 report is updated with actual results.

For Phase 9, confirm at least:

- Application does not reference `RabbitMQ.Client`;
- concrete publishers and consumers are located in Infrastructure;
- RabbitMQ hosted services have been removed from Application;
- the RabbitMQ client has been upgraded according to the documented decision;
- Phase 9 tests are passing;
- the Phase 9 architecture exceptions have been removed;
- the Phase 9 report is updated with actual results.

If either phase is not genuinely complete, do not mix implementations. Objectively record the blocker and stop the Phase 10 execution.

## Checkout and working tree

Work in the checkout that contains the accumulated and authoritative state from the previous phases.

Before editing:

- confirm that the scripts, skills, baselines, reports, and architecture tests from the previous phases exist in this checkout;
- confirm that the code from Phases 0–9 is present;
- do not use a worktree based on an old commit that does not contain these changes;
- do not selectively copy files from another checkout;
- do not reimplement previous phases.

The working tree may contain pre-existing changes belonging to the user. Preserve them in full.

Do not use:

- `git reset`;
- `git checkout --`;
- `git clean`;
- `git stash`;
- destructive rebases;
- any command that discards or overwrites existing changes.

Do not commit, push, merge, change branches, or open a pull request without explicit authorization.

## Current state to characterize

The initially known implementation includes, among other things:

- `Elastic.Clients.Elasticsearch` referenced directly by `Template.Application`;
- `ElasticService` implemented in Application;
- `IElasticService` with a vendor-oriented name and semantics;
- direct construction of `ElasticsearchClient` inside Application;
- `ElasticSearchSettings` inside Application;
- index creation, indexing, bulk update, query, and removal operations;
- use of `Template.Domain.Person.Person` as the transport/indexing document;
- v1 endpoints in `PersonController`;
- configuration shared by search, logging, and health checks;
- `Elastic.Serilog.Sinks` configured by WebApi;
- the Elasticsearch health check configured by WebApi;
- a temporary Phase 10 exception in the architecture tests.

Do not assume this list is complete.

Before implementing, create an inventory of the actual:

- package references;
- interfaces and implementations;
- DI registrations;
- client creation and lifecycle;
- indices;
- aliases, if any;
- indexed documents;
- indexing operations;
- queries;
- bulk operations;
- removals;
- configuration;
- authentication;
- health checks;
- Serilog sink configuration;
- data streams;
- startup behavior;
- behavior when Elasticsearch is unavailable;
- HTTP endpoints that depend on search;
- existing tests;
- Compose images and services related to Elasticsearch and Kibana.

Record this inventory in the final report.

## Phase 10 scope

Implement the requirements defined under “Phase 10 — Move Elastic concerns behind infrastructure boundaries” in `docs/phase-3-implementation-plan.md`:

1. Determine whether the current search/indexing functionality is an active requirement and define the smallest possible Application-owned port.
2. Move the Elasticsearch client and implementation into Infrastructure while preserving the current 8.x behavior.
3. Separate search/indexing configuration from Serilog logging transport configuration.
4. Make search and logging startup tolerate unavailability according to an explicit and documented policy.
5. Add adapter tests and functional tests for Elasticsearch available and unavailable states.

## Determining the active requirement

The plan mentions “product search,” but the known code exposes operations related to `Person`.

Do not implement a new product search merely because the plan uses that expression.

Inspect the actual behavior and determine:

- which endpoints are published;
- which operations are actually in use;
- whether the current functionality is CRUD indexing, search, or both;
- which contracts must be preserved;
- whether any consumer exists beyond `PersonController`;
- whether any code is dead or merely untested.

Treat published HTTP endpoints as active behavior until sufficient evidence proves otherwise.

Do not remove endpoints or functionality in this phase.

If the current naming does not accurately represent the capability, use use-case-oriented names while preserving the public HTTP contract.

## Application port

Replace `IElasticService` with a port whose semantics are oriented toward the application rather than the vendor.

The port must not expose:

- `ElasticsearchClient`;
- client descriptors or request builders;
- Elasticsearch query types;
- Elasticsearch-specific responses;
- `Elastic.Clients.Elasticsearch`;
- `Elastic.Transport`;
- logging sink types;
- Elasticsearch health-check types;
- vendor-specific configuration;
- index, transport, or serialization details that do not belong to the use case;
- types belonging to Infrastructure.

The port must:

- contain only the operations required by the current use cases;
- accept `CancellationToken` in every asynchronous operation;
- express results and failures in Application terms;
- avoid ambiguous null returns;
- distinguish, when necessary, missing items, external unavailability, invalid input, and unexpected errors;
- not create a dependency from Application on logging or health checks.

Evaluate whether a port specific to people, indexing, or search is more appropriate than a generic abstraction.

Do not create a broad generic abstraction “for future use” without a concrete need.

## Contracts and indexed documents

Analyze the current use of `Template.Domain.Person.Person` as a document sent directly to Elasticsearch.

Do not automatically turn a domain entity into an infrastructure contract.

When necessary, define an explicit document model in the adapter or an appropriate Application-owned contract.

Preserve the currently observable fields and external behavior.

Define and document:

- document format;
- identifier;
- index name;
- mapping between the domain/application model and the document;
- policy for missing or unknown fields;
- behavior for an incompatible document;
- compatibility with documents already indexed;
- serialization;
- handling of partial bulk results;
- current refresh policy;
- impact of any format change.

Do not perform an index migration in this phase.

If the new adapter cannot safely read existing documents, document the risk and implement the smallest reversible strategy possible without carrying out the migration planned for Phase 11.

## Infrastructure adapter

Implement the Elasticsearch adapter in Infrastructure.

The adapter must own:

- client construction and configuration;
- mapping between Application contracts and Elasticsearch documents;
- index creation or verification, if it remains part of the active requirement;
- indexing;
- queries;
- bulk operations;
- removal;
- handling of invalid responses;
- handling of transport failures;
- safe technical logging;
- `CancellationToken` propagation;
- appropriate client lifecycle.

Avoid creating a new `ElasticsearchClient` for each operation.

Use the recommended lifecycle for the currently adopted client, according to that version's official documentation.

Do not leave vendor code in:

- Domain;
- Application;
- controllers;
- application handlers;
- public HTTP contracts.

WebApi must only compose services and adapt results to the existing HTTP contract.

## Separation between search, logging, and health checks

The current configuration may share `ElasticSearchSettings` across three distinct responsibilities:

1. search/indexing;
2. Serilog log transport;
3. Elasticsearch health checks.

Separate these responsibilities explicitly.

Search/indexing configuration must be independent of logging sink configuration.

Logging configuration must be independent of the search port.

The health check may share only the physical endpoint when appropriate, but it must not depend on the search interface or build an intermediate service provider.

Remove patterns such as `services.BuildServiceProvider()` during DI registration.

Define typed and validated options for each responsibility when that improves clarity, without unnecessarily changing the external configuration keys already in use.

Preserve external configuration names when possible. If a configuration transition is necessary:

- maintain compatibility;
- document precedence;
- emit safe diagnostics;
- do not copy secrets;
- provide rollback.

Do not move logging infrastructure into Application.

Serilog and `Elastic.Serilog.Sinks` dependencies may remain in the composition/presentation layer or an appropriate infrastructure layer, provided they are isolated from Application and from the search capability.

## Availability policy

Define and document separate policies for:

- search/indexing;
- logging;
- health checks.

### Search and indexing

When Elasticsearch is unavailable:

- unrelated endpoints must continue working;
- the host must not fail during startup merely because search is unavailable, unless an already documented requirement states otherwise;
- operations that genuinely depend on search must return the corresponding legacy HTTP behavior;
- unavailability must be observable;
- do not turn unavailability into “item not found”;
- do not return incorrect data;
- do not indiscriminately hide programming errors;
- preserve `CancellationToken` cancellation.

Do not invent a PostgreSQL fallback when equivalent semantics do not exist and it has not been explicitly approved.

### Logging

When the Elasticsearch sink is unavailable:

- the application must continue starting;
- local/console logging must continue working;
- sink failures must not cause infinite logging recursion;
- unavailability must be diagnosable without exposing secrets;
- events must not contain tokens, credentials, connection strings, or personal payloads;
- do not claim a log delivery guarantee that the sink does not provide.

### Health check

When Elasticsearch is unavailable:

- the health check must indicate degradation or failure according to the documented policy;
- unrelated endpoints must continue responding;
- the response must not expose credentials or sensitive details;
- timeouts must be bounded;
- cancellation must be respected;
- the health check must not block shutdown.

Preserve the current health-check route and external format unless a change is demonstrated to be necessary and is authorized.

## Startup and fail-soft behavior

Ensure that:

- the Test environment continues to start without a real Elasticsearch instance;
- functional tests remain deterministic;
- real adapters can be replaced by fake/no-op implementations in tests;
- the adapter constructor does not make network calls;
- startup does not depend on successful index creation;
- initial unavailability does not prevent unrelated endpoints from starting;
- logging remains available through the console when the remote sink fails;
- reconnection attempts are bounded and do not block indefinitely;
- shutdown respects cancellation and finishes without depending on Elasticsearch;
- operational failures are distinguished from cancellation and code defects.

Do not hide blocking synchronous calls inside asynchronous methods.

## Configuration and security

Move `ElasticSearchSettings` to the appropriate layer or replace it with separate, responsibility-specific options.

Do not keep the following in Application:

- Elasticsearch URL;
- credentials;
- physical index name;
- data-stream configuration;
- logging namespace;
- sink options;
- health-check details;
- client-specific settings.

Do not copy real credential values into:

- documentation;
- tests;
- logs;
- code;
- version-controlled files.

Use environment variables, user secrets, or external configuration.

Never log:

- passwords;
- API keys;
- connection strings;
- tokens;
- authentication headers;
- complete payloads;
- personal data;
- the full content of indexed documents.

## Version compatibility

The initially recorded versions include:

- `Elastic.Clients.Elasticsearch` 8.17.1;
- `Elastic.Serilog.Sinks` 8.12.3;
- `AspNetCore.HealthChecks.Elasticsearch` 9.0.0.

Phase 10 is not an upgrade phase.

Do not change the major version of:

- the Elasticsearch client;
- the Elasticsearch server;
- Kibana;
- the Serilog sink;
- the health check;
- related images.

Do not perform the Elastic 9 migration. It belongs to Phase 11.

If a version change is technically indispensable to complete the architectural separation:

1. stop and confirm that it genuinely belongs to Phase 10;
2. consult only current official sources;
3. use the `package-upgrade` skill;
4. explain why the current version blocks the refactoring;
5. preserve the 8.x major when applicable;
6. document the previous version, selected version, sources, risks, and rollback;
7. do not expand the change to other stack components.

Do not update versions merely for convenience.

## HTTP compatibility

Phase 10 must not change the public HTTP contract.

In particular, preserve the v1 `PersonController` endpoints:

- routes;
- methods;
- route, query, and body parameters;
- status codes;
- response text;
- payload formats;
- missing-item behavior;
- failure behavior;
- v1 deprecation;
- existing authentication and authorization;
- published OpenAPI.

Internal methods and ports may change, but the observable HTTP behavior must remain compatible.

Capture OpenAPI again and compare it with:

`docs/refactoring/baseline-phase-6/`

The expected result is exact equality.

If any difference appears, investigate it. Do not silently update the baseline.

## Required tests

Add or update tests proportionally to the implemented design.

### Unit tests

Cover at least:

1. The controller or use case calls the correct port to create/verify an index.
2. Person indexing calls the port with the correct data.
3. Updating preserves the existing semantics.
4. Lookup by key distinguishes a missing item from unavailability.
5. Retrieving all documents preserves the existing behavior.
6. Removal calls the correct port.
7. `CancellationToken` is propagated through every operation.
8. Expected port failures are converted to the correct legacy HTTP result.
9. Cancellation is not converted into unavailability or not found.
10. Mapping between the application model and the indexed document is correct.
11. Partial bulk results are not treated as complete success.
12. No unit test depends on a real Elasticsearch instance.

### Architecture tests

Prove, using positive tests and controlled mutations when appropriate, that:

- Domain does not reference Elasticsearch, Elastic, Serilog, or health checks;
- Application does not reference `Elastic.Clients.Elasticsearch`;
- Application does not reference `Elastic.Transport`;
- Application does not reference `Elastic.Serilog.Sinks`;
- Application does not reference Elasticsearch health-check types;
- Application does not construct Elasticsearch clients;
- Application contains no vendor-specific settings;
- Application contains no concrete adapter implementation;
- Infrastructure implements Application-owned ports;
- logging configuration does not depend on the search port;
- WebApi remains the composition root;
- the Phase 8, 9, and 10 exceptions do not return;
- no new generic or broad exception has been introduced.

Remove the temporary Phase 10 exceptions from:

`Tests/Template.Architecture.Tests/ArchitectureRulesTests.cs`

only after the implementation is genuinely clean and the tests prove it.

### Adapter tests

Cover at least:

1. Idempotent index creation/verification.
2. Indexing a new document.
3. Updating an existing document.
4. Lookup by identifier.
5. Retrieving all documents according to the existing semantics.
6. Removing a document.
7. Successful bulk operation.
8. Partial bulk failure.
9. Missing document.
10. Incompatible payload/document.
11. Invalid Elasticsearch response.
12. Server unavailability.
13. Timeout.
14. Cancellation.
15. Recovery after service restoration.
16. Compatibility with the current document format.
17. Use of the configured index.
18. No leakage of sensitive data in logs.

Use deterministic test doubles when testing only application mapping and policy.

### Tests with real Elasticsearch

Create or use a separate, opt-in integration profile backed by an isolated and disposable Elasticsearch instance.

Tests with the real service must:

- use an explicit image;
- remain on the 8.x line;
- use a version compatible with the current client;
- not use a mutable tag;
- use test-exclusive indices;
- not reuse user indices or volumes;
- clean up only indices created by the tests themselves;
- have bounded timeouts;
- produce useful diagnostics;
- not depend on Kibana.

Cover at least:

- container startup;
- service health;
- index creation;
- indexing;
- updating;
- querying;
- bulk operations;
- removal;
- unavailability;
- recovery;
- document compatibility.

Do not change the main environment image merely to run tests.

Do not report these tests as passing unless they were actually executed.

### Logging tests

Validate without relying on manual inspection:

- console logging when Elasticsearch is unavailable;
- application startup without the remote sink;
- data-stream configuration when the service is available;
- no dependency between logging and the search port;
- no secrets in events;
- sink failure does not take down unrelated endpoints.

If a real sink test is excessively fragile, use the smallest deterministic test possible and clearly document what remains for the opt-in profile.

### Health-check tests

Cover:

- Elasticsearch available;
- Elasticsearch unavailable;
- timeout;
- cancellation;
- response without sensitive details;
- unrelated endpoints remaining available;
- Test environment without a real Elasticsearch dependency.

### Functional tests

Keep functional tests independent of real Elasticsearch by using a fake or no-op adapter.

Cover the existing v1 endpoints and prove that they:

- continue to start in the Test environment;
- preserve methods, routes, parameters, status codes, and responses;
- call the expected port;
- propagate cancellation;
- distinguish not found from service failure;
- make no external calls;
- allow unrelated endpoints to continue working without Elasticsearch;
- keep the documented health-check contract.

## Feature flag and rollback

The Phase 10 plan requires a safe rollback strategy during validation.

Implement the smallest reversible strategy that makes it possible to:

- disable real search/indexing through configuration;
- start the application without Elasticsearch;
- select a fake/no-op implementation in the Test environment;
- disable the Elasticsearch sink without losing console logging;
- revert the adapter without changing Application;
- temporarily restore the previous behavior if an incompatibility is found.

Do not permanently retain two active implementations writing to the same index.

If the previous adapter is retained temporarily:

- place it behind explicit configuration;
- do not run both adapters in parallel;
- do not duplicate indexing;
- remove it only after validation;
- document the rollback.

In the phase's final state, do not leave dead code or an architecture exception solely to support rollback. Rollback may be documented as a coherent reversion of the change set.

## Scope boundaries

Do not perform work from subsequent phases:

- do not migrate the client to Elasticsearch 9;
- do not upgrade the Elasticsearch server to 9;
- do not upgrade Kibana;
- do not migrate indices;
- do not change index templates or policies beyond what is necessary to preserve current behavior;
- do not reindex real data;
- do not alter existing volumes;
- do not redesign the entire observability strategy;
- do not introduce OpenTelemetry as part of this phase;
- do not change RabbitMQ;
- do not change Redis;
- do not modify HTTP contracts without a demonstrated need;
- do not remove v1 endpoints;
- do not create EF migrations without a real schema delta;
- do not manually edit migration designer or snapshot files;
- do not perform unrelated cosmetic modernization;
- do not perform work from Phases 11+.

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
- Elasticsearch adapter-specific tests;
- logging tests;
- health-check tests;
- opt-in tests with real Elasticsearch;
- direct and transitive NuGet vulnerability audit;
- `docker compose config -q`;
- OpenAPI capture and comparison;
- `git diff --check`.

Because this involves structural changes, regenerate `graphify-out` exclusively through the official Graphify command.

Do not edit `graphify-out` manually.

If Docker, Elasticsearch, or another external service is unavailable, do not report the corresponding gate as passed. Record precisely:

- the command attempted;
- the result;
- the observed cause;
- which criteria remain unverified.

Do not silently reduce coverage to obtain a green gate.

## Acceptance criteria

Phase 10 may only be considered complete when:

- Application does not reference Elastic packages;
- Application does not construct or use `ElasticsearchClient`;
- Application contains no Elasticsearch-specific settings;
- Application contains only use-case-oriented ports;
- the Elasticsearch adapter resides in Infrastructure;
- the client remains on the 8.x line;
- search/indexing is separated from logging;
- search configuration is separated from Serilog sink configuration;
- the health check does not depend on the search port;
- no `BuildServiceProvider()` is used during registration;
- WebApi remains the composition root;
- Test starts without a real Elasticsearch instance;
- WebApi starts when Elasticsearch is unavailable;
- unrelated endpoints continue working without Elasticsearch;
- console logging continues working without the remote sink;
- the health check degrades without taking down unrelated endpoints;
- the current adapter passes tests against the current version;
- search/indexing behavior remains compatible when Elasticsearch is available;
- the HTTP contract and OpenAPI remain unchanged;
- the Phase 10 architecture exceptions have been removed;
- all suites unrelated to Elasticsearch remain green;
- no Phase 11+ work was started.

## Documentation and delivery

Create:

`docs/refactoring/phase-10-report.md`

The report must include:

- a summary of the adopted design;
- an inventory of the previous Elasticsearch integration;
- the decision about the active search/indexing requirement;
- the port created in Application;
- contracts or models introduced;
- the adapter created in Infrastructure;
- configuration and DI registrations;
- client lifecycle;
- index names and preserved behavior;
- document mapping;
- failure policy;
- behavior when Elasticsearch is unavailable;
- separation between search, logging, and health checks;
- Serilog sink behavior;
- health-check behavior;
- fake/no-op strategy in the Test environment;
- tests added;
- commands executed and actual results;
- final test counts;
- results of tests with real Elasticsearch;
- OpenAPI comparison result;
- vulnerability-audit result;
- packages moved, added, or removed;
- confirmation that major versions were not upgraded;
- residual risks;
- rollback procedure;
- gates not executed;
- explicit confirmation that no Phase 11+ work was implemented.

Update the root `AGENTS.md` only with results that were actually executed and confirmed.

Upon completion, present:

1. An objective implementation summary.
2. The main files changed.
3. The port, adapter, and contracts created.
4. The decision about the active functionality.
5. The separation between search, logging, and health checks.
6. The unavailability policy.
7. Tests and validations with actual results.
8. Pending work or gates not executed.
9. Risks and rollback.
10. Confirmation that the HTTP contract was preserved.
11. Confirmation that the Phase 10 architecture exceptions were removed.
12. Confirmation that no major version in the Elastic stack was upgraded.
13. Confirmation that Phases 11+ were not started.

Do not commit, push, merge, change branches, or open a pull request without explicit authorization.
