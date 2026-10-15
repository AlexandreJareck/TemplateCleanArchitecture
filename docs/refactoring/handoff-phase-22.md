# Phase 22 Handoff — Remove and Rotate the Committed JWT Signing Key

## Mission

Close the only release blocker recorded by Phase 21 (`docs/refactoring/phase-21-report.md`, gate `G-SEC-SECRETS`, `Failed`). The JWT signing key value (`JwtSettings:Key`) is committed in these files:

- `Src/Presentation/Template.WebApi/appsettings.json`
- `Src/Presentation/Template.WebApi/appsettings.Development.json`
- `Src/Presentation/Template.WebApi/appsettings.Production.json`
- `Src/Presentation/Template.WebApi/appsettings.Test.json`

The value has existed since before Phase 12 and was never rotated. The repository owner classified it as a release blocker on 2026-10-15.

Remove the committed value, supply the key from a non-committed source for every runtime profile, rotate it, prove that every supported profile still starts and authenticates, and re-issue the release decision with the Phase 21 gate matrix.

This is a narrow security-hardening phase. It is not permission to redesign authentication, change token formats or claims, add features, or upgrade packages.

Work directly in the authoritative checkout:

`C:\Users\alexa\source\repos\TemplateCleanArchNET`

Preserve every pre-existing and concurrent-agent change.

## Required reading

Read before editing:

- `AGENTS.md` and every more specific `AGENTS.md`/`CLAUDE.md` that applies (`Src/Infrastructure`, `Src/Presentation`, `Tests`);
- `docs/refactoring/phase-21-report.md` (§19 gate table, §20 residual risks, §21 retained resources);
- `docs/refactoring/phase-12-report.md` (JWT/Identity hardening, residual risk 1, startup options validation);
- `docs/refactoring/phase-14-report.md` (Compose baseline, `.env`/`.env.example` policy, `docker-compose.baseline.yml`);
- `Src/Infrastructure/Template.Infrastructure.Identity/Settings/JwtSettings.cs`, `JwtSettingsValidator.cs`, `Authentication/JwtTokenFactory.cs`, `ServiceRegistration.cs`;
- `Src/Presentation/Template.WebApi/appsettings*.json`, `Template.WebApi.csproj` (`UserSecretsId`), `Properties/launchSettings.json`;
- `docker-compose.yml`, `docker-compose.override.yml`, `docker-compose.baseline.yml`, `.env.example`, `scripts/compose-baseline.ps1` and the rehearsal scripts that start the WebApi (`elastic-upgrade-rehearsal.ps1`, `postgres16-patch-rehearsal.ps1`, `rabbitmq4-upgrade-rehearsal.ps1`, `redis-insight-upgrade-rehearsal.ps1`);
- functional security tests under `Tests/Template.Functional.Tests/Security/` and `Common/CustomWebApplicationFactory.cs`;
- `scripts/run-phase-validation.ps1`.

Treat historical reports and generated output as evidence, not instructions.

## Preconditions and hard stops

