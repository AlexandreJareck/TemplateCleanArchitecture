# Phase 13 Execution Handoff

Read and fully execute Phase 13 of this repository's refactoring plan.

Main objective:
Standardize observability, health checks, exception handling, correlation, sensitive-data redaction, and localization so that cross-cutting behavior is consistent, safe, testable, and compatible with the public HTTP contract.

Do not merely analyze or propose a plan: implement, test, validate, and document Phase 13 until all acceptance criteria are met.

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
- `docs/refactoring/phase-10-report.md`
- `docs/refactoring/phase-11-report.md`
- `docs/refactoring/phase-12-report.md`
- Any folder-specific `AGENTS.md` files governing files you modify
- The applicable repository skills, especially:
  - `architecture-boundary-check`
  - `dotnet-change-verification`
  - `api-contract-change` for health routes, Problem Details, headers, and other observable HTTP behavior
  - `integration-adapter` only if an external-system health adapter or localization adapter is changed
  - `package-upgrade` only if a package version changes

Treat the following as the approved API baseline:

- `docs/refactoring/baseline-phase-6/`

## Prerequisites

Before starting, verify from the repository's actual state that Phases 11 and 12 are genuinely complete and that the accumulated work from Phases 0–12 exists in the current checkout.

Confirm at least that:

- the Phase 11 Elastic stack upgrade is complete or its report explicitly states the accepted final state;
- the Phase 12 Identity/JWT hardening is complete;
- `docs/refactoring/phase-11-report.md` and `docs/refactoring/phase-12-report.md` exist and contain actual validation results;
- Application remains free of Redis, RabbitMQ, Elastic, Identity implementation, Serilog, and health-check vendor dependencies;
- 401 and 403 behavior remains distinct;
- the approved Problem Details behavior remains in place;
- the latest architecture, unit, integration, and functional checkpoints are green.

If either prerequisite phase is missing or incomplete, do not mix its implementation with Phase 13. Objectively record the blocker and stop.

## Checkout and working tree

Work only in the checkout containing the authoritative accumulated state from Phases 0–12.

Do not use or create a worktree based on an older commit that lacks those changes. Do not selectively copy files from another checkout and do not reimplement previous phases.

The working tree may contain pre-existing changes belonging to the user. Preserve them in full.

Do not use:

- `git reset`;
- `git checkout --`;
- `git clean`;
- `git stash`;
- destructive rebases;
- any command that discards or overwrites existing changes.

Do not commit, push, merge, change branches, create another worktree, or open a pull request without explicit authorization.

## Scope of Phase 13

Implement the requirements defined under “Phase 13 — Standardize observability, health, and localization” in `docs/phase-3-implementation-plan.md`:

1. Define stable structured-log event names and properties and enforce sensitive-data redaction.
2. Separate liveness from readiness and categorize optional versus required dependencies.
3. Ensure the exception middleware returns stable Problem Details and logs each unexpected failure once.
4. Keep localization behind `ITranslator` or replace it only with a more focused Application-owned abstraction.
5. Remove redundant logging package references only after proving framework or transitive coverage.

Do not perform vendor server upgrades in this phase.

## Current-state inventory

Before editing, inventory the actual post-Phase-12 implementation, including:

- all logging package references;
- Serilog bootstrap and sinks;
- Microsoft logging abstractions used by each layer;
- log calls in Application, Infrastructure, WebApi, middleware, hosted services, and adapters;
- current event IDs, message templates, and property names;
- request logging and client-information enrichment;
- correlation or trace identifiers;
- sensitive or personal fields that may currently be logged;
- exception logging locations;
- Problem Details creation and serialization;
- authentication and authorization failure handling;
- current health-check registrations, routes, tags, response writers, statuses, and timeouts;
- required and optional external dependencies;
- startup behavior when each dependency is unavailable;
- `ITranslator`, `LocalizationSettings`, resources, culture selection, and validation localization;
- uses of `BuildServiceProvider()` during registration;
- existing observability, health, localization, and HTTP contract tests.

Record this inventory in the final report without copying secrets, tokens, credentials, connection strings, personal payloads, or full log events containing sensitive data.

Treat the actual repository state as authoritative. Do not assume that filenames or implementations from earlier checkpoints remain unchanged.

## Structured logging standard

Define and document a small, stable logging vocabulary appropriate to the existing system.

For important operational events, define:

- a stable event name or `EventId`;
- a structured message template;
- stable property names;
- severity level;
- owning component;
- whether an exception should be attached;
- redaction requirements.

Cover at least:

