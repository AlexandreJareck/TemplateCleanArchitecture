# Phase 16 Execution Handoff

Read and fully execute Phase 16 of this repository's refactoring plan.

Main objective:
Upgrade PostgreSQL independently and reversibly: first prove backup/restore and pin the latest supported PostgreSQL 16 patch for the current application and Identity databases; then migrate to PostgreSQL 18 only as a separate, explicitly authorized data operation. Validate extensions, collations, EF migrations, seeds, representative queries, restart persistence, and a timed rollback without changing persistence architecture or any other service.

Do not merely analyze or produce a plan: research, implement, rehearse, test, validate, and document the authorized portion of Phase 16 until its acceptance criteria are met.

## Required context

The authoritative repository is:

`C:\Users\alexa\source\repos\TemplateCleanArchNET`

Before changing any file, read in full:

- `AGENTS.md`;
- `docs/phase-2-workspace.md`;
- `docs/phase-3-implementation-plan.md`;
- `docs/refactoring/phases-0-5-report.md`;
- `docs/refactoring/phases-6-7-report.md`;
- all subsequent phase reports through the latest phase actually executed, including `docs/refactoring/phase-14-report.md`;
- `docs/refactoring/handoff-phase-14.md` for the reproducible Compose baseline;
- `docs/refactoring/handoff-phase-15.md` and `docs/refactoring/phase-15-report.md` if Phase 15 has been executed in the accumulated sequence;
- every folder-specific `AGENTS.md` governing files you modify;
- the applicable repository skills, especially:
  - `package-upgrade`, because the PostgreSQL image patch or major may change;
  - `ef-migration` only if a real application schema delta is independently proven; a server upgrade alone is not an EF model change;
  - `dotnet-change-verification` if any .NET, project, solution, or build file changes;
  - `integration-adapter` only if a minimal Npgsql/connection configuration correction is required for verified server compatibility;
  - `architecture-boundary-check` only if project references, package references, or layer placement changes;
  - `api-contract-change` only if an observable HTTP contract could change.

Treat `docs/refactoring/baseline-phase-6/` as the approved API baseline unless a later, explicitly approved phase report records a replacement baseline.

## Prerequisites and authorization gates

The formal Phase 16 dependencies are Phases 7 and 14.

Before editing, verify from actual code, reports, tests, Compose configuration, images, and database behavior that:

- Phase 7 persistence behavior is present, including explicit PostgreSQL/Npgsql configuration, EF migrations, deterministic pagination, transaction behavior, cancellation, decimal precision, and PostgreSQL-backed integration tests;
- Phase 14 is complete, with an actual `docs/refactoring/phase-14-report.md` covering pinned images, clean-volume startup, both databases, migrations/seeds, health, restart persistence, and rollback;
- the current Compose/database configuration matches the Phase 14 report;
- every prior phase the user adopted in the accumulated sequence remains present;
- if Phase 15 was executed before this phase, its Redis changes and report are present and must remain untouched;
- this is the authoritative checkout, not an outdated branch or worktree;
- the latest documented validation checkpoint is consistent with current files.

If a formal prerequisite or accumulated prior-phase state is missing, stop and report the blocker. Do not recreate it inside Phase 16 or selectively copy files from another checkout.

### Mandatory PostgreSQL 16 checkpoint

The mandatory and adopted Phase 16 path is:

1. Back up and restore both databases in an isolated rehearsal environment.
2. Verify and pin the latest currently supported PostgreSQL 16 patch.
3. Validate the entire application against PostgreSQL 16.
4. Preserve the verified PostgreSQL 16 environment as the rollback baseline.

If Phase 14 already pinned the same latest supported PostgreSQL 16 patch, verify that conclusion from current official sources and record that no patch change was necessary. The backup/restore rehearsal and database validation are still required.

### Optional PostgreSQL 18 checkpoint

PostgreSQL 18 is a separate opt-in data migration. Do not infer authorization from this handoff, from the user's request to execute Phase 16 generally, or from the historical plan.

Proceed beyond the PostgreSQL 16 checkpoint only if the user explicitly authorizes PostgreSQL 18 after being told:

