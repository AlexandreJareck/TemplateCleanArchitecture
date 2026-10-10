# Graph Report - TemplateCleanArchNET  (2026-10-10)

## Corpus Check
- 196 files · ~39,789 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 14 file(s) not represented in the graph (top: (none) 4, .resx 4, .jsonl 2)

## Summary
- 1463 nodes · 2804 edges · 117 communities (67 shown, 50 thin omitted)
- Extraction: 91% EXTRACTED · 9% INFERRED · 0% AMBIGUOUS · INFERRED: 264 edges (avg confidence: 0.87)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `95d04acb`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- Template.Application.Interfaces
- ResourceMessages
- Person
- Container (Dockerfile)
- .ToActionResult
- .Handle
- .Failure
- Order
- ProductFunctionalTests
- system
- Template.Infrastructure.Identity/ServiceRegistration.cs
- UpdateProductCommand
- .GetRepository
- ValidationBehavior
- ApplicationDbContext
- Template.WebApi.csproj
- GetPagedListProductQuery
- ChangePasswordRequest
- ProductRepository
- Template.Application/ServiceRegistration.cs
- ElasticSearchSettings
- ProductController
- IAuthenticatedUserService
- ArchitectureRulesTests
- .GetProductAsync
- Template.Application.Wrappers
- GetProductByIdQueryHandler
- GetAllUsersRequest
- Phase 3 — Incremental implementation plan
- Template.Application.csproj
- AuthenticationResponse
- CreateProductCommand
- BaseResult
- AccountServices
- .BuildModel
- LocalizationExtensions.cs
- Program.cs
- TranslatorMessageDto
- ConfigureSwaggerOptions
- ProductEntity
- OrderConsumer.cs
- .OnModelCreating
- TemplateCleanArchitecture.sln
- 5. Target code standards
- microsoft_entityframeworkcore
- Template.Functional.Tests.csproj
- PaginationResponseDto
- shouldly
- IdentitySettings
- HttpClientGetExtensionMethods
- Template.Infrastructure.Identity
- ApiErrorItem
- UnitOfWork
- Contexto técnico relevante
- AccountController
- Template.Infrastructure.Persistence.csproj
- Template.Unit.Tests.csproj
- UserDto
- Product
- ProductDto
- InitialPostgresMigration
- PagedResponse
- Template.WebApi.Infrastructure.Extensions
- Template.Integration.Tests.csproj
- .UnitOfWork_SaveChangesAsync_HonorsCancelledToken
- InitialPostgresMigration
- Template.Functional.Tests.Common
- ProductPriceDecimal
- Phase6HttpContractFunctionalTests
- Template.Architecture.Tests.csproj
- Template.Application.DTOs
- Fases 6–7 — bloqueio de pré-condições (2026-10-10)
- CorsExtensions
- Phase 0 baseline — 2026-10-10
- .AddApplicationLayer
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
- IGenericRepository
- ErrorCode
- ResourceGeneral
- IClock
- .AddIdentityInfrastructure
- Relatório das Fases 0–5 — 2026-10-10
- Template.PostgreSql.Integration.Tests.csproj
- IAccountServices
- .GetPagedUsers
- Error
- ElasticExtensions
- Template.Domain.Common
- api-v1-lifecycle.md

## God Nodes (most connected - your core abstractions)
1. `BaseResult` - 72 edges
2. `ProductEntity` - 39 edges
3. `ProductDto` - 38 edges
4. `Template.Application.Wrappers` - 35 edges
5. `Template.Application.Interfaces` - 30 edges
6. `ArchitectureRulesTests` - 26 edges
7. `Phase 3 — Incremental implementation plan` - 25 edges
8. `AuthenticationResponse` - 24 edges
9. `ApplicationDbContext` - 21 edges
10. `Template.Domain.Products.Entities` - 19 edges

## Surprising Connections (you probably didn't know these)
- `Phase 13 — Standardize observability, health, and localization` --references--> `ITranslator`  [INFERRED]
  docs/fase-3-plano-implementacao.md → Src/Core/Template.Application/Interfaces/ITranslator.cs
