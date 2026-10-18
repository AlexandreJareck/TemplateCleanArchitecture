# Execute Phase 28

Read and execute `docs/refactoring/handoff-phase-28.md` in full in `C:\Users\alexa\source\repos\TemplateCleanArchNET`.

Verify green Phases 23–27 and the approved event catalog. Create the separate Worker, transactional PostgreSQL Outbox, durable Inbox, versioned procurement events, isolated Compose wiring and business-safe RabbitMQ consumption. Preserve legacy v1 messaging behavior while moving new critical effects out of the WebApi.

Prove atomic event persistence, confirmed dispatch, duplicate/crash safety, retry/dead-letter, backlog recovery, two-worker contention and graceful shutdown with real PostgreSQL/RabbitMQ tests. Do not claim exactly-once transport. Run all handoff gates and create `docs/refactoring/phase-28-report.md` with delivery guarantees, topology, failures, rollback and readiness.

Do not commit/push/merge, change branches/worktrees, open a PR, delete queues/volumes/data, or alter user environments without authorization.