- request completion or the existing equivalent;
- unhandled exception;
- authentication/authorization failure when logging is appropriate;
- cache unavailable/timeout;
- messaging connection, publish, consume, retry, dead-letter, and shutdown events;
- search unavailable/timeout;
- Elastic logging sink failure diagnostics;
- health dependency failure;
- application startup and shutdown;
- localization configuration failure.

Do not create a large speculative event catalog. Standardize events that already exist or are required to diagnose the current integrations.

Use structured placeholders rather than string interpolation for application logs.

Do not encode dynamic values into event names.

Avoid duplicate logs for the same failure as it crosses layers. Log where sufficient context exists and document ownership.

## Correlation and tracing

Define a single correlation policy for HTTP requests and background/integration processing.

Use the existing ASP.NET Core/Activity trace context where possible rather than inventing a competing tracing system.

Ensure that:

- every HTTP request has a stable trace/correlation identifier;
- the identifier appears in unexpected-error Problem Details through the existing `traceId` contract;
- structured request and exception logs contain the same identifier;
- downstream logs produced during the request inherit the correlation context;
- background message handling uses the existing message/correlation identifier when available;
- a new safe identifier is created when an inbound message has no usable correlation metadata;
- untrusted inbound correlation values are length-limited and validated before logging or returning them;
- correlation does not expose tokens, credentials, user identifiers, or personal payloads.

Do not introduce a distributed tracing backend or OpenTelemetry as unrelated work.

If an HTTP response correlation header is added or changed, treat it as a public contract change: apply the `api-contract-change` skill, document compatibility, and test it. Prefer preserving existing observable behavior unless the phase requirement cannot be met otherwise.

## Sensitive-data redaction

Define an explicit redaction policy and enforce it in code and tests.

Never log:

- passwords or password confirmations;
- JWTs, refresh tokens, API keys, or authorization headers;
- cookies or session identifiers;
- signing keys, security stamps, password hashes, or reset tokens;
- complete connection strings;
- RabbitMQ, Redis, PostgreSQL, or Elasticsearch credentials;
- personal request/response bodies;
- full `Person`, `Order`, account, or indexed-document payloads;
- arbitrary exception data that may contain secrets;
- sensitive query-string values.

When operationally useful, log safe categories and bounded identifiers rather than payloads.

Review framework and sink enrichers as well as explicit application log calls. Do not assume structured logging is safe merely because values are stored as properties.

Do not implement redaction by mutating domain or application data.

Add deterministic tests that fail if representative secret markers appear in captured logs.

## Exception handling and log-once behavior

Review every path that can map an exception to an HTTP response.

Ensure that:

- expected validation and business failures continue through the established result model;
- validation failures produce the approved 400 `ValidationProblemDetails` response;
- not-found failures produce the approved 404 behavior;
- authentication and authorization remain 401 and 403 respectively;
- unexpected exceptions produce the approved sanitized 500 `ProblemDetails` response;
- internal exception messages and stack traces never appear in 500 responses;
- `OperationCanceledException` associated with `HttpContext.RequestAborted` is not misreported as an internal server error;
- responses that have already started are handled safely;
- the same unexpected exception is logged once, at the boundary with sufficient context;
- lower layers do not repeatedly log and rethrow the same exception unless they add unique operational value;
- exception logs include safe request method, path, and correlation context;
- exception middleware does not leak query strings or sensitive headers.

Preserve the `ApiResultMapper` as the single HTTP mapping boundary where that remains the established design.

Do not alter the Problem Details schema, status mapping, content type, or v1 legacy behavior without explicit evidence and contract handling.

## Health model

Separate liveness from readiness and document the meaning of each endpoint.

Use health-check tags or another framework-supported mechanism to ensure each endpoint executes only the intended checks.

### Liveness

Liveness must answer whether the process is running and able to execute its basic pipeline.

It must not fail merely because Redis, RabbitMQ, Elasticsearch, PostgreSQL, or another remote dependency is temporarily unavailable.

Avoid network calls in liveness checks.

### Readiness

Readiness must answer whether the application is ready to serve the capabilities classified as required.

Inventory and explicitly categorize every dependency as:

- required for core readiness;
- optional/degraded;
- feature-disabled;
- test-only or development-only.

At minimum, assess:

- application PostgreSQL database;
- Identity PostgreSQL database;
- Redis cache;
- RabbitMQ messaging;
- Elasticsearch person index;
- Elasticsearch logging sink;
- localization resources.

Do not assume every configured integration belongs in readiness. Base the classification on actual application behavior and the fail-soft policies established in Phases 8–12.