- the exact source and proposed target versions;
- the officially supported migration methods;
- the data copies and environments that will be used;
- expected downtime/cutover implications;
- extension, collation, and compatibility findings;
- the rollback point and preserved PostgreSQL 16 resources;
- any blocked or unverified prerequisite.

Without explicit PostgreSQL 18 authorization, do not pull or start PostgreSQL 18 for migration, do not change the default image to 18, and do not write to a PostgreSQL 18 data directory. Complete and report the PostgreSQL 16 checkpoint, mark the PostgreSQL 18 operation as not authorized/not executed, and do not describe it as passed.

## Checkout and working-tree safety

Work directly in the authoritative checkout above.

The working tree may contain pre-existing user or concurrent-agent changes. Inventory and preserve them in full. Do not overwrite, normalize, or reformat unrelated files.

Do not:

- run `git reset`, `git checkout --`, `git clean`, or `git stash`;
- perform a destructive rebase or discard working-tree changes;
- commit, push, merge, change branches, create another worktree, or open a pull request without explicit authorization;
- delete or overwrite database volumes, databases, schemas, tables, migration history, backups, WAL, snapshots, or other material data;
- point a rehearsal at the user's only copy of any database;
- use an older PostgreSQL server with a data directory written or upgraded by a newer major.

## Phase 16 scope

Implement the requirements under “Phase 16 — Upgrade PostgreSQL independently” in `docs/phase-3-implementation-plan.md`:

1. Back up and restore both the application and Identity databases into an isolated rehearsal environment.
2. Pin and validate the latest supported PostgreSQL 16 patch without changing the data format major.
3. Only if PostgreSQL 18 is explicitly approved, choose `pg_upgrade` or logical backup/restore from current official guidance and rehearse it on copies.
4. Run EF migrations, repository integration tests, seed idempotency, extension/collation checks, and representative query-plan checks.
5. Record and test the exact rollback point before switching any application connection.

Do not mix persistence architecture refactoring, EF model redesign, schema feature work, or other service upgrades into this phase.

## Current-state inventory before editing

Treat the actual post-Phase-14 accumulated state as authoritative. Inventory and record at least:

- PostgreSQL Compose service, image tag/digest, runtime version, architecture, command, environment keys, health check, dependencies, ports, network, restart policy, and volume;
- `init-db.sh` and how it creates the application and Identity databases;
- application and Identity database names by configuration-key name only;
- effective connection-string options with credentials and hosts redacted where necessary;
- `ApplicationDbContext`, `IdentityContext`, their design-time factories, and all migrations;
- Npgsql and EF Core package versions and their supported PostgreSQL ranges;
- startup migration and seed behavior;
- `__EFMigrationsHistory` contents for both databases;
- schemas, owners, roles, grants, default privileges, encodings, locales, collation providers/versions, character classification settings, and time zones;
- installed extensions and their versions in each database;
- custom types, sequences, generated columns, indexes, constraints, triggers, functions, large objects, and other features that can affect migration;
- row counts and safe integrity checks for representative tables without exposing personal or secret data;
- database sizes and backup/restore capacity constraints;
- persistence, repository, Identity, functional, and Testcontainers coverage;
- representative application queries and known query plans;
- existing backup, restore, retention, cutover, and rollback documentation;
- all existing PostgreSQL volumes, backups, snapshots, containers, and ports in the in-scope local environment.

Do not print or copy passwords, connection strings, password hashes, security stamps, tokens, seeded credentials, or personal data into logs, command transcripts, reports, fixtures, or documentation.

Record a sanitized pre-change inventory in `docs/refactoring/phase-16-report.md`.

## Version and compatibility research

PostgreSQL and image information is time-sensitive. Do not select versions from memory or use the historical Phase 2 table as current authorization.

Before changing an image or package, consult current official primary sources, including as applicable:

- PostgreSQL release notes and supported-version policy;
- the latest PostgreSQL 16 minor release notes;
- PostgreSQL minor-upgrade documentation;
- PostgreSQL 18 release notes and migration guidance, only for planning or after explicit opt-in for execution;
- `pg_upgrade` documentation and supported source/target paths;
- `pg_dump`, `pg_restore`, `pg_dumpall`, and backup/restore documentation;
- collation-version and locale documentation;
- extension upgrade documentation for every installed non-core extension;
- official PostgreSQL container image tags, manifests, and data-directory notes;
- official Npgsql EF Core provider compatibility documentation;
- official EF Core migration guidance.

For every selected image/package, record:

- current repository/runtime version;
- selected version;
- verification date;
- direct official source URL;
- compatibility/support evidence;
- relevant breaking or behavioral changes;
- required migration action;
- rollback action.

Use only stable supported releases and explicit tags/digests. Do not introduce `latest`, floating major/minor tags, previews, unofficial images, or an unverified architecture manifest.

Do not update Npgsql, EF Core, or unrelated package families merely because a newer version exists. If a package change is genuinely required for the authorized PostgreSQL target, update the smallest coherent supported family, document breaking changes, run direct/transitive vulnerability audits, and preserve a coherent package rollback.

## Data safety and isolated environments

Treat both databases, their backups, and their volumes as material user data.

All backup, restore, patch, and optional major-migration rehearsals must use isolated resources with:

- a unique project/environment name;
- explicit source and target container names;
- distinct host ports;
- new target volumes;
- copied or synthetic data;
- synthetic credentials supplied outside version control;
- bounded timeouts and disk-space checks;
- exact resource names recorded before any operation.

Never:

- mount the user's normal PostgreSQL volume into a rehearsal target;
- run `pg_upgrade` against the only source volume;
- use `--link` or another mode that jeopardizes the untouched rollback copy;
- attach PostgreSQL 16 to PostgreSQL 18 data files;
- attach PostgreSQL 18 directly to PostgreSQL 16 data files unless the officially selected upgrade procedure explicitly operates on verified copies and supports that exact step;
- overwrite a backup or target database whose identity has not been verified;
- drop a database, schema, table, or volume without explicit authorization;
- run broad Docker cleanup or prune commands.

If real/user-owned data is required, stop and obtain explicit authorization before reading, copying, exporting, restoring, analyzing, or migrating it. If only synthetic data is used, state that clearly and do not claim real-data migration compatibility.

Leave isolated volumes and backups intact by default. Cleanup that deletes material resources is approval-gated.

## Backup and restore rehearsal for both databases

Before any image switch, create a consistent recovery point for both the application and Identity databases using an officially supported method.

The backup plan must account for:

- both database contents and schemas;
- migration history;
- roles, ownership, grants, and required global objects;
- extensions and required server packages;
- encoding, locale, collation provider/version, and time-zone assumptions;
- large objects and other used PostgreSQL features;
- transaction consistency and the relationship between the two databases;
- backup format, tool version, encryption/storage policy if applicable, and retention location;
- available disk space and bounded execution time;
- secret-free diagnostics.

Do not count a successful backup command as proof of recoverability. Restore both databases into a new isolated PostgreSQL 16 environment and verify:

- restore completion without ignored errors;
- owners, roles, grants, schemas, extensions, and migration history;
- representative row counts and safe integrity checks;
- seeded products, roles, users, and relationships without exposing sensitive values;
- representative repository and Identity operations;
- application startup, migrations, and seeds;
- restart persistence;
- repeatability of the documented restore procedure.

Record backup identifiers, checksums where appropriate, tool/server versions, start/end times, sizes, and restore results without recording secret values or data payloads.

## PostgreSQL 16 patch checkpoint

Patch PostgreSQL 16 before considering PostgreSQL 18.

For a minor update within PostgreSQL 16:

- verify the official minor-upgrade procedure and image data-directory compatibility;
- preserve the Phase 14 PostgreSQL 16 image reference and untouched rollback volume;
- rehearse the selected patch against a copy/restore, not the only source;
- review release notes for catalog, planner, collation, extension, replication, and security changes relevant to the repository;
- verify installed extensions and collation versions before and after;
- start the patched server and inspect logs for upgrade warnings;
- run both EF contexts, migrations, seeds, repository/Identity tests, representative queries, and restart checks;
- pin the selected patch to an explicit tag/digest only after acceptance passes.

