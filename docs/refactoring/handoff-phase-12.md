# Phase 12 Execution Handoff

Read and fully execute Phase 12 of this repository's refactoring plan.

Main objective:
Harden the Identity and JWT boundaries by standardizing account use cases, keeping ASP.NET Core Identity implementation details in `Template.Infrastructure.Identity`, validating Identity/JWT options safely at startup, introducing a focused current-user/claims boundary, reviewing token validation behavior, removing any redundant legacy Identity package, and adding comprehensive authentication and authorization tests.

Do not merely analyze or propose a plan: implement, test, validate, and document Phase 12 until all acceptance criteria are met.

## Required context

The repository is located at:

`C:\Users\alexa\source\repos\TemplateCleanArchNET`

Before changing any code, read the following in full:

- `AGENTS.md`
- `docs/phase-2-workspace.md`
- `docs/phase-3-implementation-plan.md`
- `docs/refactoring/phases-0-5-report.md`
- `docs/refactoring/phases-6-7-report.md`
- `docs/refactoring/phase-8-report.md`
- `docs/refactoring/phase-9-report.md`
- `docs/refactoring/phase-10-report.md`
- `docs/refactoring/phase-11-report.md`
- Any folder-specific `AGENTS.md` files governing files you modify
- The applicable repository skills, especially:
  - `integration-adapter`
  - `architecture-boundary-check`
  - `dotnet-change-verification`
  - `api-contract-change`, because authentication failures and account endpoints are part of the public HTTP contract
  - `package-upgrade`, only if a package version changes
  - `ef-migration`, only if a real relational schema change is proven necessary

Treat the following as the approved API baseline:

- `docs/refactoring/baseline-phase-6/`

## Prerequisites

Before starting, verify from the repository's actual state that Phase 11 is complete and that the accumulated work from Phases 0–11 is present in the current checkout.

Confirm at least that:

- Phase 10's Elastic boundary remains intact;
- the Phase 11 Elastic upgrade report exists and records actual results;
- Application remains free of Redis, RabbitMQ, and Elastic implementation packages;
- architecture tests contain no reopened exceptions from Phases 8–10;
- the WebApi build and non-optional test suites are green at the latest recorded checkpoint;
- the Phase 6 HTTP baseline still represents the approved public contract.

If the accumulated Phase 0–11 state is missing, do not work from an outdated branch or worktree. Objectively record the blocker and stop rather than reimplementing or selectively copying prior phases.

## Checkout and working tree

Work only in the checkout containing the authoritative accumulated state from the previous phases.

The working tree may contain pre-existing changes belonging to the user. Preserve them in full.

Do not use:

- `git reset`;
- `git checkout --`;
- `git clean`;
- `git stash`;
- destructive rebases;
- any command that discards or overwrites existing changes.

Do not commit, push, merge, change branches, create another worktree, or open a pull request without explicit authorization.

## Scope of Phase 12

Implement the requirements defined under “Phase 12 — Harden identity and JWT boundaries” in `docs/phase-3-implementation-plan.md`:

1. Keep account orchestration and contracts in Application; keep `UserManager`, `SignInManager`, Identity models, Identity context, token construction, and provider-specific code in `Template.Infrastructure.Identity`.
2. Validate JWT and Identity options at startup without logging their values.
3. Review issuer, audience, lifetime, security stamp, 401/403 Problem Details, and HTTPS metadata behavior.
4. Remove the redundant legacy Identity 2.3.x metapackage if it still exists in the authoritative post-Phase-11 state.
5. Add tests for token expiry, invalid signature, stale security stamp, forbidden role, and password changes.

Do not change the authentication scheme and do not add an external identity provider.

## Current-state characterization

Before editing, inventory the actual post-Phase-11 implementation, including:

- Application account interfaces, DTOs, commands, queries, handlers, validators, and result wrappers;
- account controllers and their v1/v2 behavior;
- `IAccountServices`, `IGetUserServices`, and `IAuthenticatedUserService` or their current replacements;
- `AccountServices` and other Identity adapters;
- `UserManager<ApplicationUser>` and `SignInManager<ApplicationUser>` usage;
- Identity models and `IdentityContext`;
- JWT token creation;
- JWT bearer configuration;
- signing algorithm and key source;
- issuer and audience validation;
- lifetime validation and clock skew;
- security-stamp creation and validation;
- role and claim creation;
- current-user claim extraction;
- password-change behavior;
- authentication and authorization handlers;
- 401 and 403 behavior;
- `WWW-Authenticate` behavior;
- HTTPS metadata settings;
- typed settings and their configuration sources;
- secret-bearing configuration keys;
- package references, including any legacy Identity metapackage;
- Identity migrations and current schema;
- unit, integration, functional, and architecture tests.

