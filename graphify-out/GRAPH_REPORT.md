# Graph Report - TemplateCleanArchNET  (2026-10-10)

## Corpus Check
- 206 files · ~46,372 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 14 file(s) not represented in the graph (top: (none) 4, .resx 4, .jsonl 2)

## Summary
- 1643 nodes · 3244 edges · 107 communities (66 shown, 41 thin omitted)
- Extraction: 91% EXTRACTED · 9% INFERRED · 0% AMBIGUOUS · INFERRED: 303 edges (avg confidence: 0.87)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `95d04acb`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- Template.Application.Interfaces
- ResourceGeneral
- Person
- Container (Dockerfile)
- .ToActionResult
- .Handle
- ErrorCategoryTests
- Order
- IProductCache
- system
- Template.Infrastructure.Identity/ServiceRegistration.cs
- UpdateProductCommand
- ProductEntity
- ValidationBehavior
- ApplicationDbContext
- Template.WebApi.csproj
- PagedResponse
- DistributedProductCacheTests
- ProductRepository
- Template.Application/ServiceRegistration.cs
- ElasticSearchSettings
- ProductController
- IAuthenticatedUserService
- ArchitectureRulesTests
- ProductDto
- Template.Application.Wrappers
- GetProductByIdQuery
- Phase 9 Execution Handoff
- Phase 3 — Incremental implementation plan
- Template.Application.csproj
- BaseResult
- CreateProductCommand
- ProductServiceTests
- Template.Application.DTOs.Products
- .BuildModel
- .AddCustomLocalization
- Program.cs
- ErrorCategory
- ConfigureSwaggerOptions
- .AddApplicationLayer
- OrderConsumer.cs
- ApplicationUser
- TemplateCleanArchitecture.sln
- 5. Target code standards
- microsoft_entityframeworkcore
- Template.Functional.Tests.csproj
- TemplateCleanArchNET agent guide
- Template.Domain.Products.Entities
- IdentitySettings
- HttpClientGetExtensionMethods
- Template.Infrastructure.Identity
- Relatório das Fases 6–7 — 2026-10-10
- UnitOfWork
- Contexto técnico relevante
- Current configuration names
- Template.Infrastructure.Persistence.csproj
- Template.Unit.Tests.csproj
- Required tests
- Product
- ApiResultFilterAttribute
- Template.Integration.Tests.csproj
- InitialPostgresMigration
- shouldly
- ProductPriceDecimal
- Phase6HttpContractFunctionalTests
- Template.Architecture.Tests.csproj
- Template.Application.DTOs
- Fases 6–7 — bloqueio de pré-condições (2026-10-10)
- CorsExtensions
- Phase 0 baseline — 2026-10-10
- RabbitMqSettings
- contract-checklist.md
- boundaries.md
- verification-matrix.md
- postgresql-review.md
- adapter-checklist.md
- upgrade-record.md
- AuditableBaseEntity
- ServiceRegistrationTests
- init-db.sh
- skill-activation-report.md
- handoff-fases-6-7.md
- Core/AGENTS.md
- Infrastructure/AGENTS.md
- Presentation/AGENTS.md
- Tests/AGENTS.md
- GenericRepository
- ErrorCode
- IClock
- JwtSettings
- Relatório das Fases 0–5 — 2026-10-10
- Template.PostgreSql.Integration.Tests.csproj
- ElasticExtensions
- api-v1-lifecycle.md

## God Nodes (most connected - your core abstractions)
1. `BaseResult` - 76 edges
2. `ProductDto` - 49 edges
3. `ProductEntity` - 40 edges
4. `Template.Application.Wrappers` - 36 edges
5. `Template.Application.Interfaces` - 30 edges
6. `ArchitectureRulesTests` - 26 edges
7. `Phase 3 — Incremental implementation plan` - 25 edges
8. `AuthenticationResponse` - 24 edges
9. `UpdateProductCommand` - 21 edges
10. `ApplicationDbContext` - 21 edges

## Surprising Connections (you probably didn't know these)
- `Phase 4 — Clean the Domain model` --references--> `ProductDto`  [INFERRED]
  docs/fase-3-plano-implementacao.md → Src/Core/Template.Application/DTOs/Products/ProductDto.cs
