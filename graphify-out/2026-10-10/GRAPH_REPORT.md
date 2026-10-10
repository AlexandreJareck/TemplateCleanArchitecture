# Graph Report - TemplateCleanArchNET  (2026-10-10)

## Corpus Check
- 269 files · ~82,204 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 14 file(s) not represented in the graph (top: (none) 4, .resx 4, .jsonl 2)

## Summary
- 2792 nodes · 6064 edges · 169 communities (105 shown, 64 thin omitted)
- Extraction: 90% EXTRACTED · 10% INFERRED · 0% AMBIGUOUS · INFERRED: 604 edges (avg confidence: 0.88)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `8bd25482`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- Template.Integration.Tests.Data
- ResourceMessages
- Person
- Container (Dockerfile)
- .ToActionResult
- .Handle
- ErrorCategoryTests
- RecordingOrderEventPublisher
- IClassFixture
- system
- DistributedProductCacheTests.cs
- UpdateProductCommand
- .GetRepository
- ValidationBehavior
- ApplicationDbContext
- Template.WebApi.csproj
- PagedResponse
- DistributedProductCacheTests
- PostgreSqlFixture
- Template.Application.Interfaces
- OrderMessagingBrokerTests
- ProductController
- ProductFunctionalTests
- ArchitectureRulesTests
- ProductDto
- IProductCache
- GetProductByIdQuery
- Phase 9 Execution Handoff
- Phase 3 — Incremental implementation plan
- Template.Application.csproj
- AuthenticationRequest
- CreateProductCommand
- Relatório da Fase 9 — 2026-10-10
- AuthenticationResponse
- .BuildModel
- .ConfigureLogger
- Template.Application.Wrappers
- Fase 5 em detalhe
- ConfigureSwaggerOptions
- PersonSearchSettings
- RabbitMqTestSupport.cs
- BaseResult
- TemplateCleanArchitecture.sln
- 5. Target code standards
- microsoft_entityframeworkcore
- Template.Functional.Tests.csproj
- GetProductByIdQueryHandler
- ProductEntity
- IdentitySettings
- HttpClientGetExtensionMethods
- Template.Infrastructure.Identity
- Phase 0 baseline — 2026-10-10
- UnitOfWork
- .ProcessAsync
- .Decode
- Template.Infrastructure.Persistence.csproj
- Template.Unit.Tests.csproj
- IdentityContext
- Product
- Phase 11 Execution Handoff
- InitialPostgresMigration
- IndexedPerson
- IOrderEventPublisher
- Template.Integration.Tests.csproj
- ScriptedHandler
- InitialPostgresMigration
- shouldly
- ProductPriceDecimal
- Phase6HttpContractFunctionalTests
- Template.Architecture.Tests.csproj
- Program.cs
- Fases 6–7 — bloqueio de pré-condições (2026-10-10)
- PersonUseCaseHandlerTests
- IPersonSearchIndex
- RabbitMqOrderConsumerService
- contract-checklist.md
- boundaries.md
- verification-matrix.md
- postgresql-review.md
- adapter-checklist.md
- upgrade-record.md
- RecordingPersonSearchIndex
- ServiceRegistrationTests
- init-db.sh
- skill-activation-report.md
- Relatório das Fases 6–7 — 2026-10-10
- Core/AGENTS.md
- Infrastructure/AGENTS.md
- Presentation/AGENTS.md
- Tests/AGENTS.md
- Contexto técnico relevante
- .AddMessagingInfrastructure
- PersonIndexStatus
- OrderPlacedEvent
- IClock
- ChangePasswordRequest
- Relatório das Fases 0–5 — 2026-10-10
- .Handle
- Template.PostgreSql.Integration.Tests.csproj
- OrderPlacedEventHandlerTests
- RabbitMqSettings
- Phase 12 Execution Handoff
- Phase 10 Execution Handoff
- BrokerAvailabilityTests
- MessageFunctionalTests
- ElasticsearchHealthCheckTests
- api-v1-lifecycle.md
- PersonController
- GetAllUsersRequest
- PersonIndexResult
- RecordingHandler
- RabbitMqFixture
- PersonSearchIndexElasticsearchTests
- PersonBulkIndexResult
- CustomWebApplicationFactory
- Template.Infrastructure.Messaging.csproj
- ElasticsearchHealthCheck
- MessagingHarness
- UserDto
- Template.Infrastructure.Identity.Models
- TranslatorMessageDto
- ElasticLoggingTests
- ResourceGeneral
- .Log
- MessagePublishException
- IAuthenticatedUserService
- Order
- IAsyncLifetime
- Template.Infrastructure.Search.csproj
- Required tests
- Current solution structure
- ElasticLoggingSettings
- ElasticLogSinkDiagnostics
- ErrorCodeEnum
- Required tests
- microsoft_extensions_dependencyinjection
- ElasticsearchFixture
- .DeclareAsync
- CountingProcessedMessageStore
- CapturingLogger
- ElasticsearchHealthCheckSettings
- Error
- UnreachableLogSinkFactory
- .UnitOfWork_SaveChangesAsync_HonorsCancelledToken
- ApiErrorItem
- ApplicationDbContext.cs
- .AddIdentityInfrastructure
- CapturingSink
- DecodeResult
- ManualTimeProvider
- ThrowingWriter
- CorsExtensions
- Availability policy
- .AddCustomLocalization
- ApplicationUser
- PersonDocument
- Architecture boundaries

