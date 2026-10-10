# Graph Report - TemplateCleanArchNET  (2026-10-10)

## Corpus Check
- 237 files · ~67,404 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 14 file(s) not represented in the graph (top: (none) 4, .resx 4, .jsonl 2)

## Summary
- 2229 nodes · 4571 edges · 159 communities (94 shown, 65 thin omitted)
- Extraction: 91% EXTRACTED · 9% INFERRED · 0% AMBIGUOUS · INFERRED: 399 edges (avg confidence: 0.88)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `f293cb9b`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- shouldly
- ResourceMessages
- Person
- Container (Dockerfile)
- .ToActionResult
- .Handle
- ErrorCategoryTests
- Order
- IClassFixture
- system
- DistributedProductCacheTests.cs
- UpdateProductCommand
- .GetRepository
- ValidationBehavior
- ApplicationDbContext
- Template.WebApi.csproj
- GetPagedListProductQuery
- HangingDistributedCache
- PostgreSqlFixture
- ServiceRegistrationTests.cs
- OrderMessagingBrokerTests
- ProductDto
- IAuthenticatedUserService
- ArchitectureRulesTests
- Template.Infrastructure.Identity/ServiceRegistration.cs
- GetProductByIdQuery
- Phase 9 Execution Handoff
- Phase 3 — Incremental implementation plan
- Template.Application.csproj
- BaseResult
- CreateProductCommand
- IProductCache
- Template.Application.Wrappers
- .BuildModel
- .AddCustomLocalization
- Program.cs
- Fase 5 em detalhe
- ConfigureSwaggerOptions
- .AbortQuietlyAsync
- RabbitMqTestSupport.cs
- AccountServices
- TemplateCleanArchitecture.sln
- 5. Target code standards
- Template.Application.Interfaces
- Template.Functional.Tests.csproj
- TemplateCleanArchNET agent guide
- ProductEntity
- IdentitySettings
- HttpClientGetExtensionMethods
- Template.Infrastructure.Identity
- Relatório das Fases 6–7 — 2026-10-10
- UnitOfWork
- .InvokeAsync
- .Decode
- Template.Infrastructure.Persistence.csproj
- Template.Unit.Tests.csproj
- Required tests
- Product
- Phase 11 Execution Handoff
- InitialPostgresMigration
- ApiResultFilterAttribute
- IOrderEventPublisher
- Template.Integration.Tests.csproj
- ScriptedHandler
- MessageFunctionalTests.cs
- ProductPriceDecimal
- Phase6HttpContractFunctionalTests
- Template.Architecture.Tests.csproj
- PaginationResponseDto
- Fases 6–7 — bloqueio de pré-condições (2026-10-10)
- CorsExtensions
- ElasticSearchSettings
- RabbitMqOrderConsumerService
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
- Contexto técnico relevante
- .ProcessAsync
- ErrorCode
- OrderPlacedEvent
- .AddApplicationLayer
- AccountController
- Relatório das Fases 0–5 — 2026-10-10
- .Handle
- Template.PostgreSql.Integration.Tests.csproj
- .HandleAsync
- RabbitMqSettings
- Phase 12 Execution Handoff
- Phase 10 Execution Handoff
- BrokerAvailabilityTests
- MessageFunctionalTests
- AccountController
- api-v1-lifecycle.md
- IElasticService
- CacheSettings
- ApiResultMapper
- RecordingHandler
- RabbitMqFixture
- DistributedProductCache
- DistributedProductCacheTests
- CustomWebApplicationFactory
- Template.Infrastructure.Messaging.csproj
- RecordingOrderEventPublisher
- MessagingHarness
- .GetPagedUsers
- IdentityContext
- TranslatorMessageDto
- Template.Infrastructure.Identity.Models
- Program
- ResourceGeneral
- .Log
- MessagePublishException
- UserDto
- MessageController
- .HandleAsync
- ApiErrorItem
- InMemoryProcessedMessageStore
- PagedResponse
- .UnitOfWork_SaveChangesAsync_HonorsCancelledToken
- ErrorCodeEnum
- Required tests
- ElasticService.cs
- Required tests
- .DeclareAsync
- CountingProcessedMessageStore
- ApplicationRole
- IncomingMessageContext
- Error
- Architecture boundaries
- OrderDispatch
- RecordingPublisherFactory
- .HandleAsync