Record the inventory in the final report without copying secret values, raw tokens, passwords, signing keys, connection strings, password hashes, security stamps, or personal data.

Do not assume that filenames or implementations observed in earlier phases are unchanged. Treat the actual repository state as authoritative.

## Architecture boundaries

Keep responsibilities separated as follows.

### Application may own

- account use-case orchestration;
- account request and response contracts;
- validators;
- application result/error semantics;
- ports required by account use cases;
- a focused abstraction representing the authenticated/current user;
- application-level role or permission requirements when they are genuine use-case concepts.

### Infrastructure.Identity must own

- `UserManager`;
- `SignInManager`;
- `RoleManager`;
- Identity entities and stores;
- `IdentityContext`;
- password hashing and Identity token providers;
- security-stamp validation;
- JWT creation and signing;
- JWT bearer configuration;
- Identity/JWT options specific to the adapter;
- Identity persistence, migrations, and seeds;
- mapping between Application account contracts and Identity models.

### WebApi may own

- authentication/authorization middleware composition;
- HTTP-context access;
- extraction of request-scoped claims into an Application-owned current-user abstraction;
- HTTP mapping for 401 and 403;
- authorization policies tied to the HTTP host;
- DI composition.

### Domain must remain free of

- ASP.NET Core Identity;
- JWT libraries;
- claims principals;
- HTTP context;
- authorization handlers;
- authentication schemes;
- database or transport concerns.

Do not leak `UserManager`, `SignInManager`, Identity entities, `ClaimsPrincipal`, `HttpContext`, JWT handler types, or Microsoft IdentityModel types into Domain or Application ports.

## Account-use-case standardization

Review the existing account workflows and make their boundaries consistent without changing the public HTTP contract.

Cover at least:

- registration;
- authentication/login;
- refresh or token renewal, if currently implemented;
- password change;
- user lookup;
- role assignment or role-dependent behavior, if currently implemented;
- logout or token revocation behavior, if currently implemented;
- current-user access.

Do not add new account features merely for completeness.

Application use cases must not directly depend on ASP.NET Core Identity implementation types.

Expected business failures must use the established Application result/error model rather than exceptions.

Unexpected infrastructure failures must not be exposed to clients as raw exception messages.

Propagate `CancellationToken` through every API that supports it. Where ASP.NET Core Identity APIs do not accept a cancellation token, check cancellation before and between asynchronous calls and document the limitation rather than pretending an in-flight Identity operation can be canceled.

## Current-user and claims boundary

Review `IAuthenticatedUserService` and its implementation.

Define the smallest Application-owned abstraction needed by use cases and persistence auditing. It must not expose `HttpContext` or `ClaimsPrincipal`.

The boundary must define the semantics of:

- unauthenticated requests;
- missing subject/name-identifier claim;
- missing or empty username;
- malformed user identifier;
- roles or claims needed by current use cases;
- background operations without an HTTP user;
- design-time and test contexts.

Do not silently turn a missing authenticated user into a fabricated real user.

If auditing requires a system/anonymous identity, make that policy explicit and test it.

Avoid capturing request-dependent claim values at singleton construction time. Use an appropriate request-scoped lifetime or accessor behavior.

## Typed options and startup validation

Bind Identity and JWT configuration through typed options with startup validation.

Validate, as applicable:

- signing key is present;
- signing key meets the requirements of the selected signing algorithm;
- issuer is present;
- audience is present;
- token lifetime is positive and within a documented allowed range;
- Identity password length is valid;
- boolean policy values bind correctly;
- environment-specific HTTPS metadata policy is explicit;
- required configuration is not silently replaced by insecure defaults.

Use supported options-validation mechanisms such as `Validate`, `IValidateOptions<T>`, data annotations where appropriate, and `ValidateOnStart`.

Validation errors must identify configuration keys or categories, not secret values.

