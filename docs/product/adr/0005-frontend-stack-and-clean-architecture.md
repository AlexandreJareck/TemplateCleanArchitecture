# ADR-0005: Angular SPA with frontend Clean Architecture

- Status: Accepted (Phase 23, 2026-10-18); tool versions and libraries decided in Phase 26A
- Decision owners: D23-2, D23-10, D23-11, D23-12

## Context

The product needs a browser UI for all roles. The backend already follows Clean Architecture and exposes OpenAPI v2. The owner chose Angular and requires the client to be testable without the framework and protected by automated boundary checks.

## Decision

1. Angular TypeScript single-page application at `Client/Template.WebApp` in this repository (subject to D26A-1), client-rendered, no SSR by default.
2. Layers and direction `Domain <- Application <- Infrastructure / Presentation <- Bootstrap` as defined in [frontend-information-architecture.md](../frontend-information-architecture.md#4-layers-modules-and-dependency-rules). Domain/Application are framework-free TypeScript; generated OpenAPI code lives only in Infrastructure; Presentation never imports generated DTOs; Bootstrap is the only composition root.
3. Backend authority: the client never decides business outcomes (budget, eligibility, ranking, routing, totals).
4. Session per D23-11: access token in memory, refresh token in an HttpOnly cookie handled by the browser, XSRF double-submit, same-origin API access through a proxy.
5. State: server state in Application use-case stores; no global store by default.
6. Localization: pt-BR default with English-ready message structure; no business rules in translation files.
7. Enforcement: lint/dependency-graph rules with negative mutation fixtures, framework-free unit tests, adapter contract tests, generated-client drift check, Playwright journeys.

## Alternatives

- **React or Blazor:** viable; not chosen (owner decision D23-2).
- **Feature-folder Angular without layers:** faster start, but generated DTOs and HTTP concerns leak into components. Rejected by D23-12.
- **NgRx-centric architecture:** heavy for the use cases; only after a measured need.

## Consequences

- Extra mapping code in adapters; accepted for testability.
- Phase 26A must produce exact version families, UI component strategy, generator choice and test tooling consistent with this ADR.