- `Errors and API results` --references--> `BaseResult`  [INFERRED]
  docs/fase-2-workspace.md → Src/Core/Template.Application/Wrappers/BaseResult.cs
- `Phase 5 — Standardize application use cases and results` --references--> `BaseResult`  [INFERRED]
  docs/fase-3-plano-implementacao.md → Src/Core/Template.Application/Wrappers/BaseResult.cs
- `Fase 7 — persistência e PostgreSQL (somente depois da Fase 6 aprovada)` --references--> `ProductPriceDecimal`  [INFERRED]
  docs/refactoring/handoff-fases-6-7.md → Src/Infrastructure/Template.Infrastructure.Persistence/Migrations/20261010151729_ProductPriceDecimal.Designer.cs
- `Current implementation conventions` --references--> `ValidationBehavior`  [INFERRED]
  AGENTS.md → Src/Core/Template.Application/Behaviors/ValidationBehavior.cs

## Import Cycles
- None detected.

## Communities (117 total, 50 thin omitted)

### Community 0 - "Template.Application.Interfaces"
Cohesion: 0.20
Nodes (8): Template.Unit.Tests.ApplicationTests.Features.Products.Queries, Template.Application.Interfaces, Template.Application.Features.Products.Queries, Template.Unit.Tests.ApplicationTests.Features.Products.Commands, Template.Application.Helpers, Template.Application.Features.Products.Commands, Template.Application.Interfaces.Repositories, Regexs

### Community 1 - "ResourceMessages"
Cohesion: 0.17
Nodes (8): Template.Infrastructure.Resources.ProjectResources, ResourceMessages, Account_NotFound_with_UserName, Culture, Invalid_password, Product_NotFound_with_id, ResourceManager, Username_is_already_taken

### Community 2 - "Person"
Cohesion: 0.07
Nodes (17): Template.WebApi.Infrastructure.Enums, Template.Application.Services.Elastic, Template.Domain.Person, ElasticService, IElasticService, Person, FirstName, Id (+9 more)

### Community 3 - "Container (Dockerfile)"
Cohesion: 0.05
Nodes (43): commandName, environmentVariables, launchBrowser, launchUrl, publishAllPorts, useSSL, ASPNETCORE_ENVIRONMENT, ASPNETCORE_HTTP_PORTS (+35 more)

### Community 4 - ".ToActionResult"
Cohesion: 0.08
Nodes (3): ProductController, ApiResultMapper, ProblemDetailsAuthorizationMiddlewareResultHandler

### Community 5 - ".Handle"
Cohesion: 0.27
Nodes (4): DeleteProductCommand, Id, DeleteProductCommandHandler, DeleteProductCommandHandlerTests

### Community 7 - "Order"
Cohesion: 0.17
Nodes (8): IRabbitMqService, RabbitMqService, Order, Id, Price, ProductName, Quantity, MessageController

### Community 9 - "system"
Cohesion: 0.33
Nodes (3): Template.Infrastructure.Identity.Migrations, Template.Infrastructure.Identity.Contexts, Template.Infrastructure.Persistence.Migrations

### Community 11 - "UpdateProductCommand"
Cohesion: 0.18
Nodes (7): UpdateProductCommand, BarCode, Id, Name, Price, UpdateProductCommandHandler, UpdateProductCommandHandlerTests

### Community 12 - ".GetRepository"
Cohesion: 0.17
Nodes (5): BaseEfRepoTestFixture, EfRepositoryAdd, EfRepositoryDelete, EfRepositoryUpdate, ProductPersistencePrecisionTests

### Community 13 - "ValidationBehavior"
Cohesion: 0.15
Nodes (10): ValidationBehavior, AuthenticationRequestValidator, CreateProductCommandValidator, UpdateProductCommandValidator, NameRequiredValidator, SampleRequest, Name, TokenCapturingValidator (+2 more)

### Community 14 - "ApplicationDbContext"
Cohesion: 0.10
Nodes (5): ApplicationDbContext, Products, ApplicationDbContextFactory, DefaultData, ServiceRegistration

