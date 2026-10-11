# Phase 18 Execution Handoff

Read and fully execute Phase 18 of this repository's refactoring plan.

Main objective:
Upgrade Redis Insight as disposable operational tooling without coupling the application release, startup, readiness, package graph, or behavior to the tool's UI state. Select and pin a current stable official image, determine whether saved connections/settings need preservation, start the target version with a new isolated volume, reconnect it safely to the Phase 15 Redis 8 deployment, validate the UI and expected data visibility, and retain the complete legacy tooling image/volume for rollback.

Do not merely analyze or produce a plan: research, implement, test, validate, and document Phase 18 until all acceptance criteria are met.

## Required context

The authoritative repository is:

`C:\Users\alexa\source\repos\TemplateCleanArchNET`

Before changing any file, read in full:

- `AGENTS.md`;
- `docs/phase-2-workspace.md`;
- `docs/phase-3-implementation-plan.md`;
- all available phase reports through `docs/refactoring/phase-17-report.md` that correspond to phases actually executed;
- `docs/refactoring/handoff-phase-15.md` and `docs/refactoring/phase-15-report.md` for the Redis 8 consolidation, target endpoint, ACL, persistence, and rollback decisions;
- `docs/refactoring/handoff-phase-14.md` and `docs/refactoring/phase-14-report.md` for Compose/image/volume conventions;
- reports for Phases 16 and 17 if those phases were executed in the accumulated sequence;
- every folder-specific `AGENTS.md` governing files you modify;
- the applicable repository skills, especially:
  - `package-upgrade`, because a container-tool image changes;
  - `dotnet-change-verification` only if a .NET, project, solution, or build file unexpectedly changes;
  - `integration-adapter` only if a proven Redis connection/configuration defect must be corrected; do not use it to redesign the application adapter;
  - `api-contract-change` only if an observable HTTP contract could change;
  - `architecture-boundary-check` only if project references, package references, or layer placement change.

Treat `docs/refactoring/baseline-phase-6/` as the approved API baseline unless a later, explicitly approved phase report records a replacement baseline.

## Prerequisite: Phase 15 must be complete

Phase 18 depends on Phase 15.

Before editing, verify from actual code, reports, Compose configuration, containers, volumes, and tests that:

- `docs/refactoring/phase-15-report.md` records actual Redis 8 consolidation results rather than planned work;
- one supported Redis 8 deployment provides the adopted cache, Search, and TimeSeries capabilities;
- separate legacy Search/TimeSeries services and the approved RedisGraph service/reference removals are reflected in effective Compose;
- the Redis 8 target image/tag/digest, service name, network alias, port, authentication/ACL policy, health check, and volume are known and verified;
- cache, Search, TimeSeries, restart/persistence, unavailable-service, and rollback tests passed at the latest checkpoint;
- the previous Redis environment remains available according to the Phase 15 rollback plan;
- no previously adopted phase is missing from this checkout;
- if Phases 16 and 17 were executed in sequence, their accumulated state and reports are present and must remain untouched;
- this is the authoritative checkout and the current files match the latest actual report.

If Phase 15 is missing, incomplete, or not present in this checkout, stop and report the blocker. Do not consolidate or upgrade Redis inside Phase 18, and do not selectively copy prior-phase files from another checkout.

## Checkout and working-tree safety

Work directly in the authoritative checkout above.

The working tree may contain pre-existing user or concurrent-agent changes. Inventory and preserve them in full. Do not overwrite, normalize, or reformat unrelated files.

Do not:

- run `git reset`, `git checkout --`, `git clean`, or `git stash`;
- perform a destructive rebase or discard working-tree changes;
- commit, push, merge, change branches, create another worktree, or open a pull request without explicit authorization;
- delete or overwrite the legacy Redis Insight volume, saved connections, settings, database, image, container state, exports, or backups;
- delete or alter Redis server data, keys, indexes, time series, ACLs, or volumes;
- mount the legacy Redis Insight volume into the target major unless current official guidance explicitly supports that exact migration and the user has approved preserving its contents through that method.

## Phase 18 scope

Implement the requirements under “Phase 18 — Upgrade Redis Insight as disposable tooling” in `docs/phase-3-implementation-plan.md`:

1. Confirm whether saved Redis Insight connections/settings must be preserved.
2. Export or document only the required connection metadata without secrets.
3. Start the target Redis Insight version with a new volume and reconnect it to the Phase 15 Redis 8 service.
4. Retain the legacy tooling image/volume; remove the legacy volume only after separate explicit user authorization.

