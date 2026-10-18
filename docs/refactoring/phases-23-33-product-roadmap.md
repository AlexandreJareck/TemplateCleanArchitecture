# Phases 23–33 — Procurement Product Roadmap

Status: proposed execution roadmap. No Phase 23+ implementation is authorized by this document alone.

This roadmap turns the academic Clean Architecture template into a realistic, local-first procurement application while preserving the completed architecture, compatibility, rollback, and evidence discipline from Phases 0–22.

The working product name is **Procurement Hub**. The product models a single organization's purchase lifecycle:

`purchase requisition -> budget reservation -> request for quotation -> supplier quotes -> comparison -> approval -> purchase order -> partial/full receipt -> closure`

## Product boundaries

The first product release includes:

- departments, cost centers, users, roles, and permissions;
- suppliers, contacts, addresses, catalog items, and units of measure;
- budget allocations and reservations;
- purchase requisitions and explicit state transitions;
- quotation rounds, invited suppliers, quotes, and quote comparison;
- human approval with deadlines and escalation;
- purchase orders and partial/full goods or service receipt;
- asynchronous notifications, projections, and document generation;
- PDF and XLSX reports;
- an Angular single-page application;
- a separate RabbitMQ worker and an Azure Durable Functions orchestration host;
- local execution with disposable dependencies and an optional, separately authorized Azure deployment path.

The first release explicitly excludes:

- multi-tenancy;
- accounts payable, payments, banking reconciliation, tax calculation, and Brazilian electronic invoices;
- a public supplier portal (buyers enter quotes in the first release);
- a generic workflow designer;
- microservice decomposition;
- replacing RabbitMQ with Azure Service Bus;
- production Azure resource creation without explicit authorization;
- API v1 removal without a separately approved sunset.

## Architecture direction

Keep a modular monolith for business capabilities. `Template.Domain` and `Template.Application` remain the inner layers. Infrastructure implements application-owned ports. Composition roots are:

- `Template.WebApi` for synchronous HTTP;
- `Template.Worker` for RabbitMQ consumers, outbox dispatch, reports, notifications, and projections;
- `Template.WorkflowFunctions` for long-running Durable Functions workflows.

The application database is the source of truth for business state. RabbitMQ transports integration messages. Durable Functions owns process execution state only. Blob-compatible storage owns generated file bytes; PostgreSQL stores file and report metadata.

Business modules are Organization, Suppliers, Catalog, Budgeting, Requisitions, Sourcing, Approvals, Purchasing, Receiving, Reporting, and Integrations. Modules may share one deployment and database initially, but ownership, namespaces, contracts, and tests must remain explicit.

The Angular client also follows Clean Architecture with a pragmatic dependency direction:

`frontend Domain <- frontend Application <- frontend Infrastructure / frontend Presentation <- frontend Bootstrap`

- frontend Domain contains framework-free client concepts and value types only where they improve UI correctness; it does not duplicate server-owned business invariants;
- frontend Application contains framework-free use cases, orchestration, state contracts, and ports for API access, session, navigation, notifications, telemetry, and time when those abstractions are genuinely needed;
- frontend Infrastructure implements those ports with the generated OpenAPI client, browser/session mechanisms, Angular HTTP integration, and observability adapters;
- frontend Presentation contains Angular pages, components, routes, forms, view models, and local view state;
- frontend Bootstrap is the client composition root and owns dependency injection/provider wiring and runtime configuration.

Generated API DTOs stay inside Infrastructure. Explicit adapters map transport DTOs and Problem Details into Application-owned models and errors. Domain and Application must compile and test without Angular TestBed or browser globals. Enforce these boundaries with lint or dependency-graph tests; folder names alone are not architecture.

## Business-rule calibration

This remains an academic reference application, but its rules must resemble a credible corporate procurement process. Prefer a small set of explicit, configurable, testable policies over ERP-scale configurability. Each rule must be classified as one of: invariant, configurable business policy, authorization rule, workflow rule, calculation rule, or input validation. Give stable IDs to cross-module rules and trace them to use cases, API behavior, audit records, and tests.

The baseline scenarios are:

- standard competitive purchase with budget reservation, quotation, approval, purchase order, and receipt;
- low-value direct purchase or insufficient-quotation exception, allowed only by an explicit, permission-protected waiver with reason and audit;
- rejection, cancellation, expiry, concurrency conflict, supplier suspension, and partial receipt as first-class negative paths.

Corporate realism means immutable commercial snapshots, maker-checker separation, versioned approval policy, active fiscal periods, deterministic money/rounding, supplier eligibility, authoritative server calculations, reason-required overrides, and append-only business history. Academic scope means one organization, seeded policy examples, no general rules engine, no tax engine, no legal compliance certification, no ERP/accounting implementation, and no attempt to model every procurement variation.