### Community 15 - "Template.WebApi.csproj"
Cohesion: 0.12
Nodes (16): Asp.Versioning.Mvc.ApiExplorer, AspNetCore.HealthChecks.Elasticsearch, AspNetCore.HealthChecks.UI.Client, Elastic.Serilog.Sinks, Microsoft.AspNetCore.OpenApi, Microsoft.EntityFrameworkCore.Tools, Microsoft.VisualStudio.Azure.Containers.Tools.Targets, Serilog.AspNetCore (+8 more)

### Community 16 - "GetPagedListProductQuery"
Cohesion: 0.26
Nodes (4): GetPagedListProductQuery, Name, GetPagedListProductQueryHandler, GetPagedListProductQueryHandlerTests

### Community 17 - "ChangePasswordRequest"
Cohesion: 0.21
Nodes (5): ChangePasswordRequest, ConfirmPassword, Password, ChangePasswordRequestValidator, AccountController

### Community 18 - "ProductRepository"
Cohesion: 0.18
Nodes (4): ProductRepository, PostgreSqlCollection, PostgreSqlFixture, PostgreSqlPersistenceTests

### Community 19 - "Template.Application/ServiceRegistration.cs"
Cohesion: 0.16
Nodes (6): Template.Application, Template.Unit.Tests.ApplicationTests.Services, Template.Unit.Tests.ApplicationTests.Behaviors, Template.Application.Behaviors, Template.Application.Services.Time, Template.Unit.Tests.ApplicationTests

### Community 20 - "ElasticSearchSettings"
Cohesion: 0.29
Nodes (7): ElasticSearchSettings, DataSet, DefaultIndex, Name, Namespace, Type, Url

### Community 22 - "IAuthenticatedUserService"
Cohesion: 0.12
Nodes (14): IAuthenticatedUserService, UserId, UserName, DesignTimeAuthenticatedUser, Instance, UserId, UserName, AuthenticatedUserService (+6 more)

### Community 25 - "Template.Application.Wrappers"
Cohesion: 0.27
Nodes (6): Template.Application.Wrappers, Template.Functional.Tests.ApiEndpoints.v2, Template.Infrastructure.Identity.Services, Template.Application.DTOs.Account.Request, Template.Application.DTOs.Account.Response, Template.Application.Interfaces.UserInterfaces

### Community 26 - "GetProductByIdQueryHandler"
Cohesion: 0.20
Nodes (4): GetProductByIdQuery, Id, GetProductByIdQueryHandler, GetProductByIdQueryHandlerTests

### Community 27 - "GetAllUsersRequest"
Cohesion: 0.29
Nodes (6): Template.Application.Parameters, GetAllUsersRequest, Name, PaginationRequestParameter, PageNumber, PageSize

### Community 28 - "Phase 3 — Incremental implementation plan"
Cohesion: 0.08
Nodes (24): Decisions (recorded 2026-10-10), Global execution rules, Phase 0 — Freeze a reproducible baseline, Phase 10 — Move Elastic concerns behind infrastructure boundaries, Phase 11 — Upgrade the Elastic client and server stack, Phase 12 — Harden identity and JWT boundaries, Phase 13 — Standardize observability, health, and localization, Phase 14 — Establish a reproducible Compose baseline (+16 more)

### Community 29 - "Template.Application.csproj"
Cohesion: 0.15
Nodes (12): Elastic.Clients.Elasticsearch, FluentValidation.DependencyInjectionExtensions, MediatR, Microsoft.Extensions.Caching.StackExchangeRedis, Newtonsoft.Json, RabbitMQ.Client, net10.0, Microsoft.Extensions.Hosting.Abstractions (+4 more)

### Community 30 - "AuthenticationResponse"
Cohesion: 0.14
Nodes (9): AuthenticationResponse, Email, Id, IsVerified, JwToken, Roles, UserName, AccountFunctionalTests (+1 more)

### Community 31 - "CreateProductCommand"
Cohesion: 0.26
Nodes (6): CreateProductCommand, BarCode, Name, Price, CreateProductCommandHandler, CreateProductCommandHandlerTests