An optional dependency failure must remain visible without necessarily making the process unready. Document how that degraded state is exposed.

Use bounded timeouts and cancellation for every remote check.

Health checks must not log the same unchanged outage on every probe at an unbounded rate.

## Health endpoint compatibility

Preserve the existing `/health` behavior during a documented transition unless an approved contract decision says otherwise.

If new endpoints are introduced, prefer explicit names such as:

- `/health/live`;
- `/health/ready`.

The exact route policy must be derived from the current implementation and documented before changing it.

For each endpoint, define and test:

- included checks;
- HTTP status mapping;
- response media type and shape;
- whether degraded status returns 200 or 503;
- caching headers if relevant;
- anonymous/authenticated access behavior;
- redaction of dependency details;
- timeout behavior.

Do not expose hostnames containing credentials, connection strings, exception stacks, raw response bodies, or secrets in health output.

Treat route, status, or response-shape changes as API contract changes and apply the `api-contract-change` skill.

## Dependency-specific health behavior

Add or update tests for each dependency state that is registered in health checks.

Cover, as applicable:

- healthy;
- unavailable;
- timeout;
- canceled probe;
- disabled feature;
- invalid configuration;
- authentication failure without secret leakage;
- recovery after outage.

Normal functional tests must remain deterministic and must not require real Redis, RabbitMQ, Elasticsearch, or PostgreSQL beyond fixtures that explicitly provision them.

Real-service health tests belong in the existing opt-in integration profiles where appropriate.

Do not introduce startup network calls merely to populate health status.

## Localization boundary

Keep localization behind the Application-owned `ITranslator` abstraction unless a demonstrably narrower abstraction improves the existing use cases.

Do not leak:

- `IStringLocalizer`;
- resource-manager implementation types;
- ASP.NET Core localization types;
- request-culture provider types;
- `.resx` implementation details

into Domain or Application business contracts.

Keep resource lookup and concrete localization implementation in `Template.Infrastructure.Resources` or the appropriate infrastructure boundary.

Preserve existing translation keys and resource behavior.

Do not manually edit generated `.resx` designer files.

If `ITranslator` is replaced, migrate all consumers coherently and justify why the replacement is more focused. Do not create parallel abstractions that duplicate the same responsibility.

## Localization registration and options

Remove the pre-existing `BuildServiceProvider()` call from `LocalizationExtensions`.

Bind and validate `LocalizationSettings` through the normal options pipeline without constructing an intermediate container.

Validate at least:

- default culture is present;
- supported cultures are present and non-empty;
- culture names are valid;
- the default culture belongs to the supported set;
- duplicate culture names are handled deterministically;
- invalid settings fail startup with safe diagnostics.

Preserve the external `LocalizationSettings` configuration section unless a change is necessary and documented.

Use DI and middleware ordering correctly so request culture is established before localized application behavior runs.

Review the use of `ValidatorOptions.Global.LanguageManager.Culture`. It is process-global and must not be mutated per request. Preserve deterministic validation localization without introducing cross-request culture races.

## Localization behavior and tests

Test at least:

- default culture when no supported culture is requested;
- each configured supported culture;
- unsupported culture fallback;
- `Accept-Language` selection according to the current provider policy;
- localized application validation messages;
- localized not-found/business messages;
- concurrent requests using different supported cultures;
- invalid localization configuration;
- missing resource-key behavior;
- culture behavior in background operations where no HTTP request culture exists.

Preserve current translations and HTTP response structures.

Do not turn localized user-facing messages into log event templates or use localized text as a stable machine-readable error code.

## Logging package review

Inventory direct and transitive logging packages after the implementation is stable.

For each direct logging package, determine whether it is:

- directly used;
- supplied by the shared framework;
- required by the Serilog integration;
- only transitively available;
- redundant.

Remove a package reference only after proving that:

- the required compile-time API remains available through a deliberate framework or package reference;
- runtime logging behavior remains intact;
- the package graph remains coherent;
- tests and logging smoke checks pass;
- rollback is documented.

Do not remove a direct reference merely because the current build happens to receive it transitively if the project intentionally compiles against that API.

Do not upgrade package versions in this phase unless required for correctness. If a version changes, use current official sources and the `package-upgrade` skill.

Run the direct and transitive vulnerability audit after package changes.

## HTTP and API compatibility

Phase 13 must preserve the approved public API behavior except for explicitly documented health-route additions or transitions.

Preserve:

- v1 and v2 routes;
- methods and bindings;
- status codes;
- v1 legacy envelopes;
- v2 Problem Details and ValidationProblemDetails;
- 401/403 distinction;
- `WWW-Authenticate: Bearer` behavior;
- response DTOs;
- API versioning and v1 deprecation;
- published OpenAPI.