- `Riscos, pendências e rollback` --references--> `IUnitOfWork`  [INFERRED]
  docs/refactoring/phases-6-7-report.md → Src/Core/Template.Application/Interfaces/IUnitOfWork.cs
- `Errors and API results` --references--> `BaseResult`  [INFERRED]
  docs/fase-2-workspace.md → Src/Core/Template.Application/Wrappers/BaseResult.cs
- `Phase 5 — Standardize application use cases and results` --references--> `BaseResult`  [INFERRED]
  docs/fase-3-plano-implementacao.md → Src/Core/Template.Application/Wrappers/BaseResult.cs
- `Fase 7 — persistência e PostgreSQL (somente depois da Fase 6 aprovada)` --references--> `ProductPriceDecimal`  [INFERRED]
  docs/refactoring/handoff-fases-6-7.md → Src/Infrastructure/Template.Infrastructure.Persistence/Migrations/20261010151729_ProductPriceDecimal.Designer.cs

## Import Cycles
- None detected.

## Communities (107 total, 41 thin omitted)

### Community 0 - "Template.Application.Interfaces"
Cohesion: 0.22
Nodes (7): Template.Unit.Tests.ApplicationTests.Features.Products.Queries, Template.Application.Interfaces, Template.Unit.Tests.ApplicationTests.Features.Products.Commands, Template.Application.Helpers, Template.Application.Features.Products.Commands, Template.Application.Interfaces.Repositories, Regexs

### Community 1 - "ResourceGeneral"
Cohesion: 0.09
Nodes (17): Template.Infrastructure.Resources.ProjectResources, ResourceGeneral, BarCode, ConfirmPassword, Culture, Id, Name, Password (+9 more)

### Community 2 - "Person"
Cohesion: 0.07
Nodes (16): Template.WebApi.Infrastructure.Enums, Template.Domain.Person, ElasticService, IElasticService, Person, FirstName, Id, LastName (+8 more)

### Community 3 - "Container (Dockerfile)"
Cohesion: 0.05
Nodes (43): commandName, environmentVariables, launchBrowser, launchUrl, publishAllPorts, useSSL, ASPNETCORE_ENVIRONMENT, ASPNETCORE_HTTP_PORTS (+35 more)

### Community 4 - ".ToActionResult"
Cohesion: 0.08
Nodes (3): ProductController, ApiResultMapper, ProblemDetailsAuthorizationMiddlewareResultHandler

### Community 5 - ".Handle"
Cohesion: 0.28
Nodes (4): DeleteProductCommand, Id, DeleteProductCommandHandler, DeleteProductCommandHandlerTests

### Community 7 - "Order"
Cohesion: 0.18
Nodes (5): Order, Id, Price, ProductName, Quantity

### Community 8 - "IProductCache"
Cohesion: 0.05
Nodes (16): Phase 8 — Move Redis caching behind an infrastructure adapter, IProductCache, NoOpProductCache, ServiceRegistration, Program, ExternalServicesUnavailableTests, CacheDisabledFactory, AdditionalConfiguration (+8 more)

### Community 9 - "system"
Cohesion: 0.22
Nodes (5): Template.Infrastructure.Identity.Migrations, Template.Infrastructure.Identity.Contexts, Template.Domain.Common, Template.Infrastructure.Persistence.Migrations, InitialPostgresMigration

### Community 10 - "Template.Infrastructure.Identity/ServiceRegistration.cs"
Cohesion: 0.14
Nodes (4): Template.Infrastructure.Identity, Template.Integration.Tests.Caching, Template.Infrastructure.Caching, Template.Infrastructure.Caching.Products

### Community 11 - "UpdateProductCommand"
Cohesion: 0.22
Nodes (7): UpdateProductCommand, BarCode, Id, Name, Price, UpdateProductCommandHandler, UpdateProductCommandHandlerTests

### Community 12 - "ProductEntity"
Cohesion: 0.08
Nodes (13): Template.Unit.Tests.DomainTests.Products.Entities, ProductEntity, BarCode, Name, Price, ProductConfiguration, BaseEfRepoTestFixture, EfCancellationPropagationTests (+5 more)

