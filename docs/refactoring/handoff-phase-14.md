# Phase 14 Execution Handoff

Read and fully execute Phase 14 of this repository's refactoring plan.

Main objective:
Establish a reproducible, current-major Docker Compose baseline before any service major upgrade. Validate the existing PostgreSQL bootstrap, pin every existing image to a verified compatible patch and/or immutable digest without changing service majors, make startup ordering deterministic with meaningful health checks, prove migrations and seeds are idempotent, and document only commands that were actually executed.

Do not merely analyze or produce a plan: implement, test, validate, and document Phase 14 until its acceptance criteria are met.

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
- `docs/refactoring/phase-10-report.md`;
- the actual reports for Phases 11, 12, and 13, if those phases have been executed;
- `docs/refactoring/handoff-phase-13.md` for the adjacent-phase boundary;
- every folder-specific `AGENTS.md` governing files you modify;
- the applicable repository skills, especially:
  - `package-upgrade`, because container image tags or digests will change;
  - `dotnet-change-verification` if any .NET, project, solution, or build file changes;
  - `integration-adapter` only if a minimal configuration correction is required to make an already-adopted adapter work in Compose;
  - `api-contract-change` only if an observable HTTP contract could change;
  - `architecture-boundary-check` only if project references, package references, or layer placement changes.

Treat `docs/refactoring/baseline-phase-6/` as the approved API baseline unless a later, explicitly approved phase report records a replacement baseline.

## Prerequisites and accumulated state

The Phase 3 plan lists Phases 1 and 7 as the formal dependencies of Phase 14. Adapter phases are not a technical prerequisite for validating unchanged service behavior. However, this repository is being executed as an accumulated staged refactoring, so work only from the authoritative checkout containing every prior phase the user has actually adopted and completed.

Before editing, verify from code, reports, tests, and Git state that:

- Phase 1 test-host isolation remains present;
- Phase 7 persistence behavior and PostgreSQL-backed tests remain present;
- no previously completed phase is missing from this checkout;
- the latest completed phase report records actual validation rather than planned work;
- the current Compose configuration matches the application configuration and adapter state produced by the completed phases;
- this is not an outdated worktree or branch that predates accumulated changes.

If the user is following the numbered phases sequentially and any adopted Phase 11–13 implementation or report is missing, stop and report the blocker. Do not skip it, recreate it inside Phase 14, or selectively copy files from another checkout.

A handoff file is not evidence that its phase was implemented. At the authoring checkpoint, the latest completed implementation report was Phase 10; the execution agent must determine the real state at execution time.

## Checkout and working-tree safety

Work directly in the authoritative checkout above.

The working tree may contain pre-existing user changes. Inventory them before editing and preserve them in full. Do not overwrite, normalize, or reformat unrelated files.

Do not:

- run `git reset`, `git checkout --`, `git clean`, or `git stash`;
- perform a destructive rebase or discard working-tree changes;
- commit, push, merge, change branches, create another worktree, or open a pull request without explicit authorization;
- delete or replace existing volumes, databases, queues, indices, snapshots, backups, or tooling state;
- use the repository's normal named volumes for a destructive rehearsal.

## Phase 14 scope

Implement the requirements under “Phase 14 — Establish a reproducible Compose baseline” in `docs/phase-3-implementation-plan.md`:

1. Validate `init-db.sh` against a clean, isolated PostgreSQL volume and prove that both configured databases are created without embedded credentials.
2. Pin every existing service image to a current compatible patch and/or immutable digest, removing mutable tags without changing any service major.
3. Add the health checks and dependency conditions required for deterministic startup.
4. Run application migrations and seeds twice and prove idempotency.
5. Execute and document startup, smoke-test, restart, shutdown, and non-destructive cleanup procedures.

This phase establishes the baseline for the service versions already present after all completed prior phases. It is not an upgrade phase for Redis, PostgreSQL, RabbitMQ, Elasticsearch, Kibana, Redis Insight, or any other service major.

## Current-state inventory before editing

Do not assume that the authoring snapshot still matches the execution-time repository or local Docker environment. Inventory the actual state first and record it in the Phase 14 report.

Inspect at least:

