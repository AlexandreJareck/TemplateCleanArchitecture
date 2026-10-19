# Execute Phase 26B

Read and execute `docs/refactoring/handoff-phase-26b.md` in full in `C:\Users\alexa\source\repos\TemplateCleanArchNET`.

Verify Phase 26A is `Ready` and every D26A choice is approved. Re-check official compatibility, then reproducibly create `Client/Template.WebApp` with pnpm 12.14.0, Angular/CLI/CDK 22.2.3, Node 24.22.0 LTS, TypeScript 6.0.3, RxJS 7.8.2 and the exact lockfile. Implement Tailwind 4.3.3, minimal spartan/ui 1.8.0 + CDK, semantic tokens, frontend Clean Architecture, dependency enforcement, Orval 8.43.0 Angular `HttpClient` generation, Signals/RxJS, Signal Forms, Transloco 8.4.0, layered tests and deterministic scripts. Angular Material and NSwag are fallbacks only under the conditions recorded in D26A-6/D26A-9, with the trigger documented in the Phase 26B report. Do not add packages outside the approved decision set or implement Phase 26C screens.

Run every required bootstrap gate and create `docs/refactoring/phase-26b-report.md`. If the clean workspace, generated-client drift, architecture mutations, license/security audits, production build or minimal browser smoke is not green, report `Not ready` and stop before Phase 26C.
