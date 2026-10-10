# Graph Report - TemplateCleanArchNET  (2026-10-10)

## Corpus Check
- 182 files · ~31,969 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 14 file(s) not represented in the graph (top: (none) 4, .resx 4, .jsonl 2)

## Summary
- 1333 nodes · 2458 edges · 100 communities (57 shown, 43 thin omitted)
- Extraction: 92% EXTRACTED · 8% INFERRED · 0% AMBIGUOUS · INFERRED: 194 edges (avg confidence: 0.86)
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
- .CustomResponse
- .Handle
- ErrorCategory
- Order
- ProductFunctionalTests
- system
- microsoft_entityframeworkcore
- UpdateProductCommand
- .GetRepository
- ValidationBehavior
- ApplicationDbContext
- Template.WebApi.csproj
- GetPagedListProductQuery
- IAccountServices
- AuthenticationRequest
- Template.Application/ServiceRegistration.cs
- ElasticSearchSettings
- ProductController
- IAuthenticatedUserService
- ArchitectureRulesTests
- .GetProductAsync
- Template.Application.Wrappers
- GetProductByIdQueryHandler
- .GetPagedUsers
- Phase 3 — Incremental implementation plan
- Template.Application.csproj
- AuthenticationResponse
- CreateProductCommand
- BaseResult
- AccountServices
- .BuildModel
- .AddCustomLocalization
- Template.Application.DTOs.Products
- TranslatorMessageDto
- ConfigureSwaggerOptions
- ProductEntity
- Template.Application.Settings
- .AddIdentityInfrastructure
- TemplateCleanArchitecture.sln
- 5. Target code standards
- ApplicationDbContext.cs
- Template.Functional.Tests.csproj
- PaginationResponseDto
- shouldly
- IdentitySettings
- HttpClientGetExtensionMethods
- Template.Infrastructure.Identity
- ApiErrorItem
- IUnitOfWork
- ProductConfiguration.cs
- .ChangePassword
- Template.Infrastructure.Persistence.csproj
- Template.Unit.Tests.csproj
- UserDto
- ApiRoutes
- ProductDto
- InitialPostgresMigration
- PagedResponse
- SwaggerExtensions.cs
- Template.Integration.Tests.csproj
- .UnitOfWork_SaveChangesAsync_HonorsCancelledToken
- InitialPostgresMigration
- Template.Functional.Tests.Common
- ProductPriceDecimal
- CustomWebApplicationFactory
- Template.Architecture.Tests.csproj
- Template.Application.DTOs
- Fases 6–7 — bloqueio de pré-condições (2026-10-10)
- CorsExtensions
- Phase 0 baseline — 2026-10-10
- .ProductPrice_ShouldRoundTripExactlyAsDecimal
- contract-checklist.md
- boundaries.md
- verification-matrix.md
- postgresql-review.md
- adapter-checklist.md
- upgrade-record.md
- ChangePasswordRequest
- ServiceRegistrationTests
- init-db.sh
- skill-activation-report.md
- Core/AGENTS.md
- Infrastructure/AGENTS.md
- Presentation/AGENTS.md
- Tests/AGENTS.md

## God Nodes (most connected - your core abstractions)
1. `BaseResult` - 70 edges
2. `ProductDto` - 36 edges
3. `ProductEntity` - 35 edges
4. `Template.Application.Wrappers` - 34 edges
5. `Template.Application.Interfaces` - 29 edges
6. `Phase 3 — Incremental implementation plan` - 25 edges
7. `AuthenticationResponse` - 24 edges
8. `ArchitectureRulesTests` - 22 edges
9. `PagedResponse` - 18 edges
10. `Order` - 18 edges

## Surprising Connections (you probably didn't know these)
- `Phase 13 — Standardize observability, health, and localization` --references--> `ITranslator`  [INFERRED]
  docs/fase-3-plano-implementacao.md → Src/Core/Template.Application/Interfaces/ITranslator.cs
- `Errors and API results` --references--> `BaseResult`  [INFERRED]
  docs/fase-2-workspace.md → Src/Core/Template.Application/Wrappers/BaseResult.cs