## God Nodes (most connected - your core abstractions)
1. `BaseResult` - 76 edges
2. `IndexedPerson` - 58 edges
3. `ProductDto` - 50 edges
4. `OrderPlacedEvent` - 46 edges
5. `PersonIndexResult` - 45 edges
6. `ArchitectureRulesTests` - 41 edges
7. `ProductEntity` - 40 edges
8. `Template.Application.Wrappers` - 36 edges
9. `ElasticsearchPersonSearchIndexTests` - 36 edges
10. `Current solution structure` - 32 edges

## Surprising Connections (you probably didn't know these)
- `Phase 13 — Standardize observability, health, and localization` --references--> `ITranslator`  [INFERRED]
  docs/fase-3-plano-implementacao.md → Src/Core/Template.Application/Interfaces/ITranslator.cs
- `Riscos, pendências e rollback` --references--> `IUnitOfWork`  [INFERRED]
  docs/refactoring/phases-6-7-report.md → Src/Core/Template.Application/Interfaces/IUnitOfWork.cs
- `Errors and API results` --references--> `BaseResult`  [INFERRED]
  docs/fase-2-workspace.md → Src/Core/Template.Application/Wrappers/BaseResult.cs
- `Phase 5 — Standardize application use cases and results` --references--> `BaseResult`  [INFERRED]
  docs/fase-3-plano-implementacao.md → Src/Core/Template.Application/Wrappers/BaseResult.cs
- `Architecture tests` --references--> `IdentityContext`  [INFERRED]
  docs/refactoring/handoff-fase-12.md → Src/Infrastructure/Template.Infrastructure.Identity/Contexts/IdentityContext.cs

## Import Cycles
- None detected.

## Communities (169 total, 64 thin omitted)

### Community 1 - "ResourceMessages"
Cohesion: 0.17
Nodes (8): Template.Infrastructure.Resources.ProjectResources, ResourceMessages, Account_NotFound_with_UserName, Culture, Invalid_password, Product_NotFound_with_id, ResourceManager, Username_is_already_taken

### Community 2 - "Person"
Cohesion: 0.29
Nodes (6): Template.Domain.Person, Determining the active requirement, Person, FirstName, Id, LastName

### Community 3 - "Container (Dockerfile)"
Cohesion: 0.05
Nodes (43): commandName, environmentVariables, launchBrowser, launchUrl, publishAllPorts, useSSL, ASPNETCORE_ENVIRONMENT, ASPNETCORE_HTTP_PORTS (+35 more)

### Community 4 - ".ToActionResult"
Cohesion: 0.07
Nodes (4): AccountController, ProductController, ApiResultMapper, ProblemDetailsAuthorizationMiddlewareResultHandler

### Community 5 - ".Handle"
Cohesion: 0.16
Nodes (7): DeleteProductCommand, Id, DeleteProductCommandHandler, ITranslator, ServiceRegistration, Translator, DeleteProductCommandHandlerTests

### Community 7 - "RecordingOrderEventPublisher"
Cohesion: 0.13
Nodes (11): Ports e contratos criados (Application), OrderDispatch, Broadcast, Direct, Topic, RecordingOrderEventPublisher, BlockUntilCancelled, FailWith (+3 more)

### Community 8 - "IClassFixture"
Cohesion: 0.27
Nodes (3): ProductCacheDisabledFunctionalTests, ProductCacheFunctionalTests, ProductCacheScenario

### Community 9 - "system"
Cohesion: 0.27
Nodes (4): Template.Infrastructure.Identity.Migrations, Template.Infrastructure.Identity.Contexts, Template.Domain.Order, Template.Infrastructure.Persistence.Migrations

### Community 10 - "DistributedProductCacheTests.cs"
Cohesion: 0.25
Nodes (3): Template.Integration.Tests.Caching, Template.Infrastructure.Caching, Template.Infrastructure.Caching.Products