### Community 13 - "ValidationBehavior"
Cohesion: 0.15
Nodes (10): ValidationBehavior, AuthenticationRequestValidator, CreateProductCommandValidator, UpdateProductCommandValidator, NameRequiredValidator, SampleRequest, Name, TokenCapturingValidator (+2 more)

### Community 14 - "ApplicationDbContext"
Cohesion: 0.12
Nodes (4): ApplicationDbContext, Products, ApplicationDbContextFactory, DefaultData

### Community 15 - "Template.WebApi.csproj"
Cohesion: 0.12
Nodes (16): Asp.Versioning.Mvc.ApiExplorer, AspNetCore.HealthChecks.Elasticsearch, AspNetCore.HealthChecks.UI.Client, Elastic.Serilog.Sinks, Microsoft.AspNetCore.OpenApi, Microsoft.EntityFrameworkCore.Tools, Microsoft.VisualStudio.Azure.Containers.Tools.Targets, Serilog.AspNetCore (+8 more)

### Community 16 - "PagedResponse"
Cohesion: 0.05
Nodes (27): Template.Application.Parameters, GetAllUsersRequest, Name, UserDto, Created, Email, Id, Name (+19 more)

### Community 17 - "DistributedProductCacheTests"
Cohesion: 0.06
Nodes (9): CacheSettings, AbsoluteExpiration, Enabled, OperationTimeout, SlidingExpiration, DistributedProductCache, DistributedProductCacheTests, FailingDistributedCache (+1 more)

### Community 18 - "ProductRepository"
Cohesion: 0.15
Nodes (7): ProductRepository, PostgreSqlCollection, PostgreSqlFixture, TestAuthenticatedUser, UserId, UserName, PostgreSqlPersistenceTests

### Community 19 - "Template.Application/ServiceRegistration.cs"
Cohesion: 0.15
Nodes (7): Template.Application.Consumers, Template.Application, Template.Unit.Tests.ApplicationTests.Services, Template.Unit.Tests.ApplicationTests.Behaviors, Template.Application.Behaviors, Template.Application.Services.Time, Template.Unit.Tests.ApplicationTests

### Community 20 - "ElasticSearchSettings"
Cohesion: 0.29
Nodes (7): ElasticSearchSettings, DataSet, DefaultIndex, Name, Namespace, Type, Url

### Community 22 - "IAuthenticatedUserService"
Cohesion: 0.15
Nodes (11): IAuthenticatedUserService, UserId, UserName, DesignTimeAuthenticatedUser, Instance, UserId, UserName, AuthenticatedUserService (+3 more)

### Community 24 - "ProductDto"
Cohesion: 0.12
Nodes (6): ProductDto, BarCode, CreatedDateTime, Id, Name, Price

### Community 25 - "Template.Application.Wrappers"
Cohesion: 0.17
Nodes (9): Template.Application.Wrappers, Template.WebApi.Infrastructure.Filters, Template.WebApi.Controllers.v2, Template.Infrastructure.Identity.Services, Template.Application.Services.Elastic, Template.Application.DTOs.Account.Request, Template.Application.DTOs.Account.Response, Template.WebApi.Controllers.v1 (+1 more)

### Community 26 - "GetProductByIdQuery"
Cohesion: 0.24
Nodes (4): GetProductByIdQuery, Id, GetProductByIdQueryHandler, GetProductByIdQueryHandlerTests

### Community 27 - "Phase 9 Execution Handoff"
Cohesion: 0.12
Nodes (15): Acceptance criteria, Asynchronous publishing, Availability and startup, Consumption, acknowledgements, and delivery, Documentation and delivery, Event contracts, Feature flag and rollback, Phase 9 Execution Handoff (+7 more)

### Community 28 - "Phase 3 — Incremental implementation plan"
Cohesion: 0.05
Nodes (33): Decisions (recorded 2026-10-10), Global execution rules, Phase 0 — Freeze a reproducible baseline, Phase 10 — Move Elastic concerns behind infrastructure boundaries, Phase 11 — Upgrade the Elastic client and server stack, Phase 12 — Harden identity and JWT boundaries, Phase 13 — Standardize observability, health, and localization, Phase 14 — Establish a reproducible Compose baseline (+25 more)