- `Phase 5 — Standardize application use cases and results` --references--> `BaseResult`  [INFERRED]
  docs/fase-3-plano-implementacao.md → Src/Core/Template.Application/Wrappers/BaseResult.cs
- `Current solution structure` --references--> `ProductDto`  [INFERRED]
  AGENTS.md → Src/Core/Template.Application/DTOs/Products/ProductDto.cs
- `Immediate boundary corrections` --references--> `ProductDto`  [INFERRED]
  docs/fase-2-workspace.md → Src/Core/Template.Application/DTOs/Products/ProductDto.cs

## Import Cycles
- None detected.

## Communities (100 total, 43 thin omitted)

### Community 0 - "Template.Application.Interfaces"
Cohesion: 0.21
Nodes (6): Template.Application.Interfaces, Template.Unit.Tests.ApplicationTests.Features.Products.Commands, Template.Application.Helpers, Template.Application.Features.Products.Commands, Template.Application.Interfaces.Repositories, Regexs

### Community 1 - "ResourceGeneral"
Cohesion: 0.09
Nodes (17): Template.Infrastructure.Resources.ProjectResources, ResourceGeneral, BarCode, ConfirmPassword, Culture, Id, Name, Password (+9 more)

### Community 2 - "Person"
Cohesion: 0.09
Nodes (9): Template.Application.Services.Elastic, Template.Domain.Person, ElasticService, IElasticService, Person, FirstName, Id, LastName (+1 more)

### Community 3 - "Container (Dockerfile)"
Cohesion: 0.05
Nodes (43): commandName, environmentVariables, launchBrowser, launchUrl, publishAllPorts, useSSL, ASPNETCORE_ENVIRONMENT, ASPNETCORE_HTTP_PORTS (+35 more)

### Community 4 - ".CustomResponse"
Cohesion: 0.07
Nodes (25): Change and validation rules, Current implementation conventions, Current solution structure, Execution status, Repository purpose, TemplateCleanArchNET agent guide, Verified commands (2026-10-08 and 2026-10-10), Template.WebApi.Infrastructure.Enums (+17 more)

### Community 5 - ".Handle"
Cohesion: 0.09
Nodes (6): DeleteProductCommand, Id, DeleteProductCommandHandler, IGenericRepository, GenericRepository, DeleteProductCommandHandlerTests

### Community 6 - "ErrorCategory"
Cohesion: 0.05
Nodes (22): Error, Description, ErrorCode, FieldName, ErrorCategory, Conflict, Forbidden, NotFound (+14 more)

### Community 7 - "Order"
Cohesion: 0.06
Nodes (25): Template.Application.Consumers, Template.Domain.Order, OrderConsumerDirect, OrderConsumerFanoutA, OrderConsumerFanoutB, OrderConsumerTopic, IClock, UtcNow (+17 more)

### Community 9 - "system"
Cohesion: 0.33
Nodes (3): Template.Infrastructure.Identity.Migrations, Template.Infrastructure.Persistence.Contexts, Template.Infrastructure.Persistence.Migrations

### Community 10 - "microsoft_entityframeworkcore"
Cohesion: 0.13
Nodes (8): Template.Infrastructure.Identity.Seeds, Template.Infrastructure.Persistence, Template.Infrastructure.Identity, Template.Infrastructure.Identity.Contexts, Template.Infrastructure.Resources, Template.Infrastructure.Persistence.Seeds, Template.WebApi.Infrastructure.Services, Template.Infrastructure.Identity.Models

### Community 11 - "UpdateProductCommand"
Cohesion: 0.18
Nodes (7): UpdateProductCommand, BarCode, Id, Name, Price, UpdateProductCommandHandler, UpdateProductCommandHandlerTests

### Community 12 - ".GetRepository"
Cohesion: 0.22
Nodes (4): BaseEfRepoTestFixture, EfRepositoryAdd, EfRepositoryDelete, EfRepositoryUpdate

### Community 13 - "ValidationBehavior"
Cohesion: 0.15
Nodes (10): ValidationBehavior, AuthenticationRequestValidator, CreateProductCommandValidator, UpdateProductCommandValidator, NameRequiredValidator, SampleRequest, Name, TokenCapturingValidator (+2 more)

