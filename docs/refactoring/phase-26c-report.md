# Phase 26C report — Frontend shell and master data

Specification: `docs/refactoring/handoff-phase-26c.md`, executed through `docs/refactoring/execute-phase-26.md` on 2026-10-18/19.

## Release decision

**Ready.** The Procurement Hub now has a secure session flow, permission-aware responsive shell, localized Organization, Supplier and Catalog journeys, typed API/error/concurrency handling and repeatable frontend gates. The final frontend validation, four Chromium journeys, affected Web API build, architecture suite, functional suite, OpenAPI comparison and secret scan passed.

The browser tests use isolated HTTP fixtures at the real same-origin API boundary; they do not replace the 322 WebApplicationFactory tests that exercise the actual Web API host.

## Starting state and contract

- Starting HEAD: `ad06cad64d993cc73cfa490a7bff8262e3f2f356`; the working tree already contained the approved Phase 26A/26B work and was preserved.
- Phase 26B was `Ready`. Orval regeneration/drift remained clean: all 88 generated files match `docs/refactoring/baseline-phase-25/openapi-v2.json`.
- The recaptured v2 OpenAPI is identical to the Phase 25 baseline after line-ending normalization. No backend route, schema, status code or persistence behavior changed.
- The accepted D26A stack was retained. No fallback package, state library, SSR, micro-frontend, hosting or CI was introduced.

## Routes and screens

| Route | Capability / behavior |
|---|---|
| `/login` | Signal Form login; explicit expired-session message; validation and safe support details |
| `/unavailable` | bootstrap/server-unavailable experience |
| `/forbidden` | permission-denied experience, distinct from authentication failure |
| `/` | authenticated, capability-based dashboard without fabricated metrics |
| `/organization/departments` | server paging/filtering/sort, create/edit, head user, activate/deactivate |
| `/organization/cost-centers` | server paging/filtering/sort, department and manager references, create/edit/status |
| `/suppliers` | list/detail/create/edit, paging/filtering, primary contact/address, categories, lifecycle transitions, CNPJ proposal and explicit enrichment acceptance |
| `/catalog/categories` | list/filter/page/create/edit/status |
| `/catalog/units` | list/filter/page/create/edit/status and decimal precision |
| `/catalog/items` | list/filter/page/create/edit/status, Good/Service kind, category and unit references |
| `/dev/primitives` | internal development gallery retained from 26B |

The backend remains authoritative for permissions, status eligibility, totals, field/business validation, category/reference validity and concurrency. The UI sends stable permission names, idempotency keys and `If-Match`; it does not reproduce domain policy.

## Architecture mapping

```text
Domain <- Application <- Infrastructure / Presentation <- Bootstrap
```

- Application owns `ApplicationError`, session/master-data ports, use cases and framework-free models.
- Infrastructure owns the transient token store, session/generated-client adapters, one bounded refresh-and-retry interceptor, cancellation and RFC Problem Details mapping.
- Presentation owns facades, permission directive, guards, routes, Signal Forms, local Signals, screens and the reusable accessible action confirmation.
- Bootstrap alone composes concrete adapters and interceptors.
- Generated DTOs remain under Infrastructure. Presentation and Application do not import them.

Dependency Cruiser passed across 147 modules and 266 dependencies. Five controlled mutations still prove the prohibited layer directions.

## Session and HTTP security

- The access token exists only in an in-memory private field. No `localStorage` or `sessionStorage` use exists.
- The refresh token remains the backend-owned `HttpOnly` cookie. Angular's same-origin XSRF configuration uses `XSRF-TOKEN` / `X-XSRF-TOKEN` exactly as the Phase 24 contract requires.
- Concurrent 401s share one `shareReplay` refresh operation. A request receives at most one retry through an `HttpContextToken`; refresh/session endpoints are never recursively refreshed.
- Failed refresh clears the transient token and produces the expired-session path. Transport/5xx bootstrap failure produces the unavailable path.
- Login, current-account bootstrap, current-session logout and revoke-all logout are implemented. API authorization remains authoritative.
- No token or credential is logged or persisted. No browser code calls BrasilAPI, BCB, RabbitMQ, Functions or another durable system directly.

## Errors, concurrency and feedback

Infrastructure maps cancellation, 400 field validation, 401, 403, 404, 409, 412/428, 422, 429 (including `Retry-After`), status 0 and 5xx into Application-owned errors. Safe `code`, `traceId`, rule, current state/version and field messages reach consistent Presentation feedback without exposing transport objects.

