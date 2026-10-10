# Phase 3 — Incremental implementation plan

Status: refactoring plan only (decisions recorded at the end). A follow-up documentation correction, the missing PostgreSQL bootstrap script (`init-db.sh`), and its Compose environment wiring were implemented on 2026-10-10; no application code, package, skill, agent, or rule described by the refactoring phases was implemented.

The plan intentionally separates architecture moves from vendor upgrades. Each phase should be one reviewable pull request (or smaller), preserve a green baseline appropriate to its scope, and update the Graphify graph after structural changes.

## Global execution rules

- Re-query every external version from its official source on the day a phase starts. The Phase 2 table is a 2026-10-08 snapshot, not permission to upgrade blindly later.
- Run .NET builds/tests sequentially because the projects share intermediate outputs.
- Keep API compatibility unless a phase explicitly introduces a documented deprecation.
- Do not mix schema migration, architecture movement, and dependency major upgrades in one phase.
- Before a risky phase, record the current package lock/restore graph, API contract, database backup procedure, and container image digests.
- Roll back by reverting the phase commit and restoring the prior explicit package/image versions. Database phases require the phase-specific data rollback below.

## Phase 0 — Freeze a reproducible baseline

**Objective and scope:** Make the current application/test baseline reproducible without changing behavior. In scope: stable .NET 10 SDK pin, package inventory, vulnerability inventory, OpenAPI snapshot, and documented test results. Out: package upgrades and code movement.

**Dependencies:** None.

**Steps:**

0. Start from a clean working tree: commit or stash pending changes first, and record the baseline commit. Line-ending-only noise (CRLF/LF) must be normalized (for example with `.gitattributes`) before baselining, not mixed into refactor commits.
1. Install/select a stable .NET 10 SDK and add `global.json` for the chosen supported SDK.
2. Capture direct/transitive package and vulnerability reports.
3. Capture the v1/v2 OpenAPI documents and representative HTTP responses.
4. Record the current Graphify graph and Git commit.
5. Apply decision 1: VSIX validation runs only for VSIX-scoped changes.

**Risks and rollback:** A different SDK can expose analyzers or build changes. Revert `global.json` and use the previously recorded SDK while resolving differences.

**Acceptance and validation:** WebApi build succeeds; unit 10/10 and integration 6/6 remain green; functional baseline is recorded as 17 failures caused by Elastic startup; compose config validates; no source behavior changes.

## Phase 1 — Repair test-host isolation

**Objective and scope:** Make functional tests self-contained and green. In scope: Test-environment replacement/disablement of Elastic log sink, Redis, RabbitMQ consumers, and other external startup effects. Out: production integration upgrades or endpoint refactors.

**Dependencies:** Phase 0.

**Steps:**

1. Inventory every service resolved or contacted while `WebApplicationFactory` builds.
2. Introduce explicit Test-environment registrations for in-memory/no-op adapters or fixture-owned containers.
3. Ensure Serilog/Elastic bootstrap failure cannot prevent test-host creation.
4. Make seed data deterministic and isolate test state.
5. Add a test proving the host starts when Elastic, RabbitMQ, and Redis are unavailable.

**Risks and rollback:** Test registrations may diverge from production. Keep production registrations untouched and revert the Test-only override if it masks behavior; add targeted adapter integration tests instead.

**Acceptance and validation:** WebApi build succeeds; unit 10/10, integration 6/6, and functional 17/17 pass without developer-hosted services; a negative startup test confirms external observability is optional.

## Phase 2 — Centralize versions and remove immediate vulnerable/obsolete edges

**Objective and scope:** Centralize package versions, apply supported .NET 10 patch-family updates, and remove clearly redundant legacy references. In: `Directory.Packages.props`, .NET/EF/ASP.NET 10.0.x family, Npgsql 10.0.x, test SDK within major, removal of unused SQL Server packages and legacy ASP.NET Hosting/Identity metapackages when proven unused. Out: major RabbitMQ/Elastic/coverage/VSIX upgrades.

**Dependencies:** Phases 0–1.

**Steps:**

1. Add central package management with current versions first and verify no behavior delta.
2. Update one coherent Microsoft/EF/Npgsql patch family.
3. Trace the parents of vulnerable `Microsoft.OpenApi` and `System.Security.Cryptography.Xml`; resolve through supported parent updates or documented supported overrides.
4. Remove SQL Server and legacy metapackages only after compile/reference analysis.
5. Add restore/audit output to CI artifacts.