## God Nodes (most connected - your core abstractions)
1. `BaseResult` - 76 edges
2. `ProductDto` - 50 edges
3. `OrderPlacedEvent` - 44 edges
4. `ProductEntity` - 40 edges
5. `Template.Application.Wrappers` - 36 edges
6. `ArchitectureRulesTests` - 32 edges
7. `Template.Application.Interfaces` - 30 edges
8. `OrderMessagingBrokerTests` - 27 edges
9. `Phase 12 Execution Handoff` - 27 edges
10. `Phase 3 — Incremental implementation plan` - 25 edges

## Surprising Connections (you probably didn't know these)
- `Phase 4 — Clean the Domain model` --references--> `ProductDto`  [INFERRED]
  docs/fase-3-plano-implementacao.md → Src/Core/Template.Application/DTOs/Products/ProductDto.cs
- `Riscos, pendências e rollback` --references--> `IUnitOfWork`  [INFERRED]
  docs/refactoring/phases-6-7-report.md → Src/Core/Template.Application/Interfaces/IUnitOfWork.cs
- `Errors and API results` --references--> `BaseResult`  [INFERRED]
  docs/fase-2-workspace.md → Src/Core/Template.Application/Wrappers/BaseResult.cs
- `Phase 5 — Standardize application use cases and results` --references--> `BaseResult`  [INFERRED]
  docs/fase-3-plano-implementacao.md → Src/Core/Template.Application/Wrappers/BaseResult.cs
- `Infrastructure.Identity must own` --references--> `IdentityContext`  [INFERRED]
  docs/refactoring/handoff-fase-12.md → Src/Infrastructure/Template.Infrastructure.Identity/Contexts/IdentityContext.cs

## Import Cycles
- None detected.

## Communities (159 total, 65 thin omitted)

### Community 0 - "shouldly"
Cohesion: 0.18
Nodes (8): Template.Unit.Tests.ApplicationTests.Features.Products.Queries, Template.Unit.Tests.DomainTests.Products.Entities, Template.Unit.Tests.ApplicationTests.Wrappers, Template.Unit.Tests.ApplicationTests.Features.Products.Commands, Template.Application.DTOs, Template.Domain.Products.Entities, Template.Integration.Tests.Data, Template.Application.Interfaces.Repositories

### Community 1 - "ResourceMessages"
Cohesion: 0.11
Nodes (10): Template.Infrastructure.Resources.ProjectResources, Template.Infrastructure.Resources, Template.Infrastructure.Resources.Services, ResourceMessages, Account_NotFound_with_UserName, Culture, Invalid_password, Product_NotFound_with_id (+2 more)

### Community 2 - "Person"
Cohesion: 0.13
Nodes (6): Determining the active requirement, ElasticService, Person, FirstName, Id, LastName

### Community 3 - "Container (Dockerfile)"
Cohesion: 0.05
Nodes (43): commandName, environmentVariables, launchBrowser, launchUrl, publishAllPorts, useSSL, ASPNETCORE_ENVIRONMENT, ASPNETCORE_HTTP_PORTS (+35 more)

### Community 5 - ".Handle"
Cohesion: 0.28
Nodes (4): DeleteProductCommand, Id, DeleteProductCommandHandler, DeleteProductCommandHandlerTests

### Community 7 - "Order"
Cohesion: 0.25
Nodes (6): Template.Domain.Order, Order, Id, Price, ProductName, Quantity

### Community 8 - "IClassFixture"
Cohesion: 0.15
Nodes (5): ProductCacheDisabledFunctionalTests, ProductCacheFunctionalTests, ProductCacheScenario, ProductFunctionalTests, RandomDataExtensionMethods

### Community 9 - "system"
Cohesion: 0.22
Nodes (5): Template.Infrastructure.Identity.Migrations, Template.Infrastructure.Identity.Contexts, Template.Domain.Common, Template.Infrastructure.Persistence.Migrations, InitialPostgresMigration