### Community 14 - "ApplicationDbContext"
Cohesion: 0.12
Nodes (4): ApplicationDbContext, Products, ApplicationDbContextFactory, DefaultData

### Community 15 - "Template.WebApi.csproj"
Cohesion: 0.11
Nodes (17): AspNetCore.HealthChecks.Elasticsearch, AspNetCore.HealthChecks.UI.Client, Elastic.Serilog.Sinks, FluentValidation.AspNetCore, Microsoft.AspNetCore.Mvc.Versioning.ApiExplorer, Microsoft.AspNetCore.OpenApi, Microsoft.EntityFrameworkCore.Tools, Microsoft.VisualStudio.Azure.Containers.Tools.Targets (+9 more)

### Community 16 - "GetPagedListProductQuery"
Cohesion: 0.26
Nodes (4): GetPagedListProductQuery, Name, GetPagedListProductQueryHandler, GetPagedListProductQueryHandlerTests

### Community 17 - "IAccountServices"
Cohesion: 0.18
Nodes (5): ChangeUserNameRequest, UserName, ChangeUserNameRequestValidator, IAccountServices, AccountController

### Community 18 - "AuthenticationRequest"
Cohesion: 0.31
Nodes (4): AuthenticationRequest, Password, UserName, AccountFunctionalTests

### Community 19 - "Template.Application/ServiceRegistration.cs"
Cohesion: 0.12
Nodes (6): Template.Application, Template.Unit.Tests.ApplicationTests.Services, Template.Unit.Tests.ApplicationTests.Behaviors, Template.Application.Behaviors, Template.Application.Services.Time, Template.Unit.Tests.ApplicationTests

### Community 20 - "ElasticSearchSettings"
Cohesion: 0.13
Nodes (9): Baseline validation before refactoring, ElasticSearchSettings, DataSet, DefaultIndex, Name, Namespace, Type, Url (+1 more)

### Community 22 - "IAuthenticatedUserService"
Cohesion: 0.12
Nodes (12): IAuthenticatedUserService, UserId, UserName, DesignTimeAuthenticatedUser, Instance, UserId, UserName, EntityFrameworkExtensions (+4 more)

### Community 25 - "Template.Application.Wrappers"
Cohesion: 0.30
Nodes (6): Template.Application.Wrappers, Template.Functional.Tests.ApiEndpoints.v2, Template.Infrastructure.Identity.Services, Template.Application.DTOs.Account.Request, Template.Application.DTOs.Account.Response, Template.Application.Interfaces.UserInterfaces

### Community 26 - "GetProductByIdQueryHandler"
Cohesion: 0.20
Nodes (4): GetProductByIdQuery, Id, GetProductByIdQueryHandler, GetProductByIdQueryHandlerTests

### Community 27 - ".GetPagedUsers"
Cohesion: 0.20
Nodes (6): Template.Application.Parameters, GetAllUsersRequest, Name, PaginationRequestParameter, PageNumber, PageSize

### Community 28 - "Phase 3 — Incremental implementation plan"
Cohesion: 0.08
Nodes (24): Decisions (recorded 2026-10-10), Global execution rules, Phase 0 — Freeze a reproducible baseline, Phase 10 — Move Elastic concerns behind infrastructure boundaries, Phase 11 — Upgrade the Elastic client and server stack, Phase 12 — Harden identity and JWT boundaries, Phase 13 — Standardize observability, health, and localization, Phase 14 — Establish a reproducible Compose baseline (+16 more)

### Community 29 - "Template.Application.csproj"
Cohesion: 0.15
Nodes (12): Elastic.Clients.Elasticsearch, FluentValidation.DependencyInjectionExtensions, MediatR, Microsoft.Extensions.Caching.StackExchangeRedis, Newtonsoft.Json, RabbitMQ.Client, net10.0, Microsoft.Extensions.Hosting.Abstractions (+4 more)

### Community 30 - "AuthenticationResponse"
Cohesion: 0.15
Nodes (8): AuthenticationResponse, Email, Id, IsVerified, JwToken, Roles, UserName, AuthenticationExtensionMethods