- `docker-compose.yml`, `docker-compose.override.yml`, any additional Compose files, profiles, fragments, and environment templates;
- `Src/Presentation/Template.WebApi/Dockerfile`, including every base/build image;
- every service, image reference, tag, digest, build context, profile, network, config, secret, port, volume, restart policy, health check, dependency condition, and startup command;
- the effective output of `docker compose config` using safe placeholder or ephemeral values;
- locally resolved image IDs, repository digests, architecture, and reported service versions before any pull or build;
- official registry provenance for each image;
- `init-db.sh`, its line endings, mount path, required variables, SQL quoting, error behavior, and idempotency;
- application and Identity database names and connection-string wiring by configuration-key name only;
- EF migration startup behavior for `ApplicationDbContext` and `IdentityContext`;
- product, role, and user seed behavior;
- WebApi startup and shutdown behavior;
- cache, messaging, search, logging, and health-check configuration used by Compose;
- all health endpoints and current dependency classifications established by Phase 13, if implemented;
- the current validation script and real-service integration projects;
- any existing operational documentation for Compose startup and rollback.

The authoring snapshot contains mutable or broad image references such as `postgres:16`, `rabbitmq:latest`, `redis:latest`, `redislabs/redisearch:latest`, module images without explicit tags, and .NET Dockerfile tags scoped only to `10.0`. It also has incomplete health/dependency coverage. Treat these only as leads to verify, not as an exhaustive or execution-time inventory.

Also verify known configuration risks instead of carrying them forward silently. For example, the Phase 9 report records that the Compose keys named `RabbitMQ__*` did not match the adopted `RabbitMqSettings__*` configuration section. Confirm the post-prerequisite state and correct only proven Compose wiring defects needed for the baseline. Do not redesign the adapter or its public behavior.

## Version and image research

Image information is time-sensitive. Before changing any image reference, use current official primary sources and official registries.

For each service or Dockerfile stage:

1. Record the existing reference and the locally resolved image ID/digest before pulling anything.
2. Determine the actual current major in the accumulated repository and data baseline.
3. Consult the vendor's official release notes, support policy, upgrade notes, and official image registry.
4. Select a stable, supported patch in the same major where a supported release exists, or pin the already resolved compatible artifact by immutable digest when changing the patch would create an upgrade rather than a reproducibility fix. For a pre-existing EOL component whose disposition belongs to Phase 15, preserve and pin the exact artifact instead of pretending it is supported.
5. Verify host architecture and image-manifest compatibility.
6. Record the selected tag, digest when available, verification date, direct official links, relevant compatibility evidence, and rollback reference.
7. Reject preview, beta, release-candidate, nightly, unsupported unofficial, floating major, and `latest` references.
8. Re-resolve the effective Compose configuration and verify that no mutable tag remains.

Do not select versions from memory or copy the historical Phase 2 version table as the current answer.

Do not change a service major in Phase 14. In particular:

- do not perform the Redis 8 consolidation from Phase 15;
- do not perform an optional PostgreSQL 18 migration from Phase 16;
- do not perform the RabbitMQ broker upgrade from Phase 17;
- do not upgrade Redis Insight from Phase 18;
- do not redo or extend the Elastic major migration from Phase 11.

If the local artifact behind a mutable tag is no longer obtainable by an immutable reference, or official guidance makes an in-major pin unsafe, stop and report the incompatibility rather than silently crossing a major boundary.

For legacy Redis module services scheduled for consolidation or removal in Phase 15, pin the exact existing compatible artifact if possible and document any unsupported/EOL status. Do not remove, consolidate, replace, or migrate those services in Phase 14.

## Isolated baseline environment

All clean-volume and restart validation must use an isolated Compose project that cannot collide with the user's normal environment.

Before starting it:

- choose a unique, explicit Compose project name;
- render and review the effective resource names;
- verify that its containers, networks, configs, and volumes are distinct from existing resources;
- use new isolated volumes or ephemeral storage;
- use distinct host ports when the normal ports are occupied;
- use synthetic credentials supplied through an untracked temporary environment file or process environment;
- keep real secret values out of commands, shell history where practical, logs, reports, screenshots, and committed files;
- verify that `.env` remains ignored and untracked and that `.env.example` contains placeholders only.

Do not attach the rehearsal to any pre-existing user volume, even read-only, unless the user explicitly authorizes that exact resource.

Do not run `docker compose down --volumes`, `docker volume rm`, `docker system prune`, or equivalent cleanup without explicit authorization. A unique test volume is still a volume: record its exact name and leave it intact unless deletion has been authorized. Non-destructive shutdown must be the default.

