# Frontend information architecture and Clean Architecture

Scope: the Angular SPA under `Client/Template.WebApp` (D23-2), structured per D23-12. Phase 26A chooses tool versions and libraries; this document fixes the product structure, layer rules and state ownership those choices must serve. UI text is pt-BR (D23-10); route paths, code and identifiers are English.

## 1. Navigation and routes

All routes except `/login` require a session. Route guards hide what the user cannot do; the API still enforces every permission (SEC-008). A route the user lacks permission for renders the forbidden page, not a redirect loop.

| Route | Screen | Required permission (any of) | Phase |
|---|---|---|---|
| `/login` | Login | anonymous | 26C |
| `/` | Dashboard (role-specific entry points, no invented metrics) | authenticated | 26C |
| `/forbidden`, `/session-expired`, `/unavailable`, `**` | Forbidden, expired session, server unavailable, not found | — | 26C |
| `/organization/departments`, `/organization/cost-centers` (+ `/new`, `/:id`) | Department and cost-center lists/forms | `organization.read` / `organization.manage` | 26C |
| `/suppliers`, `/suppliers/new`, `/suppliers/:id`, `/suppliers/:id/edit` | Supplier list, create (with optional enrichment), detail with status history and status actions, edit | `suppliers.read` / `suppliers.manage` / `suppliers.status.change` | 26C |
| `/catalog/items`, `/catalog/categories`, `/catalog/units` (+ forms) | Catalog management | `catalog.read` / `catalog.manage` | 26C |
| `/budget/periods`, `/budget/allocations`, `/budget/allocations/:id` | Fiscal periods, allocations, availability and reservations | `budget.read` / `budget.*.manage` | 27 |
| `/requisitions/mine` | My requisitions | `requisitions.create` | 27 |
| `/requisitions/team` | Team requisitions | `requisitions.read-team` / `requisitions.read-all` | 27 |
| `/requisitions/new`, `/requisitions/:id/edit` | Draft editor with lines and previewed totals | `requisitions.create` | 27 |
| `/requisitions/:id` | Detail with timeline, budget reservation, sourcing and approval status | readable per REQ-011 | 27 |
| `/sourcing/queue` | Buyer work queue (submitted requisitions, open RFQs, closing soon) | `sourcing.rfqs.manage` | 29 |
| `/sourcing/rfqs/new?requisitionId=` | RFQ wizard (method, invitations, deadline, waiver) | `sourcing.rfqs.manage` | 29 |
| `/sourcing/rfqs/:id` | RFQ detail: invitations, quotes, deadline actions | `sourcing.quotes.read` | 29 |
| `/sourcing/rfqs/:id/quotes/new`, `/sourcing/rfqs/:id/quotes/:quoteId` | Quote entry and revision history | `sourcing.quotes.enter` | 29 |
| `/sourcing/rfqs/:id/comparison` | Comparison table (raw + normalized), warnings, award with waiver dialogs | `sourcing.quotes.read` | 29 |
| `/reports` | Report center (request, status, download) | `reports.request` | 30 |
| `/approvals/inbox` | Approval inbox (assigned to me, due soon, escalated) | `approvals.decide` | 31 |
| `/approvals/:id` | Approval detail: award summary, comparison snapshot, steps timeline, decide/delegate | assignee / `approvals.read-all` | 31 |
| `/purchasing/orders`, `/purchasing/orders/:id` | Approved-order queue, PO detail, issue/cancel/short-close, document download | `purchasing.orders.read` | 32 |
| `/receiving/queue`, `/receiving/orders/:id/receipts/new`, `/receiving/receipts/:id` | Receiving work queue, partial receipt editor with remaining quantities and discrepancies, receipt detail and corrections | `receiving.receipts.create` / `receiving.receipts.read` | 32 |
| `/admin/users`, `/admin/roles` | User and role/permission administration | `access.users.manage` / `access.roles.manage` | 33 (API in 25) |
| `/audit` | Audit search | `audit.read` | 33 |

Primary navigation groups (shown only when at least one child is permitted): **Início**, **Requisições**, **Aprovações**, **Compras** (cotações, pedidos), **Recebimento**, **Orçamento**, **Cadastros** (organização, fornecedores, catálogo), **Relatórios**, **Administração**.

Role-specific dashboard entry points: requester → "Nova requisição", my open requisitions; approvers → inbox count and due-soon list; buyer → sourcing queue and approved orders to issue; receiver → receiving queue; budget controller → allocations near exhaustion (server-computed list, no client calculation); auditor → audit search and reports.

## 2. Screen states

Every data screen implements the same five states with shared primitives:

| State | Behavior |
|---|---|
| Loading | Skeleton matching the final layout; `aria-busy="true"`; no layout shift; spinner only for actions |
| Empty | Explains why and offers the next permitted action (for example "Nova requisição") |
| Error | Maps the Application error: validation → field messages; 403 → forbidden view; 404 → not found; 409/412 → conflict panel with "reload latest" and a diff of changed fields when available; 422 → business message with `ruleId` in the support details; 429 → retry-after message; 5xx/network → retry with `traceId` shown in support details |
| Stale | After 412, the user's unsaved form is kept locally until they reload or discard |
| Success | Toast/inline confirmation announced through an ARIA live region; focus moves to the logical next element |

## 3. Accessibility and responsive scope