Never include signing keys, passwords, hashes, stamps, tokens, or full connection strings in:

- exception messages;
- validation messages;
- logs;
- test output;
- reports;
- committed configuration.

The Test environment may use an explicit test-only signing key, but it must be clearly isolated from production configuration and must not be presented as a deployable secret.

## JWT issuance and validation

Review token issuance and validation as one coherent contract.

Confirm and test:

- issuer in generated tokens matches the configured valid issuer;
- audience in generated tokens matches the configured valid audience;
- signing algorithm matches validation expectations;
- signing key usage is consistent;
- `ValidateIssuerSigningKey` remains enabled;
- issuer validation remains enabled;
- audience validation remains enabled;
- lifetime validation remains enabled;
- expiration is generated from an appropriate time source;
- expiration behavior is deterministic in tests;
- clock skew is explicit and documented;
- subject/name identifier is stable and correctly parsed;
- username and role claims are intentional;
- security-stamp claim behavior is correct;
- malformed tokens fail safely;
- tokens signed by a different key fail;
- tokens with the wrong issuer fail;
- tokens with the wrong audience fail;
- expired tokens fail;
- not-yet-valid tokens fail when applicable;
- tokens without required claims fail;
- tokens with a stale security stamp fail.

Do not weaken validation to make tests pass.

Do not log raw JWTs or include them in assertion failure messages.

If tests need to inspect tokens, decode only the minimum metadata required and redact values in diagnostics.

## Time handling

Avoid tests that depend on wall-clock sleeps.

Use the existing `IClock` abstraction where it belongs to Application behavior. For JWT framework validation, use the supported time abstraction or a deterministic token construction strategy appropriate to the installed framework version.

Do not invent unsupported framework hooks. Consult current official ASP.NET Core and Microsoft.IdentityModel documentation if token-validation time customization is required.

Keep production and test clock behavior explicit.

## Security-stamp behavior

Review the current security-stamp validation performed after JWT validation.

Ensure that:

- a token contains the required stamp claim when the current contract requires it;
- a missing stamp fails authentication;
- a token issued before a password change becomes invalid when the Identity security stamp changes;
- a token issued after the password change is valid;
- an unknown or deleted user fails authentication;
- failures do not expose whether a particular account exists beyond the established public contract;
- failure paths do not continue as authenticated;
- asynchronous validation does not ignore cancellation unnecessarily.

Verify the actual ASP.NET Core Identity behavior for password changes and security-stamp updates. Do not assume that every password-related operation automatically changes the stamp; call the supported API explicitly when required by the intended policy.

## Password policy and password changes

Preserve the currently approved password-policy behavior unless a defect or security requirement demonstrates a necessary change.

Test:

- correct current password;
- incorrect current password;
- new password validation;
- password-change success;
- password-change failure;
- security-stamp update or invalidation behavior;
- old JWT rejection after the successful password change;
- newly issued JWT acceptance;
- expected HTTP result mapping;
- absence of password values in logs and responses.

Do not include plaintext passwords in persistent test logs or reports.

Use synthetic test credentials isolated to the test fixture.

## 401 and 403 behavior

Preserve the distinction established in Phase 6:

- unauthenticated or invalid credentials/token: HTTP 401;
- authenticated principal lacking permission or role: HTTP 403.

Preserve:

- `application/problem+json` where currently established;
- the approved `ProblemDetails` shape;
- the `WWW-Authenticate: Bearer` challenge on 401;
- no authentication challenge on an ordinary 403 unless the established framework behavior requires it;
- no internal exception, token detail, signing detail, or security-stamp detail in responses;
- existing route and API-version behavior.

Test 401 and 403 separately. A test that accepts either status is not sufficient.

Do not convert authorization failures into HTTP 404 merely to obscure endpoint existence unless that behavior is already part of the approved contract.

## HTTPS metadata policy

Review the current `RequireHttpsMetadata` setting.

The application currently uses locally issued JWT bearer tokens rather than an external OpenID Connect authority. Determine the actual relevance of metadata retrieval before changing the setting.

Make the policy explicit by environment and document the reasoning.

Do not globally disable a meaningful HTTPS security check in production merely to support local development.

Do not add an external identity provider or change the authentication scheme in this phase.

