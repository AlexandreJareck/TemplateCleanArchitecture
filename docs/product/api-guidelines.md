# API guidelines (v2 procurement endpoints)

These rules apply to **new** procurement endpoints from Phase 24 onward. Existing v1 and v2 sample endpoints (`account`, `product`, `person`, `message`) keep their routes, bodies and status codes ([api-v1-lifecycle.md](../refactoring/api-v1-lifecycle.md)); the guidelines never justify changing them silently.

## 1. Resources and routes

- Base path: `/api/v2/`; URL-segment versioning stays (`Asp.Versioning`, existing `BaseApiController`). New controllers use explicit `[Route("api/v{version:apiVersion}/<resource>")]` because `[controller]` would produce singular, PascalCase-derived names.
- Resource names: plural, kebab-case nouns: `purchase-requisitions`, `fiscal-periods`, `budget-allocations`, `suppliers`, `catalog-items`, `requests-for-quotation`, `approval-instances`, `purchase-orders`, `receipts`, `report-jobs`, `files`, `departments`, `cost-centers`, `units-of-measure`, `categories`.
- Sub-resources for owned entities: `/purchase-requisitions/{id}/lines/{lineNumber}`, `/requests-for-quotation/{id}/quotes/{quoteId}/revisions`.
- State transitions are action sub-resources with `POST` and a verb: `/purchase-requisitions/{id}/submit`, `/withdraw`, `/cancel`; `/requests-for-quotation/{id}/publish`, `/close`, `/extend-deadline`, `/award`; `/approval-instances/{id}/steps/{level}/decision`, `/delegate`; `/purchase-orders/{id}/issue`, `/cancel`, `/short-close`; `/receipts/{id}/corrections`.
- IDs are GUIDs in routes (`{id:guid}`); human numbers (`REQ-2027-000001`) are filterable fields, not route keys.
- Current-user shortcuts: `/api/v2/account/me`, `/api/v2/approval-instances?assignedTo=me`, `/api/v2/purchase-requisitions?requester=me`.

## 2. Representations

- JSON with `System.Text.Json` web defaults (camelCase). Enums as strings. Instants as ISO 8601 UTC (`2027-03-01T13:00:00Z`); local dates as `YYYY-MM-DD`.
- Money as `{ "amount": "1234.50", "currency": "BRL" }`. The amount is a JSON **string** to avoid binary floating-point loss in clients; the server parses it as `decimal` and rejects more decimals than allowed (CALC-001).
- New endpoints return the resource representation directly (B23-8): `200` with the resource, `201` with the resource and `Location`, `204` for deletes. They do not wrap responses in `BaseResult`.
- Every representation of a versioned aggregate includes `version` (opaque string, same value as the `ETag`).
- Responses never expose EF entities, storage keys, blob URLs, internal exception text, password/tokens or hashes.
- Resource-scoped reads that the actor may not see return `404` (no existence leak, REQ-011); collection endpoints filter silently.

## 3. Lists: pagination, filtering, sorting

- Query parameters: `page` (≥ 1, default 1), `pageSize` (1–100, default 20), `sort` (comma-separated fields, `-` prefix for descending, for example `sort=-submittedAt,number`), and named filters (`status=Submitted,InSourcing`, `costCenterId=`, `supplierId=`, `q=` for code/name text search, `from=`/`to=` for date ranges).
- Unknown filter or sort fields → `400` with a validation problem; each endpoint documents its allow-listed sort fields and always appends `id` as the final tie-breaker for stable paging.
- Response envelope (`PagedResult<T>`): `{ "items": [...], "page": 1, "pageSize": 20, "totalItems": 135, "totalPages": 7 }`.
- The existing `PagedResponse<T>`/`PaginationRequestParameter` remain for the sample product endpoints only.

## 4. Optimistic concurrency

- Every aggregate has a version token (mechanism decided by spike SP-1). `GET` returns `ETag: "<version>"` (strong ETag).
- Every `PUT`, `PATCH`, `DELETE` and state-transition `POST` on an existing aggregate requires `If-Match`.
  - Missing `If-Match` → `428 Precondition Required` (`code: concurrency.precondition-required`).
  - Stale `If-Match` → `412 Precondition Failed` (`code: concurrency.version-mismatch`) with the current `version` in the problem extensions; no change is applied.
- A valid version but an invalid state transition → `409 Conflict` (`code: invalid-transition`, `currentState` extension).
- System transitions (Worker, Durable activities) use the same use cases with an internal expected-version check and retry on conflict; they never use `If-Match` semantics over HTTP.
- Budget reservation uses an additional row lock (BUD-006) so competing submissions get a business answer (`budget.insufficient`) instead of a version error.

## 5. Problem Details and error codes

All errors are RFC 9457 Problem Details (`application/problem+json`), produced at the HTTP boundary by the existing `ApiResultMapper` pattern.

| Situation | Status | `code` example |
|---|---|---|
| Input validation (VAL) | 400 | `validation` with `errors` per field (existing `ValidationProblemDetails` shape) |
| Not authenticated / session invalid | 401 | `auth.session-expired` |
| Permission or SoD denial | 403 | `auth.forbidden`, `approval.self-approval` |
| Not found or not visible | 404 | `not-found` |
| Invalid transition, duplicates, already-decided | 409 | `invalid-transition`, `approval.step-already-decided` |
| Expired resource | 410 | `report.expired` |
| Version mismatch | 412 | `concurrency.version-mismatch` |
| File too large / unsupported type | 413 / 415 | `file.too-large`, `file.type-not-allowed` |
| Business rule violation with valid input (INV/POL) | 422 | `budget.insufficient`, `supplier.not-eligible` |
| Missing precondition header | 428 | `concurrency.precondition-required` |
| Rate limited | 429 | `auth.rate-limited` (+ `Retry-After`) |
| Optional provider unavailable (enrichment endpoints only) | 503 | `integration.provider-unavailable` |
| Unexpected | 500 | no `code` detail, sanitized, `traceId` only |