### Community 29 - "Template.Application.csproj"
Cohesion: 0.14
Nodes (13): Elastic.Clients.Elasticsearch, FluentValidation.DependencyInjectionExtensions, MediatR, RabbitMQ.Client, net10.0, Microsoft.Extensions.Hosting.Abstractions, Microsoft.NET.Sdk, Template.Domain (+5 more)

### Community 30 - "BaseResult"
Cohesion: 0.05
Nodes (28): AuthenticationRequest, Password, UserName, ChangePasswordRequest, ConfirmPassword, Password, ChangePasswordRequestValidator, ChangeUserNameRequest (+20 more)

### Community 31 - "CreateProductCommand"
Cohesion: 0.23
Nodes (6): CreateProductCommand, BarCode, Name, Price, CreateProductCommandHandler, CreateProductCommandHandlerTests

### Community 33 - "Template.Application.DTOs.Products"
Cohesion: 0.30
Nodes (4): Template.Application.Features.Products.Queries, Template.Application.DTOs.Products, Template.Application.Interfaces.Caching, Template.Application.Services.Product

### Community 36 - "Program.cs"
Cohesion: 0.08
Nodes (6): Template.Infrastructure.Persistence, Template.WebApi.Infrastructure.Middlewares, Template.WebApi.Infrastructure.Http, Template.Infrastructure.Persistence.Seeds, Template.WebApi.Infrastructure.Extensions, Template.WebApi.Infrastructure.Services

### Community 37 - "ErrorCategory"
Cohesion: 0.18
Nodes (7): ErrorCategory, Conflict, Forbidden, NotFound, Unauthorized, Unexpected, Validation

### Community 39 - ".AddApplicationLayer"
Cohesion: 0.29
Nodes (7): Current state to characterize, HTTP compatibility, Ports and boundaries, ServiceRegistration, IRabbitMqService, RabbitMqService, MessageController

### Community 40 - "OrderConsumer.cs"
Cohesion: 0.15
Nodes (3): Template.Domain.Order, Template.Application.Services.RabbitMq, Template.Application.Settings

### Community 41 - "ApplicationUser"
Cohesion: 0.06
Nodes (12): Template.Infrastructure.Identity.Seeds, Template.Infrastructure.Identity.Models, IGetUserServices, IdentityContext, ApplicationRole, ApplicationUser, Created, Name (+4 more)

### Community 42 - "TemplateCleanArchitecture.sln"
Cohesion: 0.20
Nodes (3): Template, Microsoft.VisualStudio.SDK, Microsoft.VSSDK.BuildTools

### Community 43 - "5. Target code standards"
Cohesion: 0.08
Nodes (23): 1. Evidence and assumptions, 2. Proposed AI workspace, 3. Dependency and integration update matrix, 4. Target Clean Architecture, 5. Target code standards, Application use cases, Controllers, Design principles (+15 more)

### Community 44 - "microsoft_entityframeworkcore"
Cohesion: 0.18
Nodes (4): Template.Infrastructure.Persistence.Repositories, Template.PostgreSql.Integration.Tests, Template.Infrastructure.Persistence.Contexts, Template.Infrastructure.Persistence.Extensions

### Community 45 - "Template.Functional.Tests.csproj"
Cohesion: 0.20
Nodes (9): Microsoft.AspNetCore.Mvc.Testing, net10.0, coverlet.collector, Microsoft.Extensions.Hosting.Abstractions, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio (+1 more)

### Community 46 - "TemplateCleanArchNET agent guide"
Cohesion: 0.29
Nodes (6): Change and validation rules, Current solution structure, Execution status, Repository purpose, TemplateCleanArchNET agent guide, Verified commands (2026-10-10, Phase 7 checkpoint)

### Community 47 - "Template.Domain.Products.Entities"
Cohesion: 0.25
Nodes (3): Template.Domain.Products.Entities, Template.Infrastructure.Persistence.Contexts.Configurations, Template.Integration.Tests.Data

### Community 48 - "IdentitySettings"
Cohesion: 0.33
Nodes (6): IdentitySettings, PasswordRequireDigit, PasswordRequiredLength, PasswordRequireLowercase, PasswordRequireNonAlphanumeric, PasswordRequireUppercase

