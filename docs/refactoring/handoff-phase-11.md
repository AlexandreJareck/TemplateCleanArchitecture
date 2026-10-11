# Phase 11 Execution Handoff

Read and fully execute Phase 11 of this repository's refactoring plan.

Main objective:
Upgrade the already isolated Elastic integration from the current 8.x stack to a verified, mutually compatible 9.x set consisting of the official .NET client, Serilog sink, Elasticsearch server, and Kibana. Rehearse the server and data migration against copied data, validate templates and health behavior, and preserve a tested rollback path.

Do not merely analyze or propose a plan: research the current supported upgrade path, implement the upgrade, rehearse it safely, test it, validate it, and document Phase 11 until all acceptance criteria are met.

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
- `docs/refactoring/handoff-phase-10.md`
- Any folder-specific `AGENTS.md` files governing files you modify
- The applicable repository skills, especially:
  - `package-upgrade`
  - `integration-adapter`
  - `dotnet-change-verification`
  - `architecture-boundary-check`
  - `api-contract-change`, only if an HTTP contract genuinely needs to change

Treat the following as the approved API baseline:

- `docs/refactoring/baseline-phase-6/`

## Prerequisite: Phase 10 must be complete

Before starting, verify from the repository's actual state that Phase 10 is genuinely complete.

Confirm at least that:

- Application contains no Elastic package reference;
- Application contains no Elasticsearch client construction or vendor-specific settings;
- the search/indexing port is owned by Application;
- the Elasticsearch adapter resides in Infrastructure;
- search/indexing, Serilog transport, and health-check configuration are separated;
- WebApi and functional tests start without Elasticsearch;
- current-version 8.x adapter tests pass;
- the HTTP/OpenAPI contract is preserved;
- Phase 10 architecture exceptions have been removed;
- `docs/refactoring/phase-10-report.md` records actual green results and any unresolved gates.

If Phase 10 is missing, incomplete, or not present in the current checkout, do not mix Phase 10 boundary work with the Phase 11 major upgrade. Objectively record the blocker and stop.

## Checkout and working tree

Work only in the checkout containing the accumulated and authoritative state from Phases 0–10.

Before editing:

- verify that the scripts, skills, baselines, reports, tests, and implementation from the previous phases exist in this checkout;
- do not use a worktree based on an older commit that lacks the accumulated changes;
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

## Scope of Phase 11

Implement the requirements defined under “Phase 11 — Upgrade the Elastic client and server stack” in `docs/phase-3-implementation-plan.md`:

1. Re-query mutually compatible releases of the official .NET client, Serilog sink, Elasticsearch, Kibana, and related health-check integration using current official sources.
2. Inventory and snapshot the existing indices, templates, aliases, mappings, settings, and data streams, then run the supported deprecation and upgrade checks.
3. Migrate the Infrastructure adapter using the official .NET client 9.x breaking-change guidance.
4. Rehearse the supported server upgrade or reindex path in a parallel environment using copied, non-production data.
5. Pin explicit compatible Elasticsearch and Kibana image versions and validate component templates, index templates, data streams, indices, aliases, mappings, search, indexing, logging, and health checks.

This phase includes the Elastic client and server major upgrade. It does not include Application boundary redesign or unrelated observability work.

## Research requirements

Version and compatibility information is time-sensitive. Do not select versions from memory or rely solely on versions recorded in the repository documentation.

Before editing package or image versions, consult current official primary sources, including as applicable:

- Elastic Elasticsearch release notes;
- Elastic Kibana release notes;
- the official Elastic Stack compatibility policy or support matrix;
- the official Elasticsearch upgrade documentation;
- the official Upgrade Assistant documentation;
- the official snapshot and restore documentation;
- the official reindex and remote reindex documentation;
- official Elasticsearch breaking changes and migration guidance;
- official .NET client release notes and 9.x breaking changes;
- official `Elastic.Clients.Elasticsearch` NuGet metadata;
- official `Elastic.Serilog.Sinks` documentation, releases, and NuGet metadata;
- official health-check package documentation and compatibility information;
- official container image registries and tags for Elasticsearch and Kibana.

Record for every selected component:

- current repository version;
- selected target version;
- verification date;
- official source URL;
- compatibility evidence;
- relevant breaking changes;
- migration action required;
- rollback action.

Do not assume that all components should use the same patch number unless the official compatibility guidance requires it.

