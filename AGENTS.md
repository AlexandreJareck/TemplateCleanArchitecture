# TemplateCleanArchNET agent guide

## Repository purpose

This is an academic .NET Clean Architecture template. The executable system is an ASP.NET Core Web API with PostgreSQL persistence, ASP.NET Core Identity/JWT authentication, Redis caching, RabbitMQ messaging, Elasticsearch search/logging, Serilog, localization, OpenAPI, and health checks. A separate legacy VSIX project packages the template for Visual Studio.

Use `docs/fase-2-workspace.md` for the proposed AI workspace, target architecture, dependency upgrade matrix, and coding standards. Use `docs/fase-3-plano-implementacao.md` for the staged refactoring plan. Decisions adopted on 2026-10-10 are at the end of the Phase 3 plan. Re-check external versions before implementing upgrades; the recorded versions were verified on 2026-10-08.

## Current solution structure

- `Src/Core/Template.Domain` (`net10.0`): base/auditable entities and the product entity, plus sample `Person` and `Order` models. It has no project reference.
- `Src/Core/Template.Application` (`net10.0`): MediatR commands/queries, FluentValidation validators, application interfaces, response wrappers, account DTOs, caching service, Elasticsearch service, RabbitMQ publisher/consumers, and DI registration. It references Domain.
- `Src/Infrastructure/Template.Infrastructure.Persistence` (`net10.0`): EF Core/Npgsql contexts, repositories, unit of work, mappings, migrations, and seed data. It references Application.
- `Src/Infrastructure/Template.Infrastructure.Identity` (`net10.0`): ASP.NET Core Identity, JWT setup, identity DbContext, account services, migrations, and seeds. It references Application.
- `Src/Infrastructure/Template.Infrastructure.Resources` (`net10.0`): localization resources and `ITranslator` implementation. It references Application.
- `Src/Presentation/Template.WebApi` (`net10.0`): composition root, API v1/v2 controllers, middleware, filters, Swagger, CORS, Redis, Elastic logging, localization, and health-check setup. It references Application and all Infrastructure projects.
- `Tests/Template.Unit.Tests`: domain and application handler tests with xUnit, Moq, and Shouldly.
- `Tests/Template.Integration.Tests`: EF repository tests using the in-memory provider.
- `Tests/Template.Functional.Tests`: `WebApplicationFactory` API tests using the Test environment.
- `ExtensionVsix/Template` (`net472`): legacy Visual Studio extension packaging project.
- `graphify-out`: generated code graph (`graph.json`, `graph.html`, `GRAPH_REPORT.md`).

Compile-time dependency direction is mostly `Domain <- Application <- Infrastructure` with WebApi acting as the composition root. Known boundary violations are that Application directly implements Redis, RabbitMQ, and Elasticsearch concerns (tracked by architecture-test exceptions for Phases 8–10). Do not introduce new infrastructure packages into Domain or Application.

## Current implementation conventions

- Controllers inherit `BaseApiController`, use URL-segment API versions, MediatR for product use cases, `BaseResult`/`PagedResponse`, and `CustomResponse` for error mapping. v1 and v2 are inconsistent; treat v2 as the closer baseline, not as a finished standard.
- Product use cases co-locate request, handler, and validator in feature files. Handlers depend on repository/application interfaces and return result wrappers rather than throwing expected business errors.
- Entities expose private setters and behavior methods, inherit `BaseEntity` or `AuditableBaseEntity`, and use EF-compatible private constructors. Preserve invariants inside entities; do not turn them into public-setter persistence bags.
- Persistence implements application-owned ports with EF Core repositories and a unit of work. PostgreSQL/Npgsql is the active relational provider. Async ports accept `CancellationToken`; handlers use `IClock` for observable time and a MediatR `ValidationBehavior` runs FluentValidation validators.
- DI registration is split by project in `ServiceRegistration.cs`; WebApi is the only composition root.
- Tests use xUnit, Shouldly, and Moq. Unit tests isolate handlers, integration tests exercise EF repositories, functional tests exercise HTTP endpoints, and `Tests/Template.Architecture.Tests` enforces dependency rules.
- Nullable settings are inconsistent across projects. New code should enable nullable reference types and avoid adding `#nullable disable`.

## Verified commands (2026-10-10, Phase 7 checkpoint)