**Risks and rollback:** Centralization can accidentally change transitive selection. Keep the pre-change assets/lock report and revert the central file as one unit.

**Acceptance and validation:** Restore has no high-severity known vulnerabilities, WebApi build succeeds, all green tests from Phase 1 pass, migrations still compile, and direct versions have one source of truth.

## Phase 3 — Establish automated architecture boundaries

**Objective and scope:** Add enforceable project dependency tests before moving code. In: architecture tests and documented allowed references. Out: moving existing violations.

**Dependencies:** Phase 2.

**Steps:**

1. Encode allowed project references and forbidden namespaces/packages.
2. Assert Domain has no Application/Infrastructure/Presentation/vendor dependencies.
3. Assert Application has no EF, ASP.NET, Redis, RabbitMQ, Elastic, or Serilog implementation dependencies as the target rule; temporarily whitelist known violations with explicit TODO identifiers.
4. Add tests for Presentation not directly using DbContexts/repositories.

**Risks and rollback:** Over-broad rules can block legitimate composition. Narrow rules to compile-time dependency direction; keep runtime DI in WebApi allowed.

**Acceptance and validation:** Architecture tests pass with only named temporary exceptions; a controlled test mutation proves each rule detects a forbidden dependency; existing build/tests remain green.

## Phase 4 — Clean the Domain model

**Objective and scope:** Make Domain framework-free and invariant-focused. In: move `ProductDto` to Application, introduce `decimal` money semantics, normalize entity construction/update rules, and align nullable annotations. Out: controller contract redesign and database provider upgrade.

**Dependencies:** Phase 3.

**Steps:**

1. Move DTOs out of Domain with temporary namespace adapters if needed.
2. Add domain tests for product invariants before changing the entity.
3. Change price from `double` to `decimal` through Domain and mappings; decide precision explicitly.
4. Add an additive EF migration only if the database column type/precision changes.
5. Remove `#nullable disable` from touched domain files.

**Risks and rollback:** Money-type/schema conversion can round values. Back up sample data, use an additive/explicit conversion migration, and retain a down migration or restore path.

**Acceptance and validation:** Domain architecture tests have no exceptions; domain/unit tests pass; migration SQL is reviewed against PostgreSQL; integration tests confirm round-trip precision; API snapshot is unchanged unless the decimal representation decision says otherwise.

## Phase 5 — Standardize application use cases and results

**Objective and scope:** Give commands/queries consistent contracts and error semantics. In: cancellation propagation, request/handler organization, result/error model, time abstraction, explicit validation pipeline. Out: HTTP formatting and vendor adapters.

**Dependencies:** Phase 4.

**Steps:**

1. Define result categories: validation, not found, conflict, unauthorized/forbidden, unexpected.
2. Introduce an application clock and remove direct observable `DateTime.UtcNow` use.
3. Pass `CancellationToken` through repositories/unit of work.
4. Choose one feature organization rule and split multiple public types per file where it improves navigation.
5. Add explicit FluentValidation execution or a tested MediatR validation behavior.

**Risks and rollback:** Result changes can alter status mapping later. Preserve adapters for old `BaseResult` until controllers migrate, then remove them in a later phase.

**Acceptance and validation:** Handler tests cover success and each expected failure; cancellation tests pass; no Application handler references ASP.NET types; build/unit/integration tests pass.

## Phase 6 — Standardize HTTP controllers and API contracts

**Objective and scope:** Make v2 controllers the consistent HTTP boundary and define the v1 deprecation path. In: constructor injection, route/body/query binding, Problem Details, status codes, API versioning package migration, OpenAPI. Out: removing v1 without approval.

**Dependencies:** Phases 1 and 5.

**Steps:**

1. Replace service-located `Mediator` with injected `ISender`/`IMediator`.
2. Migrate from legacy `Microsoft.AspNetCore.Mvc.Versioning.*` to `Asp.Versioning.*` 10.x after reviewing official migration notes.
3. Map application results to RFC Problem Details in one boundary component.
4. Standardize REST routes and correct create/delete status codes while preserving compatibility or versioning the change.
5. Generate and diff OpenAPI; document v1 lifecycle.

**Risks and rollback:** Versioning migration can change 400/404/405 behavior and routes. Retain contract snapshots and revert package/config/controller changes together.

**Acceptance and validation:** WebApi build and all test suites pass; OpenAPI diff contains only approved changes; functional tests cover v1/v2 routes, auth, validation, not-found, create, update, and delete responses.

