# Execute Phase 20

Read and execute the following specification in full:

`docs/refactoring/handoff-phase-20.md`

Work directly in the authoritative checkout:

`C:\Users\alexa\source\repos\TemplateCleanArchNET`

Before editing, verify the actual VSIX project, manifest, package graph, installed Visual Studio 2022/MSBuild toolchain, existing CI/build scripts, accumulated reports, and independent application solution-filter baseline. Confirm that the recorded decision remains in force: the VSIX is an optional isolated deliverable, updates stay within Visual Studio SDK 17.x, and VSSDK 18 requires separate explicit authorization. If deliverable status or the target Visual Studio range cannot be determined, stop and request that product decision rather than deleting or broadly migrating the project.

Follow every requirement, boundary, research rule, safety rule, validation gate, rollback rule, acceptance criterion, and reporting requirement in `docs/refactoring/handoff-phase-20.md`. Implement rather than merely plan. Research current stable supported 17.x versions and compatibility from official primary sources at execution time; record the date and direct links.

Create a repository-owned VSIX-specific validation path using compatible Visual Studio 2022 MSBuild, update only the coherent supported 17.x VS SDK/BuildTools set, inspect the generated VSIX, and safely validate installation and actual extension loading/behavior in an experimental or otherwise approved target instance. Do not claim installation or runtime success from a build alone. If Visual Studio tooling is unavailable, report those gates as unverified and do not overstate phase completion.

Keep `TemplateCleanArchitecture.App.slnf` independent of the VSIX. If repository CI exists, keep VSIX validation as a separate scoped job; if it does not, provide a verified CI-suitable command without inventing a provider or claiming CI consumption. Remove unsupported active instructions that use full-solution `dotnet build` as the VSIX gate.

Run the isolated Visual Studio MSBuild validation plus the sequential application validation script, audits, tests, Compose validation, and exact OpenAPI comparison. Record only checks actually executed. Create:

`docs/refactoring/phase-20-report.md`

Do not introduce VSSDK 18, Visual Studio 18 targets, application behavior/package changes, service or data changes, unrelated test-tooling work, or Phase 21 cleanup. Preserve every pre-existing and concurrent-agent change, especially shared central package edits. Do not reset, clean, stash, checkout, rebase, commit, push, merge, change branches, create another worktree, modify a normal Visual Studio profile without authorization, delete material artifacts/state, alter a production-like environment, or open a pull request without explicit authorization.