### Community 10 - "DistributedProductCacheTests.cs"
Cohesion: 0.25
Nodes (3): Template.Application.Interfaces.Caching, Template.Integration.Tests.Caching, Template.Infrastructure.Caching.Products

### Community 11 - "UpdateProductCommand"
Cohesion: 0.20
Nodes (7): UpdateProductCommand, BarCode, Id, Name, Price, UpdateProductCommandHandler, UpdateProductCommandHandlerTests

### Community 12 - ".GetRepository"
Cohesion: 0.17
Nodes (5): BaseEfRepoTestFixture, EfRepositoryAdd, EfRepositoryDelete, EfRepositoryUpdate, ProductPersistencePrecisionTests

### Community 13 - "ValidationBehavior"
Cohesion: 0.18
Nodes (7): ValidationBehavior, NameRequiredValidator, SampleRequest, Name, TokenCapturingValidator, CapturedToken, ValidationBehaviorTests

### Community 14 - "ApplicationDbContext"
Cohesion: 0.12
Nodes (4): ApplicationDbContext, Products, ApplicationDbContextFactory, DefaultData

### Community 15 - "Template.WebApi.csproj"
Cohesion: 0.12
Nodes (16): Asp.Versioning.Mvc.ApiExplorer, AspNetCore.HealthChecks.Elasticsearch, AspNetCore.HealthChecks.UI.Client, Elastic.Serilog.Sinks, Microsoft.AspNetCore.OpenApi, Microsoft.EntityFrameworkCore.Tools, Microsoft.VisualStudio.Azure.Containers.Tools.Targets, Serilog.AspNetCore (+8 more)

### Community 16 - "GetPagedListProductQuery"
Cohesion: 0.26
Nodes (4): GetPagedListProductQuery, Name, GetPagedListProductQueryHandler, GetPagedListProductQueryHandlerTests

### Community 18 - "PostgreSqlFixture"
Cohesion: 0.17
Nodes (7): PostgreSqlCollection, PostgreSqlFixture, TestAuthenticatedUser, UserId, UserName, PostgreSqlPersistenceTests, RabbitMqCollection

### Community 19 - "ServiceRegistrationTests.cs"
Cohesion: 0.14
Nodes (7): Template.Application, Template.Unit.Tests.ApplicationTests.Services, Template.Unit.Tests.ApplicationTests.Behaviors, Template.Application.Behaviors, Template.Application.Services.Time, Template.Unit.Tests.ApplicationTests, SystemClockTests

### Community 21 - "ProductDto"
Cohesion: 0.11
Nodes (8): ProductDto, BarCode, CreatedDateTime, Id, Name, Price, IProductService, ProductController

### Community 22 - "IAuthenticatedUserService"
Cohesion: 0.10
Nodes (14): Template.WebApi.Infrastructure.Services, Current-user and claims boundary, IAuthenticatedUserService, UserId, UserName, DesignTimeAuthenticatedUser, Instance, UserId (+6 more)

### Community 25 - "Template.Infrastructure.Identity/ServiceRegistration.cs"
Cohesion: 0.18
Nodes (6): Template.Infrastructure.Identity, Template.Functional.Tests.ApiEndpoints.v2, Template.Infrastructure.Identity.Services, Template.Application.DTOs.Account.Request, Template.Application.DTOs.Account.Response, Template.Application.Interfaces.UserInterfaces

### Community 26 - "GetProductByIdQuery"
Cohesion: 0.14
Nodes (5): GetProductByIdQuery, Id, GetProductByIdQueryHandler, GetProductByIdQueryHandlerTests, ProductServiceTests

### Community 27 - "Phase 9 Execution Handoff"
Cohesion: 0.11
Nodes (18): Acceptance criteria, Asynchronous publishing, Availability and startup, Configuration and security, Consumption, acknowledgements, and delivery, Documentation and delivery, Event contracts, Feature flag and rollback (+10 more)