## Phase 7 — Simplify persistence and PostgreSQL behavior

**Objective and scope:** Make persistence explicit and PostgreSQL-correct. In: explicit repository registration, repository contracts, transaction semantics, cancellation, query pagination, migrations. Out: PostgreSQL major server upgrade.

**Dependencies:** Phases 4–5.

**Steps:**

1. Replace reflection-based repository registration with explicit registrations.
2. Decide whether the generic repository adds value; prefer aggregate/use-case repositories where not.
3. Keep `IQueryable` internal and use deterministic ordering before pagination.
4. Define unit-of-work behavior and propagate cancellation.
5. Add PostgreSQL-backed integration coverage for provider-sensitive behavior.

**Risks and rollback:** Query shape/performance and transaction boundaries can change. Keep old implementations behind the same ports until parity tests pass, then delete them.

**Acceptance and validation:** Unit/integration tests pass; PostgreSQL tests verify migrations, CRUD, pagination/order, decimal precision, and concurrency/transaction behavior; no SQL Server package remains.

## Phase 8 — Move Redis caching behind an infrastructure adapter

**Objective and scope:** Remove Redis implementation concerns from Application. In: `IProductCache` or generic bounded cache port, adapter, serialization/versioning, cache policy, failure behavior. Out: Redis server major upgrade.

**Dependencies:** Phase 5.

**Steps:**

1. Define cache semantics in Application without `IDistributedCache` or StackExchangeRedis types.
2. Move Redis package/registration and serialization into Infrastructure.
3. Define key format, TTL, invalidation after writes, and cache-miss/failure behavior.
4. Add adapter tests and functional cache behavior tests.

**Risks and rollback:** Key/serialization changes can produce stale or unreadable values. Namespace new keys with a version and allow fallback to database; rollback by restoring the previous adapter/key prefix.

**Acceptance and validation:** Application has no Redis package; cache-disabled and Redis-unavailable paths succeed; integration tests verify hit/miss/expiry/invalidation; architecture and functional tests pass.

## Phase 9 — Move RabbitMQ behind messaging ports and upgrade client

**Objective and scope:** Isolate messaging and migrate RabbitMQ.Client 6.8.1 to the current supported 7.x. In: publisher port, hosted consumers in Infrastructure, async APIs, topology ownership, retry/idempotency, graceful shutdown. Out: broker server major upgrade.

**Dependencies:** Phases 3 and 5.

**Steps:**

1. Characterize existing direct/fanout/topic exchanges, queue names, payloads, and acknowledgements.
2. Define application event contracts and publisher/handler ports.
3. Move hosted services/vendor code to Infrastructure.
4. Migrate to async RabbitMQ 7 APIs and copy/process `ReadOnlyMemory<byte>` payloads before handler return.
5. Add broker-backed contract tests for publish, consume, retry, poison message, reconnect, and shutdown.

**Risks and rollback:** API changes, topology drift, duplicate delivery, and message loss. Version message contracts, declare compatible topology, make handlers idempotent, and retain a feature flag/old adapter until verification.

**Acceptance and validation:** Application has no RabbitMQ reference; tests prove topology and at-least-once behavior; startup/shutdown works with broker available/unavailable per policy; all non-broker suites pass.

## Phase 10 — Move Elastic concerns behind infrastructure boundaries

**Objective and scope:** Separate product search from logging and remove Elastic implementation dependencies from Application without changing the client or server major version. In: search port, infrastructure adapter, logging bootstrap isolation, and unavailable-service behavior. Out: Elastic 9 migration, index migration, and unrelated controller changes.

**Dependencies:** Phases 1, 3, and 5.

**Steps:**

1. Determine whether product search is an active requirement and define the smallest application-owned search port.
2. Move the Elastic client and implementation to Infrastructure while preserving current 8.x behavior.
3. Separate product search configuration from Serilog transport configuration.
4. Make logging/search startup fail-soft according to the documented availability policy.
5. Add adapter and functional tests for available and unavailable Elastic states.

**Risks and rollback:** Moving registrations can change startup order or silently disable logging/search. Retain the old adapter behind a temporary feature flag and compare generated queries, logs, and health responses before removing it.

**Acceptance and validation:** Application has no Elastic package; WebApi and functional tests start without Elastic; current-version adapter tests pass; logging/search behavior is unchanged when Elastic is available; health checks degrade without taking down unrelated endpoints.

