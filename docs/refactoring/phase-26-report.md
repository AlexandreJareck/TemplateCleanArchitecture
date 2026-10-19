# Phase 26 report — Frontend inception, foundation and master data

Specification: `docs/refactoring/handoff-phase-26.md`, executed through `docs/refactoring/execute-phase-26.md` on 2026-10-18/19. Subphase evidence: `phase-26a-report.md`, `phase-26b-report.md`, `phase-26c-report.md`.

## Release decision

**Ready.** Phase 26A/26B/26C are mutually consistent and independently `Ready`. The approved Angular application is reproducible from exact pins and `pnpm-lock.yaml`; boundaries and negative mutations pass; all 88 generated API files reproduce; secure authentication and master-data journeys pass in Chromium; affected backend and contract gates remain green.

## Reconciled decisions and implementation

| Decision | Implemented outcome |
|---|---|
| D26A-1/2 | One Angular CLI CSR application at `Client/Template.WebApp` in this repository; no Nx, Turborepo, SSR or micro-frontends |
| D26A-3/4 | pnpm 12.14.0, Node 24.22.0 LTS, Angular/CLI/CDK 22.2.3, TypeScript 6.0.3 and RxJS 7.8.2, all exact-pinned |
| D26A-5 | Tailwind CSS 4.3.3 with semantic CSS variables and approved light theme |
| D26A-6 | Minimal copied spartan/ui 1.8.0 primitives plus Angular CDK; Material fallback not triggered |
| D26A-7/8 | Signals for local/presentation state, RxJS at async boundaries, stable Signal Forms; no NgRx |
| D26A-9 | Orval 8.43.0 Angular `HttpClient` generation; NSwag fallback not triggered |
| D26A-10 | Transloco 8.4.0, pt-BR default, English-ready catalog and `Intl` formatting |
| D26A-11 | Vitest, Playwright, axe, adapter contracts and dependency-cruiser with five negative mutations |
| D26A-12 | Internal primitive gallery; no Storybook |
| D26A-13 | local HTTPS command, same-origin proxy, production bundle and non-secret runtime config; hosting/CI deferred to Phase 33 |
| D26A-14 | exact pins, review/audit policy and explicit major rehearsal; update bot remains deferred until CI exists |

No approved fallback was needed and no unapproved dependency family was added.

## Delivered architecture and user experience

The enforced direction is `Domain <- Application <- Infrastructure / Presentation <- Bootstrap`. Application owns ports/models/errors/use cases; generated DTOs, Angular HTTP, token/cookie integration and Problem Details mapping stay in Infrastructure; Angular pages/forms/state remain in Presentation; Bootstrap composes them. Domain/Application are framework-free.

The product now includes login/session bootstrap, current and all-session logout, expiry/unavailability/forbidden experiences, bounded refresh-and-retry, permission-aware responsive navigation, dashboard, departments, cost centers, suppliers, categories, units and catalog items. Server-side paging/filter/sort, field feedback, ETag concurrency, idempotency, status confirmation and optional supplier enrichment are wired to the Phase 24–25 contract.

Access tokens are memory-only; refresh and CSRF remain backend-cookie contracts. Backend permissions and business rules remain authoritative. No secret or provider/durable-system endpoint is persisted or called directly by the browser.

## Package, generation and UI foundation

- Direct packages and versions are listed in `Client/Template.WebApp/package.json`; the exact transitive graph is locked by `pnpm-lock.yaml`.
- The final inventory contains 458 dependency records, all accepted; one package has a documented MIT-text metadata exception. The final audit reports no known vulnerabilities.
- The unused spartan CLI/Nx dependency chain was removed after generation. Copied primitives carry provenance and an exact-version disposable-update procedure.
- Orval owns 88 generated files under Infrastructure. `pnpm api:drift` regenerates separately and compares normalized file sets/content.
- Semantic tokens, responsive styles, selected accessible primitives and the development gallery form the design-system baseline. No Storybook or Angular Material was installed.

## Final gates

| Gate | Result |
|---|---|
| Frozen lockfile | Passed; 654 entries |
| Format/lint/type-check | Passed |
| Vitest | 16 files, 30/30 passed |
| Frontend architecture | 147 modules / 266 dependencies, zero violations; 5/5 mutations rejected |
| Generated client | 88/88 match current v2 baseline |
| Production build | Passed; initial 389.91 kB raw / 96.61 kB estimated transfer |
| License/security | 458 records approved; no known vulnerabilities |
| Playwright/axe | 4/4 Chromium journeys; zero detected violations on critical authenticated page |
| Backend build | 0 warnings / 0 errors |
| Backend architecture/functional | 59/59 and 322/322 passed |
| OpenAPI | v2 recapture unchanged from Phase 25 after newline normalization |
| Secret scan / diff check | Passed |

## Maintenance, limitations and rollback

Pins are reviewed monthly; patch/minor changes repeat the lock, generation, architecture, browser, license and audit gates; majors require an explicit rehearsal and rollback evidence. A bot is intentionally deferred until CI exists.

Known limitations are the non-blocking 36-selector critical-CSS optimizer warning, the first-journey primary contact/address editor and raw category-ID entry, isolated HTTP browser fixtures rather than a browser-connected live Web API, and the manual browser/screen-reader matrix deferred to the release/hosting phase. None changes the backend contract or security boundary.

Rollback is a source-only reversion/removal of `Client/` and Phase 26 documentation. Phase 26 created no migration, deployment, remote repository, PR or durable external resource. No commit, push, merge, branch/worktree change or resource deletion was performed.
