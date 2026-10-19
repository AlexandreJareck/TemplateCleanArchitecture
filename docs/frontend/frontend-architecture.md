# Frontend architecture

Status: accepted by the owner on 2026-10-18.

## Boundary and dependency map

The Angular SPA is one deployment unit under `Client/Template.WebApp`, but source dependencies are directional:

```text
Domain <- Application <- Infrastructure
                     <- Presentation
Domain/Application/Infrastructure/Presentation <- Bootstrap
```

| Layer | Owns | May import | Must not import |
|---|---|---|---|
| Domain | Small client value types only when they prevent UI mistakes | Domain | Angular, RxJS, DOM, browser APIs, generated/transport types |
| Application | Use cases, ports, inputs/results, errors, framework-free stores | Domain, Application public APIs | Angular, generated clients, `HttpClient`, storage, `window`, `document` |
| Infrastructure | Generated v2 client, transport adapters, session transport, runtime config reader, telemetry adapter | Domain, Application, generated code, Angular HTTP/browser APIs | Presentation |
| Presentation | Standalone components, routes, forms, view models, local signals, shared UI | Domain and Application public APIs, Angular, approved UI primitives | Infrastructure and generated code |
| Bootstrap | Provider wiring, route assembly, configuration and composition | All layers | Business rules or reusable UI |

`Bootstrap` is the only composition root. Feature internals are private; cross-feature use goes through an Application `index.ts` public API. Path aliases make layer intent visible, while dependency-cruiser and controlled mutation fixtures enforce it.

## State ownership

- Backend: authoritative resources, totals, permissions, transitions and concurrency versions.
- Application: framework-free use-case stores with immutable state snapshots and explicit commands.
- Presentation: Angular signals for route/component state, forms, dialog state and rendering derivations.
- Infrastructure: access token in memory and one single-flight refresh coordinator; no sensitive persisted state.
- RxJS: Angular HTTP and async coordination boundary only. Subscriptions are bounded by Angular lifecycle or converted at Presentation boundaries.

No global store is introduced. UI preferences may be considered later; Phase 26 writes no application or session state to `localStorage`/`sessionStorage`.

## Ports and adapters

Application ports express user intent, not HTTP endpoints: `SessionPort`, `OrganizationPort`, `SupplierPort`, `CatalogPort`, `NavigationPort`, `NotifierPort`, `ClockPort` and `TelemetryPort`. Infrastructure adapters:

- map generated DTOs to Application-owned models;
- preserve decimal money as strings;
- attach `Authorization`, `If-Match`, `Idempotency-Key` and XSRF behavior at the correct boundary;
- map RFC 9457 responses into `Validation`, `Unauthenticated`, `Forbidden`, `NotFound`, `Conflict`, `VersionMismatch`, `BusinessRule`, `RateLimited`, `Unavailable`, `Cancelled` or `Unexpected`;
- expose `traceId`, rule ID and safe current version/state only where applicable;
- propagate cancellation and never log tokens, cookies, personal or commercial details.

Generated code is immutable build output within Infrastructure. Presentation and Application tests use port fakes, never generated DTOs.

## Session flow

The access token exists only in an in-memory Infrastructure holder. Startup calls refresh using the browser's Secure HttpOnly cookie and `X-XSRF-TOKEN` read from the non-HttpOnly cookie. Concurrent 401 responses join one bounded refresh; exactly one retry is allowed. Refresh/logout use same-origin credentials and the XSRF header. A failed or revoked refresh clears in-memory state and transitions to the approved expired/anonymous experience without loops.

Route guards and permission directives improve navigation usability. The API remains authoritative and every 401/403/404/409/412/422/429/503 response is handled even when the UI predicted success.

## Configuration and delivery

The application loads a typed, non-secret runtime document before bootstrap. Approved fields are API base path (normally `/api`), build identifier and non-secret feature flags. Credentials, refresh/access tokens, provider URLs and storage/messaging/function endpoints are forbidden. Development proxies `/api` to the existing HTTPS WebApi so session cookies remain same-origin.

## Test boundaries

- Domain/Application: Vitest in Node, no TestBed or DOM.
- Infrastructure: deterministic generated-client adapter and Problem Details fixtures, cancellation and session-race tests.
- Presentation: Angular component tests for semantics, focus and screen states.
- Architecture: dependency-cruiser rules plus negative mutations for each forbidden direction and a generated-type leak scanner.
- Browser: Playwright journeys and axe scans against isolated local fixtures; manual keyboard/high-contrast/screen-reader checklist.
