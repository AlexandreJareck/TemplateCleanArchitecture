Work in the repository:

C:\Users\alexa\source\repos\TemplateCleanArchNET

Objective: fully execute only Phases 6 and 7 of the refactoring plan. Do not implement anything from Phase 8 onward.

## Starting state (verified on 2026-10-10 at 14:26)

- Phases 0–5 are complete and validated. Read `docs/refactoring/phases-0-5-report.md` before anything else.
- Green gates: WebApi build with 0 warnings; architecture 9/9; unit 38/38; integration 11/11 (EF InMemory); functional 20/20, self-contained; no vulnerable packages; `docker compose config -q` succeeds.
- The current OpenAPI v1/v2 is identical to `docs/refactoring/baseline/`. Use that directory as the Phase 6 contract baseline and do not overwrite it.
- Base commit: `95d04acbfeeaa1e259a3a76f74be3072c561c415`. The working tree is heavily modified (about 160 entries across staged, unstaged, and untracked files). Preserve everything. Do not commit, push, open a PR, stash, reset, checkout, or clean without explicit authorization.
- `git diff --check` exits with code 2 because of trailing whitespace in pre-existing files (VSIX, `.resx`/Designers, `OrderConsumer.cs`, `docker-compose.override.yml`). This is known. Do not "fix" generated files.

## Required sources (read before changing any file)

- `AGENTS.md`, `CLAUDE.md`
- `docs/phase-2-workspace.md` (version and source matrix)
- `docs/phase-3-implementation-plan.md` (Phases 6 and 7 and the decisions table at the end)
- `Src/Core/AGENTS.md`, `Src/Infrastructure/AGENTS.md`, `Src/Presentation/AGENTS.md`, `Tests/AGENTS.md`
- Skills under `.agents/skills/`: `dotnet-change-verification`, `api-contract-change`, `package-upgrade`, `architecture-boundary-check`, and `ef-migration` (the last one only if a migration is actually necessary)
- Review agents under `.agents/agents/` when you want an independent review

## Command execution

- Always use `C:\Users\alexa\.dotnet\dotnet.exe` (SDK 10.0.401 pinned by `global.json`). Run builds and tests sequentially, never in parallel.
- Full gate: `powershell -ExecutionPolicy Bypass -File .\scripts\run-phase-validation.ps1`. It writes logs to `artifacts/validation/*.log` and `summary.txt`, captures OpenAPI under `artifacts/validation/openapi` without touching the baseline, and runs Graphify. It works in Windows PowerShell 5.1.
- To compare the contract, perform a structural JSON diff between `artifacts/validation/openapi/*.json` and `docs/refactoring/baseline/*.json`. Ignore volatile fields (the seed's `createdDateTime` and health's `totalDuration`).
- If your environment cannot execute the Windows dotnet executable (for example, a Linux shell without the SDK), ask the user to run the script and read the logs. Never declare a gate green without a log.

## Relevant technical context

- Application: handlers are under `Features/Products/{Commands,Queries}`, with the request, handler, and validator in the same file. They return `BaseResult`/`PagedResponse` with `Error(ErrorCode, ...)`.
- `ErrorCode` is a public contract (0 ModelStateNotValid, 1 FieldDataInvalid, 2 NotFound, 3 AccessDenied, 4 ErrorInIdentity, 5 Exception). Do not renumber it. `Wrappers/ErrorCategory.cs` already maps `ErrorCode` to Validation, NotFound, Conflict, Unauthorized, Forbidden, and Unexpected (`ToCategory`, `GetErrorCategory`). Use that in the Problem Details component. No `ErrorCode` currently maps to Conflict; if you create one, add a new value (6+) and test it.
- `IClock`/`SystemClock` are registered. MediatR 14's `ValidationBehavior<,>` is registered with `AddOpenBehavior` and throws `FluentValidation.ValidationException`.
- The `CancellationToken` is already propagated: product v1/v2 controllers → MediatR → handlers → `IGenericRepository`/`IProductRepository`/`IUnitOfWork` → EF.
- Current HTTP behavior:
  - `BaseApiController` resolves `Mediator` through a service locator (`HttpContext.RequestServices`), and `MapError` produces an `{errors:[{code,field,message}]}` object that is not Problem Details.
  - `ErrorHandlerMiddleware` serializes `BaseResult` and returns `error.Message` in the 500 response, which leaks internal details.
  - `ApiResultFilterAttribute` is not applied.
  - `AddFluentValidationAutoValidation` (unsupported package `FluentValidation.AspNetCore` 11.3.1) makes `[ApiController]` return `ValidationProblemDetails` with status 400.
  - `Infrastructure/Enums/ErrorCodeEnum.cs` is unused.
  - v1 routes: `get-by-id?id=`, `get-paged-list`, and POST/PUT/DELETE on `/api/v1/product`, with DELETE and PUT receiving the id through query/body.
  - v2 routes: `/{id:guid}`, `/{id:guid}/cache`, `get-paged-list`, POST, PUT `/{id}`, and DELETE `/{id}`. The v2 POST returns 200 even though the documentation says 201.