This phase changes Redis Insight tooling only. It does not change the Redis server, application cache behavior, production observability, public API, or any other service.

## Disposable-tooling boundary

Redis Insight is a convenience UI, not an application dependency.

The final design must ensure that:

- WebApi does not reference, call, wait for, or health-check Redis Insight;
- Redis, WebApi, tests, and the release gate can start and pass when Redis Insight is stopped or absent;
- Redis Insight failure does not affect application liveness/readiness;
- no Application, Domain, Infrastructure, or Presentation package is added for Redis Insight;
- no business use case depends on Redis Insight metadata or availability;
- application credentials are not embedded in source merely to preconfigure the UI;
- Redis Insight may depend on Redis, but Redis and WebApi must not depend on Redis Insight;
- tooling-specific state stays in its own volume;
- any Compose profile or optional-start mechanism reflects existing repository conventions and does not silently change normal application behavior.

Do not add Redis Insight to a production readiness endpoint, application telemetry pipeline, or mandatory deployment gate.

## Current-state inventory before editing

Treat the actual post-Phase-15 state as authoritative. Inventory and record at least:

- current Redis Insight service name, official/unofficial image source, tag/digest, runtime version, architecture, command, environment keys, user, health check, dependencies, ports, network, restart policy, and volume mount;
- effective Compose configuration and any profiles or overrides affecting the tool;
- legacy volume name, mount path, size, ownership/permissions, backup status, and whether it is shared with any other service;
- saved Redis connection names, host/port targets, logical database selections, TLS mode, Sentinel/cluster mode, and non-secret labels/settings visible through supported UI/API mechanisms;
- whether saved connection records contain or reference credentials, certificates, SSH material, or other secrets;
- any saved workbench queries, browser favorites, settings, certificates, or other user-created tooling state whose preservation may matter;
- the Phase 15 Redis 8 service name, network alias, port, TLS/authentication/ACL requirements, database numbers, and expected cache/Search/TimeSeries data categories;
- host exposure and firewall implications of the Redis Insight UI port;
- current startup, restart, and shutdown behavior;
- documentation, scripts, environment variables, and tests referring to Redis Insight;
- all existing Redis Insight containers, volumes, exports, backups, and ports in the in-scope local environment.

At the original authoring checkpoint, Compose used `redislabs/redisinsight:1.14.0`, mounted a dedicated `/db` volume, and depended on the main Redis plus separate graph/search/timeseries services. Phase 15 should have changed the server topology. Confirm the execution-time state; do not assume the old image name, data path, port, settings schema, or dependency list still applies.

Record only sanitized inventory in `docs/refactoring/phase-18-report.md`. Do not read raw storage files merely to discover secrets when supported UI/API metadata is sufficient.

## Preservation decision for saved settings

Before starting the target version, determine whether the user needs any legacy Redis Insight state preserved beyond rollback retention.

Classify the discovered state as:

- required to recreate a connection;
- useful but safely reproducible;
- secret-bearing and therefore external to version control;
- obsolete because it points to removed Phase 15 services;
- optional/disposable;
- unknown and requiring user clarification.

Present the user with a concise sanitized inventory and preservation options if the answer materially changes the migration method. Do not assume that all saved UI state is disposable merely because Redis Insight is tooling.

Regardless of the decision, retain the legacy image and volume unchanged until the new UI smoke test passes and the user separately authorizes cleanup.

If settings must be preserved:

- use only an officially supported export/import or migration path for the exact source and target versions;
- export the minimum necessary metadata;
- exclude or separately handle credentials, private keys, certificates, and tokens;
- store any sensitive artifact outside version control in an ignored, access-controlled location;
- verify the export can be restored into an isolated target;
- do not copy the legacy volume into the target by assumption.

If settings do not need migration, create a clean target volume and recreate only the required Redis 8 connection metadata. Supply credentials at runtime through the UI or another approved secret mechanism; do not commit them.

If no supported settings migration exists, preserve the old volume for rollback and recreate connections manually from sanitized metadata. Do not reverse-engineer or mutate the tool's internal database unless explicitly authorized and officially supported.

## Version and image research

Redis Insight image information and data formats are time-sensitive. Do not select a version from memory or use the historical Phase 2 table as current authorization.

Before editing the image reference, consult current official primary sources, including as applicable:

- Redis Insight release notes;
- official installation and Docker documentation;
- official upgrade/migration guidance across the exact source and target versions;
- official saved-connection/settings export/import documentation;
- official Redis 8 compatibility documentation;
- official image registry tags, manifests, supported architectures, user, ports, health endpoints, and volume/data paths;
- security advisories and support policy for the selected Redis Insight release.

Select only a stable, supported official target image compatible with the Phase 15 Redis 8 deployment.

Record:

- source and target versions;
- source and target image names/tags/digests;
- verification date;
- direct official source links;
- architecture and runtime requirements;
- exposed/container ports;
- old and new data paths;
- settings/data-format compatibility;
- migration or recreation method;
- breaking changes relevant to saved connections and volumes;
- rollback image/volume references.

Use an explicit tag and immutable digest when available. Do not use `latest`, a floating major/minor tag, preview build, unofficial image, or an unverified architecture manifest.

## New target volume and isolated rehearsal

Always start the target Redis Insight with a new volume. Do not reuse or upgrade the legacy volume in place.

Before starting:

- choose a unique rehearsal project/container name and target volume name;
- use a distinct host port if the legacy UI is running;
- verify the target volume is new and not referenced by another service;
- verify the target image's documented user and data-directory permissions;
- connect through the same isolated network path that will be used by the final Compose service;
- provide Redis credentials outside version control;
- verify that no secret appears in rendered Compose output, logs, screenshots, reports, or shell command transcripts;
- record exact source/target resource identifiers.

The rehearsal must demonstrate:

1. The target container starts on the selected image/digest.
2. The service reaches its documented healthy/ready state within a bounded time.
3. The UI and required API/static assets load.
4. The target uses the new volume and writes no state to the legacy volume.
5. Required settings are imported or recreated according to the preservation decision.
6. The target connects to the Phase 15 Redis 8 endpoint using the approved authentication/TLS policy.
7. It can inspect the expected cache, Search, and TimeSeries data categories without modifying them.
8. Removed RedisGraph and legacy separate-service connections are not recreated.
9. A non-destructive Redis Insight restart preserves the intended target tooling state.
10. Redis and WebApi continue working when the target Redis Insight is stopped.
11. Rollback is demonstrated by stopping the target and starting the untouched legacy image/volume on an isolated port, without changing Redis server data.

If a browser/UI automation capability is available, use it for the UI smoke test and capture only non-sensitive evidence. If UI access is unavailable, do not claim the UI gate passed based only on an open TCP port; record it as unverified and use the strongest supported API/static-asset checks available.

Do not delete the rehearsal or legacy volume after validation without explicit authorization.

## Redis connection and inspection validation

Connect the target Redis Insight to the already validated Phase 15 Redis 8 service.

Verify without exposing values or modifying user data:

- connection succeeds with the intended host/network alias and port;
- authentication/ACL succeeds for the intended tooling user;
- authentication failure is reported safely without logging the password;
- TLS behavior matches the Phase 15 policy;
- the selected logical database or cluster mode is correct;
- the expected product-cache key namespace is visible by bounded scan/metadata, not a full sensitive dump;
- integrated Search indexes/capability are visible through supported tool views where applicable;
- TimeSeries keys/capability are visible through supported tool views where applicable;
- TTL and key-type metadata can be inspected without changing data;
- no graph connection or graph feature is assumed after the adopted Phase 15 removal;
- stopping Redis Insight does not change Redis data or application behavior.

Use synthetic/test-exclusive Redis data if inspecting real data would expose user content. Do not run arbitrary workbench commands against user data, delete keys, change TTLs, create indexes, alter ACLs, or mutate TimeSeries data merely to test the UI.

## Compose and configuration changes

Update only the Redis Insight portion of Compose and related placeholder documentation required by this phase.

Ensure that:

- the target image is explicit and immutable where supported;
- the target has its own new named volume;
- the legacy volume declaration is retained if required for rollback and is not attached to the target;
- dependency edges reference only the consolidated Phase 15 Redis service;
- obsolete RedisGraph/Search/TimeSeries service dependencies and port variables do not return;
- the tool's port exposure is explicit and appropriate for local tooling;
- secrets are not stored in Compose, `.env.example`, appsettings, scripts, or documentation;
- `.env.example` contains placeholders and only variables actually needed by the target setup;
- WebApi, Redis, and other services do not depend on Redis Insight;
- `docker compose config -q` succeeds;
- normal application startup remains possible with the Redis Insight service omitted or stopped.

Do not update the Redis server image, volume, persistence, ACLs, module configuration, cache adapter, or application connection semantics in this phase.

