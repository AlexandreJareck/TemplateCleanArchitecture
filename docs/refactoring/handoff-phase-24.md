# Phase 24 Execution Handoff — Identity, Authorization, and API Foundations

## Mission

Make the existing WebApi safe and predictable for a real browser SPA before procurement features are added. Implement permission-based authorization, current-session APIs, rotating refresh sessions, logout/revocation, configurable CORS, API idempotency/concurrency conventions, and stable v2 transport primitives without breaking existing v1 or v2 contracts.

## Required context and skills

Work in `C:\Users\alexa\source\repos\TemplateCleanArchNET`. Read root/nested `AGENTS.md`, the Phase 23 blueprint/report and ADRs, Phase 12/13/21/22 reports, API lifecycle/baselines, Identity/Application/WebApi code, migrations, security tests, configuration, Compose, and validation scripts.

Use `api-contract-change` for every HTTP/auth contract change, `ef-migration` for schema changes, `architecture-boundary-check` for reference/boundary changes, and `dotnet-change-verification` for final checks. Research current official security guidance and package versions; never select versions from memory.

## Preconditions and hard stops

- Phases 22 and 23 must be complete and green; D23 decisions must be recorded.
- Capture HEAD/dirty tree and preserve concurrent work.
- Capture current auth responses, 401/403 behavior, security-stamp behavior, OpenAPI v1/v2, and functional test baseline.
- Stop for owner direction if the approved refresh-session transport is unspecified. Recommended default: short-lived access token in memory plus rotating refresh token in a Secure, HttpOnly, SameSite cookie, with an explicit CSRF design.
- Do not weaken Phase 22 secret handling or print tokens/cookies.

## In scope

1. Add application-owned authentication/session contracts and use cases; keep ASP.NET Core Identity and token implementation in Infrastructure.Identity.
2. Implement refresh-session persistence as hashed, revocable, expiring, rotating records with reuse detection and bounded session families.
3. Add v2 endpoints for login/session creation as approved, refresh, logout/current-session revocation, revoke-all, and `GET /api/v2/account/me`.
4. Preserve existing authenticate/start/change routes and response shapes unless an approved additive migration explicitly supersedes them. Do not silently change every-login security-stamp behavior.
5. Define permissions and policies from the Phase 23 matrix; roles may group permissions, but controllers authorize permissions rather than embedding business decisions.
6. Add auditable security events without logging credentials, tokens, cookies, personal payloads, or hashes.

Authorization answers whether an actor may attempt an operation; the relevant use case/domain policy still decides whether the business action is valid in the current state. Keep stable permission names distinct from `BUD-*`, `REQ-*`, `SRC-*`, `APR-*`, `PUR-*`, and `RCV-*` business-rule IDs. A broad administrator role must not become an implicit bypass for budget, sourcing, approval, purchasing, or receiving rules; every approved business exception uses its dedicated permission and waiver record.
7. Replace `AddAnyCors` with validated typed settings. Development origins are explicit; Production fails safely when browser origins are required but absent. Credentials and wildcard origins must never be combined.
8. Implement reusable HTTP conventions approved in Phase 23: idempotency key semantics for selected commands, optimistic-concurrency precondition shape, pagination/filter/sort envelope, Problem Details extensions, and asynchronous-operation response types. Avoid speculative infrastructure not exercised by a focused endpoint/test fixture.
9. Add configuration documentation using key names only.

## Security requirements

- Store refresh-token verifiers as cryptographic hashes, never plaintext.
- Rotation is atomic; reuse revokes the affected family and produces a sanitized audit event.
- Define cookie path, domain, SameSite, Secure, expiration, and CSRF behavior explicitly for local and production profiles.
- Rate-limit or otherwise protect authentication endpoints using supported ASP.NET Core capabilities if approved by Phase 23; prove 429 behavior without changing unrelated endpoints.
- Password/security-stamp changes revoke relevant sessions according to the recorded decision.
- Current-user data exposes only approved claims/profile fields.
- Missing, expired, revoked, replayed, malformed, and concurrently refreshed sessions have deterministic outcomes.

## Out of scope

- Procurement entities or screens;
- external identity providers, MFA, password recovery email, multi-tenancy, or a general IAM product;
- frontend implementation (Phase 26);
- API v1 removal;
- package majors unrelated to the feature.

## Tests and validation

Add domain/application tests where applicable, Identity integration tests against real PostgreSQL for session atomicity/concurrency, functional tests for login/refresh/logout/me/permissions/CORS/CSRF/401/403/429, architecture mutation tests for boundary rules, migration review, and exact OpenAPI diff classification. Exercise refresh reuse and concurrent rotation without exposing values.

Run sequentially: restore if required, WebApi build, architecture, unit, integration, PostgreSQL, functional, discovery/coverage, secret scan, vulnerability/license audits for changed packages, Compose configuration, OpenAPI comparison, Graphify after structural changes, and `git diff --check`. Run the repository gate at the end with only the real-service switches proportionate to changed infrastructure.

## Acceptance criteria

- A SPA can establish, refresh, inspect, and revoke a session safely.
- Permission policies are enforced and tested independently of UI hiding, and no broad role silently bypasses business rules.
- Refresh reuse and concurrency are safe and auditable.
- CORS is explicit and validated; no wildcard credential policy remains.
- Existing API routes remain compatible or have an approved additive migration and exact contract evidence.
- Secrets/tokens never appear in committed files or artifacts.
- All required gates are green; otherwise the phase report is `Not ready`.

## Report and final response

Create `docs/refactoring/phase-24-report.md` with decisions, threat model, schema, endpoints, compatibility diff, migrations, tests/counts, audits, configuration keys, rollback, residual risks, and complete gate table. Lead the final response with readiness, then behavior, proof, compatibility, risks, and report path.

Do not commit, push, merge, change branches/worktrees, open a PR, rewrite migrations, or delete data/resources without explicit authorization.