Use only stable releases. Do not adopt preview, alpha, beta, release-candidate, nightly, or snapshot builds.

## Current-state inventory

Before changing versions, inventory the actual post-Phase-10 implementation, including:

- `Elastic.Clients.Elasticsearch` version;
- `Elastic.Serilog.Sinks` version;
- Elasticsearch health-check package and implementation;
- Elasticsearch container image and resolved version;
- Kibana container image and resolved version;
- client configuration;
- authentication and TLS configuration;
- search/indexing options;
- Serilog sink options;
- health-check options;
- indices;
- aliases;
- component templates;
- index templates;
- mappings;
- index settings;
- ingest pipelines, if any;
- lifecycle policies, if any;
- data streams;
- Serilog data-stream naming;
- document formats;
- current adapter queries and bulk operations;
- current startup and unavailable-service behavior;
- Compose volumes and networks;
- integration and functional tests.

Do not assume that the pre-Phase-10 filenames or structure still apply. Treat the actual repository state as authoritative.

Record the inventory in the final report without copying credentials, tokens, API keys, connection strings, personal data, or full document payloads.

## Upgrade-path decision

Determine the officially supported path from the current 8.x deployment to the selected 9.x target.

Do not assume a direct upgrade is supported.

Explicitly determine whether the path requires:

- first upgrading to the latest supported 8.x release;
- running deprecation checks;
- resolving deprecated settings or APIs;
- running Kibana Upgrade Assistant;
- reindexing old indices;
- updating system indices;
- migrating templates;
- creating a new 9.x cluster and restoring/reindexing copied data;
- multiple sequential version hops.

Document the selected path and its official justification before applying it.

If official guidance shows that the repository's current 8.x version cannot safely upgrade directly to the selected 9.x version, follow the supported intermediate path. Do not bypass required upgrade steps.

## Data safety and destructive-action boundaries

Treat indices, snapshots, volumes, and cluster state as material user data.

Do not perform the rehearsal against the user's only copy of any data.

Do not:

- attach Elasticsearch 9.x to an existing 8.x data volume;
- attach Elasticsearch 8.x to a volume already upgraded by 9.x;
- reuse the user's normal Elasticsearch volume for migration rehearsal;
- delete or overwrite existing indices;
- remove snapshots;
- delete Docker volumes;
- run destructive cleanup against broad or unresolved paths;
- execute a migration against a remote or production-like cluster without explicit authorization.

Use a parallel, isolated, disposable environment with:

- a new cluster name;
- new containers;
- new volumes or ephemeral storage;
- distinct ports when needed;
- copied or synthetic data;
- test-exclusive indices;
- explicit resource limits and bounded timeouts.

If access to real existing data is required, stop and request explicit authorization before reading, copying, snapshotting, or migrating it.

When no user data is available, create a representative synthetic dataset that exercises the documented mappings, search operations, bulk operations, logging data stream, and compatibility cases. Clearly state that synthetic data was used; do not claim that real-data compatibility was proven.

## Snapshot and restore

Before any server migration rehearsal, define and test a recovery point appropriate to the environment being exercised.

For an existing disposable 8.x test cluster:

- create a snapshot using the officially supported mechanism;
- record repository and snapshot metadata without secrets;
- verify snapshot completion;
- list the included indices and data streams;
- test restore into an isolated compatible environment when feasible;
- verify restored document counts and representative queries.

A snapshot that has not been verified or restored is not sufficient evidence of rollback readiness.

Do not describe a filesystem copy of a live Elasticsearch data directory as a valid snapshot unless official documentation explicitly supports the exact procedure.

## Deprecation and upgrade checks

Run the supported deprecation and upgrade checks against the copied 8.x environment before upgrading.

Inventory and resolve, as applicable:

- deprecated cluster settings;
- deprecated index settings;
- deprecated mappings;
- deprecated query syntax;
- deprecated client APIs;
- incompatible index versions;
- incompatible templates;
- deprecated security configuration;
- data-stream or ingest-pipeline incompatibilities;
- Kibana migration warnings.

Classify each finding as:

- blocking;
- required before upgrade;
- safe to defer with official justification;
- not applicable.

Do not suppress a blocking deprecation merely to obtain a green check.

## .NET client migration

Upgrade the already isolated Infrastructure adapter from the verified 8.x client to the selected compatible 9.x client.