If the setting has no operational effect for the current bearer configuration, document that fact using official framework evidence rather than making a cosmetic change.

## Legacy Identity package removal

Inspect the complete direct and transitive package graph for legacy ASP.NET Core Identity packages, especially any 2.3.x metapackage or redundant compatibility reference.

If such a package exists:

- prove it is unused or superseded by the .NET 10 framework/package set;
- remove it from the correct project or central package file;
- restore and build;
- verify runtime authentication behavior;
- run the vulnerability audit;
- document the removal and rollback.

If the package is already absent in the authoritative post-Phase-11 state, record that the requirement was verified and no removal was necessary.

Do not remove `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, JWT bearer support, or another active package merely because its name contains “Identity.”

Do not upgrade unrelated package families.

If an Identity/JWT package version must change, use current official sources and the `package-upgrade` skill.

## Secret handling

Inventory secret-bearing configuration keys without copying their values.

At minimum, review:

- JWT signing key;
- Identity database connection string;
- seeded-user credentials;
- any API keys or external authentication secrets;
- environment-variable mappings;
- test-only secrets.

Ensure that:

- real secrets are not committed;
- production secrets have no insecure source-code fallback;
- local secrets can be supplied through user secrets or environment variables;
- `.env` remains untracked and ignored;
- `.env.example`, if used, contains placeholders only;
- logs and startup-validation failures never print values;
- tests do not snapshot or echo raw tokens or credentials;
- documentation names configuration keys without including values.

Do not rotate or replace a real signing key as part of this phase without explicit authorization and a token-transition plan.

Preserve signing-key/config compatibility so existing valid tokens are not unintentionally invalidated by deployment.

If a key-format change is unavoidable, document staged rollout and rollback before implementing it.

## Identity schema and migrations

This phase does not require an Identity schema change by default.

Before creating any migration:

- prove that the EF model has a real relational schema delta;
- explain why the behavior cannot be implemented without it;
- use the `ef-migration` skill;
- generate an additive migration through EF tooling;
- inspect generated SQL;
- test upgrade and rollback on an isolated PostgreSQL database.

Do not manually edit migration designer or snapshot files.

Do not rewrite the existing initial Identity migration.

If no schema delta exists, create no migration and explicitly record that migrations remained unchanged.

## HTTP and API compatibility

Phase 12 must not change the public authentication scheme or unrelated HTTP contracts.

Preserve:

- account routes;
- HTTP methods;
- route, query, and body parameters;
- status codes;
- v1 legacy envelopes where applicable;
- v2 `ProblemDetails` behavior;
- response DTO shapes;
- token response field names;
- API versioning;
- v1 deprecation;
- `WWW-Authenticate` behavior;
- published OpenAPI.

Do not remove v1.

Do not silently rename token properties or claims consumed by existing clients.

Capture OpenAPI again and compare it with:

`docs/refactoring/baseline-phase-6/`

The expected result is exact equality.

If any difference appears, investigate it. Do not silently update the baseline.

If a security fix genuinely requires a public-contract change, stop and document the required change, compatibility impact, and migration options before proceeding.

## Required tests

Add or update tests proportionally to the security-sensitive behavior.

### Unit tests

Cover at least:

1. JWT issuance uses the configured issuer.
2. JWT issuance uses the configured audience.
3. JWT issuance uses the configured lifetime.
4. Required identity and role claims are emitted.
5. Security-stamp claim is emitted when required.
6. Current-user abstraction maps authenticated claims correctly.
7. Missing user identifier follows the documented policy.
8. Malformed user identifier follows the documented policy.
9. Background/no-HTTP contexts follow the documented policy.
10. Account business failures use Application results rather than leaking Identity types.
11. `CancellationToken` is propagated where supported.
12. Options validation rejects missing or invalid configuration without exposing values.

### JWT validation tests

Cover at least:

1. Valid token succeeds.
2. Expired token returns 401.
3. Invalid signature returns 401.
4. Wrong issuer returns 401.
5. Wrong audience returns 401.
6. Missing required claim returns 401.
7. Malformed token returns 401.
8. Token for an unknown or deleted user returns 401.
9. Token with a stale security stamp returns 401.
10. Token issued before a password change returns 401 afterward.
11. Token issued after the password change succeeds.
12. `WWW-Authenticate: Bearer` is preserved on 401.
13. Responses contain no raw token or internal validation detail.

### Authorization tests

Cover at least:

1. Authenticated user with the required role is allowed.
2. Authenticated user without the required role receives 403.
3. Unauthenticated user receives 401 rather than 403.
4. Invalid token receives 401 rather than 403.
5. 401 and 403 retain the approved Problem Details media type and shape.
6. 403 does not expose role internals beyond the approved response.

### Account functional tests

Cover the currently published account workflows, including as applicable:

- successful registration;
- duplicate account behavior;
- invalid registration input;
- successful authentication;
- invalid credentials;
- disabled, locked, or otherwise invalid account state if currently supported;
- password-change success;
- incorrect current password;
- invalid new password;
- post-password-change token invalidation;
- current-user lookup;
- cancellation at the HTTP boundary;
- stable v1 and v2 behavior.

Use deterministic, isolated users and databases.

Do not depend on real external identity providers.

### Options and startup tests

Cover at least:

1. Valid configuration starts successfully.
2. Missing signing key fails startup safely.
3. Missing issuer fails startup safely.
4. Missing audience fails startup safely.
5. Invalid token lifetime fails startup safely.
6. Invalid password-policy configuration fails startup safely.
7. Failure messages contain configuration names but not values.
8. Test configuration is isolated from production configuration.

### Architecture tests

Prove, using positive tests and controlled mutations when appropriate, that:

- Domain does not reference ASP.NET Core Identity, JWT, claims, or HTTP packages;
- Application does not reference `Microsoft.AspNetCore.Identity` implementation types;
- Application does not reference `System.IdentityModel.Tokens.Jwt`;
- Application does not reference `Microsoft.IdentityModel.Tokens`;
- Application does not reference `HttpContext` or `ClaimsPrincipal`;
- `UserManager`, `SignInManager`, Identity models, and `IdentityContext` remain in Infrastructure.Identity;
- JWT construction and signing remain in Infrastructure.Identity;
- WebApi remains the composition root and HTTP-context adapter;
- no broad architecture exception is introduced;
- exceptions removed in earlier phases do not return.

### Persistence and migration tests

Run the existing Identity database tests and PostgreSQL-backed validation.

If no migration is created, prove that the model has no pending migration when the available tooling supports that check.

If a migration is required, test it independently according to repository rules.

## Security test hygiene

Authentication tests must not leak sensitive values through:

- test names;
- assertion messages;
- console output;
- snapshots;
- validation artifacts;
- reports;
- committed fixtures.

When a test requires a token, keep it in memory and avoid printing it.

Use synthetic values specifically scoped to tests.

Do not weaken signing-key requirements in production code merely to simplify tests.

## Rollback requirements

Authentication regressions can lock users out. Keep the change set reversible.

The rollback plan must preserve:

- the existing authentication scheme;
- signing-key compatibility;
- issuer and audience compatibility;
- existing claim names consumed by clients;
- previous Identity/JWT configuration;
- previous package graph;
- unchanged Identity database data unless an explicitly reviewed migration is required.

Document how to coherently revert:

- option binding and validation;
- JWT issuance changes;
- JWT validation changes;
- current-user adapter changes;
- package removal;
- password/security-stamp behavior.

Do not rotate keys, delete users, reset passwords, modify production credentials, or remove Identity data as part of rollback testing.

## Scope boundaries

Do not perform work from subsequent phases:

- do not implement Phase 13 observability standardization;
- do not redesign global logging or correlation;
- do not split liveness/readiness endpoints beyond what is strictly required for an authentication test;
- do not change localization architecture;
- do not add an external identity provider;
- do not migrate to OpenID Connect or OAuth authorization-code flow;
- do not change the authentication scheme;
- do not introduce refresh tokens if they do not already exist;
- do not add MFA, passkeys, social login, or account recovery as unrelated features;
- do not redesign role/permission semantics beyond fixing a demonstrated defect;
- do not upgrade Redis, RabbitMQ, Elasticsearch, PostgreSQL, or unrelated packages;
- do not remove v1;
- do not perform unrelated cosmetic modernization;
- do not perform work from Phases 13+.

## Validation

Run build and test commands sequentially because parallel builds compete for shared `obj` directories.

Use the pinned SDK:

`C:\Users\alexa\.dotnet\dotnet.exe`

Start with the smallest relevant Identity, JWT, unit, and functional tests.

At the end, run the repository's complete validation gate:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-phase-validation.ps1
```