- Target WCAG 2.2 AA for all screens: keyboard-only operation, visible focus, skip link, landmarks, one `h1` per page, page titles per route, labelled inputs, error summary linked to fields, contrast ≥ 4.5:1, reduced motion respected, no information by color only (statuses have text/icon).
- Tables: sortable headers announce sort state; responsive tables collapse to stacked cards below 768 px; the comparison table scrolls horizontally inside its region with a sticky first column.
- Supported viewports: desktop ≥ 1024 px (primary), tablet 768–1023 px (all screens usable), phone ≥ 360 px for requester, approval inbox/decision and receiving screens (field use); administration and comparison screens are usable but not optimized on phones.
- Supported browsers and the automated accessibility tool are fixed in Phase 26A.

## 4. Layers, modules and dependency rules

Dependency direction (D23-12): `Domain <- Application <- Infrastructure / Presentation <- Bootstrap`.

```text
src/app/
  domain/            framework-free client concepts (Money display value, status labels, permission set)
  application/
    ports/           ApiPorts per feature, SessionPort, NavigationPort, NotifierPort, ClockPort, TelemetryPort
    use-cases/       per feature (session, organization, suppliers, catalog, budget, requisitions, sourcing, approvals, purchasing, receiving, reports)
    state/           signal-based stores owned by use cases (framework-free signal abstraction per 26A)
  infrastructure/
    api/generated/   generated OpenAPI client (never hand-edited)
    api/adapters/    one adapter per port: DTO <-> Application model, Problem Details -> ApplicationError
    auth/            access-token memory holder, refresh single-flight, XSRF integration
    telemetry/       correlation and error reporting adapter
  presentation/
    shell/           layout, navigation, breadcrumbs, global feedback
    shared-ui/       approved primitives only
    features/<feature>/  pages, components, forms, view models, routes
  bootstrap/         providers, runtime configuration, adapter wiring
```

| Layer | May import | Must not import |
|---|---|---|
| Domain | nothing outside `domain/` | Angular, RxJS, generated client, browser globals, other layers |
| Application | Domain | Angular (except an abstraction approved in 26A for signals), generated client, `HttpClient`, `window`, `document`, storage APIs |
| Infrastructure | Application, Domain, generated client, Angular HTTP, browser APIs | Presentation |
| Presentation | Application (use cases, models, ports' types), Domain, Angular, shared UI | `infrastructure/**` (including generated DTOs) |
| Bootstrap | everything | — (only composition) |

Feature modules are vertical folders within each layer; a feature may depend on another feature only through its Application public API (`index.ts`), never through internals.

## 5. Ports and adapters

- Ports describe client use-case needs, not endpoints (for example `RequisitionPort.submit(id, version, idempotencyKey)` returns `Result<RequisitionView, ApplicationError>`).
- `ApplicationError` is a closed union: `Validation(fields)`, `Unauthenticated`, `Forbidden`, `NotFound`, `Conflict(code, currentState?)`, `VersionMismatch(currentVersion)`, `BusinessRule(code, ruleId?)`, `RateLimited(retryAfter)`, `Unavailable`, `Cancelled`, `Unexpected(traceId)`.
- Infrastructure adapters map generated DTOs and Problem Details into these types; Money strings become a client `Money` value with decimal-safe handling (library or string arithmetic chosen in 26A; never JS `number` for authoritative values).
- The adapter layer owns `If-Match`, `Idempotency-Key` generation (one key per user intent, reused on retry) and cancellation via `AbortSignal`.

## 6. State ownership

| State kind | Owner | Mechanism | Lifetime |
|---|---|---|---|
| Server state (resources, lists) | Backend; cached by Application use-case stores | Signals fed by port calls; invalidated after commands and on version change | Screen or feature lifetime; never persisted |
| Session state (access token, current user, permissions) | `SessionPort` adapter in Infrastructure; read-only view in Application | In memory only; refresh single-flight | Tab lifetime; refresh cookie restores it |
| Cross-screen workflow state (RFQ wizard, receipt editor across steps) | Feature Application store | Signals scoped to the feature route | Until completion/cancel; unsaved drafts may be kept in memory only |
| Local component state (open dialogs, sort UI, form dirty state) | Presentation component | Component signals / form model | Component lifetime |

No global store is introduced by default; NgRx or equivalent requires a measured use case and a D26A amendment. Nothing sensitive is written to `localStorage`/`sessionStorage`; the only browser-persisted items are UI preferences (for example table density) if approved.

## 7. Backend authority

The client may preview totals, filter visible actions by permission and pre-validate inputs for usability. It never computes authoritative totals, rankings, exchange conversions, eligibility, approval routing or budget availability; the displayed values come from the API. Client validation mirrors only VAL-class rules; INV/POL/AUTH results always come from the server.

## 8. Architecture-test strategy

- Lint/dependency-graph rules (tool chosen in 26A) encode the table in section 4; each forbidden direction has a negative mutation test (a fixture file that must fail the check).
- Domain and Application unit tests run without Angular TestBed or DOM.
- Adapter contract tests run against recorded OpenAPI examples and Problem Details fixtures, including 409/412/422/429 and cancellation.
- A generated-client drift check regenerates from the committed OpenAPI artifact and fails on diff.
- Playwright journeys cover the scenarios SC-01..SC-09 progressively (each phase adds its slice; Phase 33 runs all).
