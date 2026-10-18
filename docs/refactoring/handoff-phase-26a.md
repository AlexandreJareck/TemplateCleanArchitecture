# Phase 26A Execution Handoff — Frontend Repository and Technology Decisions

## Mission

Produce an evidence-based, owner-approved frontend inception plan before any JavaScript package or application code is created. Decide where the frontend lives, how it is bootstrapped, which UI/state/forms/i18n/API/testing tools it uses, how those choices support Clean Architecture, and how the toolchain is maintained.

This is a documentation and decision phase. Do not run project generators, install packages, create a Git repository, or change runtime code.

## Required context

Read root/nested instructions, `docs/refactoring/handoff-phase-26.md`, Phase 23 frontend/API/security/business decisions, Phase 24–25 reports, current v2 OpenAPI, repository validation/package/license conventions, Compose, and API-v1 compatibility policy. Record HEAD and `git status --short` and preserve every pre-existing change.

Research only current primary sources: official Angular/Angular CLI, Node, TypeScript, Tailwind, candidate UI libraries, package managers, OpenAPI generators, testing tools, localization tools, Storybook, and accessibility guidance. Record access date, stable version, support window, peer requirements, license, material advisories, and migration notes. Community popularity may be supporting context, never the sole decision criterion.

## Required decisions

Present alternatives, tradeoffs, evidence, recommendation, and consequence for each decision. Obtain explicit owner approval and record the final answer:

| ID | Decision | Recommended starting point |
|---|---|---|
| D26A-1 | Repository topology | Keep `Client/Template.WebApp` in the existing Git repository for atomic OpenAPI/backend/frontend changes and simpler academic execution; document a separate-repository migration path |
| D26A-2 | Workspace shape | One Angular application without Nx/Turborepo until a second independently built client/library creates a demonstrated need |
| D26A-3 | Package manager | `pnpm` with an exact `packageManager` declaration and committed lockfile; compare npm before approval |
| D26A-4 | Framework toolchain | Current supported stable Angular/CLI with compatible supported Node LTS, TypeScript, and RxJS versions selected from official matrices |
| D26A-5 | Styling | Tailwind CSS using the current official Angular installation path and design tokens expressed through controlled CSS variables |
| D26A-6 | Component strategy | Prefer Tailwind + spartan/ui + Angular CDK only if the third-party health/license/accessibility evidence gate passes; otherwise choose Angular Material/CDK. Keep PrimeNG as the data-heavy alternative |
| D26A-7 | State | Angular Signals for presentation/application state and RxJS at asynchronous boundaries; no NgRx until measured complexity demonstrates a need |
| D26A-8 | Forms | Stable Signal Forms when supported by the pinned Angular line and selected controls; otherwise typed Reactive Forms. Do not use an experimental API in the baseline |
| D26A-9 | API generation | Select NSwag, OpenAPI Generator, or another approved generator through current evidence and explicit deterministic acceptance criteria; verify the selected generator in 26B and keep generated code in Infrastructure |
| D26A-10 | Localization | Runtime-capable pt-BR default with English-ready structure; compare Angular localization with an actively maintained Angular-native runtime alternative |
| D26A-11 | Tests | Angular CLI's current supported unit runner, Playwright E2E, adapter contract tests, accessibility automation, and dependency-boundary tests |
| D26A-12 | Component workbench | Storybook only if its current Angular support, maintenance cost, accessibility integration, and value for shared primitives justify it |
| D26A-13 | Delivery boundary | Local dev server in Phase 26; reproducible production build and runtime configuration contract now; hardened container/hosting and CI deployment remain Phase 33 |
| D26A-14 | Update policy | Supported-major cadence, lockfile and Renovate/Dependabot strategy if approved, vulnerability/license gates, and explicit major-upgrade rehearsals |

## UI-library decision matrix

Evaluate at least these options through current primary documentation, source/license inspection and maintained official demos. If evidence is insufficient, record a narrowly scoped disposable spike for 26B; do not install packages during 26A:

1. **Tailwind + spartan/ui + Angular CDK:** closest Angular-native fit to the shadcn ownership/customization model; assess copied-code ownership, upgrade diffing, package/CLI provenance, component completeness, keyboard/focus behavior, compatibility with the pinned Angular/Tailwind lines, and maintenance bus factor.
2. **Angular Material + CDK:** first-party Angular integration and mature accessibility/behavior; assess Material visual constraints, design-token customization, table/form coverage, bundle impact, and Tailwind coexistence.
3. **PrimeNG:** broad enterprise component/data coverage; assess theming/Tailwind interaction, API surface, bundle size, accessibility, upgrade churn, license/support model, and lock-in.

Official shadcn/ui is not an Angular option unless its official documentation supports Angular on the execution date. Do not port React components ad hoc. If a third-party registry or copied component is proposed, inspect source, transitive dependencies, license, update provenance, and accessibility before approval.

Score each option against: Angular-version compatibility, accessibility, design fidelity, component coverage, data-table needs, theming/tokens, bundle/performance, testing ergonomics, maintenance activity, supply-chain risk, license, upgrade path, and teaching value. Do not select by appearance alone.

## Repository-topology analysis

Compare:

- current monorepo folder `Client/Template.WebApp`;
- a separate local Git repository with an explicit path, ownership, independent CI/release/versioning, OpenAPI artifact flow, cross-repository change protocol, and rollback process.

The current monorepo is the recommendation because this is one product, one team-sized academic template, and backend/frontend contracts evolve together. A separate repository requires an explicit owner decision plus an amendment to D23-2, the repository name/path, remote ownership, access policy, CI, versioning, contract publication, and authorization to create/push it. Never invent or create a remote.

## Required deliverables

Create English technical documentation with English filenames:

- `docs/frontend/technology-decisions.md` containing D26A-1 through D26A-14 and the evidence matrix;
- `docs/frontend/frontend-architecture.md` containing layer/module boundaries, dependency map, state ownership, port/adapter approach, error mapping, and composition root;
- `docs/frontend/design-system-strategy.md` containing tokens, typography, spacing, color, dark-mode decision, responsive breakpoints, primitive ownership, accessibility and selected UI approach;
- `docs/frontend/toolchain-and-maintenance.md` containing exact approved version families, pinning/lock policy, scripts to be created in 26B, update cadence, audits, supported browsers, and rollback;
- focused ADRs for repository topology, UI/component stack, state/forms, API generator, localization, testing, and Storybook decision.

Update the roadmap only if approved decisions materially change it. Do not implement 26B.

## Validation and acceptance

Perform three review passes:

1. **Evidence and currency:** every version/support/license claim has a current primary source and date; shadcn versus Angular-native alternatives are represented accurately.
2. **Architecture and scope:** decisions preserve frontend Clean Architecture, backend authority, security, accessibility, contract generation, and academic maintainability without enterprise-tooling theater.
3. **Executability:** 26B can create the workspace without inventing a package, directory, command, version, token, environment, or unresolved blocking choice.

Run Markdown/link checks if available and `git diff --check`. Create `docs/refactoring/phase-26a-report.md` with all decisions, evidence, rejected alternatives, three reviews, unresolved items, files, validation, and a `Ready`/`Not ready` decision.

Phase 26A is `Ready` only when all D26A decisions are approved and no blocking placeholder remains. Otherwise stop before 26B.

Do not commit/push/merge, change branches/worktrees, run package installers/generators, create repositories/remotes, deploy, or delete resources without explicit authorization.
