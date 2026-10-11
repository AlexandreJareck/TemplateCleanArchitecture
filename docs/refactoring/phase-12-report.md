# Phase 12 report — Harden Identity and JWT boundaries

Executed on 2026-10-11 in the authoritative checkout (`codex/english-file-names`, HEAD `2842b5b`). No commit, push, branch change, key rotation, or pull request was made.

## Executive summary

- Identity/JWT options (`JwtSettings`, `IdentitySettings`) are now typed options with `IValidateOptions<T>` validators and `ValidateOnStart`. Failure messages name keys only.
- Token creation moved into `JwtTokenFactory` (Infrastructure.Identity). It uses `IClock` for `nbf`/`exp`. It shares one signing-key and validation-parameter source with the bearer handler. Issuer, audience, signing key and lifetime validation stay enabled. `RequireExpirationTime`, `RequireSignedTokens` and `ValidAlgorithms = [HS256]` were added. Clock skew is explicitly zero.
- Security-stamp validation moved to `SecurityStampTokenValidator`. The old handler kept going after `context.Fail(...)`; the new one returns at the first failure. It requires the subject and stamp claims and checks `RequestAborted` before the Identity call.
- `IncludeErrorDetails = false`: the 401 `WWW-Authenticate` header is now exactly `Bearer`, without `error`/`error_description`.
- `RequireHttpsMetadata` is now `true` (the framework default). It has no effect at runtime because neither `Authority` nor `MetadataAddress` is set, so no metadata is fetched (see the HTTPS section).
- The current-user boundary (`IAuthenticatedUserService`) is nullable-annotated with a documented no-user policy. The WebApi adapter now reads claims when a property is accessed, not at construction, and only from an authenticated principal.
- Account services return an `ErrorInIdentity` result (HTTP 401) when there is no current user. Before, a deleted user or missing subject caused a `NullReferenceException`.
- Identity migration and seeding moved out of `Program.cs` into `InitializeIdentityDatabaseAsync`. Program no longer references `IdentityContext`, `UserManager` or `RoleManager`.
- The legacy `Microsoft.AspNetCore.Identity` 2.3.x metapackage was already absent; that was checked and is now enforced by a test. No package versions changed. No migration was added.
- HTTP contract: OpenAPI v1/v2 are identical to `docs/refactoring/baseline-phase-6`, apart from line endings.

## Prerequisites verified

- `docs/refactoring/phase-11-report.md` exists and the Phase 11 commit `b9cfdc2` is in history. The Phase 10/11 Elastic boundary is intact: the architecture tests for search, logging and health checks are green.
- Application has no Redis, RabbitMQ or Elastic packages, and the Phase 8–10 architecture exceptions have not returned.
- The Phase 11 gate (build, all suites) was re-run green before the final gate (see below). `docs/refactoring/baseline-phase-6/` is the approved contract.

## Pre-change inventory (no values copied)

| Area | State found |
|---|---|
| Application ports | `IAccountServices` (ghost register, change password, change user name, authenticate, authenticate by user name), `IGetUserServices` (paged users; no published endpoint), `IAuthenticatedUserService` (`UserId`, `UserName`) |
| DTOs/validators | `AuthenticationRequest`, `ChangePasswordRequest` (`Password`, `ConfirmPassword`; **no current password**), `ChangeUserNameRequest`, `AuthenticationResponse` (`id`, `userName`, `email`, `roles`, `isVerified`, `jwToken`) |
| Controllers | v1 (deprecated, legacy envelope) and v2 (`ApiResultMapper`) `account/authenticate`, `change-user-name` [Authorize], `change-password` [Authorize], `start` |
| Identity adapter | `AccountServices` (`UserManager`, `SignInManager`, `JwtSettings` singleton); `GetUserServices` (`IdentityContext`) |
| Token creation | `JwtSecurityToken` with HS256, `DateTime.UtcNow` expiry, claims from `SignInManager.ClaimsFactory` (Identity claim URIs: name identifier, name, roles, `AspNet.Identity.SecurityStamp`); stamp rotated on every sign-in |
| Bearer config | Read eagerly at registration; issuer/audience/lifetime/signing-key validation on; `ClockSkew = 0`; `RequireHttpsMetadata = false`; `OnTokenValidated` stamp check that **continued after `Fail`** |
| Current user | Captured claims in constructor (scoped); malformed id would make `Guid.Parse` in auditing throw |
| 401/403 | `ProblemDetailsAuthorizationMiddlewareResultHandler`: 401 + `WWW-Authenticate: Bearer` + problem+json; 403 problem+json |
| Settings sources | `appsettings*.json` (`JwtSettings`, `IdentitySettings`), `ConnectionStrings:IdentityConnection` |
| Packages | `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.12, `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.12, `System.IdentityModel.Tokens.Jwt` 8.23.0; no legacy 2.3.x Identity package |
| Migrations | Single `20250326223511_InitialPostgresMigration` |