### Community 11 - "UpdateProductCommand"
Cohesion: 0.22
Nodes (7): UpdateProductCommand, BarCode, Id, Name, Price, UpdateProductCommandHandler, UpdateProductCommandHandlerTests

### Community 12 - ".GetRepository"
Cohesion: 0.18
Nodes (4): BaseEfRepoTestFixture, EfRepositoryAdd, EfRepositoryDelete, EfRepositoryUpdate

### Community 13 - "ValidationBehavior"
Cohesion: 0.08
Nodes (23): Change and validation rules, Current implementation conventions, Execution status, Repository purpose, TemplateCleanArchNET agent guide, Verified commands (2026-10-10, Phase 9 checkpoint), ValidationBehavior, AuthenticationRequestValidator (+15 more)

### Community 14 - "ApplicationDbContext"
Cohesion: 0.14
Nodes (4): ApplicationDbContext, Products, ApplicationDbContextFactory, DefaultData

### Community 15 - "Template.WebApi.csproj"
Cohesion: 0.12
Nodes (15): Asp.Versioning.Mvc.ApiExplorer, AspNetCore.HealthChecks.UI.Client, Elastic.Serilog.Sinks, Microsoft.AspNetCore.OpenApi, Microsoft.EntityFrameworkCore.Tools, Microsoft.VisualStudio.Azure.Containers.Tools.Targets, Serilog.AspNetCore, Serilog.Enrichers.ClientInfo (+7 more)

### Community 16 - "PagedResponse"
Cohesion: 0.09
Nodes (14): PaginationResponseDto, Count, Data, PageNumber, PageSize, GetPagedListProductQuery, Name, GetPagedListProductQueryHandler (+6 more)

### Community 17 - "DistributedProductCacheTests"
Cohesion: 0.06
Nodes (9): CacheSettings, AbsoluteExpiration, Enabled, OperationTimeout, SlidingExpiration, DistributedProductCache, DistributedProductCacheTests, FailingDistributedCache (+1 more)

### Community 18 - "PostgreSqlFixture"
Cohesion: 0.20
Nodes (5): PostgreSqlFixture, TestAuthenticatedUser, UserId, UserName, PostgreSqlPersistenceTests

### Community 19 - "Template.Application.Interfaces"
Cohesion: 0.09
Nodes (20): Template.Unit.Tests.ApplicationTests.Features.Products.Queries, Template.Application.Interfaces, Template.Application, Template.Application.Features.Products.Queries, Template.Unit.Tests.ApplicationTests.Services, Template.Application.DTOs.Products, Template.Unit.Tests.ApplicationTests.Features.Products.Commands, Template.Unit.Tests.ApplicationTests.Behaviors (+12 more)

### Community 24 - "ProductDto"
Cohesion: 0.14
Nodes (7): Phase 4 — Clean the Domain model, ProductDto, BarCode, CreatedDateTime, Id, Name, Price

### Community 25 - "IProductCache"
Cohesion: 0.09
Nodes (16): Phase 8 — Move Redis caching behind an infrastructure adapter, Application, Infrastructure — `Template.Infrastructure.Caching` (novo), Não verificado e riscos, O que mudou, OpenAPI e baseline (`docs/refactoring/baseline-fase-6`), Regras de arquitetura, Relatório da Fase 8 — 2026-10-10 (+8 more)

### Community 26 - "GetProductByIdQuery"
Cohesion: 0.25
Nodes (3): GetProductByIdQuery, Id, ProductServiceTests

### Community 27 - "Phase 9 Execution Handoff"
Cohesion: 0.08
Nodes (23): Acceptance criteria, Architecture tests, Asynchronous publishing, Availability and startup, Configuration and security, Consumption, acknowledgements, and delivery, Documentation and delivery, Event contracts (+15 more)

### Community 28 - "Phase 3 — Incremental implementation plan"
Cohesion: 0.08
Nodes (23): Decisions (recorded 2026-10-10), Global execution rules, Phase 0 — Freeze a reproducible baseline, Phase 10 — Move Elastic concerns behind infrastructure boundaries, Phase 11 — Upgrade the Elastic client and server stack, Phase 12 — Harden identity and JWT boundaries, Phase 13 — Standardize observability, health, and localization, Phase 14 — Establish a reproducible Compose baseline (+15 more)

### Community 29 - "Template.Application.csproj"
Cohesion: 0.15
Nodes (12): FluentValidation.DependencyInjectionExtensions, MediatR, Microsoft.Extensions.Options, net10.0, Microsoft.Extensions.Logging.Abstractions, Microsoft.NET.Sdk, Template.Domain, net10.0 (+4 more)

