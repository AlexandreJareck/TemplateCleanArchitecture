# Phase 26B report — Frontend workspace and architecture bootstrap

Specification: `docs/refactoring/handoff-phase-26b.md`, executed through `docs/refactoring/execute-phase-26.md` on 2026-10-18.

## Release decision

**Ready.** The Angular workspace is reproducible from the exact `pnpm-lock.yaml`; its clean architecture, generated-client boundary, UI foundation, runtime configuration, localization, layered tests, negative mutations, production bundle, browser smoke, licenses, vulnerability audit, secret scan and affected backend contracts all passed. Phase 26C may begin.

The spartan and Orval evidence spikes succeeded, so neither pre-approved fallback was triggered. Angular Material was not installed. NSwag was not installed.

## Starting state and creation

- Repository: `C:\Users\alexa\source\repos\TemplateCleanArchNET`; starting HEAD `ad06cad64d993cc73cfa490a7bff8262e3f2f356` with only the approved Phase 26A documentation changes present.
- Workspace: `Client/Template.WebApp`, with scoped `Client/AGENTS.md` and `Client/CLAUDE.md`.
- Runtime: official portable Node 24.22.0 archive, SHA-256 `f9683a19877e12a9b167305c8db71040d812876359de21f0d2e97c3f321f80c3`, used with Corepack/pnpm 12.14.0 from git-ignored validation storage.
- Generator command:

```text
pnpm dlx @angular/cli@22.2.3 new template-web-app --directory Client/Template.WebApp --routing --style css --standalone --strict --skip-git --skip-install --package-manager pnpm --ssr=false --test-runner=vitest --zoneless --ai-config none --file-name-style-guide 2025 --defaults
```

The generated demo was removed. `.node-version`, `.nvmrc`, `packageManager`, `engines`, exact dependency versions, the lockfile and `pnpm-workspace.yaml` pin the reproducible environment. The final frozen install verified 654 lock entries against pnpm's supply-chain policy.

## Toolchain and dependency inventory

Every direct dependency is exact-pinned. Registry metadata, peers and licenses were rechecked immediately before installation.

| Purpose | Direct packages | License |
|---|---|---|
| Angular runtime and accessible behavior | Angular/CDK 22.2.3, tslib 2.8.1, RxJS 7.8.2 | MIT / Apache-2.0 / 0BSD |
| Localization and platform formatting | Transloco 8.4.0; built-in `Intl` (Luxon 3.7.2 remains an approved exact dependency for later timezone needs) | MIT |
| UI foundation | spartan brain 1.8.0, ng-icons core/lucide 33.4.0, class-variance-authority 0.7.1, clsx 2.1.1, tailwind-merge 3.5.0, tw-animate-css 1.4.0 | MIT / ISC / Apache-2.0 |
| Build and styling | Angular build/CLI/compiler 22.2.3, TypeScript 6.0.3, Tailwind/PostCSS 4.3.3/8.5.30 | MIT / Apache-2.0 |
| Contract generation | Orval 8.43.0 | MIT |
| Quality gates | Vitest 5.0.5, jsdom 30.1.3, Playwright 1.64.1, axe 4.14.0, ESLint 10.13.0, angular-eslint 22.6.0, typescript-eslint 8.71.2, dependency-cruiser 18.6.0, Prettier 3.9.10 | permissive licenses accepted by the inventory policy |

The final transitive inventory contained 458 dependency records. All use approved permissive licenses; `union@0.5.0` is the sole metadata exception because its package omits the SPDX identifier while its distributed `LICENSE` contains the MIT text.

The spartan CLI was used only for initial generation. It was removed from the installed graph after the audit found high-severity advisories in its unused Nx/Undici/braces tooling chain. Future updates use an exact `pnpm dlx @spartan-ng/cli@<reviewed-version>` in a disposable checkout. The final audit reports no known vulnerabilities and the workspace contains no Nx dependency.

## Architecture and runtime

Implemented and enforced dependency direction:

```text
Domain <- Application <- Infrastructure / Presentation <- Bootstrap
```

- Domain demonstrates framework-free formatting behavior with `Intl`.
- Application owns `Department`, the closed `ApplicationError` union, `Result`, `DepartmentReader`, and `LoadDepartments`.
- Infrastructure owns all 88 Orval files, an Angular `HttpClient` adapter, RFC Problem Details mapping, RxJS cancellation at the async boundary, Transloco loading, and typed runtime configuration.
- Presentation owns the routed Angular primitive gallery, local Signals and stable Angular 22 Signal Forms. It never imports the generated client.
- Bootstrap alone wires the runtime config, router, HTTP interceptor, generated transport and Application port.