Capture OpenAPI again and compare it with:

`docs/refactoring/baseline-phase-6/`

The expected result is exact equality unless an explicitly approved health endpoint is included in OpenAPI. Do not silently update the baseline.

If any other difference appears, investigate and correct it.

## Required tests

Add or update tests proportionally to the cross-cutting behavior.

### Logging and correlation tests

Cover at least:

1. Stable structured event name or ID for each standardized critical event.
2. Stable property names.
3. HTTP request logs contain the selected trace/correlation identifier.
4. Unexpected-exception logs contain the same identifier returned in Problem Details.
5. Nested services inherit correlation context.
6. Background message processing uses safe correlation metadata.
7. Invalid or oversized inbound correlation values are rejected or replaced safely.
8. Representative passwords, JWTs, API keys, authorization headers, cookies, connection strings, security stamps, and personal payload markers do not appear in captured logs.
9. Elasticsearch sink failure diagnostics remain bounded and non-recursive.
10. Console logging remains available when optional sinks fail.

### Exception and Problem Details tests

Cover at least:

1. Validation failure maps to the approved 400 response.
2. Unauthenticated request maps to 401.
3. Forbidden request maps to 403.
4. Missing resource maps to 404.
5. Conflict maps to 409 where already supported.
6. Unexpected exception maps to sanitized 500.
7. Internal exception messages and stack traces are absent.
8. Request cancellation is not returned or logged as an ordinary 500.
9. An unexpected exception is logged exactly once.
10. The response content type remains `application/problem+json` where established.

### Health tests

Cover at least:

1. Liveness succeeds with all remote dependencies unavailable.
2. Readiness succeeds when all required dependencies are healthy.
3. Readiness fails when each required dependency is unavailable.
4. Each optional dependency reports the documented degraded state without incorrectly changing liveness.
5. Disabled integrations are excluded or reported according to policy.
6. Probe timeouts are bounded.
7. Cancellation is respected.
8. Responses contain no secrets or internal exception details.
9. Legacy `/health` behavior is preserved or follows the documented transition.
10. Recovery is reflected after a dependency returns.

### Localization tests

Cover at least:

1. Valid options register without `BuildServiceProvider()`.
2. Invalid default culture fails safely.
3. Invalid supported culture fails safely.
4. Default culture must be supported.
5. Default-culture response messages are preserved.
6. Each supported culture returns the expected localized validation and business messages.
7. Unsupported culture follows the documented fallback.
8. Concurrent requests with different cultures do not leak culture between requests.
9. Missing keys follow the documented fallback behavior.
10. Application depends only on the localization port.

### Architecture tests

Prove, using positive tests and controlled mutations when appropriate, that:

- Domain remains free of logging, health, HTTP, and localization-framework dependencies;
- Application remains free of Serilog, health-check, ASP.NET localization, and resource-manager implementation packages;
- logging and health checks do not depend on search, caching, or messaging ports unless explicitly required by a health adapter;
- WebApi remains the HTTP and composition boundary;
- concrete localization remains in Infrastructure.Resources;
- no production registration calls `BuildServiceProvider()`;
- the Phase 13 `LocalizationExtensions` exception is removed;
- no broad replacement exception is introduced;
- exceptions removed in earlier phases do not return.

## Rollback and compatibility

Health semantics can affect orchestrators and logging changes can break dashboards or alerts. Keep the phase reversible.

Document how to revert coherently:

- event names and property names;
- correlation enrichment;
- redaction configuration;
- exception middleware changes;
- health endpoint routing and predicates;
- required/optional dependency classification;
- localization options registration;
- logging package removals.

If event or property names used by dashboards change, document a transition or compatibility mapping. Do not silently break existing queries.

Keep the legacy `/health` behavior available during a documented transition when changing its semantics would affect external orchestration.

Do not delete dashboards, alerts, saved objects, indices, logs, or health-monitor configuration.

## Scope boundaries

Do not perform work from subsequent phases:

- do not implement the Phase 14 Compose baseline;
- do not pin or upgrade unrelated container images;
- do not perform Redis, PostgreSQL, RabbitMQ, Elasticsearch, Kibana, or Redis Insight server upgrades;
- do not redesign the complete observability platform;
- do not introduce OpenTelemetry, a tracing backend, metrics backend, or APM vendor as unrelated work;
- do not replace Serilog wholesale;
- do not change the authentication scheme;
- do not add an external identity provider;
- do not remove v1;
- do not create EF migrations without a real schema delta;
- do not manually edit EF migration designer/snapshot files or generated `.resx` designers;
- do not perform unrelated cosmetic modernization;
- do not perform work from Phases 14+.