### Community 50 - "Template.Infrastructure.Identity"
Cohesion: 0.25
Nodes (8): Microsoft.AspNetCore.Authentication.JwtBearer, Microsoft.AspNetCore.Identity.EntityFrameworkCore, System.IdentityModel.Tokens.Jwt, Template.Infrastructure.Identity, net10.0, Microsoft.EntityFrameworkCore.InMemory, Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.NET.Sdk

### Community 51 - "Relatório das Fases 6–7 — 2026-10-10"
Cohesion: 0.11
Nodes (17): Arquivos alterados nesta execução, Automação e documentação, Comandos e resultados, Confirmação de escopo, Contrato e WebApi, Diff de contrato e baseline, Migrations e SQL, Persistência e testes (+9 more)

### Community 52 - "UnitOfWork"
Cohesion: 0.16
Nodes (3): IUnitOfWork, UnitOfWork, ServiceRegistration

### Community 53 - "Contexto técnico relevante"
Cohesion: 0.26
Nodes (6): Contexto técnico relevante, Fase 5 em detalhe, IProductRepository, ErrorCategoryExtensions, EntityFrameworkExtensions, ErrorHandlerMiddleware

### Community 54 - "Current configuration names"
Cohesion: 0.40
Nodes (4): Current configuration names, LocalizationSettings, DefaultRequestCulture, SupportedCultures

### Community 55 - "Template.Infrastructure.Persistence.csproj"
Cohesion: 0.22
Nodes (8): Microsoft.EntityFrameworkCore, Microsoft.Extensions.Configuration, Microsoft.Extensions.DependencyInjection, net10.0, Microsoft.EntityFrameworkCore.Design, Microsoft.EntityFrameworkCore.InMemory, Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.NET.Sdk

### Community 56 - "Template.Unit.Tests.csproj"
Cohesion: 0.22
Nodes (8): Moq, net10.0, coverlet.collector, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk

### Community 57 - "Required tests"
Cohesion: 0.40
Nodes (5): Architecture tests, Functional tests, Required tests, Tests with a real broker, Unit tests

### Community 58 - "Product"
Cohesion: 0.24
Nodes (5): Account, ApiRoutes, Product, V1, V2

### Community 63 - "Template.Integration.Tests.csproj"
Cohesion: 0.14
Nodes (12): Microsoft.Extensions.Caching.Memory, Microsoft.Extensions.Caching.StackExchangeRedis, Microsoft.Extensions.Options.ConfigurationExtensions, net10.0, Microsoft.NET.Sdk, net10.0, coverlet.collector, Microsoft.NET.Test.Sdk (+4 more)

### Community 66 - "shouldly"
Cohesion: 0.22
Nodes (4): Template.Functional.Tests.ApiEndpoints, Template.Unit.Tests.ApplicationTests.Wrappers, Template.Functional.Tests.ApiEndpoints.v2, Template.Functional.Tests.Common

### Community 69 - "Template.Architecture.Tests.csproj"
Cohesion: 0.29
Nodes (6): net10.0, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk

### Community 70 - "Template.Application.DTOs"
Cohesion: 0.17
Nodes (3): Template.Infrastructure.Resources, Template.Application.DTOs, Template.Infrastructure.Resources.Services

### Community 71 - "Fases 6–7 — bloqueio de pré-condições (2026-10-10)"
Cohesion: 0.33
Nodes (5): Causa do bloqueio, Como desbloquear, Estado do Git registrado, Fases 6–7 — bloqueio de pré-condições (2026-10-10), Pré-condições

### Community 73 - "Phase 0 baseline — 2026-10-10"
Cohesion: 0.25
Nodes (5): Baseline validation before refactoring, Dependency and vulnerability baseline, Phase 0 baseline — 2026-10-10, Source state, Toolchain

### Community 74 - "RabbitMqSettings"
Cohesion: 0.19
Nodes (10): Configuration and security, OrderConsumerDirect, OrderConsumerFanoutA, OrderConsumerFanoutB, OrderConsumerTopic, RabbitMqSettings, HostName, Password (+2 more)

### Community 81 - "AuditableBaseEntity"
Cohesion: 0.20
Nodes (8): Current implementation conventions, AuditableBaseEntity, Created, CreatedBy, LastModified, LastModifiedBy, BaseEntity, Id