## PostgreSQL bootstrap and both databases

Validate `init-db.sh` against the pinned current-major PostgreSQL image and a genuinely new isolated volume.

Prove at least that:

- the script passes a shell syntax check in the environment that will execute it;
- required variables fail clearly when absent, without printing secret values;
- database identifiers are safely quoted;
- no username, password, connection string, or other real credential is embedded in the script;
- the script is mounted at the intended official initialization path;
- the application database is created;
- the Identity database is created;
- both databases are owned/accessed according to the documented configuration;
- connections to both databases succeed using the application configuration;
- rerunning the database-creation logic is idempotent;
- a container restart does not rerun or corrupt initialization;
- expected data remains available after restart.

Remember that official PostgreSQL entrypoint scripts run only when the data directory is initially empty. Test both the clean first initialization and the normal restart path. Do not claim that a restart re-executed `init-db.sh` unless evidence shows it did.

Do not rewrite an applied EF migration or manually edit migration designer/snapshot files.

## Health checks and deterministic startup

Add or correct health checks only where they materially support deterministic startup and useful diagnostics.

For every Compose service, determine:

- what “started” means;
- what “healthy/ready” means;
- whether a downstream service truly requires it;
- the appropriate bounded interval, timeout, retries, and start period;
- whether the chosen image actually contains the command used by the check;
- whether the check can run without exposing secrets;
- whether liveness/readiness behavior established in Phase 13 must be preserved.

Use `depends_on` conditions based on health only for genuine startup prerequisites. Do not serialize independent services unnecessarily. Avoid fixed sleeps when a bounded service-aware probe exists.

At minimum, evaluate PostgreSQL, Elasticsearch, Kibana, RabbitMQ, Redis, the legacy Redis module services, Redis Insight, and WebApi. Ensure the WebApi does not begin migration/seed work before both PostgreSQL databases can be reached. Preserve the adopted fail-soft policy for optional Redis, RabbitMQ, Elasticsearch search/logging, and other optional integrations; a Compose dependency must not accidentally make an optional application dependency mandatory unless that is the approved baseline.

Health checks must not:

- print credentials or connection strings;
- mutate application data;
- create queues, indices, or databases on every probe;
- depend on tools absent from the selected image;
- run indefinitely;
- turn transient optional-service loss into an undocumented public behavior change.

## Migrations and seed idempotency

Use the isolated environment to run the actual application startup path at least twice against the same Phase 14 volumes.

Prove for both `ApplicationDbContext` and `IdentityContext` that:

- all expected migrations apply successfully on the first run;
- the correct migration history exists in the correct database;
- the second run has no pending migration and does not rewrite an applied migration;
- there is no cross-wiring between the application and Identity databases;
- application startup does not race PostgreSQL readiness.

Prove seed idempotency with objective database queries or equivalent tests. Check stable counts and unique identifiers for the seeded products, roles, users, and role assignments that actually exist after the completed prerequisite phases. The second run must not create duplicates or corrupt existing seeded state.

Do not change seed content, production credentials, identity behavior, or relational schema merely to make the baseline test pass. If a real idempotency defect is found, make the smallest in-scope correction, add a regression test, document compatibility and rollback, and do not mix in a broader Phase 12, 13, or 16 redesign.

## Startup, smoke, restart, shutdown, and recovery validation

Execute the complete baseline with the isolated project and new volumes. Validate in sequence:

1. Effective Compose configuration renders successfully.
2. Required images are the recorded pinned artifacts.
3. Services start in a deterministic order without unbounded retries.
4. Every service expected to be healthy reaches health within a bounded time.
5. Both PostgreSQL databases exist and migrations/seeds are correct.
6. The WebApi becomes ready according to the adopted health model.
7. Representative HTTP smoke tests pass without changing the approved API contract.
8. Representative Redis cache, RabbitMQ messaging, Elasticsearch search/logging, and other currently active service paths behave as documented by the completed adapter phases.
9. Stop and restart the stack non-destructively using the same isolated volumes.
10. Services recover, required health returns, and expected durable data remains.
11. Run the application startup/migration/seed path again and re-check idempotency.
12. Shut down without deleting volumes and verify containers stop cleanly.

