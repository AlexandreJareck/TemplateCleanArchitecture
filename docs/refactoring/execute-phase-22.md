# Execute Phase 22

Read and execute the following specification in full:

`docs/refactoring/handoff-phase-22.md`

Work directly in the authoritative checkout:

`C:\Users\alexa\source\repos\TemplateCleanArchNET`

Before editing:

- Confirm that Phase 21 is merged (or that the owner explicitly authorized building on its branch).
- Re-run the green starting-point checks listed in the handoff.
- Ask the owner for decisions D1–D6 and record the answers.

If a precondition fails, stop and report the blocker instead of implementing around it.

Follow every instruction, boundary, secret-handling rule, validation gate, acceptance criterion, rollback requirement, and reporting requirement in `docs/refactoring/handoff-phase-22.md`. Implement rather than merely plan.

Remove the committed `JwtSettings:Key` value. Supply the key for every runtime profile from non-committed sources, and rotate it. Prove fail-fast startup when the key is missing. Add an automated check, proven by a controlled mutation, that prevents re-committing a key value.

Never write, print, log, or copy any key value (old or new) into source, docs, reports, commits, PR text, test output, or chat. Do not modify the user's real `.env` or user-secrets store without explicit authorization. Do not rewrite Git history.

Re-execute the complete Phase 21 gate matrix against the final workspace using isolated disposable resources:

- restore and build;
- architecture, unit, integration, real-service, functional, discovery and coverage gates;
- the Compose baseline and its four rollback overlays;
- all rehearsals that start the WebApi;
- the vulnerability audit and the license inventory;
- OpenAPI v1/v2 comparison against `docs/refactoring/baseline-phase-6`;
- the secret scan;
- `git diff --check`.

Create `docs/refactoring/phase-22-report.md` with every gate explicitly marked `Passed`, `Failed`, `Skipped`, or `Blocked`. Declare `Ready` only when every required gate passed; otherwise declare `Not ready` and list the exact blockers.

Update `AGENTS.md` and setup documentation only with commands and results actually verified in the final workspace, naming configuration keys and environment variables but never their values.

Do not:

- change token claims, lifetimes, algorithms or the 401/403 contract;
- change API routes, status codes or OpenAPI, or remove API v1;
- upgrade packages or images, or add features;
- read or delete user data volumes;
- delete rehearsal resources without authorization;
- begin any Phase 23 work.

Preserve every pre-existing and concurrent-agent change. Do not reset, clean, stash, rebase, or use destructive Git commands.