Follow the official .NET client breaking-change guidance and inspect every adapter operation, including:

- client construction and dependency injection;
- authentication and transport configuration;
- index creation and existence checks;
- document indexing;
- updates and upserts;
- get-by-id;
- search queries;
- pagination or result limits;
- delete operations;
- delete-by-query, if still present;
- bulk operations;
- partial bulk failures;
- refresh behavior;
- descriptor APIs;
- query and aggregation containers;
- field inference;
- date/time handling;
- serialization;
- response validity and exception handling;
- cancellation-token propagation.

Preserve the Application-owned port established in Phase 10.

Do not leak new Elastic 9 types into Application, Domain, controllers, or public contracts.

Do not redesign the Application boundary merely to mirror the new client API.

Update adapter tests to prove semantic parity, not merely compilation.

## Serilog sink migration

Upgrade `Elastic.Serilog.Sinks` only to a stable version officially compatible with the selected target stack.

Validate:

- sink initialization;
- fail-soft startup when Elasticsearch is unavailable;
- continued console logging;
- data-stream type, dataset, and namespace;
- component-template installation;
- index-template installation;
- bootstrap behavior;
- authentication/TLS behavior;
- batching and flush behavior;
- shutdown and final flush;
- prevention of recursive sink-failure logging;
- absence of secrets and sensitive payloads in emitted events.

Do not broaden this phase into an unrelated logging redesign.

Preserve the Phase 10 separation between search/indexing configuration and log-transport configuration.

## Health-check compatibility

Verify whether the existing Elasticsearch health-check package and implementation officially support the selected Elasticsearch 9.x target.

Do not infer compatibility solely from the health-check package's own major version.

If the existing package is compatible, retain it unless a change is required.

If it is incompatible or abandoned:

- select the smallest supported replacement based on official evidence;
- preserve the existing health endpoint route and response shape;
- use bounded timeouts;
- avoid exposing sensitive connection details;
- keep Elasticsearch unavailability from taking down unrelated endpoints;
- document the package decision and rollback.

Do not redesign the overall health model in this phase.

## Elasticsearch and Kibana images

Pin Elasticsearch and Kibana to explicit, mutually compatible versions verified from official sources.

Do not use:

- `latest`;
- floating major tags;
- floating minor tags;
- an unverified combination of Elasticsearch and Kibana;
- unofficial images when official images are available.

Record image names, tags, and resolved digests when available.

Keep Elasticsearch and Kibana upgrade changes isolated from Redis, RabbitMQ, PostgreSQL, Redis Insight, and unrelated services.

Do not alter existing user volumes in place.

## Parallel rehearsal

Rehearse the upgrade in a parallel environment before changing the default local stack.

The rehearsal must demonstrate the selected supported path using copied or synthetic data:

1. Start the source 8.x environment on explicit versions.
2. Load representative indices, mappings, templates, aliases, documents, and logging data streams.
3. Run deprecation and upgrade checks.
4. Create and verify the recovery point.
5. Execute the documented upgrade, snapshot/restore, or reindex path into an isolated 9.x environment.
6. Validate cluster health and migration completion.
7. Compare document counts and representative records.
8. Run the adapter contract tests against the target cluster.
9. Run search, indexing, update, bulk, and delete checks.
10. Run Serilog sink smoke tests.
11. Run health-check tests.
12. Restart the target services and verify retained data.
13. Demonstrate rollback by reconnecting the application to the untouched 8.x environment or by restoring the verified source environment.

Never attempt rollback by starting an older Elasticsearch binary against data already upgraded by a newer major.

## Templates, mappings, aliases, and data streams

Validate all repository-owned Elastic assets against the selected 9.x target.

Cover, as applicable:

- component templates;
- index templates;
- data-stream templates;
- index mappings;
- field types;
- dynamic mappings;
- analyzers;
- aliases;
- lifecycle settings;
- ingest pipelines;
- Serilog data streams;
- template priorities and collisions;
- bootstrap behavior.

Template installation must be idempotent.

Do not overwrite unrelated cluster templates or delete existing data streams.

If a template or mapping must change, document:

- the incompatibility;
- the minimum change;
- impact on existing documents;
- reindex requirements;
- forward migration;
- rollback implications.

## HTTP and application compatibility

Phase 11 must not change the public HTTP contract.

Preserve:

- routes;
- methods;
- route, query, and body parameters;
- status codes;
- response payloads and text;
- v1 deprecation;
- authentication and authorization;
- the health endpoint's documented public contract;
- published OpenAPI.

The Application-owned search/indexing port from Phase 10 must remain stable unless a proven defect requires a change. Do not leak migration concerns into controllers or Domain.

Capture OpenAPI again and compare it with:

`docs/refactoring/baseline-phase-6/`

The expected result is exact equality.

If any difference appears, investigate it. Do not silently update the baseline.

## Required tests

Add or update tests proportionally to the migration.

### Adapter contract tests

Run the same semantic contract suite against the final supported 8.x source and selected 9.x target when practical.

Cover at least:

1. Idempotent index creation/verification.
2. Indexing a new document.
3. Updating an existing document.
4. Get by identifier.
5. Existing search/query behavior.
6. Bulk success.
7. Partial bulk failure.
8. Delete.
9. Missing document.
10. Invalid response handling.
11. Transport failure.
12. Timeout.
13. Cancellation.
14. Recovery after service restoration.
15. Compatibility with migrated documents.
16. Compatibility with selected mappings and templates.

### Migration and data tests

Cover at least:

1. Source snapshot completion.
2. Snapshot restore or supported transfer path.
3. Source and target document counts.
4. Representative document equality.
5. Representative queries returning equivalent results.
6. Alias resolution.
7. Index and component-template installation.
8. Data-stream availability.
9. Restart persistence.
10. Explicit proof that the source 8.x data remains untouched.

### Logging tests

Cover at least:

1. Application startup when the target Elasticsearch service is available.
2. Application startup when it is unavailable.
3. Continued console logging during sink failure.
4. Successful event ingestion into the selected data stream.
5. Template/bootstrap success.
6. Bounded flush/shutdown behavior.
7. No secrets or sensitive payloads in test events.

### Health-check tests

Cover at least:

1. Healthy target cluster.
2. Unavailable target cluster.
3. Authentication or authorization failure without secret leakage.
4. Timeout.
5. Cancellation.
6. Unrelated endpoints remaining available.
7. Existing response contract preserved.

### Functional and architecture tests

Keep normal functional tests deterministic and independent of a real Elastic service through the Phase 10 fake/no-op strategy.

Prove that:

- Application remains free of Elastic packages and vendor types;
- Domain remains free of Elastic and logging dependencies;
- vendor code remains in Infrastructure or the composition/logging boundary selected in Phase 10;
- no architecture exception is reintroduced;
- WebApi starts without Elasticsearch;
- existing HTTP behavior remains unchanged;
- non-Elastic test suites remain green.

## Package and vulnerability validation

Update related Elastic package versions coherently based on verified compatibility.

Do not update unrelated package families.

After the package change:

- restore using the pinned SDK;
- inspect direct and transitive package graphs;
- run the vulnerability audit;
- confirm that no stale 8.x client dependency remains unintentionally;
- confirm that no preview package is present;
- document any transitive version constraint or incompatibility.

## Rollback requirements

The rollback plan must be executable and must not rely on downgrading an upgraded data directory.

Preserve until all acceptance criteria pass:

- the verified source 8.x environment;
- its untouched data or verified snapshot;
- previous explicit image versions;
- previous package versions;
- previous adapter implementation state in version control or the recoverable working-tree history;
- previous configuration;
- a documented connection switch back to the 8.x environment.

Rollback means routing the application back to the preserved 8.x cluster and coherently reverting client, sink, health-check, image, and configuration changes.

Do not delete the source environment, its volumes, or its snapshots as part of Phase 11.

Any later cleanup requires explicit authorization.

## Scope boundaries

Do not perform work from subsequent phases:

- do not redesign identity or JWT behavior;
- do not implement Phase 12;
- do not redesign localization, health, or observability standards beyond compatibility work required for Elastic 9;
- do not upgrade Redis, RabbitMQ, PostgreSQL, Redis Insight, test tooling, or the VSIX project;
- do not change Application boundaries established in Phase 10 without a demonstrated defect;
- do not remove v1 endpoints;
- do not change HTTP contracts;
- do not introduce OpenTelemetry as unrelated work;
- do not create EF migrations without a real relational-schema delta;
- do not manually edit EF migration designer or snapshot files;
- do not perform unrelated cosmetic modernization;
- do not perform work from Phases 12+.

## Validation