### Community 30 - "AuthenticationRequest"
Cohesion: 0.31
Nodes (4): AuthenticationRequest, Password, UserName, AccountFunctionalTests

### Community 31 - "CreateProductCommand"
Cohesion: 0.26
Nodes (6): CreateProductCommand, BarCode, Name, Price, CreateProductCommandHandler, CreateProductCommandHandlerTests

### Community 32 - "Relatório da Fase 9 — 2026-10-10"
Cohesion: 0.12
Nodes (15): Current state to characterize, Adapters e hosted services (Infrastructure.Messaging), APIs 7.x usadas, Configuração e DI, Confirmações, Contrato `order.placed` v1, Gates não executados, Política de ack, retry, poison e idempotência (+7 more)

### Community 33 - "AuthenticationResponse"
Cohesion: 0.14
Nodes (9): AuthenticationResponse, Email, Id, IsVerified, JwToken, Roles, UserName, AccountFunctionalTests (+1 more)

### Community 36 - "Template.Application.Wrappers"
Cohesion: 0.13
Nodes (10): Template.Application.Wrappers, Template.WebApi.Infrastructure.Filters, Template.Functional.Tests.ApiEndpoints.v2, Template.WebApi.Controllers.v2, Template.Infrastructure.Identity.Services, Template.Application.DTOs.Account.Request, Template.WebApi.Infrastructure.Http, Template.Application.DTOs.Account.Response (+2 more)

### Community 37 - "Fase 5 em detalhe"
Cohesion: 0.10
Nodes (17): Fase 5 em detalhe, ErrorCategory, Conflict, Forbidden, NotFound, Unauthorized, Unexpected, Validation (+9 more)

### Community 39 - "PersonSearchSettings"
Cohesion: 0.15
Nodes (6): PersonSearchSettings, Enabled, IndexName, RequestTimeout, Url, ServiceRegistration

### Community 40 - "RabbitMqTestSupport.cs"
Cohesion: 0.13
Nodes (14): Template.Integration.Tests.Messaging, Template.Infrastructure.Messaging.Publishing, Template.Infrastructure.Messaging.Serialization, Template.Application.Features.Orders.EventHandlers, Template.Application.Interfaces.Messaging, Template.Application.Messaging.Orders, Template.Unit.Tests.ApplicationTests.Features.Orders, Template.Infrastructure.Messaging.Topology (+6 more)

### Community 41 - "BaseResult"
Cohesion: 0.15
Nodes (9): ChangeUserNameRequest, UserName, ChangeUserNameRequestValidator, IAccountServices, BaseResult, Data, Errors, Success (+1 more)

### Community 42 - "TemplateCleanArchitecture.sln"
Cohesion: 0.20
Nodes (3): Template, Microsoft.VisualStudio.SDK, Microsoft.VSSDK.BuildTools

### Community 43 - "5. Target code standards"
Cohesion: 0.08
Nodes (23): 1. Evidence and assumptions, 2. Proposed AI workspace, 3. Dependency and integration update matrix, 4. Target Clean Architecture, 5. Target code standards, Application use cases, Controllers, Design principles (+15 more)

### Community 44 - "microsoft_entityframeworkcore"
Cohesion: 0.18
Nodes (5): Template.Infrastructure.Persistence.Repositories, Template.PostgreSql.Integration.Tests, Template.Infrastructure.Persistence.Contexts, PostgreSqlCollection, RabbitMqCollection

### Community 45 - "Template.Functional.Tests.csproj"
Cohesion: 0.20
Nodes (9): Microsoft.AspNetCore.Mvc.Testing, net10.0, coverlet.collector, Microsoft.Extensions.Hosting.Abstractions, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio (+1 more)

### Community 47 - "ProductEntity"
Cohesion: 0.14
Nodes (7): Template.Infrastructure.Persistence.Contexts.Configurations, ProductEntity, BarCode, Name, Price, ProductConfiguration, ProductTests

### Community 48 - "IdentitySettings"
Cohesion: 0.11
Nodes (16): Current configuration names, Template.Infrastructure.Identity.Settings, LocalizationSettings, DefaultRequestCulture, SupportedCultures, IdentitySettings, PasswordRequireDigit, PasswordRequiredLength (+8 more)

### Community 50 - "Template.Infrastructure.Identity"
Cohesion: 0.25
Nodes (8): Microsoft.AspNetCore.Authentication.JwtBearer, Microsoft.AspNetCore.Identity.EntityFrameworkCore, System.IdentityModel.Tokens.Jwt, Template.Infrastructure.Identity, net10.0, Microsoft.EntityFrameworkCore.InMemory, Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.NET.Sdk

