# Phase 15 Execution Handoff

Read and fully execute Phase 15 of this repository's refactoring plan.

Main objective:
Consolidate the existing Redis, Redis Search, and Redis TimeSeries services onto one supported Redis 8 deployment, remove RedisGraph according to the adopted decision after verifying actual usage, migrate copied data and configuration safely into a new isolated volume, prove behavioral parity, and preserve the complete pre-migration environment for rollback.

Do not merely analyze or propose a plan: research the current supported Redis 8 target, implement the consolidation, rehearse the migration, test it, validate it, and document Phase 15 until all acceptance criteria are met.

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
- all subsequent phase reports through `docs/refactoring/phase-14-report.md` that exist because their phases were actually executed;
- `docs/refactoring/handoff-phase-14.md` for the established Compose baseline;
- every folder-specific `AGENTS.md` governing files you modify;
- the applicable repository skills, especially:
  - `package-upgrade`, because the Redis server/image major and image references change;
  - `integration-adapter`, because the Redis-backed cache and its configuration must be validated against the new server;
  - `dotnet-change-verification` if any .NET, project, solution, or build file changes;
  - `architecture-boundary-check` if project references, package references, or layer placement changes;
  - `api-contract-change` only if an observable HTTP contract could change.

Treat `docs/refactoring/baseline-phase-6/` as the approved API baseline unless a later, explicitly approved phase report records a replacement baseline.

## Prerequisites

Phase 15 depends on Phases 8, 13, and 14 and on the explicit RedisGraph disposition.

Before editing, verify from the actual repository state that:

- Phase 8 is complete: `IProductCache` is owned by Application, the concrete Redis adapter and package are in `Template.Infrastructure.Caching`, cache serialization/key versioning/TTL/failure semantics are documented and tested, and Application contains no Redis vendor dependency;
- Phase 13 is complete: Redis availability and health/readiness behavior are explicitly classified, logging/redaction rules are in place, and the relevant cross-cutting tests pass;
- Phase 14 is complete: `docs/refactoring/phase-14-report.md` contains actual clean-volume Compose, health, restart, migration/seed, pinned-image, and rollback results; the current baseline has no mutable image tags;
- the current Compose files, image references, volumes, configuration keys, scripts, and tests match the state recorded in the Phase 14 report;
- the latest recorded validation checkpoint is consistent with the current files;
- this checkout contains all accumulated prior-phase work and is not an outdated worktree or branch.

The decision recorded on 2026-10-10 is to remove RedisGraph because no code reference was found and the product is end-of-life. Treat that as the adopted default, but verify the current repository and Compose environment again before removal. A historical search is not proof that no reference was added later.

If any prerequisite is missing or Phase 14 was not genuinely executed, stop and report the blocker. Do not implement the missing phase inside Phase 15 and do not selectively copy files from another checkout.

## Checkout and working-tree safety

Work directly in the authoritative checkout above.

The working tree may contain pre-existing user changes. Inventory and preserve them in full. Do not overwrite or reformat unrelated files.

Do not:

- run `git reset`, `git checkout --`, `git clean`, or `git stash`;
- perform a destructive rebase or discard working-tree changes;
- commit, push, merge, change branches, create another worktree, or open a pull request without explicit authorization;
- delete or overwrite Redis volumes, RDB files, AOF files, keys, indexes, time series, graph data, snapshots, backups, saved connections, or other material state;
- reuse the user's normal Redis volume for migration rehearsal;
- point two incompatible server majors at the same volume.

## Phase 15 scope

Implement the requirements under “Phase 15 — Consolidate Redis services on Redis 8” in `docs/phase-3-implementation-plan.md`:

1. Inventory Redis keys, module commands, persistence settings, ACLs, and any external RedisGraph use.
2. Apply the adopted decision to remove RedisGraph, but only after proving there are no current repository or required external references. If material external use is discovered, stop and report the conflict instead of deleting it or choosing a new graph database without approval.
3. Provision a stable, supported Redis 8 deployment with integrated Search and Time Series capabilities, using a new isolated volume.
4. Migrate copied data through an officially supported path and validate commands, ACL categories, expiry, serialization, and cache invalidation.
5. Switch the application/Compose configuration only after parity tests pass, retaining the complete previous Redis environment for rollback.