### Community 31 - "CreateProductCommand"
Cohesion: 0.26
Nodes (6): CreateProductCommand, BarCode, Name, Price, CreateProductCommandHandler, CreateProductCommandHandlerTests

### Community 32 - "BaseResult"
Cohesion: 0.25
Nodes (5): BaseResult, Data, Errors, Success, AccountFunctionalTests

### Community 33 - "AccountServices"
Cohesion: 0.18
Nodes (4): ITranslator, AccountServices, ServiceRegistration, Translator

### Community 36 - "Template.Application.DTOs.Products"
Cohesion: 0.15
Nodes (8): Template.Unit.Tests.ApplicationTests.Features.Products.Queries, Template.Application.Features.Products.Queries, Template.WebApi.Controllers.v2, Template.WebApi.Controllers, Template.Application.DTOs.Products, Template.Application.Services.RabbitMq, Template.WebApi.Controllers.v1, Template.Application.Services.Product

### Community 37 - "TranslatorMessageDto"
Cohesion: 0.20
Nodes (6): TranslatorMessageDto, Args, Text, AccountMessages, ProductMessages, TranslatorMessages

### Community 39 - "ProductEntity"
Cohesion: 0.21
Nodes (7): IProductRepository, ProductEntity, BarCode, Name, Price, ProductRepository, ProductTests

### Community 41 - ".AddIdentityInfrastructure"
Cohesion: 0.07
Nodes (10): IGetUserServices, IdentityContext, ApplicationRole, ApplicationUser, Created, Name, DefaultBasicUser, DefaultRoles (+2 more)

### Community 42 - "TemplateCleanArchitecture.sln"
Cohesion: 0.15
Nodes (6): Template, Microsoft.VisualStudio.SDK, Microsoft.VSSDK.BuildTools, Template.Infrastructure.Resources, net10.0, Microsoft.NET.Sdk

### Community 43 - "5. Target code standards"
Cohesion: 0.08
Nodes (23): 1. Evidence and assumptions, 2. Proposed AI workspace, 3. Dependency and integration update matrix, 4. Target Clean Architecture, 5. Target code standards, Application use cases, Controllers, Design principles (+15 more)

### Community 45 - "Template.Functional.Tests.csproj"
Cohesion: 0.20
Nodes (9): Microsoft.AspNetCore.Mvc.Testing, net10.0, coverlet.collector, Microsoft.Extensions.Hosting.Abstractions, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio (+1 more)

### Community 46 - "PaginationResponseDto"
Cohesion: 0.17
Nodes (5): PaginationResponseDto, Count, Data, PageNumber, PageSize

### Community 47 - "shouldly"
Cohesion: 0.26
Nodes (4): Template.Unit.Tests.DomainTests.Products.Entities, Template.Unit.Tests.ApplicationTests.Wrappers, Template.Domain.Products.Entities, Template.Integration.Tests.Data

### Community 48 - "IdentitySettings"
Cohesion: 0.11
Nodes (16): Current configuration names, Template.Infrastructure.Identity.Settings, LocalizationSettings, DefaultRequestCulture, SupportedCultures, IdentitySettings, PasswordRequireDigit, PasswordRequiredLength (+8 more)

### Community 50 - "Template.Infrastructure.Identity"
Cohesion: 0.25
Nodes (8): Microsoft.AspNetCore.Authentication.JwtBearer, Microsoft.AspNetCore.Identity.EntityFrameworkCore, System.IdentityModel.Tokens.Jwt, Template.Infrastructure.Identity, net10.0, Microsoft.EntityFrameworkCore.InMemory, Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.NET.Sdk

### Community 51 - "ApiErrorItem"
Cohesion: 0.29
Nodes (6): ApiErrorItem, Code, Field, Message, ApiErrorResponse, Errors

### Community 52 - "IUnitOfWork"
Cohesion: 0.15
Nodes (3): IUnitOfWork, UnitOfWork, ServiceRegistration

### Community 55 - "Template.Infrastructure.Persistence.csproj"
Cohesion: 0.22
Nodes (8): Microsoft.EntityFrameworkCore, Microsoft.Extensions.Configuration, Microsoft.Extensions.DependencyInjection, net10.0, Microsoft.EntityFrameworkCore.Design, Microsoft.EntityFrameworkCore.InMemory, Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.NET.Sdk