If Phase 14 already uses the selected patch, do not manufacture a version change. Still prove backup/restore, compatibility, restart, and rollback readiness.

The PostgreSQL 16 source environment, old explicit image reference, verified backups, and rollback configuration must remain available after the checkpoint.

## Optional PostgreSQL 18 migration

This section is executable only after explicit user approval for PostgreSQL 18.

### Migration-method decision

Choose between logical backup/restore and `pg_upgrade` using current official guidance and the actual inventory. Document why the selected method is appropriate for:

- the exact PostgreSQL 16 source and PostgreSQL 18 target patches;
- both databases and required global objects;
- installed extensions;
- collations/locales;
- platform and container architecture;
- database size and downtime constraints;
- rollback requirements.

Do not assume a direct path or tool mode is supported. Do not use a filesystem copy of a live data directory as a logical backup.

### Parallel rehearsal

Rehearse the authorized migration using verified copies:

1. Confirm the PostgreSQL 16 source backup and restore are valid.
2. Create new isolated PostgreSQL 18 containers and volumes.
3. Install/verify compatible extension versions on the target.
4. Execute the officially supported migration procedure.
5. Resolve collation-version actions according to official guidance; do not refresh collation metadata blindly without checking affected indexes and reindex requirements.
6. Verify roles, owners, grants, schemas, extensions, migration history, row counts, constraints, sequences, and safe integrity checks.
7. Run application startup, both EF contexts, migrations, seeds, repository tests, Identity tests, and representative functional flows.
8. Compare representative query results and plans.
9. Restart PostgreSQL 18 and verify persistence/recovery.
10. Demonstrate rollback by reconnecting to the untouched PostgreSQL 16 environment.

Do not attempt rollback by starting PostgreSQL 16 against PostgreSQL 18 data files.

### Cutover gate

Before switching the application's default connection to PostgreSQL 18, record:

- exact source and target identifiers;
- the final verified backup/recovery point;
- migration start and completion times;
- the latest acceptable data-loss point, if any;
- downtime and write-freeze assumptions;
- go/no-go criteria;
- connection/configuration switch;
- health/readiness verification;
- timed switch-back procedure;
- the person authorized to proceed, as represented by the user's explicit instruction in the task.

Do not perform a production-like cutover or change a remote environment without separate explicit authorization for that environment.

## EF migrations and seeds

The server patch or major upgrade does not by itself justify a new EF migration.

For both `ApplicationDbContext` and `IdentityContext`:

- list expected and applied migrations;
- verify no pending model change using supported tooling where available;
- apply existing migrations to empty restored targets;
- run startup a second time and prove migration idempotency;
- verify migration history belongs to the correct database;
- run seeds twice and prove stable counts/identifiers with no duplicates;
- preserve PostgreSQL-specific column types, decimal precision, indexes, constraints, and defaults;
- inspect generated SQL only if a real new migration is independently required.

Do not create a migration for server-version metadata, collation refresh, or image changes.

If an actual relational schema delta is discovered, stop and determine whether it is an existing drift/defect or authorized Phase 16 work. Use the `ef-migration` skill, generate an additive migration through EF tooling, review SQL, and document forward/rollback behavior. Never rewrite an applied migration or hand-edit migration designer/snapshot files.

## Extensions, collations, locales, and encoding

Inventory and compare source, restored, patched, and—if authorized—PostgreSQL 18 environments.

Validate at least:

- database encoding;
- locale and locale provider;
- database and column collations actually used;
- recorded and actual collation versions;
- installed extensions and versions;
- extension availability on the target image;
- indexes whose semantics can depend on collation;
- case/ordering behavior used by representative queries;
- time-zone configuration and timestamp behavior.

Do not ignore collation-version warnings. Do not refresh version metadata or reindex user data without understanding the required operation and obtaining authorization for destructive/material data changes.

If an extension has no compatible target version, stop the optional PostgreSQL 18 migration and report the blocker. Do not silently remove the extension or its dependent objects.

## Representative queries and query plans