This phase includes the Redis server major/consolidation work. It does not include PostgreSQL, RabbitMQ, Redis Insight, Elasticsearch, test-tooling, or VSIX upgrades.

## Current-state inventory before editing

Treat the actual post-Phase-14 state as authoritative. Do not assume the authoring snapshot still applies.

Inventory at least:

- every Redis-related Compose service, image tag/digest, command, environment variable, port, health check, dependency, network, config, and volume;
- the main Redis server version and modules reported at runtime;
- the separate Search, TimeSeries, and RedisGraph image versions and modules reported at runtime;
- Redis Insight dependencies and saved connection targets, without reading or exposing credentials unnecessarily;
- all application configuration keys and environment-variable mappings used to reach Redis;
- `ConnectionStrings:RedisConnection`, `CacheSettings`, and the effective connection target by host/port name only;
- `Template.Infrastructure.Caching`, `IProductCache`, cache registration, key format, serialization format, TTL policy, invalidation behavior, timeout behavior, and fail-soft behavior;
- application, test, script, documentation, and operational references to Redis, RediSearch/Redis Query Engine, RedisJSON, RedisTimeSeries, and RedisGraph;
- external references discoverable from the in-scope local environment, such as Compose dependencies and Redis Insight connection metadata, without scanning or changing unrelated external systems;
- logical database numbers, ACL users/categories, authentication mode, TLS state, maxmemory policy, eviction policy, persistence mode, snapshot settings, append-only settings, and data-directory layout;
- key counts, key types, TTL/PTTL behavior, and namespaced samples without dumping values or personal payloads;
- Search indexes/schemas/aliases and the commands that create/query them;
- TimeSeries keys, labels, retention, duplicate policy, rules, aggregations, and the commands that use them;
- graph keys, schemas, and clients if any exist;
- current health/readiness classification and restart behavior;
- current automated and real-service Redis coverage.

At the authoring checkpoint, the application used Redis only through the Phase 8 product-cache adapter with versioned keys such as `template:product:v1:{id}`, JSON serialization, absolute/sliding expiration, invalidation after writes, bounded timeouts, and fail-open cache misses. Separate Compose services existed for Redis Search, TimeSeries, and RedisGraph, but no application references to their commands were found. Confirm all of this again after Phase 14; do not treat it as guaranteed execution-time truth.

Be precise about terminology: Redis Search/Query Engine in this phase is not the Elasticsearch-backed person index from Phase 10/11. Do not migrate or redesign the Elasticsearch person feature.

Record the sanitized inventory in `docs/refactoring/phase-15-report.md`.

## Version and compatibility research

Version information is time-sensitive. Do not select a Redis release or image from memory, and do not use the historical Phase 2 table as current authorization.

Before editing image references, consult current official primary sources, including as applicable:

- Redis Open Source release notes and support policy;
- Redis 8 breaking changes and upgrade guidance;
- official Redis module lifecycle/consolidation documentation;
- official Redis Query Engine/Search documentation and command compatibility notes;
- official Redis Time Series documentation and command compatibility notes;
- official persistence and data-migration documentation;
- official ACL command-category changes;
- official container registry tags and manifests;
- official StackExchange.Redis and `Microsoft.Extensions.Caching.StackExchangeRedis` compatibility information if client compatibility is in question;
- official RedisGraph EOL documentation.

Select only a stable, supported Redis 8 release and official image that demonstrably includes the required integrated Search and Time Series capabilities. Verify the exact image contents with runtime commands; do not infer module availability from a marketing name or major number.

Record:

- source service/image versions and digests;
- selected Redis 8 tag and digest;
- verification date;
- direct official source links;
- supported source-to-target migration path;
- module and command compatibility evidence;
- ACL and persistence breaking changes;
- client compatibility evidence;
- rollback references.

Use explicit tags and immutable digests when available. Do not introduce `latest`, floating major/minor tags, preview releases, unofficial replacement images, or an unverified module bundle.

Do not update the .NET Redis client package merely because a newer version exists. If the current client is incompatible with the selected Redis 8 target, update the smallest coherent client/package family using official sources, document the breaking changes, run direct and transitive vulnerability audits, and preserve a coherent package rollback. Do not update unrelated packages.