### Community 32 - "BaseResult"
Cohesion: 0.20
Nodes (8): AuthenticationRequest, Password, UserName, BaseResult, Data, Errors, Success, AccountFunctionalTests

### Community 33 - "AccountServices"
Cohesion: 0.14
Nodes (7): ITranslator, ApplicationUser, Created, Name, AccountServices, ServiceRegistration, Translator

### Community 36 - "Program.cs"
Cohesion: 0.12
Nodes (8): Template.Infrastructure.Persistence, Template.WebApi.Infrastructure.Middlewares, Template.WebApi.Controllers.v2, Template.Application.DTOs.Products, Template.WebApi.Infrastructure.Http, Template.Infrastructure.Persistence.Seeds, Template.WebApi.Controllers.v1, Template.Application.Services.Product

### Community 37 - "TranslatorMessageDto"
Cohesion: 0.20
Nodes (6): TranslatorMessageDto, Args, Text, AccountMessages, ProductMessages, TranslatorMessages

### Community 39 - "ProductEntity"
Cohesion: 0.14
Nodes (7): Template.Infrastructure.Persistence.Contexts.Configurations, ProductEntity, BarCode, Name, Price, ProductConfiguration, ProductTests

### Community 40 - "OrderConsumer.cs"
Cohesion: 0.18
Nodes (4): Template.Application.Consumers, Template.Domain.Order, Template.Application.Services.RabbitMq, Template.Application.Settings

### Community 41 - ".OnModelCreating"
Cohesion: 0.08
Nodes (6): Template.Infrastructure.Identity.Seeds, Template.Infrastructure.Identity.Models, IdentityContext, ApplicationRole, DefaultBasicUser, DefaultRoles

### Community 42 - "TemplateCleanArchitecture.sln"
Cohesion: 0.15
Nodes (6): Template, Microsoft.VisualStudio.SDK, Microsoft.VSSDK.BuildTools, Template.Infrastructure.Resources, net10.0, Microsoft.NET.Sdk

### Community 43 - "5. Target code standards"
Cohesion: 0.08
Nodes (23): 1. Evidence and assumptions, 2. Proposed AI workspace, 3. Dependency and integration update matrix, 4. Target Clean Architecture, 5. Target code standards, Application use cases, Controllers, Design principles (+15 more)

### Community 44 - "microsoft_entityframeworkcore"
Cohesion: 0.17
Nodes (4): Template.Infrastructure.Persistence.Repositories, Template.PostgreSql.Integration.Tests, Template.Infrastructure.Persistence.Contexts, Template.Infrastructure.Persistence.Extensions

### Community 45 - "Template.Functional.Tests.csproj"
Cohesion: 0.20
Nodes (9): Microsoft.AspNetCore.Mvc.Testing, net10.0, coverlet.collector, Microsoft.Extensions.Hosting.Abstractions, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio (+1 more)

### Community 46 - "PaginationResponseDto"
Cohesion: 0.18
Nodes (5): PaginationResponseDto, Count, Data, PageNumber, PageSize

### Community 47 - "shouldly"
Cohesion: 0.29
Nodes (4): Template.Unit.Tests.DomainTests.Products.Entities, Template.Unit.Tests.ApplicationTests.Wrappers, Template.Domain.Products.Entities, Template.Integration.Tests.Data

### Community 48 - "IdentitySettings"
Cohesion: 0.14
Nodes (11): Current configuration names, Template.Infrastructure.Identity.Settings, LocalizationSettings, DefaultRequestCulture, SupportedCultures, IdentitySettings, PasswordRequireDigit, PasswordRequiredLength (+3 more)

### Community 50 - "Template.Infrastructure.Identity"
Cohesion: 0.25
Nodes (8): Microsoft.AspNetCore.Authentication.JwtBearer, Microsoft.AspNetCore.Identity.EntityFrameworkCore, System.IdentityModel.Tokens.Jwt, Template.Infrastructure.Identity, net10.0, Microsoft.EntityFrameworkCore.InMemory, Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.NET.Sdk

### Community 51 - "ApiErrorItem"
Cohesion: 0.29
Nodes (6): ApiErrorItem, Code, Field, Message, ApiErrorResponse, Errors