Derive representative queries from actual repositories, Identity flows, and PostgreSQL integration tests. Include at least:

- product lookup by identifier;
- deterministic product pagination/filtering;
- create/update/delete transaction paths;
- decimal precision round trip;
- concurrency behavior covered by the repository suite;
- Identity user lookup/authentication-related database access that can be exercised safely;
- migration-history and seed-verification queries.

Compare result correctness first. Capture `EXPLAIN` plans and use `EXPLAIN ANALYZE` only for safe read-only queries in isolated copied data. Do not execute write plans or expensive queries against user data merely for benchmarking.

Record significant plan shape, index usage, estimates, execution time under controlled conditions, and any regression requiring investigation. Do not claim performance equivalence from tiny synthetic data alone.

## Startup, health, restart, and failure behavior

Validate the database service and WebApi in the isolated target environment:

- PostgreSQL health check uses a bounded, non-secret probe;
- WebApi waits for database readiness before migrations/seeds;
- both database connections succeed;
- application health/readiness behavior matches Phase 13/14 decisions;
- initial unavailability does not create migration corruption;
- a non-destructive database restart preserves data and the application recovers according to policy;
- shutdown completes cleanly;
- logs and health output contain no credentials, raw connection strings, hashes, stamps, tokens, or personal data.

Do not broaden this phase into a redesign of readiness/liveness or connection resilience.

## Public HTTP and OpenAPI compatibility

Phase 16 must not change the public HTTP contract.

Preserve:

- v1 and v2 routes;
- methods, bindings, status codes, response shapes, and content types;
- authentication and authorization behavior;
- API v1 deprecation;
- health routes and semantics established in prior phases;
- published OpenAPI.

Run representative application and account flows against the patched PostgreSQL 16 environment and, if authorized, the PostgreSQL 18 target. Capture v1/v2 OpenAPI and compare exactly with the approved baseline. Investigate every semantic difference; do not silently update the baseline.

If server compatibility appears to require a public contract change, stop and present evidence and migration choices before implementing it.

## Required tests

Add or update tests proportionally to the authorized database change.

### Backup/restore tests

Prove for both databases:

1. Backup completes without ignored errors.
2. Backup artifacts are identifiable and intact.
3. Restore succeeds into a new isolated server.
4. Roles/owners/grants required by the application are correct.
5. Extensions, schemas, migration history, sequences, constraints, and indexes are present.
6. Representative row counts and safe integrity checks match.
7. Application and Identity operations work against the restore.
8. Restart preserves restored data.

### PostgreSQL-backed integration tests

Run the current repository/persistence suite against the selected PostgreSQL 16 patch and, only if authorized, the PostgreSQL 18 target. Cover:

- migrations from empty state;
- no pending model changes where supported;
- CRUD;
- deterministic pagination and ordering;
- decimal precision;
- explicit transaction commit/rollback;
- concurrency and cancellation;
- both database contexts;
- seed idempotency;
- representative query correctness.

Do not treat EF InMemory as PostgreSQL equivalence.

### Functional tests

Run account/authentication and product flows that exercise both databases. Confirm startup, migrations, seeds, reads/writes, transaction behavior, 401/403 behavior, and service restart recovery without changing public responses.

### Optional PostgreSQL 18 tests

When and only when authorized, add/execute target-specific migration, extension/collation, query-plan, restart, and switch-back tests. Keep them clearly separated from the mandatory PostgreSQL 16 checkpoint so an unapproved 18 migration cannot run accidentally.

## Validation

Run restore, build, and tests sequentially because this checkout's shared intermediate outputs contend under parallel execution.

Use the pinned SDK:

`C:\Users\alexa\.dotnet\dotnet.exe`

Start with the smallest persistence, PostgreSQL, Identity, and functional checks relevant to the change.