### Community 28 - "Phase 3 — Incremental implementation plan"
Cohesion: 0.08
Nodes (23): Decisions (recorded 2026-10-10), Global execution rules, Phase 0 — Freeze a reproducible baseline, Phase 10 — Move Elastic concerns behind infrastructure boundaries, Phase 11 — Upgrade the Elastic client and server stack, Phase 12 — Harden identity and JWT boundaries, Phase 14 — Establish a reproducible Compose baseline, Phase 15 — Consolidate Redis services on Redis 8 (+15 more)

### Community 29 - "Template.Application.csproj"
Cohesion: 0.14
Nodes (13): Elastic.Clients.Elasticsearch, FluentValidation.DependencyInjectionExtensions, MediatR, Microsoft.Extensions.Logging.Abstractions, Microsoft.Extensions.Options, net10.0, Microsoft.NET.Sdk, Template.Domain (+5 more)

### Community 30 - "BaseResult"
Cohesion: 0.10
Nodes (18): AuthenticationRequest, Password, UserName, AuthenticationRequestValidator, AuthenticationResponse, Email, Id, IsVerified (+10 more)

### Community 31 - "CreateProductCommand"
Cohesion: 0.20
Nodes (8): CreateProductCommand, BarCode, Name, Price, CreateProductCommandHandler, CreateProductCommandValidator, UpdateProductCommandValidator, CreateProductCommandHandlerTests

### Community 32 - "IProductCache"
Cohesion: 0.10
Nodes (16): Current solution structure, Phase 8 — Move Redis caching behind an infrastructure adapter, Application, Infrastructure — `Template.Infrastructure.Caching` (novo), Não verificado e riscos, O que mudou, OpenAPI e baseline (`docs/refactoring/baseline-fase-6`), Regras de arquitetura (+8 more)

### Community 33 - "Template.Application.Wrappers"
Cohesion: 0.25
Nodes (7): Template.Application.Wrappers, Template.Application.Features.Products.Queries, Template.Application.DTOs.Products, Template.Application.Helpers, Template.Application.Features.Products.Commands, Template.Application.Services.Product, Regexs

### Community 36 - "Program.cs"
Cohesion: 0.09
Nodes (9): Template.Infrastructure.Persistence, Template.WebApi.Infrastructure.Middlewares, Template.WebApi.Controllers.v2, Template.WebApi.Infrastructure.Http, Template.Infrastructure.Persistence.Seeds, Template.WebApi.Infrastructure.Extensions, Template.Infrastructure.Caching, Template.WebApi.Controllers.v1 (+1 more)

### Community 37 - "Fase 5 em detalhe"
Cohesion: 0.24
Nodes (9): Fase 5 em detalhe, ErrorCategory, Conflict, Forbidden, NotFound, Unauthorized, Unexpected, Validation (+1 more)

### Community 40 - "RabbitMqTestSupport.cs"
Cohesion: 0.12
Nodes (14): Template.Integration.Tests.Messaging, Template.Infrastructure.Messaging.Publishing, Template.Infrastructure.Messaging.Serialization, Template.Application.Features.Orders.EventHandlers, Template.Application.Interfaces.Messaging, Template.Application.Messaging.Orders, Template.Unit.Tests.ApplicationTests.Features.Orders, Template.Infrastructure.Messaging.Topology (+6 more)

### Community 41 - "AccountServices"
Cohesion: 0.08
Nodes (16): Phase 13 — Standardize observability, health, and localization, Current-state characterization, ITranslator, IGetUserServices, ApplicationUser, Created, Name, ServiceRegistration (+8 more)

### Community 42 - "TemplateCleanArchitecture.sln"
Cohesion: 0.20
Nodes (3): Template, Microsoft.VisualStudio.SDK, Microsoft.VSSDK.BuildTools

### Community 43 - "5. Target code standards"
Cohesion: 0.08
Nodes (23): 1. Evidence and assumptions, 2. Proposed AI workspace, 3. Dependency and integration update matrix, 4. Target Clean Architecture, 5. Target code standards, Application use cases, Controllers, Design principles (+15 more)

### Community 44 - "Template.Application.Interfaces"
Cohesion: 0.18
Nodes (5): Template.Infrastructure.Persistence.Repositories, Template.Application.Interfaces, Template.PostgreSql.Integration.Tests, Template.Infrastructure.Persistence.Contexts, Template.Infrastructure.Persistence.Extensions