## Boundary decisions

- **Application** keeps the account contracts, validators, result semantics and the `IAuthenticatedUserService` port. It now has a nullable-annotated contract and no HTTP or claims types.
- **Infrastructure.Identity** owns `UserManager`, `SignInManager`, `RoleManager`, the models, `IdentityContext`, seeds, migrations, `JwtTokenFactory`, `SecurityStampTokenValidator`, JWT bearer options, and the settings plus their validators.
- **WebApi** owns `AuthenticatedUserService` (HTTP-context adapter), 401/403 mapping and composition. A Test-only endpoint `/_tests/admin` (role `Admin`, excluded from OpenAPI) was added next to the existing `/_tests/forbidden`.
- The account use cases were not moved to MediatR handlers. That would have been a structural rewrite with no behavior gain, and the handoff forbids adding features for completeness. The `IAccountServices` port remains the Application boundary.

### Current-user policy

| Situation | `UserId` | `UserName` |
|---|---|---|
| Authenticated, valid GUID subject | GUID string | name or `null` if empty |
| Missing subject | `null` | as available |
| Malformed subject | `null` (treated as no user, never fabricated) | as available |
| Unauthenticated principal / no `HttpContext` (background) | `null` | `null` |
| Design time (`ApplicationDbContextFactory`) | `null` | `null` |

Auditing writes `Guid.Empty` as the explicit system/anonymous actor when `UserId` is `null` or malformed. Previously a malformed value would make `Guid.Parse` throw.

## JWT issuance and validation policy

- Algorithm HS256. One key from `JwtSettings:Key`, at least 32 UTF-8 bytes (checked at startup). Issuance and validation use the same `CreateSigningKey`.
- `iss`/`aud` come from configuration; validation pins the same values.
- `nbf = clock.UtcNow`, `exp = nbf + DurationInMinutes`, where `DurationInMinutes` must be in (0, 1440]. Clock skew is zero.
- Claim names are unchanged: the Identity claim URIs are written verbatim, with no short-name mapping. Clients that read them are unaffected.
- After cryptographic validation, a token needs a non-empty subject and security-stamp claim, an existing user, and a matching stamp. Otherwise the result is 401. Failure reasons go only to `context.Fail` messages; they are not written to responses or headers.
- Malformed, unsigned (`alg: none`), wrongly signed, wrong-issuer, wrong-audience, expired and not-yet-valid tokens all return 401. Tests cover each case.

## Security stamp and password change

- Every sign-in rotates the stamp, so one active token per user remains the behavior (unchanged; now tested).
- Change password uses `GeneratePasswordResetTokenAsync` + `ResetPasswordAsync`, which rotates the stamp. Tested: the old token gets 401 afterwards, including a second change attempt with it on v1 and v2, and a token from a new sign-in is accepted.
- The public contract has **no current-password field**, so the "incorrect current password" case does not exist. Adding one would be a public-contract change and was not made; it is recorded below as a residual risk.
- Deleted or unknown user: 401, the same as any other invalid token.
- Identity APIs take no `CancellationToken`. Cancellation is checked before and between calls in `AccountServices` and before stamp validation. An Identity call already in progress cannot be cancelled.

## 401/403 and HTTPS metadata

