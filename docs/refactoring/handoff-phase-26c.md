# Phase 26C Execution Handoff — Frontend Shell and Master Data

## Mission

Turn the verified Phase 26B skeleton into the first usable Procurement Hub frontend. Implement secure session behavior, permission-aware navigation, consistent HTTP/error handling, accessible responsive shell, localization, and Organization, Supplier, and Catalog workflows against the Phase 24–25 APIs.

## Required context and preconditions

Read root/client instructions, Phase 23 frontend/API/security/business decisions, Phase 24–25 reports, D26A decisions, Phase 26B report, current v2 OpenAPI, CORS/cookie/CSRF contracts, frontend architecture, design-system strategy, and validation scripts.

- Phase 26B must be `Ready`; do not repair an unapproved architecture/toolchain by silently replacing packages.
- Record HEAD/dirty tree and preserve concurrent work.
- Regenerate the client from current OpenAPI and classify any drift before feature work.
- Stop if session/CSRF, permission names, concurrency, localization, or master-data contracts contradict accepted decisions.

## Session and HTTP behavior

- Keep access tokens only in the approved transient client location; do not use localStorage unless explicitly approved and threat-modeled.
- Use the Phase 24 refresh-cookie and CSRF behavior exactly.
- Implement one bounded refresh-and-retry path that prevents request storms, concurrent-refresh races, and infinite loops.
- Implement login, logout, current-user/session bootstrap, expiration and revocation behavior.
- Map RFC Problem Details, validation fields, 401, 403, 409/412, 429, cancellation and unavailable-server states into Application-owned errors and consistent Presentation feedback.
- Treat backend authorization as authoritative. Guards/directives improve usability but never replace API enforcement.
- Include correlation/trace identifiers in support details without exposing internals or sensitive data.

## Frontend Clean Architecture

Implement each master-data slice through the established boundaries:

- frontend Domain contains only useful framework-free client concepts, never a duplicate of backend entities/invariants;
- Application owns session and master-data use cases plus narrow ports and view-independent state transitions;
- Infrastructure owns generated-client calls, DTO/error mapping and browser integration;
- Presentation owns Angular pages, forms, routes and local view state;
- Bootstrap composes adapters/providers.

Presentation must not import generated DTOs. Domain/Application must remain Angular/browser-free and test without Angular TestBed. Do not hide backend business policy in guards, components, form schemas or client stores.

## User experience

Implement:

- login, logout, session bootstrap, unauthorized, forbidden, expired-session and unavailable-server experiences;
- accessible responsive application shell, skip link, navigation, breadcrumbs, page titles, focus restoration, global feedback, loading/skeleton, empty and error states;
- dashboard landing page based on current-user capabilities, without invented metrics;
- department and cost-center lists/forms;
- supplier list/detail/create/edit/status transition and optional enrichment acceptance flow;
- catalog item, category and unit-of-measure lists/forms;
- reusable approved primitives for server-side paging/filter/sort, confirmation, field errors, concurrency conflicts, permissions and destructive/status actions.

Use pt-BR as the approved default and preserve the English-ready message structure. Meet keyboard navigation, semantic labeling, focus management, contrast, reduced motion, responsive tables/forms and supported-browser requirements. Server data, totals, permissions and status eligibility remain authoritative.

## Testing

- Framework-free tests cover Application session/master-data use cases and state/error behavior.
- Infrastructure contract tests cover generated adapters, DTO/Problem Details mapping, cancellation, refresh concurrency and 409/412 handling.
- Angular component tests cover shell navigation, guards, permission directives, forms, focus, loading/error/empty states and representative screens.
- Accessibility automation covers critical components and pages; document the required keyboard/manual smoke.
- Playwright covers login, session refresh race, logout/revocation, unauthorized/forbidden behavior, supplier/catalog CRUD and status transitions, validation, enrichment failure/fallback, paging/filtering and concurrency conflict against isolated WebApi fixtures.
- Verify no token/secret is persisted or logged and no browser request calls BrasilAPI, BCB, storage, RabbitMQ or Functions directly.

## Out of scope

- Requisitions, RFQs, approvals, reports, purchase orders, receipts, public pages, supplier portal, SSR, micro-frontends, public deployment or unapproved design-system expansion;
- changing backend behavior merely to simplify UI code;
- adding state/UI/form libraries not approved in 26A.

## Validation and acceptance

Run frozen-lock install, formatting, lint, type-check, framework-free/application tests, adapter contracts, Angular component/accessibility tests, architecture tests/mutations, generated-client clean/drift, production build/budgets, dependency/license/vulnerability audit, Playwright E2E, affected backend functional/OpenAPI gates, secret scan and `git diff --check`.

Create `docs/refactoring/phase-26c-report.md` with routes/screens, layer mapping, session/security behavior, accessibility, localization, generated-client delta, tests/counts, browser evidence, bundle/audits, commands, limitations, rollback and gates.

Phase 26C is `Ready` only when the approved master-data journey is usable, secure, accessible, architecture-compliant and green. Then create the reconciled umbrella `phase-26-report.md` required by `handoff-phase-26.md`.

Do not commit/push/merge, change branches/worktrees, create a remote/PR, deploy, or delete resources without explicit authorization.