### Community 51 - "Phase 0 baseline — 2026-10-10"
Cohesion: 0.33
Nodes (5): Baseline validation before refactoring, Dependency and vulnerability baseline, Phase 0 baseline — 2026-10-10, Source state, Toolchain

### Community 53 - ".ProcessAsync"
Cohesion: 0.20
Nodes (7): DeliveryAction, Ack, DeadLetter, Requeue, DeliveryDecision, Ack, Requeue

### Community 54 - ".Decode"
Cohesion: 0.14
Nodes (7): OrderPlacedMessageSerializer, OrderPlacedMessageV1, Id, Price, ProductName, Quantity, OrderPlacedMessageSerializerTests

### Community 55 - "Template.Infrastructure.Persistence.csproj"
Cohesion: 0.22
Nodes (8): Microsoft.EntityFrameworkCore, Microsoft.Extensions.Configuration, net10.0, Microsoft.EntityFrameworkCore.Design, Microsoft.EntityFrameworkCore.InMemory, Microsoft.Extensions.DependencyInjection, Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.NET.Sdk

### Community 56 - "Template.Unit.Tests.csproj"
Cohesion: 0.22
Nodes (8): Moq, net10.0, coverlet.collector, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk

### Community 58 - "Product"
Cohesion: 0.24
Nodes (5): Account, ApiRoutes, Product, V1, V2

### Community 59 - "Phase 11 Execution Handoff"
Cohesion: 0.06
Nodes (30): Acceptance criteria, Adapter contract tests, Checkout and working tree, Current-state inventory, Data safety and destructive-action boundaries, Deprecation and upgrade checks, Documentation and delivery, Elasticsearch and Kibana images (+22 more)

### Community 61 - "IndexedPerson"
Cohesion: 0.08
Nodes (11): IndexedPerson, FakeElasticsearchServer, Handler, Port, Requests, Url, FakeResponse, RecordedRequest (+3 more)

### Community 62 - "IOrderEventPublisher"
Cohesion: 0.19
Nodes (3): IOrderEventPublisher, MessagingAdapterWithoutBrokerTests, NoOpHandler

### Community 63 - "Template.Integration.Tests.csproj"
Cohesion: 0.14
Nodes (12): Microsoft.Extensions.Caching.Memory, Microsoft.Extensions.Caching.StackExchangeRedis, net10.0, Microsoft.Extensions.Options.ConfigurationExtensions, Microsoft.NET.Sdk, net10.0, coverlet.collector, Microsoft.NET.Test.Sdk (+4 more)

### Community 64 - "ScriptedHandler"
Cohesion: 0.20
Nodes (6): IncomingMessageContext, CountingStore, Marks, OrderDeliveryProcessorTests, ScriptedHandler, Calls

### Community 66 - "shouldly"
Cohesion: 0.18
Nodes (5): Template.Functional.Tests.ApiEndpoints, Template.Unit.Tests.DomainTests.Products.Entities, Template.Unit.Tests.ApplicationTests.Wrappers, Template.Unit.Tests.ApplicationTests.Search, Template.Functional.Tests.Common

### Community 69 - "Template.Architecture.Tests.csproj"
Cohesion: 0.29
Nodes (6): net10.0, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk

### Community 70 - "Program.cs"
Cohesion: 0.07
Nodes (9): Template.Infrastructure.Persistence, Template.Infrastructure.Identity, Template.WebApi.Infrastructure.Middlewares, Template.Infrastructure.Resources, Template.Infrastructure.Persistence.Seeds, Template.WebApi.Infrastructure.Extensions, Template.WebApi.Infrastructure.Services, Template.Application.Settings (+1 more)

### Community 71 - "Fases 6–7 — bloqueio de pré-condições (2026-10-10)"
Cohesion: 0.33
Nodes (5): Causa do bloqueio, Como desbloquear, Estado do Git registrado, Fases 6–7 — bloqueio de pré-condições (2026-10-10), Pré-condições

### Community 72 - "PersonUseCaseHandlerTests"
Cohesion: 0.10
Nodes (14): Template.Application.Features.Persons.Queries, Template.Unit.Tests.ApplicationTests.Features.Persons, Template.Application.Features.Persons.Commands, EnsurePersonIndexCommand, EnsurePersonIndexCommandHandler, IndexPersonCommand, IndexPersonCommandHandler, RemovePersonCommand (+6 more)

### Community 74 - "RabbitMqOrderConsumerService"
Cohesion: 0.10
Nodes (5): ReceivedDelivery, RabbitMqOrderConsumerService, RabbitMqConnections, OrderSubscription, DeadLetterQueue