### Community 45 - "Template.Functional.Tests.csproj"
Cohesion: 0.20
Nodes (9): Microsoft.AspNetCore.Mvc.Testing, net10.0, coverlet.collector, Microsoft.Extensions.Hosting.Abstractions, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio (+1 more)

### Community 46 - "TemplateCleanArchNET agent guide"
Cohesion: 0.18
Nodes (9): Change and validation rules, Current configuration names, Execution status, Repository purpose, TemplateCleanArchNET agent guide, Verified commands (2026-10-10, Phase 8 checkpoint), LocalizationSettings, DefaultRequestCulture (+1 more)

### Community 47 - "ProductEntity"
Cohesion: 0.14
Nodes (7): Template.Infrastructure.Persistence.Contexts.Configurations, ProductEntity, BarCode, Name, Price, ProductConfiguration, ProductTests

### Community 48 - "IdentitySettings"
Cohesion: 0.22
Nodes (7): Template.Infrastructure.Identity.Settings, IdentitySettings, PasswordRequireDigit, PasswordRequiredLength, PasswordRequireLowercase, PasswordRequireNonAlphanumeric, PasswordRequireUppercase

### Community 50 - "Template.Infrastructure.Identity"
Cohesion: 0.25
Nodes (8): Microsoft.AspNetCore.Authentication.JwtBearer, Microsoft.AspNetCore.Identity.EntityFrameworkCore, System.IdentityModel.Tokens.Jwt, Template.Infrastructure.Identity, net10.0, Microsoft.EntityFrameworkCore.InMemory, Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.NET.Sdk

### Community 51 - "Relatório das Fases 6–7 — 2026-10-10"
Cohesion: 0.15
Nodes (12): Arquivos alterados nesta execução, Automação e documentação, Comandos e resultados, Confirmação de escopo, Contrato e WebApi, Fase 6 — controllers e contratos HTTP, Migrations e SQL, Persistência e testes (+4 more)

### Community 54 - ".Decode"
Cohesion: 0.09
Nodes (14): DecodeResult, Incompatible, Invalid, DecodeStatus, Decoded, IncompatibleContract, InvalidPayload, OrderPlacedMessageSerializer (+6 more)

### Community 55 - "Template.Infrastructure.Persistence.csproj"
Cohesion: 0.22
Nodes (8): Microsoft.EntityFrameworkCore, Microsoft.Extensions.Configuration, net10.0, Microsoft.EntityFrameworkCore.Design, Microsoft.EntityFrameworkCore.InMemory, Microsoft.Extensions.DependencyInjection, Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.NET.Sdk

### Community 56 - "Template.Unit.Tests.csproj"
Cohesion: 0.22
Nodes (8): Moq, net10.0, coverlet.collector, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk

### Community 57 - "Required tests"
Cohesion: 0.40
Nodes (5): Architecture tests, Functional tests, Required tests, Tests with a real broker, Unit tests

### Community 58 - "Product"
Cohesion: 0.24
Nodes (5): Account, ApiRoutes, Product, V1, V2

### Community 59 - "Phase 11 Execution Handoff"
Cohesion: 0.06
Nodes (30): Acceptance criteria, Adapter contract tests, Checkout and working tree, Current-state inventory, Data safety and destructive-action boundaries, Deprecation and upgrade checks, Documentation and delivery, Elasticsearch and Kibana images (+22 more)

### Community 63 - "Template.Integration.Tests.csproj"
Cohesion: 0.14
Nodes (12): Microsoft.Extensions.Caching.Memory, Microsoft.Extensions.Caching.StackExchangeRedis, net10.0, Microsoft.Extensions.Options.ConfigurationExtensions, Microsoft.NET.Sdk, net10.0, coverlet.collector, Microsoft.NET.Test.Sdk (+4 more)

### Community 64 - "ScriptedHandler"
Cohesion: 0.23
Nodes (5): CountingStore, Marks, OrderDeliveryProcessorTests, ScriptedHandler, Calls