Run from the repository root with the pinned stable SDK `C:\Users\alexa\.dotnet\dotnet.exe` (10.0.401 via `global.json`), one command at a time. All of the following were executed on 2026-10-10 at 15:38–15:40 through `scripts/run-phase-validation.ps1`, which runs them in order and writes logs to `artifacts/validation/` (git-ignored). The script disables parallel restore/build workers because this checkout's shared intermediate outputs and compiler server can contend.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-phase-validation.ps1
```

| Command | Verified result |
|---|---|
| `dotnet restore TemplateCleanArchitecture.App.slnf --verbosity minimal --disable-parallel` | Succeeded |
| `dotnet build Src\Presentation\Template.WebApi\Template.WebApi.csproj --no-restore --verbosity minimal --maxcpucount:1 /p:UseSharedCompilation=false` | Succeeded, 0 warnings |
| `dotnet test Tests\Template.Architecture.Tests\Template.Architecture.Tests.csproj --no-restore --verbosity minimal --maxcpucount:1` | 12/12 passed |
| `dotnet test Tests\Template.Unit.Tests\Template.Unit.Tests.csproj --no-restore --verbosity minimal --maxcpucount:1` | 38/38 passed |
| `dotnet test Tests\Template.Integration.Tests\Template.Integration.Tests.csproj --no-restore --verbosity minimal --maxcpucount:1` | 11/11 passed (EF InMemory; not PostgreSQL-equivalent) |
| `dotnet test Tests\Template.PostgreSql.Integration.Tests\Template.PostgreSql.Integration.Tests.csproj --no-restore --verbosity minimal --maxcpucount:1` | 5/5 passed against disposable PostgreSQL 16.15 |
| `dotnet test Tests\Template.Functional.Tests\Template.Functional.Tests.csproj --no-restore --verbosity minimal --maxcpucount:1` | 27/27 passed; self-contained Test host |
| `dotnet list TemplateCleanArchitecture.App.slnf package --vulnerable --include-transitive` | No vulnerable packages |
| `docker compose config -q` | Succeeded |
| `scripts\capture-api-baseline.ps1 -OutputDirectory artifacts\validation\openapi` | Succeeded under Windows PowerShell 5.1; OpenAPI v1/v2 identical to `docs/refactoring/baseline-fase-6` |
| `graphify update . --force` | Succeeded: 1,463 nodes, 2,804 edges, 117 communities |
| `git diff --check` | Exits 2 only for whitespace in pre-existing user/generated changes (see `docs/refactoring/phases-0-5-report.md`) |

Known non-gates:

- `dotnet build TemplateCleanArchitecture.sln` includes the legacy VSIX and fails with `VSIX deployment is not supported with 'dotnet build'`; validate the VSIX separately with Visual Studio MSBuild (pending).
- `& 'C:\Program Files\Git\bin\bash.exe' -n init-db.sh` succeeded on 2026-10-10 for the PostgreSQL init script.

## Execution status

- Phases 0–7 are closed; see `docs/refactoring/phases-0-5-report.md` and `docs/refactoring/phases-6-7-report.md`. Phases 8+ are not started.
- PostgreSQL-backed persistence tests are green. A full multi-service `docker compose up` and local `dotnet run` against all real services are not yet verified green paths.

## Change and validation rules

- Keep Domain free of framework, transport, database, cache, broker, logging, and search dependencies.
- Put use-case orchestration and ports in Application; put EF Core, Identity, Redis, RabbitMQ, Elastic, and localization implementations in Infrastructure; keep WebApi focused on HTTP and composition.
- Preserve API compatibility deliberately. Do not silently remove v1 or change routes/status codes; document and test any deprecation path.
- Add or update tests with each behavior change. Run the smallest relevant green commands above, then broader validation when dependencies are available.
- Run build/test commands sequentially because parallel builds contend for shared `obj` outputs.
- Do not add package versions from memory. Verify stable versions and migration notes using official sources, update related package families together, and pin Docker images to explicit versions or digests.
- Treat migrations and integration upgrades as reversible steps. Never rewrite an applied migration merely to clean it up.
- Never commit secrets. `.env` and appsettings may contain sensitive values; document only configuration key or environment-variable names. Use user secrets or environment variables locally.
- Do not edit generated EF migration designer/snapshot files or generated `.resx` designer files by hand.
- Do not hand-edit `graphify-out`; regenerate it with Graphify after structural code changes.
- Preserve unrelated user changes and do not use destructive Git commands.

## Current configuration names

Relevant sections are `ConnectionStrings`, `IdentitySettings`, `JwtSettings`, `LocalizationSettings`, `ElasticSearchSettings`, `RabbitMqSettings`, `Serilog`, and `UseInMemoryDatabase`. Compose uses environment-variable names including `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB_NAME`, `POSTGRES_IDENTITY_DB_NAME`, `RABBITMQ_DEFAULT_USER`, `RABBITMQ_DEFAULT_PASS`, `REDIS_PASSWORD`, `ASPNETCORE_CERTIFICATE_PASSWORD`, service port variables, and `APPDATA`. Never copy their values into documentation or logs. `.env` is git-ignored and untracked; use `.env.example` (placeholders only) as the template and never re-add `.env` to Git.