### Community 53 - "Contexto técnico relevante"
Cohesion: 0.15
Nodes (13): Contexto técnico relevante, Fase 5 em detalhe, IProductRepository, ErrorCategory, Conflict, Forbidden, NotFound, Unauthorized (+5 more)

### Community 54 - "AccountController"
Cohesion: 0.24
Nodes (4): ChangeUserNameRequest, UserName, ChangeUserNameRequestValidator, AccountController

### Community 55 - "Template.Infrastructure.Persistence.csproj"
Cohesion: 0.22
Nodes (8): Microsoft.EntityFrameworkCore, Microsoft.Extensions.Configuration, Microsoft.Extensions.DependencyInjection, net10.0, Microsoft.EntityFrameworkCore.Design, Microsoft.EntityFrameworkCore.InMemory, Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.NET.Sdk

### Community 56 - "Template.Unit.Tests.csproj"
Cohesion: 0.22
Nodes (8): Moq, net10.0, coverlet.collector, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk

### Community 57 - "UserDto"
Cohesion: 0.20
Nodes (7): UserDto, Created, Email, Id, Name, PhoneNumber, UserName

### Community 58 - "Product"
Cohesion: 0.25
Nodes (5): Account, ApiRoutes, Product, V1, V2

### Community 59 - "ProductDto"
Cohesion: 0.22
Nodes (7): Phase 4 — Clean the Domain model, ProductDto, BarCode, CreatedDateTime, Id, Name, Price

### Community 61 - "PagedResponse"
Cohesion: 0.22
Nodes (5): PagedResponse, PageNumber, PageSize, TotalItems, TotalPages

### Community 63 - "Template.Integration.Tests.csproj"
Cohesion: 0.25
Nodes (7): net10.0, coverlet.collector, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk

### Community 66 - "Template.Functional.Tests.Common"
Cohesion: 0.21
Nodes (3): Template.Functional.Tests.ApiEndpoints, Template.WebApi.Infrastructure.Filters, Template.Functional.Tests.Common

### Community 68 - "Phase6HttpContractFunctionalTests"
Cohesion: 0.09
Nodes (5): Program, ExternalServicesUnavailableTests, Phase6HttpContractFunctionalTests, ProductValidationFunctionalTests, CustomWebApplicationFactory

### Community 69 - "Template.Architecture.Tests.csproj"
Cohesion: 0.29
Nodes (6): net10.0, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk

### Community 70 - "Template.Application.DTOs"
Cohesion: 0.22
Nodes (3): Template.Infrastructure.Resources, Template.Application.DTOs, Template.Infrastructure.Resources.Services

### Community 71 - "Fases 6–7 — bloqueio de pré-condições (2026-10-10)"
Cohesion: 0.33
Nodes (5): Causa do bloqueio, Como desbloquear, Estado do Git registrado, Fases 6–7 — bloqueio de pré-condições (2026-10-10), Pré-condições

### Community 73 - "Phase 0 baseline — 2026-10-10"
Cohesion: 0.25
Nodes (5): Baseline validation before refactoring, Dependency and vulnerability baseline, Phase 0 baseline — 2026-10-10, Source state, Toolchain

### Community 74 - ".AddApplicationLayer"
Cohesion: 0.18
Nodes (10): OrderConsumerDirect, OrderConsumerFanoutA, OrderConsumerFanoutB, OrderConsumerTopic, ServiceRegistration, RabbitMqSettings, HostName, Password (+2 more)

### Community 81 - "AuditableBaseEntity"
Cohesion: 0.12
Nodes (14): Change and validation rules, Current implementation conventions, Current solution structure, Execution status, Repository purpose, TemplateCleanArchNET agent guide, Verified commands (2026-10-10, Phase 5 checkpoint), AuditableBaseEntity (+6 more)

### Community 85 - "handoff-fases-6-7.md"
Cohesion: 0.17
Nodes (9): Template.WebApi.Controllers, Entrega, Estado de partida (verificado em 2026-10-10 às 14:26), Execução de comandos, Fase 6 — controllers e contratos HTTP, Fase 7 — persistência e PostgreSQL (somente depois da Fase 6 aprovada), Fontes obrigatórias (ler antes de alterar qualquer arquivo), Regras gerais (+1 more)