### Community 66 - "MessageFunctionalTests.cs"
Cohesion: 0.15
Nodes (3): Template.Functional.Tests.ApiEndpoints, Template.WebApi.Infrastructure.Filters, Template.Functional.Tests.Common

### Community 69 - "Template.Architecture.Tests.csproj"
Cohesion: 0.29
Nodes (6): net10.0, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk

### Community 70 - "PaginationResponseDto"
Cohesion: 0.18
Nodes (5): PaginationResponseDto, Count, Data, PageNumber, PageSize

### Community 71 - "Fases 6–7 — bloqueio de pré-condições (2026-10-10)"
Cohesion: 0.33
Nodes (5): Causa do bloqueio, Como desbloquear, Estado do Git registrado, Fases 6–7 — bloqueio de pré-condições (2026-10-10), Pré-condições

### Community 73 - "ElasticSearchSettings"
Cohesion: 0.09
Nodes (15): Baseline validation before refactoring, Dependency and vulnerability baseline, Phase 0 baseline — 2026-10-10, Source state, Toolchain, Configuration and security, Separation between search, logging, and health checks, ElasticSearchSettings (+7 more)

### Community 74 - "RabbitMqOrderConsumerService"
Cohesion: 0.17
Nodes (4): ReceivedDelivery, RabbitMqOrderConsumerService, OrderSubscription, DeadLetterQueue

### Community 81 - "AuditableBaseEntity"
Cohesion: 0.20
Nodes (8): Current implementation conventions, AuditableBaseEntity, Created, CreatedBy, LastModified, LastModifiedBy, BaseEntity, Id

### Community 85 - "handoff-fases-6-7.md"
Cohesion: 0.17
Nodes (9): Template.WebApi.Controllers, Entrega, Estado de partida (verificado em 2026-10-10 às 14:26), Execução de comandos, Fase 6 — controllers e contratos HTTP, Fase 7 — persistência e PostgreSQL (somente depois da Fase 6 aprovada), Fontes obrigatórias (ler antes de alterar qualquer arquivo), Regras gerais (+1 more)

### Community 100 - "Contexto técnico relevante"
Cohesion: 0.09
Nodes (8): Contexto técnico relevante, Fase 7 — persistência e PostgreSQL, IGenericRepository, IProductRepository, GenericRepository, DbContext, ProductRepository, ServiceRegistration

### Community 101 - ".ProcessAsync"
Cohesion: 0.10
Nodes (9): DeliveryAttemptTracker, DeliveryAction, Ack, DeadLetter, Requeue, DeliveryDecision, Ack, Requeue (+1 more)

### Community 102 - "ErrorCode"
Cohesion: 0.18
Nodes (7): ErrorCode, AccessDenied, ErrorInIdentity, Exception, FieldDataInvalid, ModelStateNotValid, NotFound

### Community 104 - ".AddApplicationLayer"
Cohesion: 0.17
Nodes (7): Time handling, Current state to characterize, IClock, UtcNow, ServiceRegistration, SystemClock, UtcNow

### Community 105 - "AccountController"
Cohesion: 0.17
Nodes (5): ChangeUserNameRequest, UserName, ChangeUserNameRequestValidator, IAccountServices, AccountController

### Community 106 - "Relatório das Fases 0–5 — 2026-10-10"
Cohesion: 0.22
Nodes (8): Comparação de contrato, Confirmação, Mudanças por fase, Relatório das Fases 0–5 — 2026-10-10, Revisão da migration e do SQL (Fase 4), Riscos e pendências, Testes e gates (execução de 2026-10-10, 14:25–14:26), Versões e fontes oficiais

### Community 107 - ".Handle"
Cohesion: 0.20
Nodes (4): PublishOrderCommand, PublishOrderCommandHandler, PublishOrderCommandHandlerTests, Handler

### Community 108 - "Template.PostgreSql.Integration.Tests.csproj"
Cohesion: 0.25
Nodes (7): Testcontainers.PostgreSql, net10.0, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk

### Community 109 - ".HandleAsync"
Cohesion: 0.16
Nodes (4): OrderPlacedEventHandler, IProcessedMessageStore, OrderPlacedEventHandlerTests, Handler

