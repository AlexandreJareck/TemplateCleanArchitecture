# Execute Phase 18

Read and execute the following specification in full:

`docs/refactoring/handoff-phase-18.md`

Work directly in the authoritative checkout:

`C:\Users\alexa\source\repos\TemplateCleanArchNET`

Before making changes, verify from actual code, reports, Compose configuration, containers, volumes, and tests that Phase 15 is genuinely complete and that every prior phase adopted in the accumulated sequence remains present. `docs/refactoring/phase-15-report.md` must contain actual Redis 8 consolidation, migration, cache/Search/TimeSeries, restart, and rollback evidence; a handoff alone is not proof. Preserve the accumulated state from Phases 16 and 17 if they were executed. If required prior work is missing, stop and report the blocker rather than reimplementing or copying it.

Follow every instruction, scope boundary, tooling-state safeguard, research requirement, validation requirement, acceptance criterion, rollback requirement, and reporting requirement in `docs/refactoring/handoff-phase-18.md`.

Implement the phase rather than merely planning it. Treat Redis Insight as disposable tooling: research and pin a stable supported official image/digest, inventory saved connections/settings safely, confirm whether they need preservation, start the target with a new isolated volume, import or recreate only required non-secret metadata, reconnect it to the Phase 15 Redis 8 service, and validate actual UI loading, safe cache/Search/TimeSeries inspection, restart persistence, application independence, and rollback.

Never mount the legacy Redis Insight volume into the target by assumption. Preserve the old image, configuration, saved state, and volume until the user separately authorizes cleanup. Do not delete either tooling volume, Redis data, connections/settings, exports, or other material state. Keep Redis credentials, TLS/SSH private material, tokens, connection strings, payloads, and sensitive screenshots out of source, logs, reports, and test output.

Run build and tests sequentially with the pinned SDK. Run the repository validation script, Compose validation, Redis Insight startup/UI/connection/restart checks, WebApi and Redis checks with Redis Insight absent, rollback to the legacy tooling state, vulnerability audit, test-discovery comparison, and exact OpenAPI comparison. Record only checks actually executed; never report unavailable, skipped, or failed gates as passed. Create:

`docs/refactoring/phase-18-report.md`

Do not upgrade or change the Redis server, application behavior/packages, observability platform, another service, or Phase 19+ tooling. Preserve every pre-existing and concurrent-agent working-tree change. Do not reset, clean, stash, checkout, rebase, commit, push, merge, change branches, create another worktree, alter a production-like environment, delete Docker/tooling resources, or open a pull request without explicit authorization.
