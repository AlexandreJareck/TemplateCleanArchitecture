# Execute Phase 25

Read and execute `docs/refactoring/handoff-phase-25.md` in full in `C:\Users\alexa\source\repos\TemplateCleanArchNET`.

Verify green Phases 23–24, inventory concurrent work and baselines, then implement Organization and procurement master data with approved invariants, PostgreSQL mappings/migrations, permissions, concurrency, v2 endpoints, audit, and tests. Implement optional BrasilAPI and Banco Central enrichment only behind Application-owned ports with fake-server coverage, bounded resilience, safe logs, accepted snapshots, and manual fallback.

Do not let external providers become authoritative or required for core operations. Preserve current API contracts and architecture. Run every required sequential backend, PostgreSQL, functional, adapter, architecture, OpenAPI, audit, Graphify and diff gate. Create `docs/refactoring/phase-25-report.md` with actual results and `Ready` only when all required gates pass.

Do not commit/push/merge, change branches/worktrees, open a PR, call production providers, or delete/alter data without authorization.
