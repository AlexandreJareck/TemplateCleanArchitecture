# Frontend technology decisions

Status: **Accepted by the owner**
Evidence reviewed: 2026-10-18 (America/Sao_Paulo)
Owner approval: 2026-10-18

This document records the owner's explicit approval of D26A-1 through D26A-14. D26A-6 was approved after clarifying that spartan/ui is the selected owned-design approach rather than the most widely adopted Angular component library. D26A-9 was revised after comparing .NET and JavaScript generators and was then explicitly approved with Orval as the primary generator.

## Decision summary

| ID | Accepted decision | Principal consequence |
|---|---|---|
| D26A-1 | Keep the SPA in this repository at `Client/Template.WebApp`. | Backend, OpenAPI artifact, generated client and UI change atomically. |
| D26A-2 | One routed, client-rendered, standalone Angular CLI application; no Nx, Turborepo, SSR or micro-frontends. | Lowest adequate workspace complexity; revisit only for a second independently built client/library. |
| D26A-3 | pnpm 12.14.0, exact `packageManager`, committed `pnpm-lock.yaml`, frozen installs. | Deterministic installs and a smaller content-addressed store; contributors need the pinned pnpm version. |
| D26A-4 | Angular/CLI 22.2.3, Node 24.22.0 LTS, TypeScript 6.0.3 and RxJS 7.8.2. | All versions fit Angular 22's official matrix; Node 26 Current and TypeScript 7 are deliberately excluded. |
| D26A-5 | Tailwind CSS 4.3.3 through the official PostCSS integration and semantic CSS variables. | Utility CSS remains subordinate to controlled tokens and modern-browser requirements. |
| D26A-6 | Tailwind + spartan/ui 1.8.0 + Angular CDK 22.2.3, with only reviewed primitives copied/installed. Angular Material/CDK is the fallback. | Own the copied `helm` styling code; update `brain`/CLI together and manually diff customized `helm` code. |
| D26A-7 | Angular Signals for local/view and use-case state; RxJS only at asynchronous/Angular HTTP boundaries; no NgRx. | Small explicit stores, no global event architecture. |
| D26A-8 | Stable Angular Signal Forms from Angular 22. | New signal-based application with typed schemas; adapt domain/application models to plain form models at the Presentation boundary. |
| D26A-9 | Orval 8.43.0 as an exact pnpm development dependency, using its Angular `HttpClient` output in Infrastructure; NSwag.ConsoleCore 14.7.1 is the pre-approved fallback if the Phase 26B spike proves Orval unsuitable. | No Java runtime; the generator shares the pinned Node/pnpm toolchain, while handwritten adapters contain generated types and normalize cancellation/errors. |
| D26A-10 | Transloco 8.4.0 for runtime messages, pt-BR default and checked-in English resources; use platform `Intl` for formatting. | Runtime switching without separate application builds; no language preference is persisted in Phase 26. |
| D26A-11 | Angular CLI Vitest, Playwright, `@axe-core/playwright`, adapter fixtures and dependency-cruiser architecture tests with controlled mutations. | One unit runner plus real-browser journeys; manual keyboard/accessibility checks remain required. |
| D26A-12 | Do not install Storybook in Phase 26. | A small in-app primitive gallery covers bootstrap evidence without a second build surface. Reconsider when shared primitives have multiple consumers. |
| D26A-13 | Local HTTPS Angular dev server with a same-origin API proxy; deterministic production bundle and typed non-secret runtime configuration. Hosting/container/CI deployment remain Phase 33. | Secure cookies can be exercised now without selecting a production host. |
| D26A-14 | Exact pins and lockfile; monthly patch/minor review, supported-major review before current major leaves active support, Renovate/Dependabot deferred until CI exists, explicit major rehearsals. | Vulnerability, license, generated-client drift and frozen-install gates block release. |

## D26A-1 repository topology

### Selected: existing monorepo