## Phase 11 — Upgrade the Elastic client and server stack

**Objective and scope:** Upgrade the already-isolated Elastic integration from 8.x to a verified compatible 9.x client, sink, Elasticsearch, and Kibana set. In: API migration, templates/index policy, copied-data rehearsal, health checks, and explicit image versions. Out: application boundary changes and unrelated observability standards.

**Dependencies:** Phases 2 and 10.

**Steps:**

1. Re-query compatible client, sink, Elasticsearch, and Kibana releases from official sources.
2. Snapshot existing indices and run Elastic deprecation/upgrade checks.
3. Migrate the infrastructure adapter using the official .NET client breaking-change guide.
4. Rehearse the server upgrade or reindex path against copied data in a parallel environment.
5. Pin compatible Elasticsearch/Kibana images and validate component/index templates.

**Risks and rollback:** Client API changes, template incompatibility, and unreadable indices. Preserve snapshots, the 8.x adapter branch, images, and configuration; roll traffic back to the old cluster instead of attaching an old binary to upgraded data.

**Acceptance and validation:** Adapter contract tests pass against the target Elastic version; search and logging smoke tests pass; templates install cleanly; copied data remains queryable; unavailable Elastic still does not prevent WebApi startup.

## Phase 12 — Harden identity and JWT boundaries

**Objective and scope:** Standardize account use cases, identity adapter, token validation, and secret handling. In: removal of legacy Identity metapackage, options validation, claims/current-user port, auth functional tests. Out: changing the authentication scheme or adding an external identity provider.

**Dependencies:** Phases 2, 5, and 6.

**Steps:**

1. Keep account orchestration/contracts in Application; keep `UserManager`, `SignInManager`, Identity models/context in Infrastructure.Identity.
2. Validate JWT/Identity options at startup without logging values.
3. Review issuer, audience, lifetime, security stamp, 401/403 Problem Details, and HTTPS metadata policy.
4. Remove redundant 2.3.x Identity package.
5. Add token expiry, invalid signature, stale security stamp, forbidden role, and password-change tests.

**Risks and rollback:** Authentication regressions can lock users out. Preserve signing-key/config compatibility, stage claim changes, and keep previous deployment/config ready.

**Acceptance and validation:** Auth/account functional tests pass; 401 and 403 are distinct; no secrets appear in source/logs; package audit is clean; migrations are unchanged or separately reviewed.

## Phase 13 — Standardize observability, health, and localization

**Objective and scope:** Make cross-cutting behavior consistent and safe. In: structured logging, correlation, redaction, health readiness/liveness, exception mapping, localization port. Out: vendor server upgrades.

**Dependencies:** Phases 6 and 8–12.

**Steps:**

1. Define log event names/properties and sensitive-data redaction.
2. Separate liveness from readiness and categorize optional vs required dependencies.
3. Ensure exception middleware returns stable Problem Details and logs once.
4. Keep localization behind `ITranslator` or replace it with a more focused application abstraction.
5. Remove redundant logging package references after confirming transitive/framework coverage.

**Risks and rollback:** Health semantics can affect orchestration; logging changes can break dashboards. Version dashboards/alerts and keep old health endpoint behavior during a transition.

**Acceptance and validation:** Health tests cover each dependency state; logs contain correlation but no credentials/tokens/personal payloads; localized validation tests pass; endpoint contract tests stay green.

## Phase 14 — Establish a reproducible Compose baseline

**Objective and scope:** Make the current-version local environment reproducible before any service major upgrade. In: validate the existing idempotent `init-db.sh`, pin current compatible patch tags/digests, add missing health checks, and document clean-volume startup. Out: Redis, PostgreSQL, RabbitMQ, and Elastic major upgrades.

**Dependencies:** Phases 1 and 7; adapter phases are not required because service behavior must remain unchanged.

**Steps:**

1. Validate `init-db.sh` against a clean PostgreSQL volume and confirm both configured databases are created without embedded credentials.
2. Pin each existing service to its current compatible patch or digest; remove mutable tags without changing majors.
3. Add health checks and dependency conditions needed for deterministic startup.
4. Run migrations/seeds twice to prove idempotency.
5. Document startup, smoke-test, shutdown, and non-destructive cleanup commands after executing them.

**Risks and rollback:** Pinning may expose an implicit image change or startup-order dependency. Preserve current resolved image digests and Compose configuration, and revert only the baseline commit without touching volumes.