Extensions on every problem: `traceId` (existing behavior, equals the logging correlation ID), `code` (stable English string), and when applicable `ruleId` (for example `BUD-005`), `currentState`, `version`, `errors`. `title` and `detail` are localized (pt-BR default) and may change; clients branch only on `status` and `code`.

Phase 24 adds the transport-neutral categories needed for this table (business rule → 422, precondition → 412/428, rate limit, gone) to `ErrorCategory`, appending new `ErrorCode` values without renumbering existing ones.

## 6. Idempotency

- Required `Idempotency-Key` header (1–64 chars, client-generated UUID recommended) on every `POST` that creates a resource or performs a state transition; optional elsewhere. Missing on a required endpoint → `400` (`code: idempotency.key-required`).
- The server stores (key, user ID, method + route template, SHA-256 of the canonical request body, response status, response body or resource location, expiry 24 h) in PostgreSQL.
- Same key + same payload → the stored response is replayed (same status and body) with header `Idempotent-Replayed: true`.
- Same key + different payload → `422` (`code: idempotency.key-reused`).
- Same key while the first request is still executing → `409` (`code: idempotency.in-progress`).
- Idempotency complements but does not replace domain idempotency (BUD-007, APR-014, PUR-006).

## 7. Browser session and CSRF

Implements D23-11 in Phase 24 (rules SEC-001..010).

| Item | Design |
|---|---|
| Endpoints | `POST /api/v2/auth/sessions` (login), `POST /api/v2/auth/sessions/refresh`, `DELETE /api/v2/auth/sessions/current` (logout), `DELETE /api/v2/auth/sessions` (revoke all), `GET /api/v2/account/me` |
| Access token | JWT returned in the JSON body, lifetime 15 min, kept only in SPA memory; sent as `Authorization: Bearer` |
| Refresh cookie | `__Secure-ph_refresh`; `HttpOnly`; `Secure`; `SameSite=Strict`; `Path=/api/v2/auth/sessions`; no `Domain`; expires at the family's absolute expiry |
| CSRF | Double-submit token: non-HttpOnly cookie `XSRF-TOKEN` (`Secure`, `SameSite=Strict`, `Path=/`) holding a value bound to the session family by HMAC; the SPA echoes it in `X-XSRF-TOKEN` on refresh and logout (Angular's default cookie/header names). The server also verifies `Origin` against the configured allowed origins. Bearer-authenticated endpoints do not need CSRF because the browser never attaches the access token automatically. |
| Topology | The SPA reaches the API **same-origin**: through the Angular dev-server proxy locally and through the same host/reverse proxy when deployed. CORS stays explicit and validated for other configured origins; credentials are never combined with wildcard origins. |
| Local HTTPS | Development uses the existing HTTPS development certificate so `Secure` cookies work locally. |

## 8. Asynchronous operations (`202 Accepted`)

- Long-running requests (report generation, document regeneration) return `202 Accepted` with `Location: /api/v2/report-jobs/{id}`, `Retry-After: 2`, and the job representation (`id`, `status`, `requestedAt`).
- `GET` on the job returns its state; when `Ready` it includes a `contentUrl` (`/api/v2/report-jobs/{id}/content`), an API path, never a storage URL.
- Clients poll with bounded exponential backoff (2 s doubling up to 30 s, stop after 10 min and show "still running").

## 9. File download and upload

- Upload: `POST /api/v2/files` (`multipart/form-data`, field `file`, plus `ownerType`/`ownerId`) streams to storage and returns the `StoredFile` metadata (`201`). Limits per `FilePolicy` (FIL-002).
- Download: `GET /api/v2/files/{id}/content` and `GET /api/v2/report-jobs/{id}/content` stream bytes after authorization (FIL-003), with `Content-Disposition: attachment; filename*=UTF-8''<sanitized>`, exact `Content-Type`, `X-Content-Type-Options: nosniff`, `Cache-Control: private, no-store`.
- Object-storage URLs (including delegated SAS URLs) are never returned in the API unless a later decision approves bounded delegated URLs; they are not a public contract.

## 10. Compatibility and legacy samples

- New procurement endpoints are additive; each phase captures OpenAPI v1/v2 with `scripts/capture-api-baseline.ps1` and classifies every difference (additive / breaking). Breaking changes to existing operations require explicit approval and a migration note.
- API v1 stays deprecated, supported and without sunset date.
- Sample capabilities (`product`, `person`, `message`, `ProductEntity`, `Person`, `Order`, the product cache and the person search index) stay untouched and isolated. They are removed only after: (1) the procurement replacement for the demonstrated technique exists (cache → catalog/lookup caching, search → a procurement projection, messaging → Outbox events), (2) the owner approves a sunset with dates, and (3) a new contract baseline is captured. This is not part of Phases 23–33 unless separately approved; Phase 33 keeps them by default.
- OpenAPI documents permission requirements per operation (description or extension `x-permissions`) and lists the error `code` values each operation can return.
