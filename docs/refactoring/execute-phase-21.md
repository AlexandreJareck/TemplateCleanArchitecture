# Execute Phase 21

Read and execute the following specification in full:

`docs/refactoring/handoff-phase-21.md`

Work directly in the authoritative checkout:

`C:\Users\alexa\source\repos\TemplateCleanArchNET`

Before editing, reconcile every adopted Phase 0–20 against actual code, reports, packages, images, migrations, scripts, tests, rollback assets, and the current working tree. A handoff is not implementation evidence. If an adopted prerequisite is missing or materially incomplete, stop cleanup, document the blocker, and do not implement that earlier phase inside Phase 21 or claim release readiness.

Follow every instruction, boundary, evidence rule, safety rule, validation gate, acceptance criterion, rollback requirement, and reporting requirement in `docs/refactoring/handoff-phase-21.md`. Implement rather than merely plan.

Remove only compatibility shims, temporary architecture exceptions, redundant packages/configuration, duplicate code, and mutable tags that are proven dead or resolved under adopted decisions. Use static analysis plus targeted tests for every removal and keep ambiguous reflection, serialization, DI, migration, wire-format, configuration, and rollback paths. Do not remove API v1: it must remain functional, contract-compatible, and deprecated in OpenAPI until the user explicitly approves a sunset.

Re-check current official support/version sources, run package vulnerability/outdated/deprecation/license audits, regenerate Graphify after structural cleanup, compare dependency direction and hubs, and rebuild/compare OpenAPI v1/v2 against the approved baseline. Never overwrite a baseline merely to hide an unapproved difference.

Execute the complete final matrix against the final workspace: sequential restore/build; architecture, unit, integration, PostgreSQL, RabbitMQ, Elasticsearch, functional, discovery, and coverage gates; isolated clean-volume Compose and full-stack smoke tests; adopted Redis, PostgreSQL, RabbitMQ, Elastic/Kibana, Redis Insight, and VSIX real-service/restart/rollback gates; audits; Graphify; OpenAPI; documentation verification; and `git diff --check`. Use disposable isolated resources and never delete user data or retained rollback assets.

Create `docs/refactoring/phase-21-report.md` with every gate explicitly marked `Passed`, `Failed`, `Skipped`, or `Blocked`, including commands, versions, counts, artifacts, and actual results. Declare `Ready` only when every required gate passed. Otherwise declare `Not ready` and list the exact blockers; never infer a final pass from an earlier report or an unavailable dependency.

Update `AGENTS.md` and setup/validation documentation only with commands and results actually verified in the final workspace. Do not add features, perform new major upgrades, migrate to PostgreSQL 18 or VSSDK 18, rewrite applied migrations, hand-edit Graphify, weaken gates, remove API v1, or begin any Phase 22 work.

Preserve every pre-existing and concurrent-agent change. Do not reset, clean, stash, checkout, rebase, commit, push, merge, change branches, create another worktree, delete volumes/databases/queues/indices/backups/images/tooling state or other material resources, alter a production-like environment, or open a pull request without explicit authorization.