## Validation

Run build and test commands sequentially because parallel builds compete for shared `obj` directories.

Use the pinned SDK:

`C:\Users\alexa\.dotnet\dotnet.exe`

Start with the smallest relevant logging, middleware, health, localization, architecture, and functional tests.

At the end, run the repository's complete validation gate:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-phase-validation.ps1
```

Confirm at least:

- restore with parallel workers disabled where required by the repository;
- WebApi build without warnings;
- architecture tests;
- unit tests;
- EF InMemory integration tests;
- real PostgreSQL integration tests;
- RabbitMQ integration tests;
- Elasticsearch integration tests against the post-Phase-11 target;
- functional tests;
- logging and redaction tests;
- correlation tests;
- exception/log-once tests;
- liveness and readiness tests;
- dependency-state health tests;
- localization and concurrent-culture tests;
- direct and transitive NuGet vulnerability audit;
- `docker compose config -q`;
- OpenAPI capture and exact comparison;
- `git diff --check`.

Because this phase changes cross-cutting structure, regenerate `graphify-out` exclusively through the official Graphify command.

Do not edit `graphify-out` manually.

If Docker or another external dependency is unavailable, do not report the corresponding gate as passed. Record precisely:

- the command attempted;
- the result;
- the observed cause;
- which acceptance criteria remain unverified.

Do not silently reduce coverage or weaken safety assertions to obtain a green gate.

## Acceptance criteria

Phase 13 may only be considered complete when:

- stable structured-log event names/IDs and property names are documented and tested;
- logs contain correlation context;
- Problem Details and unexpected-error logs use the same trace identifier;
- representative credentials, tokens, security stamps, connection strings, and personal payloads are absent from logs;
- unexpected exceptions are logged exactly once;
- expected business failures are not misreported as unexpected exceptions;
- cancellation is not misreported as a normal 500;
- liveness and readiness have explicit, tested semantics;
- every registered dependency is classified as required, optional, disabled, or environment-specific;
- health checks cover healthy, unavailable, timeout, and recovery states as applicable;
- health output contains no sensitive details;
- legacy health behavior is preserved or transitioned compatibly;
- localization remains behind an Application-owned port;
- localization options are validated without constructing an intermediate service provider;
- no production code calls `BuildServiceProvider()` during registration;
- the Phase 13 architecture exception is removed;
- localized validation and business-message tests pass;
- concurrent cultures do not leak across requests;
- redundant logging packages are removed only when proven safe;
- the package vulnerability audit is clean;
- HTTP endpoint contracts and OpenAPI remain green;
- all unrelated suites remain green;
- no Phase 14+ work was started.

## Documentation and delivery

Create:

`docs/refactoring/phase-13-report.md`

The report must include:

- an executive summary;
- proof that prerequisite phases were complete;
- the pre-change observability, health, and localization inventory;
- the structured event-name/ID and property convention;
- correlation policy;
- redaction policy;
- log ownership and log-once policy;
- exception and Problem Details behavior;
- liveness and readiness definitions;
- dependency classification table;
- health routes, predicates, statuses, and compatibility decision;
- localization boundary and options-validation decisions;
- removal of `BuildServiceProvider()` and the architecture exception;
- logging package review and any removals;
- tests added or updated;
- commands executed and actual results;
- final test counts;
- OpenAPI comparison result;
- vulnerability-audit result;
- residual risks;
- executable rollback procedure;
- gates not executed;
- explicit confirmation that no vendor server upgrade occurred;
- explicit confirmation that no Phase 14+ work was implemented.

Update the root `AGENTS.md` only with commands and results that were actually executed and confirmed.

Upon completion, present:

1. An objective implementation summary.
2. The main files and packages changed.
3. Structured logging, correlation, and redaction decisions.
4. Exception/log-once and Problem Details results.
5. Liveness/readiness routes and dependency classification.
6. Localization changes and culture-test results.
7. Full validation results with actual counts.
8. OpenAPI and vulnerability-audit results.
9. Pending work or gates not executed.
10. Residual risks and rollback.
11. Confirmation that the Phase 13 architecture exception was removed.
12. Confirmation that public HTTP compatibility was preserved.
13. Confirmation that no Phase 14+ work was started.

Do not commit, push, merge, change branches, create another worktree, alter production monitoring, delete logs, or open a pull request without explicit authorization.
