# Execute Phase 19

Read and execute the following specification in full:

`docs/refactoring/handoff-phase-19.md`

Work directly in the authoritative checkout:

`C:\Users\alexa\source\repos\TemplateCleanArchNET`

Before editing, verify from actual code, reports, package graphs, test discovery, test execution, and coverage artifacts that the accumulated earlier-phase checkout has stable green suites. A handoff is not proof of completion. Capture exact per-project discovery/execution counts, the existing coverage pipeline and consumer, and separate production/test dependency graphs. If the baseline is missing or unreliable, stop and report the blocker rather than hiding it with tooling changes.

Follow every requirement, boundary, research rule, validation gate, rollback rule, acceptance criterion, and reporting requirement in `docs/refactoring/handoff-phase-19.md`. Implement rather than merely plan. Research current stable supported versions from official primary sources at execution time and record the date and direct links.

Upgrade test-only families as independently reversible logical batches: first `Microsoft.NET.Test.Sdk` and Moq, then coverlet and its report pipeline, then evaluate xUnit VS runner 4.0 against xUnit 2.9.3. Adopt runner 4.0 only if official compatibility plus required CLI and IDE discovery are proven; otherwise retain 3.1.5 and document the constraint. Do not upgrade xUnit merely to force runner compatibility.

Compare exact test discovery and execution counts before and after for every test project. Generate equivalent before/after coverage, validate modules/sources/formats and actual report consumption, and establish defensible enforced thresholds by layer. Do not invent thresholds, weaken tests, or accept a plausible percentage when assemblies or sources disappeared. Keep all test and coverage packages test-only and prove the resolved production package graph is unchanged.

Run restore, build, test, coverage, audit, Compose, OpenAPI, and repository validation commands sequentially with the pinned SDK. Record only checks actually executed; never report unavailable IDE, Docker, coverage-consumer, or real-service gates as passed. Create:

`docs/refactoring/phase-19-report.md`

Do not change production behavior or packages, service images/data/volumes, API contracts, xUnit framework major, VSIX tooling, or Phase 20+ work. Preserve every pre-existing and concurrent-agent change. Do not reset, clean, stash, checkout, rebase, commit, push, merge, change branches, create another worktree, delete material data, alter a production-like environment, or open a pull request without explicit authorization.