### Community 110 - "RabbitMqSettings"
Cohesion: 0.09
Nodes (15): RabbitMqSettings, ConnectionTimeout, ConsumersEnabled, Enabled, HostName, MaxDeliveryAttempts, MaxReconnectDelay, Password (+7 more)

### Community 111 - "Phase 12 Execution Handoff"
Cohesion: 0.09
Nodes (21): 401 and 403 behavior, Account-use-case standardization, Checkout and working tree, Documentation and delivery, HTTP and API compatibility, HTTPS metadata policy, Identity schema and migrations, JWT issuance and validation (+13 more)

### Community 112 - "Phase 10 Execution Handoff"
Cohesion: 0.11
Nodes (18): Acceptance criteria, Availability policy, Checkout and working tree, Contracts and indexed documents, Documentation and delivery, Feature flag and rollback, Health check, Infrastructure adapter (+10 more)

### Community 115 - "AccountController"
Cohesion: 0.20
Nodes (5): ChangePasswordRequest, ConfirmPassword, Password, ChangePasswordRequestValidator, AccountController

### Community 117 - "IElasticService"
Cohesion: 0.24
Nodes (6): Application port, Current state to characterize, HTTP compatibility, IElasticService, PersonController, NotFound

### Community 118 - "CacheSettings"
Cohesion: 0.13
Nodes (5): CacheSettings, AbsoluteExpiration, Enabled, OperationTimeout, SlidingExpiration

### Community 120 - "RecordingHandler"
Cohesion: 0.18
Nodes (3): RecordingHandler, Behavior, Calls

### Community 121 - "RabbitMqFixture"
Cohesion: 0.18
Nodes (4): RabbitMqFixture, Container, Host, Port

### Community 124 - "CustomWebApplicationFactory"
Cohesion: 0.13
Nodes (3): ExternalServicesUnavailableTests, CustomWebApplicationFactory, AdditionalConfiguration

### Community 125 - "Template.Infrastructure.Messaging.csproj"
Cohesion: 0.13
Nodes (13): RabbitMQ.Client, Testcontainers.RabbitMq, net10.0, Microsoft.Extensions.Hosting.Abstractions, Microsoft.Extensions.Options.ConfigurationExtensions, Microsoft.NET.Sdk, net10.0, Microsoft.Extensions.DependencyInjection (+5 more)

### Community 126 - "RecordingOrderEventPublisher"
Cohesion: 0.19
Nodes (6): RecordingOrderEventPublisher, BlockUntilCancelled, FailWith, ObservedCancellation, Published, Started

### Community 127 - "MessagingHarness"
Cohesion: 0.15
Nodes (4): CapturedLog, MessagingHarness, Consumer, Publisher

### Community 128 - ".GetPagedUsers"
Cohesion: 0.18
Nodes (7): Template.Application.Parameters, GetAllUsersRequest, Name, PaginationRequestParameter, PageNumber, PageSize, GetUserServices

### Community 129 - "IdentityContext"
Cohesion: 0.17
Nodes (3): Acceptance criteria, Architecture tests, IdentityContext

### Community 130 - "TranslatorMessageDto"
Cohesion: 0.18
Nodes (6): TranslatorMessageDto, Args, Text, AccountMessages, ProductMessages, TranslatorMessages

### Community 131 - "Template.Infrastructure.Identity.Models"
Cohesion: 0.23
Nodes (4): Template.Infrastructure.Identity.Seeds, Template.Infrastructure.Identity.Models, DefaultBasicUser, DefaultRoles

### Community 132 - "Program"
Cohesion: 0.17
Nodes (5): Program, MessageWithoutBrokerFunctionalTests, CacheDisabledFactory, AdditionalConfiguration, ProductValidationFunctionalTests

### Community 133 - "ResourceGeneral"
Cohesion: 0.18
Nodes (9): ResourceGeneral, BarCode, ConfirmPassword, Culture, Id, Name, Password, ResourceManager (+1 more)

### Community 136 - "UserDto"
Cohesion: 0.20
Nodes (7): UserDto, Created, Email, Id, Name, PhoneNumber, UserName