### Community 56 - "Template.Unit.Tests.csproj"
Cohesion: 0.22
Nodes (8): Moq, net10.0, coverlet.collector, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk

### Community 57 - "UserDto"
Cohesion: 0.20
Nodes (7): UserDto, Created, Email, Id, Name, PhoneNumber, UserName

### Community 58 - "ApiRoutes"
Cohesion: 0.38
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
Cohesion: 0.16
Nodes (4): Template.Functional.Tests.ApiEndpoints, Template.WebApi.Infrastructure.Filters, Template.WebApi.Infrastructure.Middlewares, Template.Functional.Tests.Common

### Community 68 - "CustomWebApplicationFactory"
Cohesion: 0.10
Nodes (4): Program, ExternalServicesUnavailableTests, ProductValidationFunctionalTests, CustomWebApplicationFactory

### Community 69 - "Template.Architecture.Tests.csproj"
Cohesion: 0.29
Nodes (6): net10.0, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk

### Community 70 - "Template.Application.DTOs"
Cohesion: 0.18
Nodes (3): Template.Infrastructure.Persistence.Repositories, Template.Application.DTOs, Template.Infrastructure.Resources.Services

### Community 71 - "Fases 6–7 — bloqueio de pré-condições (2026-10-10)"
Cohesion: 0.33
Nodes (5): Causa do bloqueio, Como desbloquear, Estado do Git registrado, Fases 6–7 — bloqueio de pré-condições (2026-10-10), Pré-condições

### Community 73 - "Phase 0 baseline — 2026-10-10"
Cohesion: 0.40
Nodes (4): Dependency and vulnerability baseline, Phase 0 baseline — 2026-10-10, Source state, Toolchain

### Community 81 - "ChangePasswordRequest"
Cohesion: 0.50
Nodes (4): ChangePasswordRequest, ConfirmPassword, Password, ChangePasswordRequestValidator

## Knowledge Gaps
- **319 isolated node(s):** `Microsoft.VisualStudio.SDK`, `Microsoft.VSSDK.BuildTools`, `UserName`, `Password`, `Password` (+314 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 577 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **43 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `BaseResult` connect `BaseResult` to `AccountServices`, `.CustomResponse`, `.Handle`, `ErrorCategory`, `ProductFunctionalTests`, `5. Target code standards`, `UpdateProductCommand`, `IAccountServices`, `AuthenticationRequest`, `ProductController`, `.GetProductAsync`, `GetProductByIdQueryHandler`, `Phase 3 — Incremental implementation plan`, `PagedResponse`, `AuthenticationResponse`, `CreateProductCommand`?**
  _High betweenness centrality (0.170) - this node is a cross-community bridge._
- **Are the 3 inferred relationships involving `BaseResult` (e.g. with `Current implementation conventions` and `Errors and API results`) actually correct?**
  _`BaseResult` has 3 INFERRED edges - model-reasoned connections that need verification._
- **What connects `Microsoft.VisualStudio.SDK`, `Microsoft.VSSDK.BuildTools`, `UserName` to the rest of the system?**
  _319 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `ResourceGeneral` be split into smaller, more focused modules?**
  _Cohesion score 0.08695652173913043 - nodes in this community are weakly interconnected._
- **Why does `ProductDto` connect `ProductDto` to `.CustomResponse`, `ProductFunctionalTests`, `UpdateProductCommand`, `5. Target code standards`, `PaginationResponseDto`, `shouldly`, `GetPagedListProductQuery`, `ProductController`, `.GetProductAsync`, `GetProductByIdQueryHandler`, `CreateProductCommand`?**
  _High betweenness centrality (0.084) - this node is a cross-community bridge._
- **Are the 4 inferred relationships involving `ProductDto` (e.g. with `Current solution structure` and `Immediate boundary corrections`) actually correct?**
  _`ProductDto` has 4 INFERRED edges - model-reasoned connections that need verification._
- **Should `Person` be split into smaller, more focused modules?**
  _Cohesion score 0.09250693802035152 - nodes in this community are weakly interconnected._