## Cross-phase rules

Every execution phase must:

1. Verify that Phase 22 has a completed report and a `Ready` release decision before Phase 23 implementation starts.
2. Verify its direct prerequisite reports and actual code; a handoff file is not implementation evidence.
3. Inventory and preserve all pre-existing and concurrent changes.
4. Read root and folder-specific `AGENTS.md` files and applicable repository skills.
5. Keep Domain free of framework and vendor dependencies.
6. Keep Azure, RabbitMQ, Redis, Elasticsearch, file-format, browser, and HTTP client types behind Application ports.
7. Preserve API v1 until an explicit sunset phase is authorized.
8. Treat new v2 endpoints as deliberate contracts with functional tests and OpenAPI review.
9. Use additive EF migrations; never rewrite an applied migration or hand-edit generated migration or resource designer files.
10. Add unit, integration, functional, architecture, and real-service tests in proportion to the change.
11. Run .NET restore/build/test commands sequentially with the pinned SDK.
12. Re-query current official versions, support status, licenses, and migration notes before adding or changing packages, SDKs, images, Node tooling, or cloud runtimes.
13. Keep secrets out of source, reports, artifacts, prompts, logs, and chat.
14. Use isolated disposable resources for destructive or migration tests; never read, migrate, or delete user data without explicit authorization.
15. Regenerate Graphify only after structural code changes and never hand-edit its output.
16. Produce `docs/refactoring/phase-N-report.md` with actual commands, counts, artifacts, and `Passed`/`Failed`/`Skipped`/`Blocked` gates.
17. Avoid commits, pushes, merges, branch changes, worktrees, pull requests, and destructive cleanup unless explicitly authorized.
18. Preserve frontend Clean Architecture: no Angular, generated-client, browser-storage, or transport dependency may enter frontend Domain/Application, and no frontend rule may replace backend authority.
19. Implement only Phase 23-approved business rules. Policy values must be versioned/configurable where corporate behavior can change; invariants must stay inside the domain model; overrides require permission, reason, timestamp, actor, and audit evidence.
20. Maintain business-rule traceability from stable rule ID to implementation and tests. Do not hide business policy in controllers, UI conditions, queue consumers, Durable orchestrators, EF mappings, or magic constants.

## Phase sequence

### Phase 23 — Product blueprint and architecture decisions

Define the product language, actors, permissions, module ownership, state machines, corporate-style business rules, policy defaults/exceptions, domain events, API conventions, frontend information and Clean Architecture, non-functional requirements, and Architecture Decision Records. Record owner decisions for single organization, Angular, frontend layer boundaries, procurement policy, report engines, background execution, file storage, quote entry, budget reservation timing, and Azure deployment scope. This phase is documentation and executable design only.

### Phase 24 — Identity, authorization, and API foundations

Make the API safe for a real SPA: permission-based authorization, current-user endpoint, rotating refresh sessions, logout/revocation, configurable CORS, explicit idempotency and optimistic-concurrency conventions, stable pagination/filter conventions, and security audit events. Preserve existing authentication routes unless a compatible migration is approved.

### Phase 25 — Organization and procurement master data

Implement departments, cost centers, suppliers, supplier contacts/addresses, catalog items, categories, and units of measure, including active/inactive eligibility and history rules. Add optional, fail-soft BrasilAPI and Banco Central adapters behind ports. Deliver v2 endpoints and the initial schema without coupling core layers to external APIs.

### Phase 26 — Frontend inception and foundation

Execute Phase 26 through three gated subphases:

- **Phase 26A — Repository and technology decisions:** decide monorepo versus separate repository, package manager, supported Angular/Node/TypeScript line, Tailwind and component strategy, state/forms/i18n, OpenAPI generator, testing, Storybook, delivery boundary, licenses, and update policy. The recommended starting point is the existing monorepo, Angular, Tailwind, a researched Angular-native shadcn-style option versus Angular Material/PrimeNG, Signals plus RxJS without NgRx by default, Vitest, and Playwright. No frontend code is created in 26A.
- **Phase 26B — Workspace bootstrap:** create the approved repository/workspace, pin the toolchain and lockfile, establish frontend Clean Architecture, design tokens and selected UI primitives, generated-client pipeline, runtime configuration, lint/format/type/test/architecture/build commands, scoped agent instructions, and a minimal verified application skeleton.
- **Phase 26C — Application shell and master data:** implement authentication/session behavior, guards, permissions, Problem Details mapping, accessible responsive shell, localization, Organization/Supplier/Catalog screens, layered tests, and Playwright journeys.

