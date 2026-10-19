# Execute Phase 26 — Frontend from Zero

Work in the authoritative checkout `C:\Users\alexa\source\repos\TemplateCleanArchNET` and execute `docs/refactoring/handoff-phase-26.md` through its three gated subphases. Read every referenced instruction and report in full. Preserve all pre-existing and concurrent changes.

## Objective

Plan and create the Procurement Hub Angular frontend from zero, from repository-topology and library decisions through a reproducible workspace, enforceable frontend Clean Architecture, secure application shell, design system, generated API integration, master-data screens, and automated tests.

The frontend must be academically understandable and close to corporate practice. Avoid both tutorial shortcuts and unjustified enterprise tooling.

## Evidence baseline to revalidate

The planning review on 2026-10-18 found that Angular maintains an official Angular/Node/TypeScript/RxJS [version compatibility matrix](https://angular.dev/reference/versions), current Angular CLI projects use [Vitest by default](https://angular.dev/guide/testing), and Tailwind provides an official [Angular installation guide](https://tailwindcss.com/docs/installation/framework-guides/angular). Official [shadcn/ui installation templates](https://ui.shadcn.com/docs/installation) do not list Angular. The Angular-oriented [spartan/ui installation](https://www.spartan.ng/documentation/installation) describes Tailwind, Angular CDK, installed behavior primitives, and copied styling code. Compare it with [Angular Material/CDK](https://material.angular.dev/) and [PrimeNG](https://primeng.org/). Treat these as starting evidence, not permission to reuse stale versions; verify all facts again on the execution date.

## Mandatory sequence

### Phase 26A — Repository and technology decisions

Execute `docs/refactoring/handoff-phase-26a.md` first. This is documentation-only.

Research current primary sources and obtain explicit owner approval for D26A-1 through D26A-14: repository topology, workspace shape, package manager, Angular/Node/TypeScript/RxJS line, Tailwind, component library, state, forms, OpenAPI generator, localization, tests, Storybook, delivery boundary, and update policy. This gate was completed on 2026-10-18; the accepted record is `docs/frontend/technology-decisions.md` and `docs/refactoring/phase-26a-report.md` is `Ready`.

Use the existing monorepo with `Client/Template.WebApp` as the recommended topology, but compare a separate repository and document its independent CI/release/OpenAPI consequences. Never create a remote repository or push without explicit authorization.

Explicitly compare:

1. Tailwind + spartan/ui + Angular CDK as the closest Angular-native shadcn-style option;
2. Angular Material + CDK as the first-party alternative;
3. PrimeNG as the broad enterprise-component alternative.

Verify maintenance, Angular/Tailwind compatibility, accessibility, component coverage, theming, bundle impact, copied-code ownership, supply-chain risk, licenses and upgrade strategy. Official shadcn/ui does not currently list Angular templates; do not install React/TSX shadcn code or call it Angular-compatible unless official documentation has changed.

The approved baseline is: existing monorepo at `Client/Template.WebApp`; one CSR standalone Angular CLI application; pnpm 12.14.0; Angular/CLI/CDK 22.2.3; Node 24.22.0 LTS; TypeScript 6.0.3; RxJS 7.8.2; Tailwind 4.3.3; minimal spartan/ui 1.8.0 + CDK with Angular Material fallback only when its evidence gate fails; Signals plus RxJS; no NgRx; stable Signal Forms; Orval 8.43.0 Angular `HttpClient` generation with NSwag 14.7.1 fallback only when its evidence gate fails; Transloco 8.4.0; Vitest, Playwright, axe and dependency-cruiser; no Storybook; local HTTPS/proxy with deployment deferred to Phase 33; exact pins and deliberate upgrade rehearsals.

Create the required English frontend decision documents/ADRs and `docs/refactoring/phase-26a-report.md`. Perform the three required reviews. If any blocking decision remains unapproved, report `Not ready` and stop. Do not install packages or generate code in 26A.

### Phase 26B — Workspace and architecture bootstrap

Proceed only when Phase 26A is `Ready`. Execute `docs/refactoring/handoff-phase-26b.md`.

Re-check official compatibility and use the approved pinned package manager/CLI to create the selected local workspace. If a separate repository was approved, require its exact local path and explicit creation authorization; never invent a path or remote.

Create the exact lockfile, runtime/package-manager declarations, scoped `AGENTS.md` and sibling `CLAUDE.md` at the approved frontend root, strict Angular/TypeScript configuration, Tailwind and approved minimal UI primitives, semantic design tokens, localization foundation, typed non-secret runtime configuration, deterministic OpenAPI generation, and this dependency direction:

`Domain <- Application <- Infrastructure / Presentation <- Bootstrap`

Keep Domain/Application plain TypeScript. Keep Angular, DOM, browser storage, transport and generated DTOs out of them. Presentation cannot import generated clients. Infrastructure maps DTOs and Problem Details through Application-owned ports. Bootstrap is the only client composition root.

Add automated import/dependency boundaries and negative mutation tests. Establish the approved Signals/RxJS and forms conventions. Add deterministic scripts for frozen install, format, lint, type-check, layered tests, architecture tests, build/budgets, generated-client drift, audits, and Playwright bootstrap. Produce a minimal runnable skeleton, not feature screens.

Create `docs/refactoring/phase-26b-report.md`. If clean-checkout reproduction, architecture mutations, generator drift, license/security audit, build or minimal browser smoke is not green, report `Not ready` and stop before 26C.

### Phase 26C — Application shell and master data

Proceed only when Phase 26B is `Ready`. Execute `docs/refactoring/handoff-phase-26c.md`.

Implement the approved secure session/refresh-cookie/CSRF behavior, bounded refresh concurrency, logout/revocation, route guards, permission directives, RFC Problem Details mapping, responsive accessible shell, pt-BR default/English-ready localization, and Organization/Supplier/Catalog screens.

Implement every slice through frontend Domain/Application/Infrastructure/Presentation/Bootstrap as applicable. Keep backend permissions, business rules, calculations and state transitions authoritative. Do not call BrasilAPI, BCB, blob storage, RabbitMQ or Durable Functions directly from the browser.

Test framework-free use cases, generated-client adapters, session races, components, accessibility, architecture boundaries and Playwright journeys for authentication, authorization, supplier/catalog CRUD/status, validation, enrichment fallback, pagination/filtering and concurrency conflict.

Create `docs/refactoring/phase-26c-report.md`, then reconcile all decisions and evidence into `docs/refactoring/phase-26-report.md`.

## Final validation and response

Run every required frontend gate with the frozen lockfile plus affected backend functional/OpenAPI gates, dependency/license/vulnerability audit, secret scan and `git diff --check`. Mark every gate `Passed`, `Failed`, `Skipped`, or `Blocked`; any required non-passing gate makes Phase 26 `Not ready`.

Lead the final response with `Ready` or `Not ready`, followed by approved repository/toolchain decisions, created workspace, architecture, UI/design system, session/security, implemented screens, tests/counts, audits, unresolved risks, rollback and report paths.

Do not commit/push/merge, change branches/worktrees, create a remote repository or PR, deploy/publish, provision infrastructure, or delete resources without explicit authorization.