`Client/Template.WebApp` stays under the existing Git root. This matches accepted D23-2 and allows one pull request to carry an additive API change, its captured v2 OpenAPI artifact, regenerated client, adapter and UI. The existing repository gates remain the release authority.

### Rejected for now: separate repository

A separate repository would need an owner, exact local/remote path, access policy, independent CI and release version, an immutable OpenAPI publication channel, consumer-driven compatibility checks, a cross-repository change protocol and coordinated rollback. It would turn atomic contract changes into ordered releases. No second team or independent deployment currently justifies that cost. Migration remains possible by extracting `Client/Template.WebApp` with history after a new owner decision; no remote is created in Phase 26.

## D26A-2 and D26A-3 workspace and package manager

One Angular application is sufficient for the one product and one delivery unit. Nx/Turborepo would add another project graph, executor and upgrade surface without a second buildable product.

pnpm is selected over the npm 11.19.1 bundled with Node 24.22.0 because the exact `packageManager` declaration and content-addressed store improve reproducibility and disk use. npm remains viable and simpler for unfamiliar contributors, but offers no benefit that outweighs the accepted pin. Phase 26B uses `pnpm install --frozen-lockfile`, forbids floating versions in the manifest, and records allowed dependency build scripts.

## D26A-4 framework compatibility

Angular 22 is in active support until June 2027 and LTS until June 2028. Its official compatibility table accepts Node `^22.22.3 || ^24.15.0 || ^26.0.0`, TypeScript `>=6.0.0 <6.1.0`, and RxJS `^6.5.3 || ^7.4.0`. Node 24.22.0 is LTS; Node 26 is Current, so the LTS line is selected. TypeScript 7.0.2 is newer but outside Angular's supported range.

Exact package versions are re-queried immediately before the first lockfile is created. A changed patch within the approved lines may replace the values above only after recording the reason and rerunning the compatibility/license check; a major change requires a new owner decision.

## D26A-5 styling

Tailwind 4.3.3 is installed through `tailwindcss`, `@tailwindcss/postcss` and PostCSS as documented by Tailwind for Angular. Tailwind 4 requires modern CSS features and its published minimum browser versions are compatible with Angular 22's Baseline browser policy. Components consume semantic variables (`--color-surface`, `--color-action`, `--space-*`, `--radius-*`) rather than raw palette utilities for product meaning.

## D26A-6 UI library evidence matrix

Scoring: 1 poor, 3 adequate, 5 strong. Bundle score reflects the ability to import/copy only what is used, not an unmeasured byte claim.

| Criterion | spartan/ui + CDK | Angular Material + CDK | PrimeNG 22 |
|---|---:|---:|---:|
| Angular 22 compatibility | 5 | 5 | 5 |
| Accessibility foundation | 4 | 5 | 3 |
| Product design fidelity | 5 | 3 | 3 |
| Component coverage | 4 | 4 | 5 |
| Data-table needs | 3 | 4 | 5 |
| Token/theming control | 5 | 3 | 4 |
| Bundle selectivity | 4 | 4 | 3 |
| Testing ergonomics | 4 | 5 | 4 |
| Maintenance/support | 3 | 5 | 4 |
| Supply-chain/licensing | 4 | 5 | 2 |
| Upgrade path | 3 | 5 | 2 |
| Teaching value | 5 | 4 | 3 |
| **Total / 60** | **49** | **52** | **43** |

The scores are not a popularity contest. Angular Material scores highest on first-party maintenance and accessibility, but its Material visual/theming contract works against the requested owned, shadcn-style design language. Spartan's evidence gate passes narrowly: its official policy supports Angular 22, its repository and packages are MIT, current documentation requires Tailwind 4 and CDK, it has broad primitive coverage, and `brain` owns ARIA/keyboard/focus behavior while `helm` is copied. The cost is real: copied code stops receiving automatic fixes, a smaller maintainer base raises bus-factor risk, and every update needs a provenance diff plus browser/a11y tests. Those controls are mandatory.

