# TemplateCleanArchNET agent guide

## Repository purpose

This is an academic .NET Clean Architecture template. The executable system is an ASP.NET Core Web API with PostgreSQL persistence, ASP.NET Core Identity/JWT authentication, Redis caching, RabbitMQ messaging, Elasticsearch search/logging, Serilog, localization, OpenAPI, and health checks. A separate legacy VSIX project packages the template for Visual Studio.

Use `docs/fase-2-workspace.md` for the proposed AI workspace, target architecture, dependency upgrade matrix, and coding standards. Use `docs/fase-3-plano-implementacao.md` for the staged refactoring plan. Decisions adopted on 2026-10-10 are at the end of the Phase 3 plan. Re-check external versions before implementing upgrades; the recorded versions were verified on 2026-10-08.

## Current solution structure

- `Src/Core/Template.Domain` (`netstandard2.1`): base/auditable entities, product entity and product DTO, plus sample `Person` and `Order` models. It has no project reference.
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

Compile-time dependency direction is mostly `Domain <- Application <- Infrastructure` with WebApi acting as the composition root. Known boundary violations are that Application directly implements Redis, RabbitMQ, and Elasticsearch concerns, and Domain contains `ProductDto`. Do not introduce new infrastructure packages into Domain or Application.

## Current implementation conventions

- Controllers inherit `BaseApiController`, use URL-segment API versions, MediatR for product use cases, `BaseResult`/`PagedResponse`, and `CustomResponse` for error mapping. v1 and v2 are inconsistent; treat v2 as the closer baseline, not as a finished standard.
- Product use cases co-locate request, handler, and validator in feature files. Handlers depend on repository/application interfaces and return result wrappers rather than throwing expected business errors.
- Entities expose private setters and behavior methods, inherit `BaseEntity` or `AuditableBaseEntity`, and use EF-compatible private constructors. Preserve invariants inside entities; do not turn them into public-setter persistence bags.
- Persistence implements application-owned ports with EF Core repositories and a unit of work. PostgreSQL/Npgsql is the active relational provider; SQL Server packages are referenced but no SQL Server configuration path is used.
- DI registration is split by project in `ServiceRegistration.cs`; WebApi is the only composition root.
- Tests use xUnit, Shouldly, and Moq. Unit tests isolate handlers, integration tests exercise EF repositories, and functional tests exercise HTTP endpoints.
- Nullable settings are inconsistent across projects. New code should enable nullable reference types and avoid adding `#nullable disable`.

## Verified commands (2026-10-08 and 2026-10-10)

Run commands from the repository root. These commands were actually executed in this workspace.

```powershell
graphify update . --force
```

Succeeded and produced 1,034 nodes, 1,946 edges, and 74 communities in `graphify-out`.

```powershell
dotnet restore TemplateCleanArchitecture.sln --verbosity minimal
```

Succeeded. Restore currently reports high-severity transitive vulnerability warnings for `Microsoft.OpenApi` 2.3.0 and `System.Security.Cryptography.Xml` 8.0.2.

```powershell
dotnet build Src\Presentation\Template.WebApi\Template.WebApi.csproj --no-restore --verbosity minimal
```

Succeeded with the same NuGet audit warnings.

```powershell
dotnet test Tests\Template.Unit.Tests\Template.Unit.Tests.csproj --no-restore --verbosity minimal
```

Succeeded: 10 passed.

```powershell
dotnet test Tests\Template.Integration.Tests\Template.Integration.Tests.csproj --no-restore --verbosity minimal
```

Succeeded: 6 passed.

```powershell
docker compose config -q
```

Succeeded with Docker 28.4.0 and Docker Compose 2.39.4.

```powershell
& 'C:\Program Files\Git\bin\bash.exe' -n init-db.sh
```

Succeeded on 2026-10-10 and validates the PostgreSQL initialization script's Bash syntax.

The following tested commands have known failures and are diagnostic, not green validation gates:

```powershell
dotnet test Tests\Template.Functional.Tests\Template.Functional.Tests.csproj --no-restore --verbosity minimal
```

Compiles, then fails 17/17 because `Elastic.Serilog.Sinks` contacts `http://localhost:9200` while the test host is being built and Elasticsearch is not running.

```powershell
dotnet build TemplateCleanArchitecture.sln --no-restore --verbosity minimal
```

Builds the application/test assemblies and creates the VSIX, then exits with `VSIX deployment is not supported with 'dotnet build'`. Validate the VSIX separately with a compatible Visual Studio MSBuild environment; that command is pending verification.

## Execution status

- Restore, WebApi compilation, unit tests, integration tests, and Compose configuration have been verified with the commands above.
- `init-db.sh` creates `POSTGRES_DB_NAME` and `POSTGRES_IDENTITY_DB_NAME` idempotently during first-time PostgreSQL volume initialization. On 2026-10-10 it was verified against an isolated clean PostgreSQL 16 volume: both databases were created, accepted connections, and remained valid after a second script execution.
- A complete multi-service `docker compose up` and HTTP smoke test are still pending. The PostgreSQL-only initialization path is a verified green path.
- Functional tests remain a known failing diagnostic until the Test host stops requiring Elasticsearch during startup.
- Local `dotnet run` behavior is not a verified green path because its external PostgreSQL, Elastic, RabbitMQ, and Redis dependencies were not all available during this run.

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