- 401: `application/problem+json`, the Phase 6 shape (`type`, `title`, `status`, `instance`, `traceId`), and exactly one challenge, `WWW-Authenticate: Bearer`, with no parameters. Ordinary 403: problem+json with no challenge header. Tests check both statuses separately.
- `RequireHttpsMetadata`: `JwtBearerPostConfigureOptions` builds a metadata `ConfigurationManager` (and enforces HTTPS on it) only when `Authority` or `MetadataAddress` is set. Neither is set, so the flag is inert in every environment. It is set to the secure default `true` so that adding an authority later cannot fetch metadata over HTTP. A test asserts the flag, `Authority == null`, and successful startup in Test. No per-environment variance is needed.

## Typed options and startup validation

| Key | Rule |
|---|---|
| `JwtSettings:Key` | required, ≥ 32 UTF-8 bytes |
| `JwtSettings:Issuer` / `:Audience` | required |
| `JwtSettings:DurationInMinutes` | > 0 and ≤ 1440 (NaN rejected) |
| `IdentitySettings:PasswordRequiredLength` | 4–128 |
| `IdentitySettings:PasswordRequire*` | booleans bound into `IdentityOptions.Password` (verified equal) |

Bearer and Identity options are composed from the validated `IOptions<T>` rather than read at registration. Invalid configuration stops the host, and the exception text contains only the key name. Tests use a sentinel value and assert it does not appear in the message.

## Secret handling (key names only)

- `JwtSettings:Key`: the same 32-byte value is committed in `appsettings.json`, `.Development.json`, `.Production.json` and `.Test.json`. It existed before Phase 12 and was **not changed or rotated**, which keeps existing tokens valid. Values can be overridden through environment variables (`JwtSettings__Key`) or user secrets. See the residual risks.
- `ConnectionStrings:IdentityConnection`, the seeded `Admin` user's password (seed code, pre-existing), `.env` (untracked; only `.env.example` is tracked) were reviewed and not changed.
- Tests use synthetic passwords and keys scoped to the fixture. Tokens and stamps stay in memory and are never written to output or assertion messages. This report contains no values.

## Package graph and legacy package

`Microsoft.AspNetCore.Identity` 2.3.x is absent from `Directory.Packages.props` and from every `Src` project. It appears only in a stale local `.claude/worktrees/fase-8-*` copy, which is outside the checkout. No removal was needed; `Identity_Implementation_Lives_In_Infrastructure_Identity` now asserts it stays absent. No versions changed. The vulnerability audit is clean.

## Migrations

There is no schema change and no new migration. `IdentityModel_HasNoPendingModelChanges` uses `Database.HasPendingModelChanges()` with the Npgsql provider (no connection) and asserts the single existing migration.

## Tests added or updated

- `Tests/Template.Functional.Tests/Security/JwtAuthenticationFunctionalTests.cs` (24 cases): valid issued and crafted tokens; expired; not yet valid; another key; wrong issuer; wrong audience; missing stamp; missing subject; stale stamp; malformed/unsigned (3); missing token; deleted user; newer sign-in invalidates older token; password change invalidates old token and accepts new one (v1, v2); invalid new password (400); mismatched confirmation (400); wrong password (400, no token or password echoed); role allowed (204) versus missing role (403). Every 401 check also asserts problem shape, a bare `Bearer` challenge, and no token, stamp or signature text in the body.
- `Security/IdentityComponentTests.cs` (21 cases): token issuer, audience, lifetime, claims and stamp with a fixed `IClock`; validation parameters; composed bearer options; current-user mapping, missing, malformed, unauthenticated, no-HttpContext and lazy-read cases; account no-user results (`ErrorInIdentity` → Unauthorized); cancellation on all five `IAccountServices` methods; validator rejections without echoing values.
- `Security/IdentityOptionsStartupTests.cs` (8 cases): valid Test startup, and failed startup for missing or short key, missing issuer, missing audience, lifetime 0 or too large, and invalid password length, with key names but no values in the messages.
- `Security/IdentityModelMigrationTests.cs` (1 case).
- `Tests/Template.Architecture.Tests`: three new rules. Core has no Identity, JWT, claims or HTTP references or Identity packages. Identity types, JWT construction and Identity packages exist only in Infrastructure.Identity. WebApi is the only HTTP current-user adapter. The Program.cs exception is now Phase 7 only and asserts no `IdentityContext`. Controlled mutations were added for each new rule.