## RedisGraph removal decision

The adopted plan decision is to remove RedisGraph, not to replace it in this phase.

Before removing the service or its Compose references, search the actual repository and in-scope configuration for:

- graph commands such as `GRAPH.*`;
- RedisGraph client libraries or packages;
- graph-specific ports, services, DTOs, scripts, tests, documentation, dashboards, or examples;
- connection strings or environment variables pointing to the graph service;
- Redis Insight saved connections to the graph endpoint;
- persisted graph data in the isolated source rehearsal environment;
- any documented external consumer the user has placed in scope.

If no usage is found, remove the RedisGraph service, port mappings, dependency edges, variables, and documentation that exist solely to support it. Remove only proven-dead references.

Do not delete the old RedisGraph image, container data, volume, or backup. Preserve the complete Phase 14 source environment for rollback. If material graph data or a real consumer is found, stop before removal and report the conflict, evidence, and decision required from the user. Do not silently select another graph database or invent a migration requirement.

## Target Redis 8 topology

Replace the separate main Redis, Search, and TimeSeries service topology with one supported Redis 8 deployment that owns the required integrated capabilities.

The target must use:

- one explicit Redis 8 image/tag/digest;
- one new isolated target volume;
- explicit authentication and ACL behavior;
- a documented persistence policy;
- a bounded, non-secret health check;
- deterministic startup dependencies;
- only the ports actually required by the application and tooling;
- configuration names compatible with the existing application unless a transition is justified and documented.

Remove the separate Search and TimeSeries services, their obsolete port mappings, dependency edges, and variables only after parity is proven. Do not keep dead duplicate servers running merely to make Compose appear compatible.

Keep Redis Insight as tooling and keep its current image/version and existing tooling volume unchanged. Update only its connection target or dependency list as required to reach the consolidated Redis 8 service. Redis Insight's image/volume migration belongs to Phase 18.

Preserve the Phase 13 availability classification: cache failure must not prevent unrelated application functionality from starting or succeeding according to the adopted fail-soft policy. Do not make Redis an undocumented mandatory WebApi startup dependency.

## Isolated migration rehearsal

Rehearse the migration in a parallel, isolated environment before switching the default Compose configuration.

Use:

- the preserved Phase 14 source images and source volume, or a verified copy/snapshot of them;
- a new project name, network, ports, containers, and target volume;
- synthetic credentials supplied outside version control;
- copied or synthetic data only;
- bounded waits and deterministic probes.

Never mount both source and target server majors against the same volume. Never start an older server against a volume written or upgraded by a newer major.

Before the rehearsal, identify the officially supported transfer mechanism for the actual source persistence mode and module data. Possible mechanisms must be chosen from official guidance, not improvised filesystem copying. If cache data is intentionally disposable, still characterize it and prove the empty-cache fallback; do not use “cache” as justification to ignore Search or TimeSeries state that may be persistent.

If access to real or user-owned Redis data is needed, stop and obtain explicit authorization before reading, copying, exporting, restoring, or migrating it. If only synthetic data is used, state that clearly and do not claim real-data compatibility.

The rehearsal must demonstrate:

1. The source environment remains unchanged and can still start.
2. A recovery artifact or reproducible source copy exists when supported and is verified.
3. Representative cache, Search, and TimeSeries data is present in the source copy or created synthetically from the actual schemas/commands.
4. Data is transferred through the supported path into the new Redis 8 volume.
5. Target key counts, types, values where safe, TTLs, Search definitions/results, and TimeSeries metadata/ranges match the expected source semantics.
6. Authentication and ACL behavior is compatible.
7. Restart preserves the expected persistent target data.
8. The application and tooling connect only to the target during parity testing.
9. Rollback is demonstrated by reconnecting to the untouched source environment, not by mounting the new volume with the old server.

Do not delete the source environment or migration artifacts after a successful rehearsal.

## Data, serialization, expiry, and command compatibility

Validate the application-visible and module-visible contract, not merely server startup.

### Product cache

Against real Redis 8, cover at least:

- cache miss;
- set/get round trip using the existing `template:product:v1:` key namespace;
- JSON payload compatibility without logging the payload;
- absolute expiration;
- sliding expiration/refresh behavior as implemented by the current client;
- invalidation after product update;
- invalidation after product deletion;
- unreadable payload treated as a miss and evicted according to the established adapter contract;
- caller cancellation;
- bounded timeout;
- server unavailability treated as a miss;
- recovery after the server returns;
- restart persistence according to the selected persistence policy.

Do not change the key prefix or serialization format unless a proven incompatibility requires it. If either must change, introduce a versioned transition, document read/write compatibility and rollback, and do not overwrite old keys.

### Search / Query Engine

For every actual Search use found in the inventory, validate the corresponding index/schema creation, indexing, query, result ordering/limits, expiration interaction, ACL category, restart behavior, and compatibility with migrated data.

If no repository or in-scope consumer exists, prove that result and demonstrate only the minimum integrated Search capability required by the phase using isolated synthetic keys. Do not invent an application feature or redirect the Elasticsearch person index to Redis.

### Time Series

For every actual TimeSeries use found in the inventory, validate key creation, labels, retention, duplicate policy, range queries, aggregation/rules, ACL category, restart behavior, and compatibility with migrated data.

If no repository or in-scope consumer exists, prove that result and demonstrate only the minimum integrated Time Series capability required by the phase using isolated synthetic keys. Do not invent product metrics or production telemetry.

### ACL and configuration

Validate the exact command categories and permissions needed by the cache, Search, TimeSeries, health checks, and Redis Insight. Use least privilege where the current architecture supports it without a contract change. Do not weaken authentication or grant broad administrative access merely to pass tests.

Keep passwords, ACL rules containing secrets, connection strings, and full configuration dumps out of source, logs, reports, and test output. Document configuration-key names and redacted policy only.

## Cutover and rollback

Do not switch the default Compose/application configuration until all isolated parity checks pass.

Before cutover, record:

- the exact source image references and digests;
- source container and volume names;
- target image reference and digest;
- target container and volume names;
- the last verified source state;
- migration artifact identifiers;
- the exact configuration change needed to select source or target;
- objective go/no-go criteria.

Perform the switch as one coherent configuration change. Do not dual-write cache/module data unless an approved design explicitly requires and tests it. Do not run old and new servers against one volume.

Rollback must switch configuration back to the preserved source containers and volume. It must not require data-directory downgrade, deletion, destructive conversion, or mounting Redis 8 data with an older server.

Retain the old Redis environment and volume after acceptance. Any later deletion requires explicit authorization.

## Public HTTP and OpenAPI compatibility

Phase 15 must not change the public HTTP contract.

Preserve:

- v1 and v2 routes;
- methods, bindings, status codes, response shapes, and content types;
- authentication and authorization behavior;
- API v1 deprecation;
- health routes and semantics established in Phase 13;
- the Elasticsearch-backed person endpoints;
- published OpenAPI.

Capture v1 and v2 OpenAPI after the target environment is active and compare them exactly with the approved baseline. Investigate every semantic difference. Do not silently update the baseline.

If Redis 8 compatibility appears to require an HTTP contract change, stop and present the evidence and migration choices before implementing it.

## Required tests

Add or update tests proportionally to the consolidation.

### Adapter and integration tests

Run the existing cache contract against a real isolated Redis 8 target and add missing coverage for:

1. Hit and miss.
2. Serialization compatibility.
3. Absolute and sliding expiry.
4. Invalidation after update/delete.
5. Unreadable payload behavior.
6. Authentication success and failure without secret leakage.
7. ACL allow/deny behavior for required commands.
8. Timeout and caller cancellation.
9. Initial unavailability.
10. Recovery after restart/outage.
11. Persistence across restart.
12. No dependency on a developer-hosted Redis instance.

Keep ordinary functional tests deterministic through the existing in-memory/no-op strategy. Real Redis belongs in a separate explicit integration profile or the isolated Compose rehearsal.

### Search and TimeSeries tests

Exercise the commands and data structures proven necessary by the inventory. At minimum, demonstrate that the selected Redis 8 target actually provides integrated Search and Time Series capabilities, that ACLs permit the required operations, and that restart preserves expected persistent data.