**Acceptance and validation:** Compose config validates; all current-major services become healthy from a clean environment; both databases exist; WebApi smoke tests pass; restart preserves expected data; no mutable tag remains.

## Phase 15 — Consolidate Redis services on Redis 8

**Objective and scope:** Replace floating Redis/module containers with one supported Redis 8 deployment and explicit data migration. In: Redis, Search, TimeSeries, ACL/command compatibility, and RedisGraph disposition. Out: PostgreSQL, RabbitMQ, and Redis Insight upgrades.

**Dependencies:** Phases 8, 13, and 14; explicit user decision on RedisGraph.

**Steps:**

1. Inventory Redis keys, module commands, persistence settings, and any external RedisGraph use.
2. Remove RedisGraph if approved; otherwise select and plan a supported graph database separately.
3. Provision Redis 8 with integrated Search and Time Series using a new volume.
4. Migrate copied data and validate commands, ACL categories, expiry, serialization, and cache invalidation.
5. Switch the adapter only after parity tests pass, retaining the old volume for rollback.

**Risks and rollback:** Module command or persistence incompatibility and lost cache/state. Never attach both versions to one volume; retain the old containers and volume and switch configuration back if validation fails.

**Acceptance and validation:** Redis adapter/contract tests and WebApi smoke tests pass; required Search/TimeSeries behavior is demonstrated; restart retains expected persistent data; removed services have no remaining Compose dependency.

## Phase 16 — Upgrade PostgreSQL independently

**Objective and scope:** Patch PostgreSQL 16 first, then optionally migrate to PostgreSQL 18 as a separate data operation. In: database backup, restore rehearsal, extension/collation checks, EF migrations, and both application databases. Out: changes to persistence architecture or other services.

**Dependencies:** Phases 7 and 14; explicit user decision on PostgreSQL 18.

**Steps:**

1. Back up and restore both databases into an isolated rehearsal environment.
2. Pin and validate the latest supported PostgreSQL 16 patch without changing data format.
3. If PostgreSQL 18 is approved, choose `pg_upgrade` or logical backup/restore and rehearse it on copies.
4. Run EF migrations, repository integration tests, seed idempotency, and representative query plans.
5. Record the exact rollback point before switching the application connection.

**Risks and rollback:** Data-format, collation, extension, or query-plan regressions. Keep verified backups and the PostgreSQL 16 volume untouched; restore/switch back rather than mounting upgraded data with an older server.

**Acceptance and validation:** Both contexts connect; migrations and seeds are idempotent; repository and functional tests pass; backup restore is proven; restart preserves data; the rollback procedure is timed and documented.

## Phase 17 — Upgrade RabbitMQ server independently

**Objective and scope:** Upgrade and pin the RabbitMQ management image after the client adapter is stable. In: supported 4.x broker, plugins, feature flags, topology, persistence, and rolling/backup policy. Out: message contract redesign and other containers.

**Dependencies:** Phases 9, 13, and 14.

**Steps:**

1. Re-query the supported broker upgrade path and feature-flag requirements.
2. Export definitions and record queues/exchanges/bindings plus persistence expectations.
3. Rehearse the supported version sequence on an isolated broker and copied definitions.
4. Pin the management image and enable required plugins explicitly.
5. Run publish/consume, retry, poison-message, reconnect, restart, and shutdown tests.

**Risks and rollback:** Unsupported upgrade hops, feature-flag incompatibility, or message loss. Preserve definitions and the old broker data backup; drain or mirror traffic and switch back to the old isolated broker if acceptance fails.

**Acceptance and validation:** Broker is healthy on the pinned version; topology is correct; contract tests prove at-least-once behavior and recovery; restart preserves durable state; the WebApi handles broker unavailability per policy.

## Phase 18 — Upgrade Redis Insight as disposable tooling

**Objective and scope:** Upgrade Redis Insight without coupling the application release to its UI state. In: explicit image version, connection recreation, and volume disposition. Out: Redis server changes and production observability.

**Dependencies:** Phase 15.

**Steps:**

1. Confirm whether saved Redis Insight connections/settings must be preserved.
2. Export or document required connection metadata without secrets.
3. Start the target version with a new volume and reconnect it to Redis 8.
4. Remove the legacy tooling volume only after user approval.

**Risks and rollback:** Tooling settings may not migrate across majors. Treat the new volume as separate and retain the old image/volume until the UI smoke test passes.