Smoke tests must be derived from the actual published API and current reports. Include the health endpoints, Swagger/OpenAPI documents, representative application and Identity database paths, and currently active integration paths. Do not invent new product behavior.

Capture useful bounded diagnostics for failures, but redact secrets and personal payloads. Do not paste complete environment dumps or inspect output containing credentials into the phase report.

## Public HTTP and OpenAPI compatibility

Phase 14 must preserve the public HTTP contract.

Do not change:

- v1 or v2 routes;
- methods, bindings, status codes, response shapes, or content types;
- authentication and authorization behavior;
- v1 deprecation;
- health routes or semantics established by Phase 13;
- the published OpenAPI contract.

Capture v1 and v2 OpenAPI from the validated environment and compare them exactly with the approved baseline. Normalize only known checkout line-ending differences for byte comparison; also perform a structural JSON comparison. Investigate every semantic difference. Do not silently update the approved baseline.

If deterministic startup cannot be achieved without an HTTP contract change, stop and present the evidence and migration options rather than implementing the change without approval.

## Required tests and validation evidence

Add or update automated checks proportionally to the Compose changes. Prefer deterministic scripts and integration tests over manual-only instructions.

At minimum, cover:

- Compose configuration rendering with all required variable names present;
- absence of mutable image tags across Compose and Dockerfile inputs;
- explicit image provenance/tag/digest checks;
- `init-db.sh` syntax and missing-variable behavior;
- clean-volume creation of both PostgreSQL databases;
- connectivity to both databases;
- migration history for both contexts;
- migration idempotency on the second startup;
- seed idempotency on the second startup;
- health transitions and startup dependency ordering;
- WebApi startup from a clean environment;
- representative HTTP and integration smoke tests;
- non-destructive restart with expected persisted data;
- graceful shutdown;
- exact OpenAPI comparison;
- direct and transitive NuGet vulnerability audit, even if no package is expected to change;
- verification that no service major changed.

Do not weaken or replace the existing Testcontainers suites. They remain useful isolated adapter checks but do not substitute for the full Compose baseline required by this phase.

Run .NET restore, build, and test commands sequentially because this checkout's shared intermediate outputs contend under parallel execution.

Use the pinned SDK:

`C:\Users\alexa\.dotnet\dotnet.exe`