Do not report a generic `PING` as proof that Search or TimeSeries works.

### Compose and removal checks

Prove that:

- the consolidated Redis 8 service is healthy;
- WebApi and Redis Insight use the intended target endpoint;
- separate Redis Search and TimeSeries services no longer exist in effective Compose;
- RedisGraph no longer exists in effective Compose when the no-usage verification succeeds;
- obsolete ports, environment variables, dependency edges, and volumes are not referenced;
- no removed service name remains in scripts, tests, setup documentation, or configuration;
- the preserved old environment remains available for rollback outside the new default topology.

### Functional and architecture tests

Prove that:

- Application remains free of Redis implementation packages and vendor types;
- the cache adapter remains in Infrastructure;
- WebApi remains the composition root;
- cache-disabled and Redis-unavailable paths still succeed according to policy;
- product cache hit/miss/invalidation behavior remains correct;
- unrelated endpoints continue working during Redis unavailability;
- the public HTTP/OpenAPI contract is unchanged;
- no architecture exception is introduced.

## Validation

Run build and test commands sequentially because this checkout's shared intermediate outputs contend under parallel execution.

Use the pinned SDK:

`C:\Users\alexa\.dotnet\dotnet.exe`

Start with the smallest Redis adapter, real-service, Compose, and functional checks.

