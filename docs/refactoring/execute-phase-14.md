# Execute Phase 14

Read and execute the following specification in full:

`docs/refactoring/handoff-phase-14.md`

Work directly in the authoritative checkout:

`C:\Users\alexa\source\repos\TemplateCleanArchNET`

Before making changes, verify from the actual repository state that the formal Phase 14 prerequisites (Phases 1 and 7) and every prior phase the user has adopted are genuinely complete, with their accumulated code, tests, scripts, skills, reports, and approved API baseline present in this checkout. A handoff file is not evidence of implementation. If the user is executing the numbered phases sequentially and any required Phase 11–13 implementation or report is missing, stop and report the blocker; do not skip, reimplement, or selectively copy prior phases.

Follow every instruction, scope boundary, data/volume safeguard, validation requirement, acceptance criterion, rollback requirement, and reporting requirement in `docs/refactoring/handoff-phase-14.md`.

Implement the phase rather than merely planning it. Establish only the reproducible current-major Compose baseline: verify current official image information, pin explicit compatible patches and/or immutable digests, validate `init-db.sh`, both PostgreSQL databases, health-based startup ordering, migrations and seeds twice, clean isolated startup, representative smoke tests, non-destructive restart, persistence, recovery, and shutdown. Do not perform any service major upgrade or any Phase 15+ work.

Use a unique isolated Compose project and new isolated volumes. Never attach the rehearsal to existing user volumes. Do not delete any volume, database, queue, index, snapshot, backup, tooling state, or other material data without explicit authorization. Keep secret values out of source, commands recorded in documentation, logs, reports, and test output.

Run build and test commands sequentially with the pinned SDK. Run the repository validation script and the separate full Compose baseline required by the handoff. Record only commands and checks actually executed; never report an unavailable, skipped, or failed gate as passed. Create:

`docs/refactoring/phase-14-report.md`

Preserve all pre-existing working-tree changes. Do not reset, clean, stash, checkout, rebase, commit, push, merge, change branches, create another worktree, delete Docker resources, alter a production-like environment, or open a pull request without explicit authorization.
