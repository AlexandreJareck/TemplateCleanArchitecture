# Execute Phase 24

Read and execute `docs/refactoring/handoff-phase-24.md` in full in `C:\Users\alexa\source\repos\TemplateCleanArchNET`.

Verify completed, green Phases 22–23 and the approved authentication/session decision before editing. Implement the real-SPA identity/API foundation: permission policies, current-user endpoint, secure rotating refresh sessions with reuse detection, logout/revocation, configurable CORS, security auditing, and the approved idempotency/concurrency/API conventions. Preserve every existing v1/v2 contract unless an explicitly approved additive migration says otherwise.

Apply every security, migration, secret, compatibility, test, rollback, and evidence rule in the handoff. Run sequential targeted and wider gates, exact OpenAPI review, secret/package/license audits, and create `docs/refactoring/phase-24-report.md`. Declare `Ready` only if all required gates pass.

Do not commit/push/merge, change branches/worktrees, open a PR, rewrite migrations, expose tokens/secrets, or delete data/resources without explicit authorization.