## Security and privacy

Redis Insight is an administrative interface. Review its exposure and credentials accordingly.

Do not:

- bind the UI broadly beyond the established local/tooling requirement without explicit authorization;
- commit Redis passwords, ACL secrets, TLS private keys, SSH keys, certificates containing private material, tokens, or saved-connection databases;
- print complete Redis URLs or connection strings containing credentials;
- include secret-bearing exports in validation artifacts;
- capture screenshots showing credentials, personal data, payloads, or sensitive key values;
- weaken Redis authentication or ACLs to make the UI connect;
- enable anonymous access unless that is the documented existing local policy and explicitly accepted.

Document configuration-key names, redacted endpoints, and permission categories only.

## Application and release independence

Prove that the application behavior and package graph remain unchanged.

Validate that:

- no production `.csproj`, `Directory.Packages.props`, or application source file changes solely for Redis Insight;
- no NuGet dependency is added;
- WebApi starts with Redis Insight absent;
- application liveness/readiness is unchanged when Redis Insight is stopped;
- Redis cache behavior remains the Phase 15 behavior;
- functional tests do not start or require Redis Insight;
- CI/release gates do not require UI availability unless a separate optional tooling check is explicitly documented;
- public HTTP behavior and OpenAPI remain unchanged.

If an application code or package change appears necessary, stop and demonstrate why; Redis Insight must not leak into application architecture.

## Required tests and validation

Add or update tooling/Compose checks proportionally. Do not create production application behavior merely to make Redis Insight testable.

Cover at least:

- target image tag/digest and runtime version;
- target container startup and bounded health/readiness;
- UI/API/static-asset smoke test;
- use of a new target volume;
- legacy volume remains unchanged and unattached to the target;
- settings import or clean connection recreation according to the preservation decision;
- Redis 8 connection with the intended auth/TLS mode;
- safe visibility of expected cache, Search, and TimeSeries data categories;
- restart persistence of intended target tooling settings;
- target stopped/absent while Redis and WebApi continue working;
- rollback to the untouched legacy image/volume;
- effective Compose has no dependency on removed legacy Redis services;
- no credentials or secret-bearing exports are committed or printed;
- application package graph and test discovery/counts are unchanged.

Run restore, build, and tests sequentially because this checkout's shared intermediate outputs contend under parallel execution.

Use the pinned SDK:

`C:\Users\alexa\.dotnet\dotnet.exe`