**Acceptance and validation:** Redis Insight starts on the pinned version, connects without stored credentials in source, and can inspect expected Redis data; application tests remain unchanged.

## Phase 19 — Upgrade test and coverage tooling

**Objective and scope:** Upgrade coverlet, test SDK, Moq, and optionally the xUnit VS runner after compatibility proof. In: test-only packages and coverage pipeline. Out: production code behavior.

**Dependencies:** Stable green suites from earlier phases.

**Steps:**

1. Update `Microsoft.NET.Test.Sdk` and Moq within compatible ranges.
2. Upgrade coverlet in its own commit and compare coverage collection/output.
3. Evaluate xUnit runner 4.0 against xUnit 2.9.3; adopt only if official compatibility and IDE/CLI discovery are verified, otherwise retain 3.1.5 and record the constraint.
4. Set pragmatic coverage thresholds by layer.

**Risks and rollback:** Tests may disappear from discovery or coverage formats may change. Compare discovered test counts and reports before/after; revert each tooling family independently.

**Acceptance and validation:** Exactly expected tests are discovered; all pass; coverage report is generated and consumed by CI; no production package graph changes.

## Phase 20 — Isolate and modernize the VSIX project

**Objective and scope:** Make VSIX support explicit and independently buildable. In: decide support matrix, update Visual Studio SDK 17.x or deliberately migrate to VSSDK 18, CI separation. Out: WebApi behavior.

**Dependencies:** Phase 0 decision; otherwise independent of application refactors.

**Steps:**

1. Confirm target Visual Studio versions and whether the extension is still a deliverable.
2. Create a VSIX-specific build path using compatible Visual Studio MSBuild.
3. First update `Microsoft.VisualStudio.SDK` within 17.x and verify installation.
4. Treat VSSDK 18 as a separate opt-in migration with manifest/compatibility changes.
5. Remove VSIX from the application build gate or use a solution filter if it remains independently validated.

**Risks and rollback:** Visual Studio compatibility and manifest changes. Keep the previous VSIX artifact/toolchain and revert the isolated project/package commit.

**Acceptance and validation:** The chosen Visual Studio version installs and runs the VSIX; its build has a documented green command in CI; WebApi solution-filter build remains green; no unsupported `dotnet build` expectation remains.

## Phase 21 — Final consistency and release gate

**Objective and scope:** Remove temporary adapters/exceptions and prove the target state. In: dead code/packages, architecture exceptions, docs, graph, full validation. Out: new features.

**Dependencies:** All adopted phases.

**Steps:**

1. Remove compatibility shims, temporary architecture allowlists, dead packages, duplicate v1 code if its approved sunset is complete, and floating tags.
2. Regenerate Graphify output and compare dependency direction/hubs.
3. Re-run dependency/version/security/license audits.
4. Rebuild OpenAPI and compare approved contract baseline.
5. Update `AGENTS.md` commands only with commands verified in the final workspace.

**Risks and rollback:** Cleanup can remove apparently unused reflection/serialization paths. Use static analysis plus targeted tests and split questionable removals into separate commits.

**Acceptance and validation:** Stable SDK; clean restore/audit at the agreed severity; application, architecture, unit, integration, and functional tests pass; compose environment is healthy; API diff is approved; Graphify shows no forbidden outward Core dependencies; VSIX passes its separate gate if retained.

## Decisions (recorded 2026-10-10)

These defaults were adopted on 2026-10-10 and may be overridden by the user before the affected phase starts.

| # | Decision | Adopted default | Affects |
|---|---|---|---|
| 1 | VSIX | Keep it as an isolated, optional deliverable: update only within Visual Studio SDK 17.x, exclude it from the application build gate, and do not migrate to VSSDK 18 unless requested. | Phase 20 |
| 2 | API v1 | Keep v1 working with unchanged routes and status codes; mark it deprecated in OpenAPI. No removal date until the user sets one. | Phase 6 |
| 3 | RedisGraph | Remove it. No code references it and the product is end-of-life. | Phase 15 |
| 4 | Functional tests | Deterministic fakes/no-op adapters for Redis, RabbitMQ and Elastic. Real services only in a separate, opt-in integration profile. | Phase 1 |
| 5 | Domain target | Retarget `Template.Domain` from `netstandard2.1` to `net10.0` (no downstream consumer needs netstandard). | Phase 4 |
| 6 | PostgreSQL | Pin the latest supported 16.x patch. PostgreSQL 18 is a later, separate opt-in. | Phase 16 |
