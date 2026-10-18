# Execute Phase 31

Read and execute `docs/refactoring/handoff-phase-31.md` in full in `C:\Users\alexa\source\repos\TemplateCleanArchNET`.

Verify green Phases 23–30 and approved approval policies. Research current Microsoft Durable Functions/.NET isolated support and use a supported TFM even if it differs from the WebApi. Create the separate Functions composition root, application-owned workflow client boundary, deterministic approval orchestrator, idempotent Activities, external events, durable timers, reminder/escalation, rejection/cancellation/compensation, local emulator/Azurite environment, v2 APIs and Angular approval UI.

Keep business truth in PostgreSQL, process state in Durable history, side effects out of orchestrator code, and Azure types out of Domain/Application. Prove replay, restart, duplicate/late events, timeout, compensation and versioning. Run all handoff gates and create `docs/refactoring/phase-31-report.md` with official-source research, determinism review, local evidence and readiness.

Do not commit/push/merge, change branches/worktrees, open a PR, deploy/provision Azure, or delete orchestration/storage state without authorization.