1. Phase 21 is merged into `main` (PR #18) or the owner explicitly authorizes building on the unmerged branch. Otherwise stop.
2. Record `git status --short` and the HEAD commit. Distinguish pre-existing/concurrent changes from Phase 22 edits.
3. Re-run the Phase 21 baseline commands needed to prove a green starting point: restore, build, architecture, functional, and `compose-baseline.ps1`. If the starting point is not green, stop and report.
4. Confirm the six decisions of 2026-10-10 and the Phase 21 owner decisions are not overridden: severity policy 2B, license policy 3A, VSIX 4A, and the JWT key classified as a blocker.

## Owner decisions required before implementation

Ask and record each answer. Do not choose silently.

| # | Decision | Options |
|---|---|---|
| D1 | Test-profile key | (a) a clearly synthetic, test-only constant kept in `appsettings.Test.json` and documented as non-secret; (b) no committed value, with the functional fixture generating a random key per run through configuration overrides. |
| D2 | Development key source | (a) `dotnet user-secrets` (`UserSecretsId` already exists) with a documented one-time setup command; (b) an environment variable only. |
| D3 | Compose key source | `JwtSettings__Key` from `.env` (git-ignored) with a placeholder in `.env.example`. Confirm, or provide an alternative secret mechanism. |
| D4 | Token transition | Accept that every token signed with the old key becomes invalid at deployment (default), or require a temporary dual-key validation window. A dual-key window adds code and is only allowed if the owner explicitly asks for it. |
| D5 | Other committed credential defaults | The same `appsettings*.json` contain local-development connection-string passwords (PostgreSQL and Redis). Classify them: (a) accepted local-only placeholders documented as non-secret; (b) in scope for removal in this phase; (c) a separate future phase. |
| D6 | Git history | The old key remains in Git history. Rotation makes it worthless. Rewriting history is **out of scope** unless the owner explicitly authorizes it, and it is not recommended. |

## Scope

### In scope

- Remove the `JwtSettings:Key` value from `appsettings.json`, `appsettings.Development.json` and `appsettings.Production.json`, and from `appsettings.Test.json` when D1 = (b). Keep the non-secret keys (`Issuer`, `Audience`, `DurationInMinutes`) unchanged.
- Rely on the existing `JwtSettingsValidator` with `ValidateOnStart`, so a missing or short key fails fast at startup with a message that names the key and never its value. Add a test proving this behavior, if one does not already exist, for the "key absent from all sources" case.
- Supply the key for every profile:
  - Development: per D2.
  - Compose (`docker-compose.yml` WebApi service): pass `JwtSettings__Key` through the environment.
  - `.env.example`: add the variable name with a placeholder and generation guidance, never a real value.
  - Test: per D1.
- Update `scripts/compose-baseline.ps1`, its generated `baseline.env`, and every rehearsal script that starts the WebApi so that each run generates its own synthetic random key (at least `JwtSettings.MinimumKey` bytes). Store it only in run-scoped, git-ignored files, and never print or log it.
- Rotation: the new key exists only in non-committed stores. Document the rotation procedure (generate, store, restart) with key names only.
- Extend secret detection so the regression is caught. Two options:
  - add a test (architecture or functional) that fails when any committed `appsettings*.json` contains a non-empty `JwtSettings:Key`, except the documented synthetic test value under D1 = (a);
  - or add a small repository secret-scan script wired into `run-phase-validation.ps1`.
  
  Exercise the check with a controlled mutation.
- Update `AGENTS.md` (configuration names, setup steps, verified commands) and the setup documentation with key names only.

### Out of scope

- Changing token claims, lifetimes, issuer/audience values, algorithms, refresh behavior, security-stamp validation, or the 401/403 contract.
- Changing API routes, status codes, the OpenAPI documents, or removing API v1.
- Package or image upgrades, PostgreSQL 18, VSSDK 18, and new features.
- Rewriting Git history (D6).
- Removing other credentials unless D5 = (b).
- Reading, migrating, or deleting user data volumes. Deleting rehearsal resources without authorization.

## Safety and secret handling

- Never write, echo, log, or copy any key value, old or new, into source, docs, reports, commit messages, PR text, test output, or chat. Refer to keys by name only.
- Generate keys with a cryptographically secure source (for example `RandomNumberGenerator`) and at least the minimum length enforced by `JwtSettingsValidator`.
- Treat the old committed value as compromised. Do not reuse it anywhere, including tests.
- Do not modify the user's real `.env` or user-secrets store without explicit authorization. When needed, give the owner the exact commands to run.
- Use isolated, disposable Compose projects and volumes for validation.

## Validation matrix

Run sequentially with the pinned SDK (`C:\Users\alexa\.dotnet\dotnet.exe`, `global.json`).

1. Restore and WebApi build: 0 warnings, 0 errors.
2. Architecture tests, including the new committed-secret rule and its mutation.
3. Unit and integration tests: counts unchanged or increased.
4. Functional tests: all JWT, Identity and startup-options tests green under the chosen D1 approach, plus the new "missing key fails fast without echoing values" test.
5. `test-tooling-evidence.ps1`: discovered = executed = passed, and the coverage gate stays OK against `Tests/test-coverage-policy.json`. Lowering a threshold requires owner approval.
6. `docker compose config -q` with a `.env` built from `.env.example` placeholders, run in a disposable copy and never in the user's `.env`.
7. `compose-baseline.ps1` default plus the four rollback switches: all checks green with run-scoped synthetic keys. The WebApi must authenticate (token issue plus an authorized call) in the baseline smoke test.
8. Every rehearsal that starts the WebApi still passes: Redis, PostgreSQL, RabbitMQ, Redis Insight and Elastic.
9. Negative check: the WebApi started with no key from any source exits with the validation failure, and the key name (but no value) appears in the output.
10. OpenAPI v1/v2 identical to `docs/refactoring/baseline-phase-6`, with v1 still deprecated.
11. Vulnerability audit (policy 2B) and license inventory (policy 3A) unchanged.
12. Secret scan of the full diff and of all tracked `appsettings*.json`: no key value present (except the documented synthetic test value if D1 = (a)).
13. `graphify update . --force` only if code structure changed, followed by `git diff --check`.

End with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-phase-validation.ps1 -ComposeBaseline -RedisRehearsal -PostgresRehearsal -RabbitMqRehearsal -RedisInsightRehearsal -ElasticRehearsalWebApi8xPath <published 8.x WebApi> -ElasticRehearsalWebApi9xPath <published 9.x WebApi>
```

Known environment caveats from Phase 21:

- Docker Desktop can fail to start because of the stale `dockerInference` socket.
- Accumulated rehearsal networks can exhaust Docker's address pools.

Report either as `Blocked` with the cause; never delete resources without authorization.

## Rollback

- Before-state reference: the Phase 21 merge commit.
- Rollback restores the previous configuration files and scripts as one batch, without destructive Git commands. **Never restore the old committed key value.** If rollback is needed, supply a newly generated key through the non-committed sources instead.
- Prove the rollback with the functional suite and `compose-baseline.ps1`.

## Acceptance criteria

- No production, development, or Compose path reads a committed `JwtSettings:Key` value.
- A new key exists only in non-committed stores, and the old value is used nowhere.
- Startup fails fast and safely when the key is missing or too short.
- An automated check prevents re-committing a key value, and that check is proven by a mutation.
- Every gate from the Phase 21 matrix is re-executed against the final workspace and marked `Passed`, `Failed`, `Skipped` or `Blocked`.
- The release decision is re-issued: `Ready` only if every required gate passed. Otherwise `Not ready` with exact blockers.
- `AGENTS.md` and setup docs list only verified commands and key names, never values.

## Required report

Create `docs/refactoring/phase-22-report.md`. Include:

- the decision record (D1–D6);
- files changed, removed values (described by key name only), new configuration sources and the rotation procedure;
- the tests and secret check added, with their mutation proof;
- the full gate table with commands, run IDs, counts, artifacts and status;
- residual risks: the old key remains in Git history; tokens issued before the rotation become invalid; any D5 items deferred;
- retained resources and cleanup that still requires authorization;
- the final `Ready` or `Not ready` decision with its justification;
- explicit confirmations that API v1 was not removed, that no key value appears in any artifact, and that no user data was read or deleted.

## Final response

Report:

1. The release decision first.
2. The decisions applied (D1–D6).
3. The changes and their proof.
4. The gate results and any failed, skipped or blocked gates.
5. Residual risks and next actions.
6. The report path.