- Versioning: the legacy package is `Microsoft.AspNetCore.Mvc.Versioning.ApiExplorer` 5.1.0. Configuration is in `Infrastructure/Extensions/SwaggerExtensions.cs`. Controllers use `[ApiVersion]`. The indicated target is `Asp.Versioning.Mvc.ApiExplorer` 10.x; consult the stable version and official migration guide on the day of execution.
- Persistence:
  - `ServiceRegistration` registers repositories through reflection.
  - `GenericRepository` uses `DbContext`; `ProductRepository` builds `IQueryable` and paginates with `OrderBy(Created)`, which is not deterministic when values tie.
  - `UnitOfWork` has no explicit transaction semantics and still exposes a synchronous `SaveChanges()`.
  - Auditing (`EntityFrameworkExtensions.ApplyAuditing`) uses `dynamic` and `DateTime.UtcNow`.
  - Pending migration: `20261010151729_ProductPriceDecimal`. Its review, risks, prior verification, and rollback are in the 0–5 report. It has not yet been applied against real PostgreSQL.
  - SQL Server packages were already removed in Phase 2. Confirm in code, csproj files, and migrations that no reference remains.
- Architecture tests (`Tests/Template.Architecture.Tests/ArchitectureRulesTests.cs`): every new rule needs a controlled mutation. Temporary exceptions for Program.cs and Redis/RabbitMQ/Elastic services are phase-labelled; do not remove those for Phases 8–10.

## Phase 6 — controllers and HTTP contracts

Use v2 as the reference without treating it as an already finished standard.

1. Inject `ISender`/`IMediator` through constructors and remove the service locator from `BaseApiController`.
2. Migrate `Microsoft.AspNetCore.Mvc.Versioning.*` to stable `Asp.Versioning.*` after reading the documentation and official guide. Update `Directory.Packages.props`.
3. Create a single boundary component that converts Application results into RFC Problem Details (`ProblemDetails`/`ValidationProblemDetails`, `application/problem+json`) using `ErrorCategory`.
4. Map: validation → 400, unauthenticated → 401, forbidden → 403, not found → 404, conflict → 409, unexpected → 500 without internal details. The exception middleware must also stop exposing `error.Message`.
5. Make route, query, and body binding explicit.
6. Propagate `CancellationToken` through every controller, including Account.
7. Use consistent REST routes and status codes for CRUD (for example, create → 201 with Location, delete → 204). Make any change only in v2 or a new version and do so explicitly.
8. **Recorded decision 2:** v1 continues working with the same routes and status codes and is marked deprecated in OpenAPI. There is no removal date. Document the v1 lifecycle.
9. Evaluate removing unsupported `FluentValidation.AspNetCore` in favor of the existing pipeline, ensuring the 400 response format is covered by tests.
10. Regenerate and compare OpenAPI v1/v2. Record the new approved baseline under `docs/refactoring/baseline-phase-6/` without deleting the original.

Required functional tests:
- v1 and v2 routes;
- authentication (401) and authorization (403);
- validation (400);
- not found (404);
- conflict, if applicable;
- create, update, and delete;
- Problem Details format;
- 405 and unsupported versions;
- approved differences between v1 and v2.

Completion criteria:
- WebApi builds and every suite passes;
- the OpenAPI diff contains only approved and documented changes;
- controllers contain no service locator (add an architecture test);
- errors do not expose internal details.

If the versioning migration changes behavior unexpectedly, revert the package, configuration, and controller set and **do not** start Phase 7.

## Phase 7 — persistence and PostgreSQL (only after Phase 6 is approved)

1. Register repositories explicitly, without reflection.
2. Evaluate the generic repository: keep it only where it has proven value, preserving Application ports during the transition.
3. No `IQueryable` crosses the Infrastructure boundary (add an architecture test).
4. Apply deterministic ordering before every pagination operation, for example `Created` and then `Id`.
5. Define explicit Unit of Work and transaction semantics, with commit and rollback.
6. Pass `CancellationToken` through every asynchronous operation.
7. Remove any remaining SQL Server material after proving it is unused.
8. Add real PostgreSQL tests in a separate opt-in project or profile, using a pinned 16.x image with no major upgrade. They must cover:
   - migrations, including `ProductPriceDecimal`, with the report's prior verification;
   - CRUD;
   - deterministic pagination and ordering;
   - decimal precision 18,2;
   - concurrency;
   - commit and rollback;
   - cancellation;
   - transactions across repositories.

   EF InMemory is not sufficient evidence. To start PostgreSQL, prefer Testcontainers or an isolated Compose service without exposing credentials in logs.

Do not rewrite applied migrations. If the schema must change, create an additive migration through the CLI, review the generated SQL (`dotnet ef migrations script`), and document rollback.

Completion criteria:
- every suite passes, including PostgreSQL;
- no `IQueryable` crosses the boundary;
- pagination is deterministic;
- transactions and cancellation are covered;
- no SQL Server reference remains;
- Phase 6 HTTP remains stable (OpenAPI identical to the Phase 6 baseline).

## General rules

- Consult package versions and migration guides from official sources on the day of execution and record the date and URL.
- Do not move Redis, RabbitMQ, or Elastic. Do not perform a major PostgreSQL upgrade or upgrade other integrations.
- Do not include secrets or configuration values in logs and documentation; include key names only.
- Do not hand-edit migrations, Designers, snapshots, `.resx` Designers, or `graphify-out`. Regenerate Graphify after structural changes.
- Every behavior change needs a test.

## Delivery

Create `docs/refactoring/phases-6-7-report.md` containing:
- changed files;
- result of each phase;
- versions and sources consulted;
- HTTP contract changes and their tests;
- OpenAPI diff;
- persistence changes;
- migrations and reviewed SQL;
- PostgreSQL tests executed;
- commands and results;
- risks, rollbacks, and pending work.

Update `AGENTS.md` only with commands that were actually verified, and explicitly confirm that nothing from Phase 8 onward was done. Do not declare completion while any criterion remains pending.