Versioned writes send quoted `If-Match`; creations/transitions send a fresh `Idempotency-Key`. A stale version shows the stable problem code, correlation trace and explicit reload action. Destructive/status actions use a reusable semantic modal surface with initial focus, Escape cancellation, required reason and approved input/button primitives rather than a browser prompt.

## Accessibility, responsive behavior and localization

- Skip link, semantic landmarks, labels, page titles, breadcrumbs, focus restoration after navigation and permission-aware navigation are implemented.
- Tables are keyboard-scrollable and become labeled card rows on narrow screens; forms and confirmation surfaces collapse to one column.
- Loading, skeleton, empty, global error, field error, conflict, expired, forbidden and unavailable states are represented.
- The default catalog is pt-BR; the English catalog preserves the same message structure. `Intl` remains the formatting boundary.
- Reduced-motion/focus/contrast behavior comes from the semantic token foundation and approved primitives.
- Axe ran on the authenticated critical page with zero automatically detectable violations. Component tests cover confirmation focus and permission rendering.

Manual pre-release smoke to repeat on each supported browser: keyboard-only login/navigation/form/confirmation/logout; visible focus and Escape cancellation; 200% zoom and narrow viewport; one screen-reader pass over login, navigation, table/card labels, feedback and confirmation. This browser-matrix smoke is documented evidence for release/Phase 33 and is not automated by axe.

## Test evidence

| Layer | Coverage |
|---|---|
| Framework-free/Application | permission evaluation, session use cases and paging intent |
| Infrastructure | DTO/page mapping, cancellation, safe Problem Details, validation, 409/412/428, 429, refresh single-flight, token clearing and bounded retry |
| Angular Presentation | facade, guards, permission directive, primitive gallery, confirmation focus/emission and application shell creation |
| Playwright | login/expired path, permission shell, concurrent refresh path, paging/filter, 412 conflict/support trace, revoke-all, forbidden, supplier detail/status/enrichment failure, catalog validation and create, axe |

Final counts: 16 Vitest files, 30/30 tests; four Playwright Chromium journeys, 4/4 passed. The existing backend functional suite passed 322/322 and architecture passed 59/59.

## Validation evidence

| Gate | Final result |
|---|---|
| `pnpm install --frozen-lockfile --offline` | Passed against 654 lock entries and the real pnpm cache |
| `pnpm validate` | Passed: format, lint, type-check, 30 tests, graph, mutations, API drift and production build |
| Dependency graph / mutations | 147 modules, 266 dependencies, zero violations; 5/5 negative mutations rejected |
| Orval drift | 88/88 generated files match |
| Production bundle | Passed budgets; initial 389.91 kB raw / 96.61 kB estimated transfer |
| Licenses | 458 approved dependency records; unchanged documented `union@0.5.0` MIT metadata exception |
| Vulnerability audit | No known vulnerabilities |
| Playwright/axe | 4/4 Chromium journeys; zero axe violations on the critical authenticated page |
| Web API build | Passed, 0 warnings and 0 errors |
| .NET architecture / functional | 59/59 and 322/322 passed outside the sandbox with `VSTEST_CONNECTION_TIMEOUT=300` |
| OpenAPI v2 | Recapture matches `baseline-phase-25` after line-ending normalization |
| Secret scan | Passed: 5 settings, 8 Compose/env templates, fingerprint absent from 896 scoped files |
| `git diff --check` | Passed before the reports; repeated at closure |

The Angular critical-CSS optimizer still reports the documented 26B warning for 36 nested `&` selectors emitted by the Tailwind/spartan styling path. The complete stylesheet is emitted, budgets pass and browser rendering/axe pass; no optimization was disabled.

## Limitations and rollback

- The supplier screen intentionally edits one primary contact and address per submission in this first journey; the port and API preserve replacement-collection semantics and can be expanded without crossing layers. Category references are entered as explicit IDs until a reusable lookup selector is approved.
- Browser E2E uses deterministic same-origin HTTP interception, not external providers or a Docker/browser-connected live host. Backend functional and adapter suites remain the source of truth for the actual API/providers.
- Production hosting, CSP/reverse-proxy hardening, runtime config caching, CI and the full supported-browser matrix remain Phase 33 work.

Rollback is removal/reversion of the Phase 26C files under `Client/Template.WebApp/src/app/{application,infrastructure,presentation,bootstrap}`, the expanded translations/E2E and this report. There is no database migration, external resource, deployment, remote or persistent browser credential to roll back. No commit, push, branch change, remote or PR was created.