### Community 139 - "ApiErrorItem"
Cohesion: 0.28
Nodes (7): Diff de contrato e baseline, ApiErrorItem, Code, Field, Message, ApiErrorResponse, Errors

### Community 141 - "PagedResponse"
Cohesion: 0.22
Nodes (5): PagedResponse, PageNumber, PageSize, TotalItems, TotalPages

### Community 144 - "ErrorCodeEnum"
Cohesion: 0.25
Nodes (7): Template.WebApi.Infrastructure.Enums, ErrorCodeEnum, Conflict, Forbidden, Unauthorized, Unexpected, Validation

### Community 145 - "Required tests"
Cohesion: 0.25
Nodes (8): Adapter tests, Architecture tests, Functional tests, Health-check tests, Logging tests, Required tests, Tests with real Elasticsearch, Unit tests

### Community 147 - "Required tests"
Cohesion: 0.29
Nodes (7): Account functional tests, Authorization tests, JWT validation tests, Options and startup tests, Persistence and migration tests, Required tests, Unit tests

### Community 149 - "CountingProcessedMessageStore"
Cohesion: 0.33
Nodes (3): CountingProcessedMessageStore, Checks, Marks

### Community 152 - "Error"
Cohesion: 0.33
Nodes (4): Error, Description, ErrorCode, FieldName

### Community 153 - "Architecture boundaries"
Cohesion: 0.40
Nodes (5): Application may own, Architecture boundaries, Domain must remain free of, Infrastructure.Identity must own, WebApi may own

### Community 155 - "OrderDispatch"
Cohesion: 0.40
Nodes (4): OrderDispatch, Broadcast, Direct, Topic

## Knowledge Gaps
- **516 isolated node(s):** `Microsoft.VisualStudio.SDK`, `Microsoft.VSSDK.BuildTools`, `UserName`, `Password`, `Password` (+511 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 945 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **65 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `BaseResult` connect `BaseResult` to `.ToActionResult`, `.Handle`, `ErrorCategoryTests`, `IClassFixture`, `UpdateProductCommand`, `PagedResponse`, `ProductDto`, `.GetProductAsync`, `Error`, `GetProductByIdQuery`, `Phase 3 — Incremental implementation plan`, `CreateProductCommand`, `Template.Application.Wrappers`, `Fase 5 em detalhe`, `AccountServices`, `5. Target code standards`, `Phase6HttpContractFunctionalTests`, `AuditableBaseEntity`, `Contexto técnico relevante`, `AccountController`, `ApiResultMapper`?**
  _High betweenness centrality (0.099) - this node is a cross-community bridge._
- **Are the 4 inferred relationships involving `BaseResult` (e.g. with `Current implementation conventions` and `Errors and API results`) actually correct?**
  _`BaseResult` has 4 INFERRED edges - model-reasoned connections that need verification._
- **What connects `Microsoft.VisualStudio.SDK`, `Microsoft.VSSDK.BuildTools`, `UserName` to the rest of the system?**
  _516 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `ResourceMessages` be split into smaller, more focused modules?**
  _Cohesion score 0.1111111111111111 - nodes in this community are weakly interconnected._
- **Why does `OrderPlacedEvent` connect `OrderPlacedEvent` to `ScriptedHandler`, `MessagePublishException`, `.Handle`, `.HandleAsync`, `BrokerAvailabilityTests`, `OrderMessagingBrokerTests`, `.Decode`, `IncomingMessageContext`, `IOrderEventPublisher`, `.PublishBroadcastAsync`, `.HandleAsync`, `RecordingHandler`, `RecordingOrderEventPublisher`?**
  _High betweenness centrality (0.048) - this node is a cross-community bridge._
- **Are the 6 inferred relationships involving `ProductDto` (e.g. with `Immediate boundary corrections` and `Phase 4 — Clean the Domain model`) actually correct?**
  _`ProductDto` has 6 INFERRED edges - model-reasoned connections that need verification._
- **Should `Person` be split into smaller, more focused modules?**
  _Cohesion score 0.1339031339031339 - nodes in this community are weakly interconnected._