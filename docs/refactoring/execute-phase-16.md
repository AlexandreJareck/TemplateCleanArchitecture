# Execute Phase 16

Read and execute the following specification in full:

`docs/refactoring/handoff-phase-16.md`

Work directly in the authoritative checkout:

`C:\Users\alexa\source\repos\TemplateCleanArchNET`

Before making changes, verify from actual code, reports, tests, Compose configuration, images, and database behavior that the formal prerequisites—Phases 7 and 14—and every prior phase adopted in the accumulated sequence are genuinely complete. `docs/refactoring/phase-14-report.md` must contain actual clean-volume, both-database, migration/seed, restart, and rollback evidence; a handoff file alone is not proof. If Phase 15 was executed in sequence, preserve and verify its accumulated state as well. If required prior work is missing, stop and report the blocker rather than reimplementing or copying it.

Follow every instruction, scope boundary, data/volume safeguard, research requirement, validation requirement, acceptance criterion, rollback requirement, and reporting requirement in `docs/refactoring/handoff-phase-16.md`.

Implement the authorized phase rather than merely planning it. First back up and restore both the application and Identity databases in an isolated environment, verify and pin the latest supported PostgreSQL 16 patch, validate extensions/collations, both EF contexts, migrations, seeds, representative queries/plans, restart persistence, WebApi behavior, and a timed rollback to the preserved PostgreSQL 16 baseline.

PostgreSQL 18 is a separate opt-in. Do not infer approval from this prompt or from a general instruction to execute Phase 16. Proceed with an actual 16-to-18 migration only after the user explicitly authorizes it with the exact source/target versions, method, data scope, downtime implications, and rollback point disclosed. Without that authorization, record “PostgreSQL 18 not authorized and not executed” and complete only the adopted PostgreSQL 16 checkpoint.

Use only copies and new isolated target volumes. Never point PostgreSQL 16 at data files written/upgraded by PostgreSQL 18, never run an upgrade against the only source copy, and never delete a database, volume, backup, extension, index, or other material data without explicit authorization. Keep credentials, hashes, stamps, tokens, connection-string values, and personal data out of source, logs, reports, and test output.

Run build and tests sequentially with the pinned SDK. Run the repository validation script, both-database backup/restore rehearsal, PostgreSQL-backed and functional suites, migration/seed idempotency, restart/recovery checks, timed rollback, vulnerability audit, and exact OpenAPI comparison. Run PostgreSQL 18 gates only when explicitly authorized. Record only checks actually executed; never report unavailable, skipped, or failed gates as passed. Create:

`docs/refactoring/phase-16-report.md`

Do not redesign persistence, create an EF migration without a real schema delta, upgrade another service, or perform Phase 17+ work. Preserve every pre-existing and concurrent-agent working-tree change. Do not reset, clean, stash, checkout, rebase, commit, push, merge, change branches, create another worktree, alter a production-like environment, delete Docker resources or data, or open a pull request without explicit authorization.