Start with the smallest checks relevant to modified files. At the end, run the repository validation script:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-phase-validation.ps1
```

Confirm and record the actual result of every step, including:

- restore;
- WebApi build with warning/error count;
- architecture tests;
- unit tests;
- EF InMemory/adapter integration tests;
- real PostgreSQL integration tests;
- real RabbitMQ integration tests;
- real Elasticsearch integration tests for the current adopted stack;
- functional tests;
- package vulnerability audit;
- `docker compose config -q`;
- OpenAPI capture and comparison;
- `git diff --check` for the changed documentation and implementation files.

Run the full isolated Compose baseline separately because the existing validation script's `docker compose config -q` step alone does not prove startup, health, persistence, or restart behavior.

If files affecting project structure or code dependencies change, regenerate `graphify-out` only with the official Graphify command. Do not hand-edit generated Graphify files. If changes are limited to Compose, scripts, and documentation and no structural code change occurred, record why regeneration was not required. If regeneration is required, do not edit its output manually.

If Docker, a registry, a required image, or another external dependency is unavailable, do not claim the corresponding gate passed. Record the attempted command, the sanitized result, the observed cause, and every remaining unverified criterion. Phase 14 cannot be declared complete without an actually executed clean-volume Compose baseline.

## Rollback requirements

Before changing image references or Compose behavior, preserve:

- the original Compose and Dockerfile references;
- locally resolved image IDs and digests;
- the effective pre-change Compose configuration with secrets redacted;
- the existing normal environment and all its volumes untouched;
- the isolated Phase 14 project and volumes until validation and review are complete.

Rollback must be a coherent restoration of the previous explicit configuration and image references. Demonstrate that rollback does not require deleting or remounting user data.

Do not use an older server binary against data files written or upgraded by a newer major. Phase 14 must not change data formats, but the rollback procedure must still verify this invariant rather than assume it.

Do not delete old images, build cache, containers, networks, volumes, or snapshots as part of cleanup without explicit authorization. Document exact resource names and safe, non-destructive stop commands. Mark destructive cleanup as optional and approval-gated.

## Scope boundaries

Do not perform work from adjacent or later phases:

- do not consolidate Redis, Search, or TimeSeries;
- do not remove RedisGraph in this phase, even though its Phase 15 removal decision is already adopted;
- do not migrate Redis data or ACL behavior;
- do not upgrade PostgreSQL to another major;
- do not perform backup/restore or `pg_upgrade` work belonging to Phase 16 beyond the clean baseline proof needed here;
- do not upgrade RabbitMQ across server majors or change feature flags;
- do not migrate RabbitMQ definitions or discard queues/messages;
- do not upgrade Redis Insight;
- do not upgrade test/coverage packages;
- do not modernize the VSIX;
- do not implement final Phase 21 cleanup;
- do not remove API v1;
- do not redesign adapters, application boundaries, health semantics, logging, localization, identity, or public contracts;
- do not create an EF migration without a proven schema delta;
- do not hand-edit EF migration designer/snapshot files or generated `.resx` designers;
- do not perform unrelated package, code, formatting, or documentation cleanup.

## Acceptance criteria

Phase 14 may be considered complete only when:

- the authoritative accumulated checkout and prerequisites were verified;
- every Compose and Dockerfile image reference is inventoried;
- current official sources and registries were consulted and recorded;
- every selected image is stable, explicit, and compatible with the current major baseline;
- no mutable tag remains;
- no service major changed;
- `docker compose config -q` succeeds with safe configuration;
- `init-db.sh` passes syntax and behavior checks;
- a new isolated PostgreSQL volume creates both configured databases;
- both Application and Identity connections work;
- both EF contexts apply their expected migrations;
- migrations and seeds are idempotent on a second startup;
- health checks and dependency conditions produce deterministic bounded startup;
- all current-major services expected by the baseline become healthy from a clean environment;
- representative WebApi and integration smoke tests pass;
- a non-destructive restart preserves expected durable data and recovers health;
- shutdown completes cleanly without deleting volumes;
- the public HTTP/OpenAPI contract remains approved and unchanged;
- the complete sequential validation gate passes, or any unavailable gate is explicitly marked failed/skipped and the phase is not overstated as complete;
- normal user volumes, data, images, and external systems remain untouched;
- no work from Phase 15 or later was implemented.

## Documentation and delivery

Create:

`docs/refactoring/phase-14-report.md`

The report must include:

- an executive summary;
- proof of prerequisite and authoritative-checkout verification;
- the pre-change Compose, Dockerfile, image, volume, health, dependency, migration, and seed inventory;
- the current-major image compatibility matrix;
- old and selected tags/digests;
- verification date and direct official source links;
- confirmation that no service major changed;
- effective Compose configuration decisions with secrets redacted;
- the isolated project name and exact resource names;
- `init-db.sh` validation evidence;
- proof that both databases were created and reachable;
- migration history and second-run idempotency evidence for both contexts;
- seed counts/identifiers before and after the second run, without personal or secret data;
- health-check and startup-order decisions;
- startup, smoke, restart, persistence, shutdown, and recovery results;
- commands actually executed and sanitized actual results;
- final test counts;
- OpenAPI comparison result;
- vulnerability-audit result;
- Graphify result or the documented reason it was not required;
- residual risks and known limitations;
- an executable rollback procedure;
- isolated resources intentionally retained;
- gates not executed or blocked;
- explicit confirmation that no user volume or material data was deleted;
- explicit confirmation that no Phase 15+ work was implemented.

Update `AGENTS.md` or setup instructions only with commands and results actually executed and confirmed. Do not replace historical checkpoints with unverified expectations.

Upon completion, present:

1. An objective implementation summary.
2. The main files changed.
3. The pinned image/version matrix and official sources.
4. The Compose health and dependency model.
5. PostgreSQL bootstrap, database, migration, and seed-idempotency results.
6. Clean startup, smoke, restart, persistence, shutdown, and recovery results.
7. Full validation results with actual counts.
8. OpenAPI, vulnerability-audit, and Graphify results.
9. Pending or blocked gates.
10. Residual risks and the rollback procedure.
11. Exact isolated resources retained and whether cleanup requires approval.
12. Confirmation that no service major, public API contract, user volume, or external system was changed outside scope.
13. Confirmation that Phase 15 was not started.

Do not commit, push, merge, change branches, create another worktree, delete volumes or data, alter a production-like environment, or open a pull request without explicit authorization.