## Commands executed and results

All commands used the pinned SDK `C:\Users\alexa\.dotnet\dotnet.exe` and ran sequentially.

- Incremental runs: WebApi build; Functional `--filter Security` (first run 48/50, then fixed: a claim-type assertion was wrong because tokens carry Identity URIs, and an over-broad body assertion matched the route path); full Functional 146/146; Architecture 25/25 (after correcting one mutation sample); Unit 77/77; Integration 136/136.
- Final gate `powershell -ExecutionPolicy Bypass -File .\scripts\run-phase-validation.ps1` (2026-10-11 10:44–10:49 −03:00), with every step exiting 0:

| Step | Result |
|---|---|
| restore | succeeded |
| build WebApi | succeeded, 0 warnings |
| Architecture | 25/25 |
| Unit | 77/77 |
| Integration (EF InMemory, adapters) | 136/136 |
| PostgreSQL (disposable 16.15) | 5/5 |
| RabbitMQ (disposable 4.2.2) | 17/17 |
| Elasticsearch (disposable 9.5.5/8.19.23) | 13/13 |
| Functional | 147/147 (96 before + 51 new) |
| vulnerable packages (direct + transitive) | none |
| `docker compose config -q` | succeeded |
| OpenAPI capture | v1 and v2 identical to `baseline-phase-6`, apart from line endings (`diff --strip-trailing-cr`) |
| `graphify update . --force` | 3,366 nodes, 7,123 edges, 211 communities |
| `git diff --check` | exit 0 (LF/CRLF notices only) |

## Residual risks

1. The JWT signing key value is committed in all `appsettings*.json`, including Production. It was not rotated because that needs explicit authorization and a token-transition plan. Recommendation: supply `JwtSettings__Key` from a secret store, then remove the committed value in a coordinated change, accepting that every token issued with the old key will be invalidated.
2. Change password requires no current password (existing contract). A stolen valid token can therefore change the password. Fixing this is a public-contract change for a later, explicitly approved phase.
3. Change user name uses `UpdateAsync`, which does not rotate the stamp. Tokens keep the old name claim until the next sign-in. This is unchanged.
4. The Test fixture key is the same value as the other environments (pre-existing). It is used only in memory by tests.
5. `IncludeErrorDetails = false` removes `error="invalid_token"` details from the 401 header. A client that parsed those details will now see only `Bearer`.

## Rollback procedure

To revert the whole phase, revert this change set with `git revert <phase-12 commit>`. For a targeted rollback:

- **Options validation:** remove the two `AddSingleton<IValidateOptions<…>>` and `ValidateOnStart` registrations. Binding stays the same.
- **Issuance:** `JwtTokenFactory` produces the same claims, issuer, audience, algorithm and key. To revert, inline the previous `JwtSecurityToken` creation into `AccountServices`.
- **Validation:** restore `RequireHttpsMetadata = false` and `IncludeErrorDetails` = default (true). Remove `RequireExpirationTime` and `ValidAlgorithms`. Restore the inline `OnTokenValidated`.
- **Current user:** restore the constructor-captured `AuthenticatedUserService`, and `Guid.Parse` in `ApplyAuditing`.
- **Password/stamp:** behavior is unchanged except for the `null`-user guard.
- **Packages:** no package changed.

The authentication scheme, key, issuer, audience, claim names, Identity schema and data are unchanged, so tokens issued before the change stay valid until their normal expiry or the next sign-in.

## Gates not executed

- The Elastic upgrade rehearsal (`06d`) is optional; its paths were not passed and it is outside Phase 12.
- Visual Studio MSBuild VSIX validation is still pending (a known non-gate).
- Full `docker compose up` and `dotnet run` against all real services were not run.

## Confirmations

- No new authentication scheme, external identity provider, OIDC, refresh tokens, MFA or other new feature was added.
- No Phase 13+ work was started (`BuildServiceProvider()` in `LocalizationExtensions` remains as before).
- No secrets were introduced, rotated or printed. The OpenAPI and HTTP contract were preserved, and 401/403 remain distinct.