Dependency Cruiser checked 111 modules and 189 dependencies with no violations. Five controlled mutations proved rejection of Domain→Angular, Application→Infrastructure, Infrastructure→Presentation, Presentation→generated transport, and layer→Bootstrap references. The repository has no `scripts/check-boundaries.ps1`; the required equivalent checks were the frontend graph/mutations plus the existing .NET architecture suite (59/59).

`public/config/runtime-config.json` contains only public `apiBaseUrl`, `buildLabel` and feature flags. It is fetched with `no-store` before bootstrap. API requests remain same-origin by default and can receive an explicitly public base URL through the bootstrap interceptor. No credential, refresh token, storage key or durable-system URL is exposed.

## UI, forms and localization

- Tailwind 4 semantic CSS variables cover colors, typography, spacing, radius, elevation and focus; only the approved light theme exists.
- Selected spartan Helm sources live under Presentation/shared UI with provenance and a reviewed update procedure. The health check passed every dependency, import, migration, naming and scaffold-integrity check.
- The internal gallery covers button, input, native select, checkbox, dialog, alert/toast, table/pagination, skeleton/loading, empty/error states and accessible form behavior. It is a bootstrap gallery, not a Phase 26C product screen or Storybook.
- Signals own local presentation state; RxJS is confined to HTTP/localization boundaries. No NgRx was added.
- Stable Signal Forms binds a typed signal model and accessible required validation.
- Transloco uses pt-BR by default with an English catalog containing the same keys; `Intl` handles currency formatting.
- Playwright + axe initially found an unnamed select and an invalid ARIA label on the loading state. Both were corrected; the final Chromium smoke passed with zero automatically detectable violations.

## OpenAPI pipeline

Orval 8.43.0 generates Angular `HttpClient` services from `docs/refactoring/baseline-phase-25/openapi-v2.json` into Infrastructure. Generated files are never hand-edited and are excluded from Prettier/ESLint mechanical rewriting. `pnpm api:drift` regenerates into a safe temporary directory, compares normalized content and file names, and deletes the temporary output; all 88 files matched.

`DepartmentApiReader` is the handwritten proof adapter. Its tests cover DTO-to-Application mapping, RFC Problem Details mapping and `AbortSignal` cancellation without leaking generated types to Application or Presentation.

## Validation evidence

| Gate | Final result |
|---|---|
| `pnpm install --frozen-lockfile` | Passed; exact lockfile and supply-chain verification |
| `pnpm format:check` / `pnpm lint` / `pnpm typecheck` | Passed |
| Angular/Vitest layered tests | 6 files, 9/9 passed |
| Dependency graph | 111 modules, 189 dependencies, 0 violations |
| Negative architecture mutations | 5/5 rejected by their expected rules |
| Orval drift | 88/88 generated files matched |
| Production build and budgets | Passed; initial 339.81 kB raw / 87.56 kB estimated transfer; lazy gallery 237.59 kB raw / 55.38 kB estimated transfer |
| License inventory | 458 dependency records approved; one documented MIT metadata exception |
| Vulnerability audit | No known vulnerabilities |
| Spartan health check | All checks passed; no changes required |
| Playwright/axe | 1/1 Chromium smoke passed; zero detected accessibility violations |
| Web API build | Passed, 0 warnings and 0 errors |
| .NET architecture | 59/59 passed (rerun with `VSTEST_CONNECTION_TIMEOUT=300` after the default testhost connection timeout) |
| Functional HTTP suite | 322/322 passed |
| OpenAPI v2 recapture | Byte-equivalent after line-ending normalization to `baseline-phase-25` |
| Secret scan | Passed; 5 settings files, 8 compose/env templates, compromised fingerprint absent from 7,214 files |
| `git diff --check` | Passed |

The production optimizer emits a non-blocking warning that it cannot inline 36 nested `&` selectors from the spartan/Tailwind stylesheet into critical CSS. The full stylesheet is emitted and applied, budgets pass, and the browser smoke validates the rendered result. This is retained as a toolchain limitation rather than disabling optimization.

## Scope, rollback and handoff

No login/session behavior, guard, procurement navigation, master-data workflow, backend contract, database, container, hosting, CI, remote or deployment was added. Phase 33 still owns production hosting and runtime injection/caching policy.

Rollback is removal of `Client/` plus this report and the Phase 26A documentation changes. No external or persistent runtime resource was created; Playwright's local browser cache and git-ignored validation artifacts are disposable. No commit, push, branch change, remote or PR was created.

Phase 26C may now implement the product shell, session flow and approved master-data slices against this foundation.