Confirm at least:

- restore;
- WebApi build without warnings;
- architecture tests;
- unit tests;
- EF InMemory integration tests;
- real PostgreSQL integration tests;
- authentication/account functional tests;
- token-expiry tests;
- invalid-signature tests;
- issuer and audience tests;
- stale-security-stamp tests;
- password-change tests;
- forbidden-role tests;
- startup options-validation tests;
- direct and transitive NuGet vulnerability audit;
- `docker compose config -q`;
- OpenAPI capture and exact comparison;
- migration/no-pending-model-change validation where available;
- `git diff --check`.

Because this phase may change project structure or dependencies, regenerate `graphify-out` through the official Graphify command when required by repository rules.

Do not edit `graphify-out` manually.

If Docker, PostgreSQL, a required tool, or another dependency is unavailable, do not report the corresponding gate as passed. Record precisely:

- the command attempted;
- the result;
- the observed cause;
- which acceptance criteria remain unverified.

Do not silently reduce security checks or test coverage to obtain a green gate.

## Acceptance criteria

Phase 12 may only be considered complete when:

- account orchestration and contracts remain in Application;
- `UserManager`, `SignInManager`, Identity models, and `IdentityContext` remain in Infrastructure.Identity;
- JWT construction and validation details remain outside Domain and Application;
- the current-user boundary does not expose `HttpContext` or `ClaimsPrincipal`;
- JWT and Identity options are validated at startup;
- validation failures do not expose secret values;
- issuer, audience, signing key, and lifetime validation are enabled and tested;
- clock skew and HTTPS metadata policies are explicit and documented;
- security-stamp validation is correct and tested;
- a successful password change invalidates the previous token according to the documented policy;
- 401 and 403 remain distinct;
- 401 preserves `WWW-Authenticate: Bearer`;
- responses contain no internal token or Identity details;
- no secrets appear in source, logs, reports, or test output;
- the legacy Identity 2.3.x package is removed if present, or its absence is verified and documented;
- the package vulnerability audit is clean;
- Identity migrations are unchanged unless a separately justified and reviewed schema delta exists;
- account/auth functional tests pass;
- the HTTP contract and OpenAPI remain unchanged;
- architecture and all unrelated suites remain green;
- no Phase 13+ work was started.