At the end, run the repository validation script:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-phase-validation.ps1
```

Confirm and record the actual result of every applicable step, including:

- restore;
- WebApi build with warning/error count;
- architecture tests;
- unit tests;
- adapter integration tests;
- real Redis 8 cache/Search/TimeSeries tests;
- EF InMemory integration tests;
- real PostgreSQL integration tests;
- real RabbitMQ integration tests;
- real Elasticsearch integration tests for the current adopted stack;
- functional tests;
- direct and transitive package vulnerability audit;
- effective Compose validation with `docker compose config -q`;
- isolated migration rehearsal;
- target restart/persistence checks;
- rollback switch to the source environment;
- OpenAPI capture and exact comparison;
- `git diff --check`.

Because this phase changes infrastructure topology, regenerate `graphify-out` exclusively with the official Graphify command when repository rules or structural source changes require it. Do not hand-edit generated Graphify output. If Graphify does not model Compose-only topology and no structural source change occurred, record that fact and the reason regeneration was not required.

If Docker, Redis 8, an official image, the source environment, or another required dependency is unavailable, do not report the corresponding gate as passed. Record the attempted command, sanitized result, cause, and unverified acceptance criteria. Do not silently reduce the migration rehearsal or module coverage to obtain a green result.

## Package, image, and security audit

After all version/configuration changes:

- verify the selected Redis 8 image digest and runtime version;
- verify integrated module/capability versions reported by the running server;
- confirm that no floating Redis-related image tag remains;
- confirm that no old module image remains in the effective target Compose configuration;
- inspect direct and transitive NuGet package graphs;
- run the vulnerability audit;
- confirm that no unrelated production package changed;
- record any supported client-version constraint;
- verify `.env` is still ignored and untracked and `.env.example` contains placeholders only.

Do not include secret values in the audit artifacts or report.

## Scope boundaries

Do not perform work from adjacent or later phases:

- do not patch or upgrade PostgreSQL beyond the already established Phase 14 baseline;
- do not perform the optional PostgreSQL 18 migration;
- do not upgrade RabbitMQ server or feature flags;
- do not upgrade Elasticsearch or Kibana;
- do not upgrade Redis Insight or migrate/delete its tooling volume;
- do not update test/coverage tooling;
- do not modernize the VSIX;
- do not implement final Phase 21 cleanup;
- do not introduce a replacement graph database;
- do not move the Elasticsearch person index to Redis Search;
- do not invent Search, TimeSeries, graph, caching, or observability product features;
- do not redesign the Phase 8 cache port or change its semantics without a proven compatibility defect;
- do not change public HTTP contracts or remove API v1;
- do not create EF migrations without a real relational schema delta;
- do not hand-edit EF migration designer/snapshot files or generated `.resx` designers;
- do not delete old Redis environments, volumes, backups, images, or user data;
- do not perform unrelated package, code, formatting, or documentation cleanup.

## Acceptance criteria

Phase 15 may be considered complete only when:

- Phases 8, 13, and 14 were verified from actual implementation and reports;
- current official Redis sources were consulted and recorded;
- a stable supported Redis 8 image with explicit tag/digest was selected;
- the source Redis/module topology, data, persistence, commands, ACLs, and consumers were inventoried;
- the adopted RedisGraph removal decision was revalidated against actual references;
- no required RedisGraph use exists; if a material consumer or graph dataset is discovered, the phase remains blocked pending user direction and must not be reported complete;
- a new isolated Redis 8 volume was used;
- the old and new server majors were never mounted against the same volume;
- copied or synthetic data was migrated through an officially supported path;
- cache key, serialization, TTL, and invalidation behavior is compatible;
- required Search and TimeSeries behavior is demonstrated on Redis 8;
- ACL categories and authentication behavior are validated;
- the application cache adapter/contract tests pass against Redis 8;
- WebApi smoke and cache functional tests pass;
- cache-disabled and Redis-unavailable behavior remains correct;
- restart preserves expected target data;
- the old source containers, image references, volume, and recovery artifacts remain available for rollback;
- default Compose contains one target Redis 8 service and no obsolete separate Search/TimeSeries dependencies;
- RedisGraph is removed from default Compose only after the no-usage proof;
- Redis Insight remains disposable tooling on its existing Phase 14 version/volume and can target Redis 8 without secrets in source;
- no unrelated production package graph or public HTTP/OpenAPI contract changed;
- all required validation gates actually pass;
- no Phase 16+ work was implemented.

If only synthetic data was available, explicitly mark real-data migration compatibility as unverified. Do not claim full data compatibility from synthetic coverage alone.

## Documentation and delivery

Create:

`docs/refactoring/phase-15-report.md`

The report must include:

- an executive summary;
- proof that Phases 8, 13, and 14 were complete;
- the pre-change Redis/module/Compose inventory;
- application and external-consumer reference inventory;
- RedisGraph no-usage evidence and removal result, or the discovered conflict;
- source and target version/image/digest matrix;
- verification date and direct official source links;
- Redis 8 capability/module evidence;
- persistence, ACL, authentication, eviction, and health configuration;
- source and target project/container/network/volume identifiers;
- whether copied real data or synthetic data was used;
- the supported migration mechanism and rehearsal steps;
- cache key/serialization/TTL/invalidation parity results;
- Search and TimeSeries parity/capability results;
- data counts, types, TTL comparisons, Search query results, and TimeSeries range/metadata results, with sensitive values omitted;
- restart and persistence evidence;
- cutover decision and configuration changes;
- proof that obsolete services and references were removed from the target Compose topology;
- proof that the source environment remains intact;
- rollback demonstration and exact switch-back procedure;
- packages or images changed and confirmation that unrelated families were untouched;
- tests added or updated;
- commands actually executed and sanitized actual results;
- final test counts;
- OpenAPI comparison result;
- vulnerability-audit result;
- Graphify result or documented reason it was not required;
- residual risks and limitations;
- gates not executed or blocked;
- retained source resources and approval-gated cleanup;
- explicit confirmation that no Phase 16+ work was implemented.

Update `AGENTS.md`, `.env.example`, setup instructions, and validation scripts only with configuration names, commands, and results actually verified. Never copy real secret values.

Upon completion, present:

1. An objective implementation summary.
2. The main files, images, services, and configuration changed.
3. The selected Redis 8 version/digest and official compatibility sources.
4. The RedisGraph reference audit and removal result.
5. The source-to-target migration path and whether real or synthetic data was used.
6. Cache, Search, TimeSeries, ACL, restart, and persistence results.
7. The final Compose topology and Redis Insight connection result.
8. Full validation results with actual counts.
9. OpenAPI, vulnerability-audit, and Graphify results.
10. Pending or blocked gates and residual risks.
11. The demonstrated rollback procedure and exact retained source resources.
12. Confirmation that no old volume or material data was deleted.
13. Confirmation that Phase 16 was not started.

Do not commit, push, merge, change branches, create another worktree, delete volumes or data, alter a production-like environment, or open a pull request without explicit authorization.