The umbrella Phase 26 closes only when all three subphase reports are reconciled into `phase-26-report.md` and every required gate is green.

### Phase 27 — Purchase requisitions and budget control

Implement active-period budget allocations/reservations and the purchase-requisition aggregate with lines, authoritative totals, explicit transitions, immutable submitted snapshots, audit history, optimistic concurrency, authorization, approved exception handling, v2 API, and matching frontend screens.

### Phase 28 — Reliable asynchronous execution

Create `Template.Worker`; add transactional Outbox and durable Inbox persistence; move business-critical RabbitMQ consumption out of the WebApi; define versioned procurement integration events; prove atomic publication, idempotent handling, retry, dead-letter, recovery, and horizontal-worker behavior.

### Phase 29 — Requests for quotation and supplier quotes

Implement quotation rounds, eligible invited suppliers, buyer-entered quotes, validity and delivery terms, configurable competition/waiver policy, currency normalization, deterministic comparison, selection recommendation, notifications, APIs, events, and frontend workflows.

### Phase 30 — Files and reporting platform

Add blob-compatible file storage, report jobs, authenticated downloads, retention metadata, HTML-to-PDF generation, XLSX generation, RabbitMQ-backed execution, report status endpoints, and a frontend report center. The recommended defaults are Playwright-based PDF rendering and ClosedXML, subject to execution-time license and compatibility verification.

### Phase 31 — Durable approval workflow

Create the .NET isolated `Template.WorkflowFunctions` host. Orchestrate versioned approval policies with amount thresholds, maker-checker separation, external events, durable timers, escalation, rejection, cancellation, retry, compensation, versioning, and observability. Keep all side effects in activities and business state in the application database. Add the approval inbox and timeline to the frontend.

### Phase 32 — Purchase orders and receiving

Implement purchase-order snapshots and numbering, approved-source enforcement, issue/cancel rules, document generation, supplier notification, partial/full receiving, tolerance/discrepancy and auditable correction rules, completion, events, APIs, and frontend screens. Do not add invoicing, payment, or tax integrations.

### Phase 33 — Operational readiness and product release gate

Finish recurring reminders and expirations, local email through Mailpit, end-to-end workflows, observability across HTTP/worker/Durable boundaries, CI, container builds, normal local Compose verification, optional Azure Infrastructure-as-Code without deployment, backup/restore and rollback procedures, accessibility and performance smoke tests, documentation, demo data, and the complete release decision.

## Frontend delivery model

Phases 26A–26C establish the repository, toolchain, SPA, design system, and enforceable Clean Architecture. Phases 27, 29, 30, 31, and 32 each deliver their matching user experience as a vertical slice across frontend Domain/Application/Infrastructure/Presentation as needed. Backend and frontend contracts evolve together through OpenAPI, generated-client, adapter-contract, and architecture-boundary checks.

Planned screens are login, dashboard, my requisitions, requisition editor/detail/timeline, approval inbox, quotation rounds, quote comparison, suppliers, catalog, cost centers and budgets, purchase orders, receipts, report center, and user/permission administration.

## Planned asynchronous workloads

- dispatch outbox messages;
- send approval and supplier notifications;
- update search projections;
- generate purchase-order and quote-comparison PDFs;
- generate budget and supplier-performance spreadsheets;
- expire quotation rounds and budget reservations;
- retry transient external integration failures;
- escalate overdue approval steps.

Workflow-specific delays belong to Durable timers. Global recurring schedules may use isolated Azure Functions timer triggers or a separately approved scheduler after Phase 31; do not introduce Hangfire or Quartz by default while RabbitMQ, the Worker, and Functions already cover the accepted use cases.

## Planned external integrations

- BrasilAPI for optional supplier and postal-address enrichment;
- Banco Central do Brasil PTAX data for quote comparison in a base currency;
- Mailpit for local email testing;
- Azure Blob Storage in Azure and Azurite locally for report/attachment bytes;
- generic outbound webhooks as a later adapter, not a Phase 23–33 release requirement.

External data is advisory enrichment. Cache bounded responses, apply timeouts/retries/circuit breaking, preserve the user-entered snapshot, and keep core operations available when providers are unavailable.

## Release definition

The product is release-ready only when a user can authenticate, create a requisition, reserve budget, collect and compare quotes, complete a timed multi-step approval, issue a purchase order, generate PDF/XLSX artifacts asynchronously, record partial and final receipts, and audit the full history through the UI; every required automated and real-service gate must be green, and no required secret or external service may be silently assumed.