Angular Material/CDK is the pre-approved fallback if the Phase 26B spike finds peer conflicts, unacceptable transitive dependencies, inaccessible behavior or generator output that cannot pass review. PrimeNG is rejected as the baseline: v22 moved to Community/Commercial licensing with a license key, has a large API surface and published deprecations through v24; its broad table/widget coverage does not justify that lock-in here. Official shadcn/ui still lists framework-specific installation templates without Angular; React/TSX code will not be installed or ported ad hoc.

Only the primitives required by Phase 26 are accepted. A semantic HTML table and server paging wrapper are preferred over adding a second table engine.

## D26A-7 and D26A-8 state and forms

Application use cases expose immutable snapshots and commands. Presentation uses Angular signals for component/route state. Infrastructure may use RxJS for `HttpClient`, cancellation and single-flight refresh, but Observables do not leak into Domain. Application remains Angular-free; it receives small framework-free store interfaces/implementations rather than Angular signals.

Angular 22 marks Signal Forms stable. Form models are plain Presentation objects; conversion to Application input models is explicit. Client validation mirrors input-quality rules for usability only. Backend responses remain authoritative for invariants, policy, authorization and concurrency. NgRx is reconsidered only with evidence of cross-feature event coordination that scoped stores cannot express clearly.

## D26A-9 generated API

Orval 8.43.0 is MIT, runs in the already approved Node/pnpm toolchain, has first-class Angular output, and documents both Angular `HttpClient` services and signal-first `httpResource` generation. The accepted baseline deliberately selects `HttpClient`: generated transport code stays in Infrastructure, while Signals remain an Application/Presentation state choice. The Orval repository tests against Angular 22 and TypeScript 6. Its rapid release cadence is controlled by an exact dependency pin, committed lockfile, deterministic output comparison and the D26A-14 review policy.

NSwag.ConsoleCore 14.7.1 is the pre-approved fallback because it is MIT, runs on the repository's .NET toolchain and generates Angular TypeScript clients. Phase 26B may use it only if the recorded Orval spike finds a contract incompatibility, nondeterministic output, unacceptable dependency/license result or an Angular compilation failure; the Phase 26B report must record the trigger and comparison. `ng-openapi-gen` 1.1.0 was a viable Angular-native Node alternative but has a narrower ecosystem and configuration surface. Kiota was rejected because its TypeScript generation remains experimental and is not Angular `HttpClient`-native. OpenAPI Generator 7.26.0 was rejected because its npm CLI is a wrapper over a Java JAR (or requires a Java-bearing container), adding a runtime solely for code generation.

The committed source is `docs/refactoring/baseline-phase-25/openapi-v2.json`; generation uses a checked-in Orval configuration. Output is deterministic, regenerated cleanly and never hand-edited. A drift script regenerates to a temporary directory and compares bytes.

Generated clients and DTOs stay in `infrastructure/api/generated`. Handwritten adapters map them to Application models and the closed `ApplicationError` union, apply `If-Match`/`Idempotency-Key`, preserve money strings, and translate cancellation and RFC 9457 Problem Details.

## D26A-10 localization

Angular's built-in localization is first-party and well suited to compile-time localized bundles, but runtime switching normally implies separate localized builds/reload and does not match the runtime-capable requirement cleanly. Transloco 8.4.0 is the latest stable line (9.0 is alpha), MIT, signal/standalone aware and supports runtime switching. Only core Transloco is approved; persistence, message-format and locale plugins are not added initially. Checked-in `pt-BR.json` and `en.json` must have identical key sets. `Intl.DateTimeFormat` and `Intl.NumberFormat` handle dates, numbers and currency.

## D26A-11 and D26A-12 testing and workbench