At the end, run the repository validation script:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-phase-validation.ps1
```

Record the actual results of all applicable gates, including:

- restore;
- WebApi build with warning/error count;
- architecture tests;
- unit tests;
- integration and real-service suites for the current adopted stack;
- functional tests;
- Phase 15 Redis contract/smoke checks;
- Redis Insight target startup/UI/connection/restart checks;
- application startup with Redis Insight absent;
- rollback to the legacy tooling image/volume;
- direct and transitive NuGet vulnerability audit, confirming no application package change;
- `docker compose config -q`;
- OpenAPI capture and exact comparison;
- `git diff --check`.

Because this phase should be limited to Compose/tooling/documentation, regenerate `graphify-out` only if repository rules or an unexpected structural source change require it. Do not hand-edit generated Graphify output. If no regeneration is required, record why.

If Docker, a browser, the official target image, Redis Insight, or another dependency is unavailable, do not claim the corresponding gate passed. Record the attempted command, sanitized result, cause, and unverified criteria. A listening port alone is not proof of a successful UI smoke test.

## Rollback and cleanup

Rollback must be immediate and independent of Redis server data:

1. Stop the target Redis Insight container.
2. Restore the previous explicit image/configuration.
3. Start the legacy image with the untouched legacy tooling volume on its compatible configuration.
4. Verify the legacy UI starts and any intentionally preserved connection metadata remains available.
5. Confirm Redis and WebApi were unaffected throughout.

Preserve:

- legacy image reference/digest;
- legacy container configuration;
- legacy volume;
- any approved secure export/backup;
- target image reference/digest;
- target volume until review is complete;
- a sanitized connection-recreation procedure.

Do not delete the legacy or target tooling volume, export, image, or container state without explicit user authorization. If the user later approves cleanup, verify exact resource names and ownership before deletion and report whether recovery remains possible.

## Scope boundaries

Do not perform work from adjacent or later phases:

- do not change or upgrade the Redis server, modules, persistence, ACL architecture, or data volume;
- do not restore RedisGraph or separate Redis Search/TimeSeries services;
- do not change application cache keys, serialization, TTL, invalidation, or fail-soft behavior;
- do not add production observability, dashboards, alerts, metrics, or APM;
- do not change WebApi health/readiness to depend on Redis Insight;
- do not upgrade PostgreSQL, RabbitMQ, Elasticsearch, Kibana, test tooling, or VSIX;
- do not implement Phase 19 test/coverage upgrades;
- do not add or update production NuGet packages;
- do not change public HTTP contracts or remove API v1;
- do not create EF migrations;
- do not delete legacy or target tooling volumes/settings;
- do not perform unrelated code, package, formatting, or documentation cleanup.

## Acceptance criteria

Phase 18 may be considered complete only when:

- Phase 15 and the authoritative accumulated checkout were verified;
- current official Redis Insight sources were consulted and recorded;
- a stable supported official image with explicit tag/digest was selected;
- the legacy image, volume, saved connections/settings, and secret-bearing state were inventoried safely;
- the user preservation requirement was confirmed or the clean-recreation decision was documented;
- the target runs with a new volume and never mounts the legacy volume;
- required connection metadata was imported or recreated without credentials in source;
- Redis Insight connects to the Phase 15 Redis 8 deployment using the approved authentication/TLS policy;
- the UI and required API/static assets were actually smoke-tested, or any unavailable UI gate is explicitly marked unverified and the phase is not overstated;
- expected cache, Search, and TimeSeries data categories can be inspected safely;
- target restart preserves intended tooling state;
- Redis and WebApi continue working when Redis Insight is stopped or absent;
- the application package graph, behavior, test discovery/counts, health semantics, and public OpenAPI remain unchanged;
- effective Compose no longer references removed legacy Redis services;
- the legacy image/volume remains intact and rollback was demonstrated;
- no secret values or sensitive exports were committed or exposed;
- no volume, saved setting, Redis data, or material tooling state was deleted without explicit authorization;
- all required validation gates actually pass;
- no Phase 19+ work was implemented.

## Documentation and delivery

Create:

`docs/refactoring/phase-18-report.md`

The report must include:

- an executive summary;
- proof that Phase 15 and the authoritative checkout were verified;
- the pre-change Redis Insight/Compose/volume inventory;
- sanitized saved-connection/settings inventory;
- the preservation decision and user clarification, if required;
- source/target version, image, digest, architecture, port, and data-path matrix;
- verification date and direct official source links;
- settings-format compatibility and migration/recreation decision;
- exact legacy and target container/volume identifiers;
- target startup, health/readiness, UI/API/static-asset results;
- Redis 8 connection and auth/TLS result without secret values;
- cache/Search/TimeSeries inspection results without payloads;
- restart-persistence result;
- proof that WebApi/Redis work with Redis Insight absent;
- Compose changes and proof removed service dependencies did not return;
- rollback demonstration and exact procedure;
- exact retained resources and approval-gated cleanup;
- confirmation that application code/package graph did not change, or a blocker if it did;
- tests/scripts added or updated;
- commands actually executed and sanitized actual results;
- final test counts and discovery comparison;
- OpenAPI comparison result;
- vulnerability-audit result;
- Graphify result or documented reason it was not required;
- residual risks and known limitations;
- gates not executed or blocked;
- explicit confirmation that no volume, saved setting, Redis data, or material tooling state was deleted;
- explicit confirmation that no Phase 19+ work was implemented.

Update `AGENTS.md`, `.env.example`, Compose/setup instructions, and validation scripts only with configuration-key names, commands, and results actually verified. Never include credentials or secret-bearing exports.

Upon completion, present:

1. An objective implementation summary.
2. The main Compose, image, volume, and documentation changes.
3. The selected Redis Insight version/digest and official sources.
4. The saved-settings preservation decision.
5. Target startup, UI, Redis 8 connection, data-inspection, and restart results.
6. Proof that application startup, health, package graph, tests, and OpenAPI remained unchanged.
7. Full validation results with actual counts.
8. Pending/blocked gates and residual risks.
9. The rollback procedure and exact retained legacy/target resources.
10. Whether cleanup needs separate authorization.
11. Confirmation that no secret was committed or exposed.
12. Confirmation that no volume or Redis data was deleted.
13. Confirmation that Phase 19 was not started.

Do not commit, push, merge, change branches, create another worktree, delete tooling resources/data, alter a production-like environment, or open a pull request without explicit authorization.