### Community 85 - "handoff-fases-6-7.md"
Cohesion: 0.14
Nodes (11): Template.WebApi.Controllers, Entrega, Estado de partida (verificado em 2026-10-10 às 14:26), Execução de comandos, Fase 6 — controllers e contratos HTTP, Fase 7 — persistência e PostgreSQL (somente depois da Fase 6 aprovada), Fontes obrigatórias (ler antes de alterar qualquer arquivo), Regras gerais (+3 more)

### Community 100 - "GenericRepository"
Cohesion: 0.14
Nodes (3): IGenericRepository, GenericRepository, DbContext

### Community 102 - "ErrorCode"
Cohesion: 0.14
Nodes (11): Error, Description, ErrorCode, FieldName, ErrorCode, AccessDenied, ErrorInIdentity, Exception (+3 more)

### Community 104 - "IClock"
Cohesion: 0.20
Nodes (5): IClock, UtcNow, SystemClock, UtcNow, SystemClockTests

### Community 105 - "JwtSettings"
Cohesion: 0.25
Nodes (6): Template.Infrastructure.Identity.Settings, JwtSettings, Audience, DurationInMinutes, Issuer, Key

### Community 106 - "Relatório das Fases 0–5 — 2026-10-10"
Cohesion: 0.22
Nodes (8): Comparação de contrato, Confirmação, Mudanças por fase, Relatório das Fases 0–5 — 2026-10-10, Revisão da migration e do SQL (Fase 4), Riscos e pendências, Testes e gates (execução de 2026-10-10, 14:25–14:26), Versões e fontes oficiais

### Community 108 - "Template.PostgreSql.Integration.Tests.csproj"
Cohesion: 0.25
Nodes (7): Testcontainers.PostgreSql, net10.0, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk

## Knowledge Gaps
- **373 isolated node(s):** `Microsoft.VisualStudio.SDK`, `Microsoft.VSSDK.BuildTools`, `UserName`, `Password`, `Password` (+368 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 689 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **41 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `BaseResult` connect `BaseResult` to `.ToActionResult`, `.Handle`, `ErrorCode`, `ErrorCategoryTests`, `Phase6HttpContractFunctionalTests`, `IProductCache`, `5. Target code standards`, `UpdateProductCommand`, `PagedResponse`, `AuditableBaseEntity`, `Contexto técnico relevante`, `ProductController`, `ProductDto`, `GetProductByIdQuery`, `Phase 3 — Incremental implementation plan`, `CreateProductCommand`?**
  _High betweenness centrality (0.179) - this node is a cross-community bridge._
- **Are the 4 inferred relationships involving `BaseResult` (e.g. with `Current implementation conventions` and `Errors and API results`) actually correct?**
  _`BaseResult` has 4 INFERRED edges - model-reasoned connections that need verification._
- **What connects `Microsoft.VisualStudio.SDK`, `Microsoft.VSSDK.BuildTools`, `UserName` to the rest of the system?**
  _373 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `ResourceGeneral` be split into smaller, more focused modules?**
  _Cohesion score 0.08695652173913043 - nodes in this community are weakly interconnected._
- **Why does `ProductDto` connect `ProductDto` to `ProductServiceTests`, `Phase6HttpContractFunctionalTests`, `IProductCache`, `Relatório das Fases 0–5 — 2026-10-10`, `5. Target code standards`, `UpdateProductCommand`, `Template.Domain.Products.Entities`, `PagedResponse`, `DistributedProductCacheTests`, `ProductRepository`, `ProductController`, `GetProductByIdQuery`, `Phase 3 — Incremental implementation plan`, `CreateProductCommand`?**
  _High betweenness centrality (0.116) - this node is a cross-community bridge._
- **Are the 5 inferred relationships involving `ProductDto` (e.g. with `Immediate boundary corrections` and `Phase 4 — Clean the Domain model`) actually correct?**
  _`ProductDto` has 5 INFERRED edges - model-reasoned connections that need verification._
- **Should `Person` be split into smaller, more focused modules?**
  _Cohesion score 0.07207792207792207 - nodes in this community are weakly interconnected._