### Community 81 - "RecordingPersonSearchIndex"
Cohesion: 0.09
Nodes (14): PersonFunctionalTests, Index, PersonWithoutSearchEngineFunctionalTests, RecordingPersonSearchIndex, BlockUntilCancelled, Calls, Listed, NextStatus (+6 more)

### Community 85 - "Relatório das Fases 6–7 — 2026-10-10"
Cohesion: 0.08
Nodes (21): Template.WebApi.Controllers, Entrega, Estado de partida (verificado em 2026-10-10 às 14:26), Execução de comandos, Fase 6 — controllers e contratos HTTP, Fase 7 — persistência e PostgreSQL (somente depois da Fase 6 aprovada), Fontes obrigatórias (ler antes de alterar qualquer arquivo), Regras gerais (+13 more)

### Community 100 - "Contexto técnico relevante"
Cohesion: 0.09
Nodes (8): Contexto técnico relevante, Fase 7 — persistência e PostgreSQL, IGenericRepository, IProductRepository, GenericRepository, DbContext, ProductRepository, ServiceRegistration

### Community 101 - ".AddMessagingInfrastructure"
Cohesion: 0.10
Nodes (6): Defeitos encontrados pelos testes com broker e corrigidos, Testes adicionados, DeliveryAttemptTracker, OrderDeliveryProcessor, InMemoryProcessedMessageStore, ServiceRegistration

### Community 102 - "PersonIndexStatus"
Cohesion: 0.15
Nodes (7): PersonIndexStatus, IncompatibleDocument, NotFound, Rejected, Succeeded, Unavailable, ElasticsearchPersonSearchIndex

### Community 104 - "IClock"
Cohesion: 0.18
Nodes (6): Time handling, IClock, UtcNow, SystemClock, UtcNow, SystemClockTests

### Community 105 - "ChangePasswordRequest"
Cohesion: 0.21
Nodes (5): ChangePasswordRequest, ConfirmPassword, Password, ChangePasswordRequestValidator, AccountController

### Community 106 - "Relatório das Fases 0–5 — 2026-10-10"
Cohesion: 0.22
Nodes (8): Comparação de contrato, Confirmação, Mudanças por fase, Relatório das Fases 0–5 — 2026-10-10, Revisão da migration e do SQL (Fase 4), Riscos e pendências, Testes e gates (execução de 2026-10-10, 14:25–14:26), Versões e fontes oficiais

### Community 107 - ".Handle"
Cohesion: 0.20
Nodes (4): PublishOrderCommand, PublishOrderCommandHandler, PublishOrderCommandHandlerTests, Handler

### Community 108 - "Template.PostgreSql.Integration.Tests.csproj"
Cohesion: 0.25
Nodes (7): Testcontainers.PostgreSql, net10.0, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk

### Community 110 - "RabbitMqSettings"
Cohesion: 0.12
Nodes (14): RabbitMqSettings, ConnectionTimeout, ConsumersEnabled, Enabled, HostName, MaxDeliveryAttempts, MaxReconnectDelay, Password (+6 more)

### Community 111 - "Phase 12 Execution Handoff"
Cohesion: 0.09
Nodes (21): 401 and 403 behavior, Account-use-case standardization, Checkout and working tree, Documentation and delivery, HTTP and API compatibility, HTTPS metadata policy, Identity schema and migrations, JWT issuance and validation (+13 more)

### Community 112 - "Phase 10 Execution Handoff"
Cohesion: 0.11
Nodes (17): Acceptance criteria, Application port, Checkout and working tree, Configuration and security, Contracts and indexed documents, Documentation and delivery, Feature flag and rollback, Infrastructure adapter (+9 more)

### Community 117 - "PersonController"
Cohesion: 0.25
Nodes (4): Current state to characterize, HTTP compatibility, PersonController, NotFound

### Community 118 - "GetAllUsersRequest"
Cohesion: 0.29
Nodes (6): Template.Application.Parameters, GetAllUsersRequest, Name, PaginationRequestParameter, PageNumber, PageSize

### Community 119 - "PersonIndexResult"
Cohesion: 0.13
Nodes (4): PersonIndexResult, Status, Succeeded, Value

### Community 120 - "RecordingHandler"
Cohesion: 0.21
Nodes (3): RecordingHandler, Behavior, Calls

### Community 121 - "RabbitMqFixture"
Cohesion: 0.21
Nodes (4): RabbitMqFixture, Container, Host, Port

### Community 122 - "PersonSearchIndexElasticsearchTests"
Cohesion: 0.26
Nodes (5): LegacyPerson, FirstName, Id, LastName, PersonSearchIndexElasticsearchTests