## Documentation and delivery

Create:

`docs/refactoring/phase-12-report.md`

The report must include:

- an executive summary;
- proof that prerequisites were met;
- the pre-change Identity/JWT inventory;
- account-use-case boundary decisions;
- current-user/claims boundary decisions;
- JWT issuance and validation policy;
- issuer, audience, lifetime, signing, and clock-skew behavior;
- security-stamp behavior;
- password-change and token-invalidation behavior;
- 401/403 and `WWW-Authenticate` behavior;
- HTTPS metadata decision and rationale;
- typed options and startup validation;
- secret-handling review by configuration-key name only;
- package graph review;
- legacy package removal or proof that it was already absent;
- migration review and confirmation of whether schema changed;
- tests added or updated;
- commands executed and actual results;
- final test counts;
- OpenAPI comparison result;
- vulnerability-audit result;
- residual risks;
- executable rollback procedure;
- gates not executed;
- explicit confirmation that no authentication scheme or external identity provider was introduced;
- explicit confirmation that no Phase 13+ work was implemented.

Update the root `AGENTS.md` only with commands and results that were actually executed and confirmed.

Upon completion, present:

1. An objective implementation summary.
2. The main files and packages changed.
3. Identity/Application boundary decisions.
4. JWT validation and security-stamp decisions.
5. Options-validation and secret-handling results.
6. Authentication and authorization test results.
7. Migration/schema result.
8. Full validation results.
9. Pending work or gates not executed.
10. Residual risks and rollback.
11. Confirmation that 401/403 and HTTP contracts were preserved.
12. Confirmation that no secrets were introduced or exposed.
13. Confirmation that no external identity provider or new authentication scheme was added.
14. Confirmation that Phases 13+ were not started.

Do not commit, push, merge, change branches, create another worktree, rotate keys, modify production credentials, or open a pull request without explicit authorization.