At the end, run the repository validation script:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-phase-validation.ps1
```

Confirm and record the actual results of all applicable gates, including:

- restore;
- WebApi build with warning/error count;
- architecture tests;
- unit tests;
- EF InMemory/adapter integration tests;
- PostgreSQL-backed integration tests against the selected 16 patch;
- PostgreSQL 18 integration/migration tests only if explicitly authorized;
- RabbitMQ, Elasticsearch, and Redis real-service suites for their current adopted versions;
- functional tests, especially product and account flows;
- backup and restore rehearsal for both databases;
- migration and seed idempotency;
- extension/collation/locale comparison;
- representative query results/plans;
- restart persistence and recovery;
- timed rollback/switch-back rehearsal;
- direct and transitive NuGet vulnerability audit;
- `docker compose config -q`;
- OpenAPI capture and exact comparison;
- `git diff --check`.

Because image/configuration changes may not alter code structure, regenerate `graphify-out` only when repository rules or structural source changes require it. Do not hand-edit generated Graphify output. If regeneration is not required, record why.

If Docker, PostgreSQL, backup tooling, required extensions, or another dependency is unavailable, do not report the corresponding gate as passed. Record the command attempted, sanitized result, cause, and unverified criteria. Do not substitute an unexecuted checklist for proof.

## Rollback requirements

The rollback path must be executable, timed, and must not rely on data-directory downgrade.

Preserve until the phase is accepted:

- the untouched PostgreSQL 16 source environment and volume;
- the Phase 14 and patched-16 explicit image references/digests as applicable;
- verified backups for both databases and required global objects;
- restore instructions and checksums/identifiers;
- previous package versions if any package changed;
- previous connection configuration;
- a documented and tested switch back to the PostgreSQL 16 environment.

For the mandatory 16 patch checkpoint, demonstrate restoring/switching back to the recorded 16 baseline without deleting data.

For an authorized PostgreSQL 18 migration, rollback means stopping writes/cutover safely and reconnecting the application to the untouched PostgreSQL 16 environment or a verified PostgreSQL 16 restore at the recorded rollback point. It never means starting PostgreSQL 16 on PostgreSQL 18 data files.

Measure and record rollback timing under the isolated rehearsal conditions. State the limitations of that timing; do not present a synthetic rehearsal as a production recovery-time guarantee.

Do not delete source/target volumes, backups, containers, images, or migration artifacts without explicit authorization. Document exact retained resources and approval-gated cleanup.

## Package, image, and security audit

After the authorized changes:

- verify runtime server version and image digest;
- verify no mutable PostgreSQL image tag remains;
- verify extension versions and target availability;
- inspect direct and transitive NuGet package graphs;
- run the vulnerability audit;
- confirm no preview or unsupported package/image was introduced;
- confirm unrelated package families and service images were untouched;
- verify `.env` remains ignored/untracked and `.env.example` contains placeholders only;
- ensure backup/report artifacts do not contain credentials or personal data.

## Scope boundaries

Do not perform work from adjacent or later phases:

- do not execute PostgreSQL 18 without explicit user authorization;
- do not redesign repositories, DbContexts, unit-of-work semantics, entity mappings, or persistence boundaries;
- do not add product features or schema changes unrelated to server compatibility;
- do not upgrade Redis, RabbitMQ, Elasticsearch, Kibana, Redis Insight, test tooling, or VSIX;
- do not implement RabbitMQ Phase 17 work;
- do not update unrelated package families;
- do not change public HTTP contracts or remove API v1;
- do not create an EF migration without a proven relational model delta;
- do not rewrite applied migrations or hand-edit migration designer/snapshot files;
- do not refresh collations, reindex user data, drop extensions, or remove database objects without explicit authorization;
- do not delete old volumes, backups, databases, or material data;
- do not perform unrelated formatting, cleanup, or documentation rewrites.

## Acceptance criteria

### Mandatory PostgreSQL 16 acceptance

The mandatory portion of Phase 16 may be considered complete only when:

- Phases 7 and 14 and the authoritative accumulated checkout were verified;
- current official PostgreSQL sources were consulted and recorded;
- the latest supported PostgreSQL 16 patch was selected and pinned explicitly, or the existing Phase 14 pin was proven current;
- both application and Identity databases were backed up and successfully restored into an isolated PostgreSQL 16 environment;
- roles, owners, grants, schemas, extensions, collations/locales, migration history, sequences, constraints, and representative data were verified;
- both EF contexts connect and existing migrations apply from empty state;
- migrations and seeds are idempotent on a second startup;
- repository, Identity, PostgreSQL integration, and functional tests pass;
- representative query results are correct and query plans have no unexplained critical regression;
- restart preserves expected data and application recovery works;
- the rollback procedure to the preserved PostgreSQL 16 baseline was executed and timed;
- the public HTTP/OpenAPI contract remains unchanged;
- normal user volumes/data and unrelated services remain untouched;
- all required validation gates actually pass;
- no Phase 17+ work was implemented.

### Additional PostgreSQL 18 acceptance, only if authorized

Do not claim PostgreSQL 18 acceptance unless all of the following are true:

- explicit user authorization was obtained and recorded;
- the exact supported 16-to-18 migration path was verified from official sources;
- the migration used copies/new target volumes and never endangered the untouched 16 source;
- both databases and required global objects migrated successfully;
- extensions and collations are compatible or remediated through reviewed supported steps;
- EF migrations/seeds, repository/Identity/functional tests, representative queries/plans, restart, and recovery pass on PostgreSQL 18;
- cutover and timed switch-back were rehearsed;
- the PostgreSQL 16 environment and verified backups remain recoverable;
- no older server was pointed at PostgreSQL 18 data files.

Without explicit authorization, state “PostgreSQL 18 not authorized and not executed.” This is the correct completion state for the adopted PostgreSQL 16 default, not a passed PostgreSQL 18 gate.

If real data was not available, explicitly state that real-data migration compatibility remains unverified. Do not infer it from synthetic data.

## Documentation and delivery

Create:

`docs/refactoring/phase-16-report.md`

The report must include:

- an executive summary;
- proof of prerequisite and authoritative-checkout verification;
- explicit PostgreSQL 18 authorization status;
- the pre-change database/image/configuration inventory;
- source/target version and digest matrix;
- verification date and direct official source links;
- minor/major compatibility findings and relevant release-note changes;
- both-database schema, role, grant, extension, collation/locale, encoding, size, and migration-history inventory;
- backup method, artifact identifiers/checksums, sizes, timings, and secret-safe storage location;
- restore rehearsal steps and results for both databases;
- whether copied real data or synthetic data was used;
- selected PostgreSQL 16 patch result;
- PostgreSQL 18 method, rehearsal, and result only if authorized;
- EF migration/no-pending-change and seed-idempotency evidence;
- representative row-count/integrity comparisons without sensitive data;
- representative query-result and plan comparisons;
- startup, health, restart, shutdown, and recovery results;
- cutover/switch-back result where applicable;
- timed rollback procedure and limitations;
- exact retained volumes, backups, images, and containers;
- packages/images changed and confirmation unrelated families were untouched;
- tests added or updated;
- commands actually executed and sanitized actual results;
- final test counts;
- OpenAPI comparison result;
- vulnerability-audit result;
- Graphify result or documented reason it was not required;
- residual risks and known limitations;
- gates not executed or blocked;
- explicit confirmation that no volume, backup, database, or material data was deleted;
- explicit confirmation that no Phase 17+ work was implemented.

Update `AGENTS.md`, `.env.example`, setup/backup instructions, and validation scripts only with configuration-key names, commands, and results actually verified. Never include secret values.

Upon completion, present:

1. An objective implementation summary.
2. The main files, images, packages, and configuration changed.
3. The PostgreSQL 16 version/digest and official compatibility sources.
4. Backup/restore results for both databases.
5. Extension, collation/locale, migration, seed, and query-plan results.
6. PostgreSQL 18 authorization status and, only if authorized, migration results.
7. Startup, restart, persistence, and recovery results.
8. Full validation results with actual counts.
9. OpenAPI, vulnerability-audit, and Graphify results.
10. Pending/blocked gates and real-data limitations.
11. The timed rollback/switch-back procedure and exact retained resources.
12. Confirmation that no older server was pointed at newer-major data.
13. Confirmation that no material data was deleted and Phase 17 was not started.

Do not commit, push, merge, change branches, create another worktree, delete volumes/backups/data, alter a production-like environment, or open a pull request without explicit authorization.