### Community 123 - "PersonBulkIndexResult"
Cohesion: 0.16
Nodes (7): PersonBulkIndexResult, FailedIds, IndexedIds, IsComplete, IsPartial, Status, PersonIndexResultTests

### Community 124 - "CustomWebApplicationFactory"
Cohesion: 0.08
Nodes (11): Program, ExternalServicesUnavailableTests, MessageWithoutBrokerFunctionalTests, RecordingPublisherFactory, Publisher, CacheDisabledFactory, AdditionalConfiguration, ProductValidationFunctionalTests (+3 more)

### Community 125 - "Template.Infrastructure.Messaging.csproj"
Cohesion: 0.13
Nodes (13): RabbitMQ.Client, Testcontainers.RabbitMq, net10.0, Microsoft.Extensions.Hosting.Abstractions, Microsoft.Extensions.Options.ConfigurationExtensions, Microsoft.NET.Sdk, net10.0, Microsoft.Extensions.DependencyInjection (+5 more)

### Community 127 - "MessagingHarness"
Cohesion: 0.15
Nodes (4): CapturedLog, MessagingHarness, Consumer, Publisher

### Community 128 - "UserDto"
Cohesion: 0.15
Nodes (7): UserDto, Created, Email, Id, Name, PhoneNumber, UserName

### Community 129 - "Template.Infrastructure.Identity.Models"
Cohesion: 0.13
Nodes (5): Template.Infrastructure.Identity.Seeds, Template.Infrastructure.Identity.Models, ApplicationRole, DefaultBasicUser, DefaultRoles

### Community 130 - "TranslatorMessageDto"
Cohesion: 0.20
Nodes (6): TranslatorMessageDto, Args, Text, AccountMessages, ProductMessages, TranslatorMessages

### Community 133 - "ResourceGeneral"
Cohesion: 0.18
Nodes (9): ResourceGeneral, BarCode, ConfirmPassword, Culture, Id, Name, Password, ResourceManager (+1 more)

### Community 135 - "MessagePublishException"
Cohesion: 0.35
Nodes (3): Reconexão, startup, shutdown e indisponibilidade, MessagePublishException, DisabledOrderEventPublisher

### Community 136 - "IAuthenticatedUserService"
Cohesion: 0.12
Nodes (13): Current-user and claims boundary, IAuthenticatedUserService, UserId, UserName, DesignTimeAuthenticatedUser, Instance, UserId, UserName (+5 more)

### Community 137 - "Order"
Cohesion: 0.11
Nodes (17): Breaking changes tratados, Comandos e resultados, Comparação do OpenAPI com `docs/refactoring/baseline-fase-6/`, Código e ciclo de vida, Fontes oficiais, Inventário anterior (estado de `f293cb9`), Mensagens, ack e falhas, Rollback do pacote (+9 more)

### Community 139 - "Template.Infrastructure.Search.csproj"
Cohesion: 0.14
Nodes (12): Elastic.Clients.Elasticsearch, Testcontainers, net10.0, Microsoft.Extensions.Logging.Abstractions, Microsoft.Extensions.Options.ConfigurationExtensions, Microsoft.NET.Sdk, net10.0, Microsoft.NET.Test.Sdk (+4 more)

### Community 140 - "Required tests"
Cohesion: 0.25
Nodes (8): Account functional tests, Architecture tests, Authorization tests, JWT validation tests, Options and startup tests, Persistence and migration tests, Required tests, Unit tests

### Community 142 - "ElasticLoggingSettings"
Cohesion: 0.15
Nodes (7): ElasticLoggingSettings, DataSet, Enabled, Namespace, RequestTimeout, Type, Url

### Community 144 - "ErrorCodeEnum"
Cohesion: 0.25
Nodes (7): Template.WebApi.Infrastructure.Enums, ErrorCodeEnum, Conflict, Forbidden, Unauthorized, Unexpected, Validation

### Community 145 - "Required tests"
Cohesion: 0.25
Nodes (8): Adapter tests, Architecture tests, Functional tests, Health-check tests, Logging tests, Required tests, Tests with real Elasticsearch, Unit tests

### Community 146 - "microsoft_extensions_dependencyinjection"
Cohesion: 0.08
Nodes (11): Template.Integration.Tests.Search, Template.Tests.Shared, Template.Functional.Tests.Observability, Template.Application.Search.Persons, Template.WebApi.Infrastructure.Logging, Template.Infrastructure.Search.Persons, Template.WebApi.Infrastructure.HealthChecks, Template.Infrastructure.Search (+3 more)

### Community 147 - "ElasticsearchFixture"
Cohesion: 0.21
Nodes (4): ElasticsearchFixture, Container, Http, Url

