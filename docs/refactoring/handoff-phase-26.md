# Phase 26 Execution Handoff — Frontend Inception and Foundation

## Mission

Create the Procurement Hub frontend from zero through three independently reviewable gates. Phase 26A makes repository and technology decisions without creating application code. Phase 26B bootstraps the approved Angular workspace and enforceable Clean Architecture. Phase 26C delivers the secure accessible shell and Organization, Supplier, and Catalog experience.

Work in the authoritative checkout:

`C:\Users\alexa\source\repos\TemplateCleanArchNET`

## Mandatory sequence

Execute and report each subphase in order:

1. `docs/refactoring/handoff-phase-26a.md` — repository and technology decisions;
2. `docs/refactoring/handoff-phase-26b.md` — workspace and architecture bootstrap;
3. `docs/refactoring/handoff-phase-26c.md` — application shell and master data.

Do not begin 26B until all blocking D26A decisions are explicitly approved and `phase-26a-report.md` is `Ready`. Do not begin 26C until `phase-26b-report.md` is `Ready`. A handoff file is not implementation evidence.

## Global constraints

- Phases 23–25 must be complete and green.
- Preserve current API v1/v2 contracts and all unrelated/concurrent changes.
- Re-query official sources on the execution date before pinning Node, Angular, TypeScript, Tailwind, test tools, generators, or component packages.
- Use exact versions and a committed lockfile; do not use floating `latest` in manifests or automation.
- Do not create a remote repository, push, publish a package/image/site, or provision hosting without explicit authorization.
- Keep the frontend dependency direction `Domain <- Application <- Infrastructure / Presentation <- Bootstrap`.
- Domain and Application remain framework-free TypeScript. Generated DTOs, Angular, DOM, browser storage, and transport types stay out of those layers.
- The backend remains authoritative for permissions, business rules, calculations, and state transitions.
- Record every added direct/transitive package, license, support status, rationale, and rejected alternative.

## Current evidence baseline

These observations were checked on 2026-10-18. Revalidate rather than blindly copying them:

- Angular publishes an official compatibility table for Angular, Node.js, TypeScript, and RxJS and currently documents standalone, Signals, stable Signal Forms for the applicable line, and Vitest for new CLI projects.
- Tailwind publishes an official Angular installation guide.
- Official shadcn/ui templates do not list Angular. Do not install React/TSX shadcn components into Angular or describe them as Angular support.
- spartan/ui is a third-party Angular-oriented shadcn-style option built around Tailwind and Angular CDK with installed behavior primitives and copied styling code; its maintenance, compatibility, licenses, accessibility, upgrade model, and supply-chain implications require explicit evaluation.
- Angular Material/CDK and PrimeNG are alternatives with different ownership, styling, component-coverage, accessibility, bundle, and lock-in tradeoffs.

Primary starting sources:

- `https://angular.dev/reference/versions`
- `https://angular.dev/tools/cli/setup-local`
- `https://angular.dev/guide/signals`
- `https://angular.dev/guide/forms`
- `https://angular.dev/guide/testing`
- `https://tailwindcss.com/docs/installation/framework-guides/angular`
- `https://ui.shadcn.com/docs/installation`
- `https://www.spartan.ng/documentation/installation`
- `https://material.angular.dev/`
- `https://primeng.org/`

## Closure criteria

After 26C, create `docs/refactoring/phase-26-report.md` that reconciles D26A decisions with the actual 26B/26C implementation, package graph, licenses, architecture, UI primitives, routes, security, accessibility, generated-client process, tests/counts, commands, limitations, rollback, and gates.

Phase 26 is `Ready` only when:

- all three subphase reports are `Ready` and mutually consistent;
- the approved repository topology and exact toolchain are reproducible from a clean checkout;
- architecture-boundary negative mutations fail as intended;
- the generated client is reproducible and unmodified;
- authentication and master-data browser journeys pass;
- required frontend and affected backend gates are green.

Do not commit/push/merge, change branches/worktrees, create a remote repository or PR, deploy, or delete resources without explicit authorization.
