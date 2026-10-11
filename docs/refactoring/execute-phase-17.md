# Execute Phase 17

Read and execute the following specification in full:

`docs/refactoring/handoff-phase-17.md`

Work directly in the authoritative checkout:

`C:\Users\alexa\source\repos\TemplateCleanArchNET`

Before making changes, verify from actual code, reports, tests, Compose configuration, and broker behavior that Phases 9, 13, and 14 are genuinely complete and that every prior phase adopted in the accumulated sequence remains present. `docs/refactoring/phase-14-report.md` must contain actual pinned-image, clean-volume, health, restart, persistence, and rollback evidence; a handoff alone is not proof. Preserve the accumulated state from Phases 15 and 16 if they were executed. If required prior work is missing, stop and report the blocker rather than reimplementing or copying it.

Follow every instruction, scope boundary, broker-data safeguard, research requirement, validation requirement, acceptance criterion, rollback requirement, and reporting requirement in `docs/refactoring/handoff-phase-17.md`.

Implement the phase rather than merely planning it. Research and follow the exact officially supported RabbitMQ 4.x upgrade sequence and feature-flag requirements; select an explicit stable management image/digest with compatible Erlang/OTP; export and securely verify definitions; inventory and preserve plugins, topology, policies, durable queues/messages, and the Phase 9 v1 contract; rehearse the upgrade on isolated copied state; then validate publish/consume, confirms, ack, bounded retry, poison-message dead-lettering, duplicates, reconnect, restart, shutdown, recovery, and timed rollback.

Definitions do not contain queued messages. Prove durable-message preservation separately with isolated persistent sentinel messages and the officially supported upgrade/transfer method. Never run source and target against the same incompatible data volume, never point an older broker at upgraded target data, and never purge/delete queues, definitions, messages, feature-flag state, volumes, backups, or other material broker state without explicit authorization. Keep credentials, password hashes, Erlang cookies, connection strings, and payloads out of source, logs, reports, and test output.

Run build and tests sequentially with the pinned SDK. Run the repository validation script, the broker-backed Phase 9 contract suite against the target, isolated upgrade/definitions/persistent-message rehearsal, feature-flag/plugin checks, restart/recovery/shutdown tests, timed switch-back, vulnerability audit, and exact OpenAPI comparison. Record only checks actually executed; never report unavailable, skipped, or failed gates as passed. Create:

`docs/refactoring/phase-17-report.md`

Do not redesign message contracts/topology, upgrade another service, discard broker state, or perform Phase 18+ work. Preserve every pre-existing and concurrent-agent working-tree change. Do not reset, clean, stash, checkout, rebase, commit, push, merge, change branches, create another worktree, alter a production-like environment, delete Docker/broker resources, or open a pull request without explicit authorization.