### Community 149 - "CountingProcessedMessageStore"
Cohesion: 0.33
Nodes (3): CountingProcessedMessageStore, Checks, Marks

### Community 151 - "ElasticsearchHealthCheckSettings"
Cohesion: 0.20
Nodes (6): ElasticsearchHealthCheckSettings, Enabled, FailureStatus, Name, Timeout, Url

### Community 152 - "Error"
Cohesion: 0.33
Nodes (4): Error, Description, ErrorCode, FieldName

### Community 153 - "UnreachableLogSinkFactory"
Cohesion: 0.20
Nodes (3): LoggingSinkUnavailableHostTests, UnreachableLogSinkFactory, AdditionalConfiguration

### Community 155 - "ApiErrorItem"
Cohesion: 0.28
Nodes (7): Diff de contrato e baseline, ApiErrorItem, Code, Field, Message, ApiErrorResponse, Errors

### Community 157 - ".AddIdentityInfrastructure"
Cohesion: 0.29
Nodes (4): Current-state characterization, IGetUserServices, ServiceRegistration, GetUserServices

### Community 159 - "DecodeResult"
Cohesion: 0.29
Nodes (7): DecodeResult, Incompatible, Invalid, DecodeStatus, Decoded, IncompatibleContract, InvalidPayload

### Community 163 - "Availability policy"
Cohesion: 0.50
Nodes (4): Availability policy, Health check, Logging, Search and indexing

### Community 165 - "ApplicationUser"
Cohesion: 0.33
Nodes (3): ApplicationUser, Created, Name

### Community 166 - "PersonDocument"
Cohesion: 0.33
Nodes (4): PersonDocument, FirstName, Id, LastName

### Community 167 - "Architecture boundaries"
Cohesion: 0.40
Nodes (5): Application may own, Architecture boundaries, Domain must remain free of, Infrastructure.Identity must own, WebApi may own

## Knowledge Gaps
- **592 isolated node(s):** `Microsoft.VisualStudio.SDK`, `Microsoft.VSSDK.BuildTools`, `UserName`, `Password`, `Password` (+587 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 1142 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **64 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Current solution structure` connect `Current solution structure` to `Person`, `.Handle`, `MessagePublishException`, `Order`, `ValidationBehavior`, `DistributedProductCacheTests`, `.DeclareAsync`, `IProductCache`, `Relatório da Fase 9 — 2026-10-10`, `.AddCustomLocalization`, `PersonDocument`, `PersonSearchSettings`, `IndexedPerson`, `IOrderEventPublisher`, `IPersonSearchIndex`, `RabbitMqOrderConsumerService`, `PersonIndexStatus`, `OrderPlacedEvent`, `.Handle`, `RabbitMqSettings`, `PersonIndexResult`, `PersonBulkIndexResult`?**
  _High betweenness centrality (0.186) - this node is a cross-community bridge._
- **Are the 4 inferred relationships involving `BaseResult` (e.g. with `Current implementation conventions` and `Errors and API results`) actually correct?**
  _`BaseResult` has 4 INFERRED edges - model-reasoned connections that need verification._
- **What connects `Microsoft.VisualStudio.SDK`, `Microsoft.VSSDK.BuildTools`, `UserName` to the rest of the system?**
  _592 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Container (Dockerfile)` be split into smaller, more focused modules?**
  _Cohesion score 0.048625792811839326 - nodes in this community are weakly interconnected._
- **Why does `BaseResult` connect `BaseResult` to `.ToActionResult`, `.Handle`, `ErrorCategoryTests`, `IClassFixture`, `UpdateProductCommand`, `ValidationBehavior`, `PagedResponse`, `ProductController`, `ProductFunctionalTests`, `ProductDto`, `Error`, `GetProductByIdQuery`, `Phase 3 — Incremental implementation plan`, `AuthenticationRequest`, `CreateProductCommand`, `AuthenticationResponse`, `Fase 5 em detalhe`, `5. Target code standards`, `GetProductByIdQueryHandler`, `Phase6HttpContractFunctionalTests`, `Contexto técnico relevante`, `ChangePasswordRequest`?**
  _High betweenness centrality (0.081) - this node is a cross-community bridge._
- **Are the 28 inferred relationships involving `IndexedPerson` (e.g. with `Current solution structure` and `.Handle()`) actually correct?**
  _`IndexedPerson` has 28 INFERRED edges - model-reasoned connections that need verification._
- **Should `.ToActionResult` be split into smaller, more focused modules?**
  _Cohesion score 0.06597222222222222 - nodes in this community are weakly interconnected._