### Community 102 - "ErrorCode"
Cohesion: 0.18
Nodes (7): ErrorCode, AccessDenied, ErrorInIdentity, Exception, FieldDataInvalid, ModelStateNotValid, NotFound

### Community 103 - "ResourceGeneral"
Cohesion: 0.18
Nodes (9): ResourceGeneral, BarCode, ConfirmPassword, Culture, Id, Name, Password, ResourceManager (+1 more)

### Community 104 - "IClock"
Cohesion: 0.20
Nodes (5): IClock, UtcNow, SystemClock, UtcNow, SystemClockTests

### Community 105 - ".AddIdentityInfrastructure"
Cohesion: 0.20
Nodes (6): ServiceRegistration, JwtSettings, Audience, DurationInMinutes, Issuer, Key

### Community 106 - "Relatório das Fases 0–5 — 2026-10-10"
Cohesion: 0.22
Nodes (8): Comparação de contrato, Confirmação, Mudanças por fase, Relatório das Fases 0–5 — 2026-10-10, Revisão da migration e do SQL (Fase 4), Riscos e pendências, Testes e gates (execução de 2026-10-10, 14:25–14:26), Versões e fontes oficiais

### Community 108 - "Template.PostgreSql.Integration.Tests.csproj"
Cohesion: 0.25
Nodes (7): Testcontainers.PostgreSql, net10.0, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk

### Community 112 - "Error"
Cohesion: 0.33
Nodes (4): Error, Description, ErrorCode, FieldName

## Knowledge Gaps
- **340 isolated node(s):** `Microsoft.VisualStudio.SDK`, `Microsoft.VSSDK.BuildTools`, `UserName`, `Password`, `Password` (+335 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 624 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **50 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `BaseResult` connect `BaseResult` to `.ToActionResult`, `.Handle`, `.Failure`, `ProductFunctionalTests`, `UpdateProductCommand`, `ChangePasswordRequest`, `ProductController`, `.GetProductAsync`, `GetProductByIdQueryHandler`, `Phase 3 — Incremental implementation plan`, `AuthenticationResponse`, `CreateProductCommand`, `AccountServices`, `5. Target code standards`, `Contexto técnico relevante`, `PagedResponse`, `Phase6HttpContractFunctionalTests`, `AuditableBaseEntity`, `IAccountServices`, `Error`?**
  _High betweenness centrality (0.163) - this node is a cross-community bridge._
- **Are the 4 inferred relationships involving `BaseResult` (e.g. with `Current implementation conventions` and `Errors and API results`) actually correct?**
  _`BaseResult` has 4 INFERRED edges - model-reasoned connections that need verification._
- **What connects `Microsoft.VisualStudio.SDK`, `Microsoft.VSSDK.BuildTools`, `UserName` to the rest of the system?**
  _340 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Person` be split into smaller, more focused modules?**
  _Cohesion score 0.07017543859649122 - nodes in this community are weakly interconnected._
- **Why does `ProductDto` connect `ProductDto` to `Program.cs`, `Phase6HttpContractFunctionalTests`, `ProductFunctionalTests`, `Relatório das Fases 0–5 — 2026-10-10`, `UpdateProductCommand`, `5. Target code standards`, `PaginationResponseDto`, `GetPagedListProductQuery`, `ProductRepository`, `ProductController`, `.GetProductAsync`, `GetProductByIdQueryHandler`, `CreateProductCommand`?**
  _High betweenness centrality (0.077) - this node is a cross-community bridge._
- **Are the 14 inferred relationships involving `ProductEntity` (e.g. with `.Handle()` and `.SeedAsync()`) actually correct?**
  _`ProductEntity` has 14 INFERRED edges - model-reasoned connections that need verification._
- **Should `Container (Dockerfile)` be split into smaller, more focused modules?**
  _Cohesion score 0.048625792811839326 - nodes in this community are weakly interconnected._