New Angular CLI projects use Vitest. Framework-free Domain/Application tests run directly in Vitest without TestBed or DOM. Adapter contract tests use deterministic fixtures and fake transport. Angular component tests use the CLI runner. Playwright covers Chromium initially for required journeys; Firefox/WebKit are a scheduled compatibility smoke before release. `@axe-core/playwright` catches automatable WCAG failures; keyboard, focus order, zoom, high contrast and screen-reader behavior retain a manual checklist.

Storybook 10 supports Angular 18+ and has accessibility integration, but adds another builder/configuration/dependency surface. One application and no separately consumed component package do not yet justify it. Phase 26B instead provides a development-only primitive gallery route excluded from production navigation. Storybook is reconsidered when two consumers or a dedicated design-system release workflow exists.

## D26A-13 and D26A-14 delivery and maintenance

Phase 26 delivers local HTTPS development, same-origin proxying, a deterministic production bundle, Angular budgets and runtime configuration loaded before bootstrap. Runtime configuration contains only API base path, build metadata and non-secret feature flags. Production hosting, container hardening, CSP headers at the host, CI and deployment stay in Phase 33.

Dependency policy:

- exact direct versions and a committed lockfile;
- frozen installs and package-signature/integrity checks where supported;
- monthly patch/minor review within supported majors;
- explicit rehearsal and owner approval for major Angular, Node, pnpm, Tailwind, spartan, Transloco, Orval or Playwright changes;
- security audit blocks high/critical findings; every non-permissive/unknown license blocks until reviewed;
- spartan `brain` and CLI remain aligned, health checks run after updates, and customized `helm` files are manually diffed;
- no automated dependency-update bot until Phase 33 selects CI; then grouped Angular/tooling updates are preferred over isolated churn.

## Primary evidence

- [Angular version compatibility](https://angular.dev/reference/versions), [release/support policy](https://angular.dev/reference/releases), [Signal Forms API](https://angular.dev/api/forms/signals/form), [Vitest testing guide](https://angular.dev/guide/testing)
- [Node.js releases](https://nodejs.org/en/about/previous-releases), [Node 24 archive](https://nodejs.org/en/download/archive/v24)
- [pnpm releases](https://pnpm.io/blog?type=releases), [pnpm 12.14.0 registry metadata](https://registry.npmjs.org/pnpm/latest)
- [Tailwind Angular guide](https://tailwindcss.com/docs/installation/framework-guides/angular), [Tailwind upgrade/browser requirements](https://tailwindcss.com/docs/upgrade-guide)
- [spartan installation](https://www.spartan.ng/documentation/installation), [version support](https://www.spartan.ng/documentation/version-support), [update guide](https://www.spartan.ng/documentation/update-guide), [MIT repository](https://github.com/spartan-ng/spartan)
- [Angular Material](https://material.angular.dev/), [Angular components repository](https://github.com/angular/components)
- [PrimeNG installation](https://primeng.dev/installation), [v22 migration/licensing](https://primeng.dev/migration/v22), [accessibility](https://primeng.dev/guides/accessibility)
- [shadcn/ui installation](https://ui.shadcn.com/docs/installation)
- [Orval Angular generator](https://orval.dev/docs/guides/angular/), [Orval package](https://www.npmjs.com/package/orval), [Orval repository](https://github.com/orval-labs/orval)
- [NSwag releases](https://github.com/RicoSuter/NSwag/releases), [NSwag.ConsoleCore](https://www.nuget.org/packages/NSwag.ConsoleCore), [ng-openapi-gen](https://www.npmjs.com/package/ng-openapi-gen), [Kiota](https://learn.microsoft.com/openapi/kiota/using), [OpenAPI Generator installation](https://openapi-generator.tech/docs/installation/)
- [Transloco releases](https://github.com/jsverse/transloco/releases), [Transloco package](https://www.npmjs.com/package/@jsverse/transloco)
- [Playwright installation](https://playwright.dev/docs/intro), [accessibility testing](https://playwright.dev/docs/accessibility-testing)
- [Storybook Angular requirements](https://storybook.js.org/docs/get-started/install/), [support policy](https://storybook.js.org/docs/releases)