Run build and test commands sequentially because parallel builds compete for shared `obj` directories.

Use the pinned SDK:

`C:\Users\alexa\.dotnet\dotnet.exe`

Start with the smallest relevant package, adapter, and integration checks.

At the end, run the repository's complete validation gate:

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
- Elastic adapter contract tests against the selected 9.x target;
- migration rehearsal tests;
- copied/synthetic-data parity checks;
- Serilog sink smoke tests;
- health-check tests;
- direct and transitive NuGet vulnerability audit;
- `docker compose config -q`;
- startup of the isolated source and target Elastic environments;
- restart persistence in the target environment;
- OpenAPI capture and exact comparison;
- `git diff --check`.

Because this phase changes packages and infrastructure structure, regenerate `graphify-out` through the official Graphify command when the repository rules require it.

Do not edit `graphify-out` manually.

If Docker, Elasticsearch, Kibana, a snapshot repository, or another external dependency is unavailable, do not report the corresponding gate as passed. Record precisely:

- the command attempted;
- the result;
- the observed cause;
- which acceptance criteria remain unverified.

Do not silently reduce coverage or substitute an unexecuted checklist for evidence.

## Acceptance criteria

Phase 11 may only be considered complete when:

- current official sources were consulted and recorded;
- a mutually compatible stable 9.x client, sink, Elasticsearch, and Kibana set was selected;
- the officially supported upgrade path was identified and followed;
- blocking deprecations were resolved;
- the Infrastructure adapter uses the selected 9.x .NET client;
- Application and Domain remain free of Elastic vendor types;
- adapter contract tests pass against the target Elastic version;
- component templates and index templates install cleanly and idempotently;
- copied or synthetic data remains queryable after the migration rehearsal;
- representative document counts and queries match expected results;
- Serilog events reach the selected target data stream;
- console logging still works when Elasticsearch is unavailable;
- health checks work against the target and degrade safely when it is unavailable;
- WebApi startup remains fail-soft when Elasticsearch is unavailable;
- Elasticsearch and Kibana images are pinned to explicit compatible versions;
- source 8.x data remains untouched and available for rollback;
- rollback does not require mounting upgraded data with an older binary;
- the public HTTP contract and OpenAPI remain unchanged;
- architecture and all non-Elastic suites remain green;
- no unrelated Phase 12+ work was started.

If real copied data was not available, explicitly state that real-data migration compatibility remains unverified. Do not mark that criterion as passed based only on synthetic data.

## Documentation and delivery

Create:

`docs/refactoring/phase-11-report.md`

The report must include:

- an executive summary of the upgrade;
- proof that Phase 10 was complete before starting;
- the pre-upgrade inventory;
- the complete compatibility matrix;
- selected package and image versions;
- verification date and official source links;
- relevant breaking changes;
- the officially supported upgrade path;
- deprecation and Upgrade Assistant findings;
- snapshot and restore evidence;
- whether copied or synthetic data was used;
- adapter changes;
- query, mapping, serialization, and bulk-operation changes;
- Serilog sink changes;
- health-check compatibility decision;
- template, mapping, alias, and data-stream validation;
- migration rehearsal steps and results;
- document-count and representative-query comparisons;
- restart-persistence result;
- tests added or updated;
- commands executed and actual results;
- final test counts;
- OpenAPI comparison result;
- vulnerability-audit result;
- residual risks;
- executable rollback procedure;
- gates not executed;
- explicit confirmation that source 8.x data and snapshots were not deleted;
- explicit confirmation that no unrelated Phase 12+ work was implemented.

Update the root `AGENTS.md` only with commands and results that were actually executed and confirmed.

Upon completion, present:

1. An objective implementation summary.
2. The selected compatibility matrix and official sources.
3. The supported upgrade path used.
4. The main files, packages, and images changed.
5. Adapter, sink, health-check, template, and data-migration results.
6. Tests and validations with actual results.
7. Whether real copied data or synthetic data was used.
8. Pending work or gates not executed.
9. Residual risks.
10. The executable rollback procedure.
11. Confirmation that the HTTP contract was preserved.
12. Confirmation that Application boundaries remained intact.
13. Confirmation that the source 8.x environment remains recoverable.
14. Confirmation that Phases 12+ were not started.

Do not commit, push, merge, change branches, delete volumes or snapshots, switch a production-like cluster, or open a pull request without explicit authorization.
