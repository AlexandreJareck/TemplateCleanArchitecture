# Execute Phase 15

Read and execute the following specification in full:

`docs/refactoring/handoff-phase-15.md`

Work directly in the authoritative checkout:

`C:\Users\alexa\source\repos\TemplateCleanArchNET`

Before making changes, verify from actual code, reports, tests, Compose configuration, and validation evidence that Phases 8, 13, and 14 are genuinely complete and that this checkout contains all accumulated prior-phase work. In particular, `docs/refactoring/phase-14-report.md` must contain actual clean-volume Compose and restart results; the Phase 14 handoff alone is not proof of completion. If any prerequisite or accumulated prior-phase state is missing, stop and report the blocker. Do not reimplement it inside Phase 15 or selectively copy files from another checkout.

Follow every instruction, scope boundary, data/volume safeguard, research requirement, validation requirement, acceptance criterion, rollback requirement, and reporting requirement in `docs/refactoring/handoff-phase-15.md`.

Implement the phase rather than merely planning it. Consolidate the current Redis, Search, and TimeSeries topology onto one verified stable Redis 8 deployment with an explicit tag/digest and a new isolated volume. Revalidate the adopted RedisGraph removal decision against actual references before removing it. Rehearse an officially supported data/configuration migration on copied or synthetic data, validate cache serialization, TTL, invalidation, ACLs, Search and TimeSeries behavior, restart persistence, WebApi behavior, and rollback before switching the default configuration.

Never mount the source and target server majors against the same volume. Preserve the previous Redis containers, images, configuration, volume, and migration artifacts for rollback. Do not delete Redis data, volumes, backups, Search indexes, TimeSeries data, graph data, Redis Insight state, or other material resources without explicit authorization. Keep credentials and connection-string values out of source, logs, reports, and test output.

Run build and test commands sequentially with the pinned SDK. Run the repository validation script, the real Redis 8 contract tests, the isolated migration rehearsal, restart/persistence checks, rollback switch, and exact OpenAPI comparison. Record only checks actually executed; never report an unavailable, skipped, or failed gate as passed. Create:

`docs/refactoring/phase-15-report.md`

Do not perform PostgreSQL, RabbitMQ, Elasticsearch, Redis Insight, test-tooling, VSIX, or Phase 16+ upgrades. Preserve every pre-existing working-tree change. Do not reset, clean, stash, checkout, rebase, commit, push, merge, change branches, create another worktree, alter a production-like environment, delete Docker resources, or open a pull request without explicit authorization.
