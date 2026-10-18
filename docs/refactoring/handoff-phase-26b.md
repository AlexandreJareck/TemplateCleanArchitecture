# Phase 26B Execution Handoff — Frontend Workspace and Architecture Bootstrap

## Mission

Create the approved frontend repository/workspace from zero and prove its toolchain, dependency boundaries, UI foundation, generated-client pipeline, and clean-build reproducibility. Deliver a minimal runnable skeleton, not product screens.

## Preconditions and hard stops

- Phases 23–25 and Phase 26A are complete and `Ready`.
- Read `phase-26a-report.md`, every D26A decision/ADR, current v2 OpenAPI, security/session contracts, repository instructions, and validation conventions.
- Record HEAD/dirty tree and preserve concurrent work.
- Re-check the approved exact versions and peer requirements immediately before installation. If official support or licenses changed since 26A, stop and revise the decision instead of forcing installation.
- If D26A selected a separate repository, require the exact approved local path, ownership and explicit authorization before creating it. Do not create a remote or push. Otherwise create `Client/Template.WebApp` in this repository.

## Reproducible creation

Use the approved package manager and a pinned Angular CLI invocation; do not depend on an unrecorded global CLI. Capture the exact generator command and options. The baseline should be a client-rendered routed standalone Angular application with strict TypeScript/compiler settings and no SSR unless D26A explicitly approved it.

Create and commit the approved lockfile and declare package-manager/runtime constraints. Add only packages approved by D26A. For every package, record direct purpose, version, source, license and transitive impact. Remove generator sample/demo content that is not part of the verified skeleton.

Add scoped instructions at the approved frontend root (`Client/AGENTS.md` in the recommended monorepo topology, or root `AGENTS.md` in an explicitly approved separate repository) plus the sibling `CLAUDE.md`, following repository rules. Document Windows and cross-platform commands where behavior differs.

## Clean Architecture structure

Establish and document an enforceable structure equivalent to:

```text
src/app/
  domain/
  application/
    ports/
    use-cases/
  infrastructure/
    api/generated/
    api/adapters/
    auth/
    telemetry/
  presentation/
    shell/
    shared-ui/
    features/
  bootstrap/
```

The exact names may follow approved ADRs, but dependency direction remains:

`Domain <- Application <- Infrastructure / Presentation <- Bootstrap`

- Domain/Application contain plain TypeScript and compile/test without Angular TestBed, DOM, generated DTOs, storage, or browser globals.
- Application ports describe client use-case needs instead of mirroring every HTTP endpoint.
- Infrastructure owns generated OpenAPI code and maps transport DTOs/Problem Details into Application-owned models/errors.
- Presentation owns Angular components, routes, forms and local view state but never imports generated API code.
- Bootstrap alone wires runtime configuration and concrete adapters.
- Shared UI contains approved presentation primitives only; it is not a dumping ground for business or infrastructure code.

Configure path aliases only where they make boundaries clearer. Add ESLint/import-boundary or approved dependency-graph enforcement and negative mutation tests for every forbidden direction.

## Styling and design-system foundation

Install only the D26A-approved Tailwind/component stack. If spartan/ui or another copied-code approach is selected:

- add only the minimal primitives needed for 26C;
- keep copied sources in the approved Presentation/shared-UI location;
- record their provenance/version and define an update/diff procedure;
- review generated/copied code before accepting it;
- verify keyboard, focus, ARIA, reduced-motion, high-contrast, light/dark decision, and design-token behavior.

Implement approved CSS variables/design tokens for semantic color, typography, spacing, radius, elevation, focus and breakpoints. Do not scatter raw brand colors or arbitrary spacing across components. Configure class sorting/formatting only through approved pinned tools.

Create a small primitive gallery or Storybook only if D26A approved it. Include button, input, select, checkbox, dialog, alert/toast, table/pagination, skeleton/loading, empty state, error state and form-field behavior required by 26C; do not import an entire component suite without use.

## State, forms, localization, and configuration

- Establish the approved Signals/RxJS boundary and a documented state-ownership pattern; do not add NgRx unless D26A approved it.
- Configure the approved stable forms approach with typed examples and accessible error behavior.
- Configure pt-BR default and English-ready localization without embedding business rules in translations.
- Define typed runtime configuration for API base URL and non-secret feature/build metadata. Browser configuration must never contain credentials, refresh tokens, storage keys or Durable system URLs.
- Use `Intl` and built-in platform capabilities for date/number/currency where sufficient; add a library only if approved by D26A.

## OpenAPI client pipeline

Pin the approved generator and configuration. Generate from the captured v2 OpenAPI artifact into Infrastructure. Add deterministic clean-generation and drift-check commands. Generated files are never hand-edited and are excluded from formatting rules only where technically required, not from security/license review.

Add at least one handwritten adapter with a disposable contract test proving DTO mapping, cancellation and RFC Problem Details mapping without allowing generated types into Application/Presentation. Do not call live production services.

## Tooling and test baseline

Provide deterministic commands for clean install with frozen lockfile, development, formatting check/write, lint, type-check, framework-free tests, adapter contract tests, Angular component tests, architecture tests/mutations, production build, bundle-budget check, generated-client generate/drift, dependency/license/vulnerability audit, and E2E bootstrap.

Use the current Angular CLI-supported unit runner approved in 26A and Playwright for later E2E. Add one passing test per layer and a minimal browser smoke showing the skeleton loads. Configure accessibility automation if approved. Storybook/build tests apply only when selected.

Production hosting/deployment remains Phase 33, but produce a deterministic production bundle and document runtime configuration injection/caching assumptions. Add a container only if D26A explicitly placed it in 26B.

## Out of scope

- Login/session implementation, guards, procurement navigation, master-data screens, or real feature workflows;
- NgRx, Nx/Turborepo, SSR, micro-frontends, public deployment, remote repository creation, or extra UI packages not approved in 26A;
- backend contract changes merely to make generation easier.

## Validation and acceptance

Run install with frozen lockfile, format check, lint, type-check, all layered/architecture tests, negative mutations, generated-client clean/drift, production build, bundle budget, dependency/license/vulnerability audit, secret scan, minimal Playwright smoke, affected backend OpenAPI/functional checks, and `git diff --check`.

Create `docs/refactoring/phase-26b-report.md` with generator command, file tree, exact toolchain/packages/licenses, architecture graph, design tokens/primitives, state/forms/i18n decisions applied, OpenAPI pipeline, commands/counts, mutations, bundle output, audits, limitations, rollback and gates.

Phase 26B is `Ready` only when a clean checkout can reproduce the workspace and all required gates pass. Otherwise do not begin 26C.

Do not commit/push/merge, change branches/worktrees, create a remote/PR, deploy, or delete resources without explicit authorization.
