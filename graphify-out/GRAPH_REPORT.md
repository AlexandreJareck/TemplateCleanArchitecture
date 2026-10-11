# Graph Report - TemplateCleanArchNET  (2026-10-10)

## Corpus Check
- 270 files · ~86,892 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 14 file(s) not represented in the graph (top: (none) 4, .resx 4, .jsonl 2)

## Summary
- 2821 nodes · 6137 edges · 171 communities (107 shown, 64 thin omitted)
- Extraction: 89% EXTRACTED · 11% INFERRED · 0% AMBIGUOUS · INFERRED: 649 edges (avg confidence: 0.88)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `8bd25482`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- AccountServices.cs
- ResourceMessages
- Person
- Container (Dockerfile)
- ApiResultMapper
- .Handle
- Error
- OrderPlacedEvent
- IndexedPerson
- microsoft_entityframeworkcore
- DistributedProductCacheTests.cs
- UpdateProductCommand
- .GetRepository
- ValidationBehavior
- ApplicationDbContext
- Template.WebApi.csproj
- IRequest
- DistributedProductCacheTests
- ProductRepository
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
- Contexto técnico relevante
- ConfigureSwaggerOptions
- .ToActionResult
- RabbitMqTestSupport.cs
- BaseResult
- TemplateCleanArchitecture.sln
- 5. Target code standards
- Relatório da Fase 10 — 2026-10-10
- Template.Functional.Tests.csproj
- AccountController
- ProductEntity
- Current configuration names
- HttpClientGetExtensionMethods
- Template.Infrastructure.Identity
- Phase 0 baseline — 2026-10-10
- UnitOfWork
- DeliveryDecision
- .Decode
- Template.Infrastructure.Persistence.csproj
- Template.Unit.Tests.csproj
- IdentityContext
- Product
- Phase 11 Execution Handoff
- .CreateIndex
- IOrderEventPublisher
- Template.Integration.Tests.csproj
- ScriptedHandler
- shouldly
- ProductPriceDecimal
- Phase6HttpContractFunctionalTests
- Template.Architecture.Tests.csproj
- Program.cs
- Fases 6–7 — bloqueio de pré-condições (2026-10-10)
- PersonUseCaseHandlerTests
- Current solution structure
- RabbitMqOrderConsumerService
- contract-checklist.md
- boundaries.md
- verification-matrix.md
- postgresql-review.md
- adapter-checklist.md
- upgrade-record.md
- PersonIndexStatus
- .AddMessagingInfrastructure
- init-db.sh
- skill-activation-report.md
- handoff-fases-6-7.md
- Core/AGENTS.md
- Infrastructure/AGENTS.md
- Presentation/AGENTS.md
- Tests/AGENTS.md
- DeliveryAttemptTracker
- ElasticsearchPersonSearchIndex
- RabbitMqOrderEventPublisher
- Template.Application/ServiceRegistration.cs
- ChangePasswordRequest
- Relatório das Fases 0–5 — 2026-10-10
- PublishOrderCommand
- Template.PostgreSql.Integration.Tests.csproj
- OrderPlacedEventHandlerTests
- RabbitMqSettings
- Phase 12 Execution Handoff
- Phase 10 Execution Handoff
- MessagingHarness
- MessageFunctionalTests
- ElasticsearchTestSupport.cs
- api-v1-lifecycle.md
- .GetPagedUsers
- PersonIndexResult
- RecordingHandler
- RabbitMqFixture
- PersonSearchIndexElasticsearchTests
- PersonBulkIndexResult
- CustomWebApplicationFactory
- Template.Infrastructure.Messaging.csproj
- ElasticsearchHealthCheck
- PaginationResponseDto
- UserDto
- ApplicationRole
- TranslatorMessageDto
- ElasticLoggingTests
- ResourceGeneral
- .Log
- MessagePublishException
- RabbitMqConnections
- Order
- ElasticsearchImage
- Template.Infrastructure.Search.csproj
- Required tests
- .AddHealthChecksService
- ElasticLoggingSettings
- ElasticLogSinkDiagnostics
- ErrorCodeEnum
- Required tests
- Template.Application.Search.Persons
- ElasticsearchFixture
- .DeclareAsync
- CountingProcessedMessageStore
- CapturingLogger
- ElasticsearchHealthCheckSettings
- PagedResponse
- UnreachableLogSinkFactory
- LoggingExtensions.cs
- Relatório das Fases 6–7 — 2026-10-10
- PersonController
- ITranslator
- CapturingSink
- OrderPlacedMessageSerializer.cs
- ThrowingWriter
- LocalizationExtensions.cs
- Availability policy
- ApiResultFilterAttribute
- ApplicationUser
- Código, DI e ciclo de vida
- Architecture boundaries
- LegacyPerson
- ProductConfiguration.cs

## God Nodes (most connected - your core abstractions)
1. `BaseResult` - 76 edges
2. `IndexedPerson` - 59 edges
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
- `Execution status` --references--> `IPersonSearchIndex`  [INFERRED]
  AGENTS.md → Src/Core/Template.Application/Interfaces/Search/IPersonSearchIndex.cs
- `Errors and API results` --references--> `BaseResult`  [INFERRED]
  docs/fase-2-workspace.md → Src/Core/Template.Application/Wrappers/BaseResult.cs
- `Phase 5 — Standardize application use cases and results` --references--> `BaseResult`  [INFERRED]
  docs/fase-3-plano-implementacao.md → Src/Core/Template.Application/Wrappers/BaseResult.cs

## Import Cycles
- None detected.

## Communities (171 total, 64 thin omitted)

### Community 0 - "AccountServices.cs"
Cohesion: 0.11
Nodes (9): Template.Functional.Tests.ApiEndpoints.v2, Template.WebApi.Infrastructure.Middlewares, Template.Infrastructure.Identity.Services, Template.Application.DTOs.Account.Request, Template.Application.Helpers, Template.Application.DTOs.Account.Response, Template.Infrastructure.Identity.Settings, Template.Application.Interfaces.UserInterfaces (+1 more)

### Community 1 - "ResourceMessages"
Cohesion: 0.22
Nodes (7): ResourceMessages, Account_NotFound_with_UserName, Culture, Invalid_password, Product_NotFound_with_id, ResourceManager, Username_is_already_taken

### Community 2 - "Person"
Cohesion: 0.29
Nodes (6): Template.Domain.Person, Determining the active requirement, Person, FirstName, Id, LastName

### Community 3 - "Container (Dockerfile)"
Cohesion: 0.05
Nodes (43): commandName, environmentVariables, launchBrowser, launchUrl, publishAllPorts, useSSL, ASPNETCORE_ENVIRONMENT, ASPNETCORE_HTTP_PORTS (+35 more)

### Community 5 - ".Handle"
Cohesion: 0.28
Nodes (4): DeleteProductCommand, Id, DeleteProductCommandHandler, DeleteProductCommandHandlerTests

### Community 6 - "Error"
Cohesion: 0.15
Nodes (6): Template.Unit.Tests.ApplicationTests.Wrappers, Error, Description, ErrorCode, FieldName, ErrorCategoryTests

### Community 7 - "OrderPlacedEvent"
Cohesion: 0.14
Nodes (7): OrderPlacedEvent, RecordingOrderEventPublisher, BlockUntilCancelled, FailWith, ObservedCancellation, Published, Started

### Community 8 - "IndexedPerson"
Cohesion: 0.15
Nodes (10): IndexedPerson, RecordingPersonSearchIndex, BlockUntilCancelled, Calls, Listed, NextStatus, ObservedCancellation, Started (+2 more)

### Community 9 - "microsoft_entityframeworkcore"
Cohesion: 0.10
Nodes (10): Template.Infrastructure.Persistence.Repositories, Template.Infrastructure.Resources.ProjectResources, Template.PostgreSql.Integration.Tests, Template.Infrastructure.Identity.Migrations, Template.Infrastructure.Persistence.Contexts, Template.Domain.Common, Template.Infrastructure.Persistence.Migrations, Template.Infrastructure.Persistence.Extensions (+2 more)

### Community 10 - "DistributedProductCacheTests.cs"
Cohesion: 0.27
Nodes (4): Template.Application.Interfaces.Caching, Template.Integration.Tests.Caching, Template.Infrastructure.Caching, Template.Infrastructure.Caching.Products

### Community 11 - "UpdateProductCommand"
Cohesion: 0.22
Nodes (7): UpdateProductCommand, BarCode, Id, Name, Price, UpdateProductCommandHandler, UpdateProductCommandHandlerTests

### Community 12 - ".GetRepository"
Cohesion: 0.13
Nodes (6): BaseEfRepoTestFixture, EfCancellationPropagationTests, EfRepositoryAdd, EfRepositoryDelete, EfRepositoryUpdate, ProductPersistencePrecisionTests

### Community 13 - "ValidationBehavior"
Cohesion: 0.18
Nodes (7): ValidationBehavior, NameRequiredValidator, SampleRequest, Name, TokenCapturingValidator, CapturedToken, ValidationBehaviorTests

### Community 14 - "ApplicationDbContext"
Cohesion: 0.07
Nodes (16): Current-user and claims boundary, IAuthenticatedUserService, UserId, UserName, ApplicationDbContext, Products, ApplicationDbContextFactory, DesignTimeAuthenticatedUser (+8 more)

### Community 15 - "Template.WebApi.csproj"
Cohesion: 0.12
Nodes (15): Asp.Versioning.Mvc.ApiExplorer, AspNetCore.HealthChecks.UI.Client, Elastic.Serilog.Sinks, Microsoft.AspNetCore.OpenApi, Microsoft.EntityFrameworkCore.Tools, Microsoft.VisualStudio.Azure.Containers.Tools.Targets, Serilog.AspNetCore, Serilog.Enrichers.ClientInfo (+7 more)

### Community 16 - "IRequest"
Cohesion: 0.23
Nodes (4): GetPagedListProductQuery, Name, GetPagedListProductQueryHandler, GetPagedListProductQueryHandlerTests

### Community 17 - "DistributedProductCacheTests"
Cohesion: 0.06
Nodes (9): CacheSettings, AbsoluteExpiration, Enabled, OperationTimeout, SlidingExpiration, DistributedProductCache, DistributedProductCacheTests, FailingDistributedCache (+1 more)

### Community 18 - "ProductRepository"
Cohesion: 0.17
Nodes (6): ProductRepository, PostgreSqlFixture, TestAuthenticatedUser, UserId, UserName, PostgreSqlPersistenceTests

### Community 19 - "Template.Application.Interfaces"
Cohesion: 0.14
Nodes (10): Template.Application.Interfaces, Template.Unit.Tests.DomainTests.Products.Entities, Template.Unit.Tests.ApplicationTests.Features.Products.Commands, Template.Infrastructure.Resources, Template.Application.DTOs, Template.Domain.Products.Entities, Template.Application.Features.Products.Commands, Template.Integration.Tests.Data (+2 more)

### Community 24 - "ProductDto"
Cohesion: 0.13
Nodes (7): Phase 4 — Clean the Domain model, ProductDto, BarCode, CreatedDateTime, Id, Name, Price

### Community 25 - "IProductCache"
Cohesion: 0.09
Nodes (17): Phase 8 — Move Redis caching behind an infrastructure adapter, Pré-condições verificadas (Fases 8 e 9), Application, Infrastructure — `Template.Infrastructure.Caching` (novo), Não verificado e riscos, O que mudou, OpenAPI e baseline (`docs/refactoring/baseline-fase-6`), Regras de arquitetura (+9 more)

### Community 26 - "GetProductByIdQuery"
Cohesion: 0.14
Nodes (5): GetProductByIdQuery, Id, GetProductByIdQueryHandler, GetProductByIdQueryHandlerTests, ProductServiceTests

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
Cohesion: 0.16
Nodes (11): AuthenticationRequest, Password, UserName, AuthenticationRequestValidator, ChangePasswordRequestValidator, ChangeUserNameRequest, UserName, ChangeUserNameRequestValidator (+3 more)

### Community 31 - "CreateProductCommand"
Cohesion: 0.26
Nodes (6): CreateProductCommand, BarCode, Name, Price, CreateProductCommandHandler, CreateProductCommandHandlerTests

### Community 32 - "Relatório da Fase 9 — 2026-10-10"
Cohesion: 0.12
Nodes (16): Current state to characterize, Adapters e hosted services (Infrastructure.Messaging), APIs 7.x usadas, Configuração e DI, Confirmações, Contrato `order.placed` v1, Gates não executados, Política de ack, retry, poison e idempotência (+8 more)

### Community 33 - "AuthenticationResponse"
Cohesion: 0.14
Nodes (9): AuthenticationResponse, Email, Id, IsVerified, JwToken, Roles, UserName, AccountFunctionalTests (+1 more)

### Community 35 - ".ConfigureLogger"
Cohesion: 0.24
Nodes (3): Pacote do health check removido, Separação entre busca, logging e health check, LoggingExtensions

### Community 36 - "Template.Application.Wrappers"
Cohesion: 0.13
Nodes (10): Template.Unit.Tests.ApplicationTests.Features.Products.Queries, Template.Application.Wrappers, Template.Application.Features.Products.Queries, Template.WebApi.Infrastructure.Filters, Template.WebApi.Controllers.v2, Template.Application.DTOs.Products, Template.Domain.Order, Template.WebApi.Infrastructure.Http (+2 more)

### Community 37 - "Contexto técnico relevante"
Cohesion: 0.07
Nodes (23): Contexto técnico relevante, Fase 5 em detalhe, IGenericRepository, IProductRepository, ErrorCategory, Conflict, Forbidden, NotFound (+15 more)

### Community 40 - "RabbitMqTestSupport.cs"
Cohesion: 0.14
Nodes (14): Template.Integration.Tests.Messaging, Template.Infrastructure.Messaging.Publishing, Template.Infrastructure.Messaging.Serialization, Template.Application.Features.Orders.EventHandlers, Template.Application.Interfaces.Messaging, Template.Application.Messaging.Orders, Template.Unit.Tests.ApplicationTests.Features.Orders, Template.Infrastructure.Messaging.Topology (+6 more)

### Community 41 - "BaseResult"
Cohesion: 0.17
Nodes (8): Current-state characterization, IAccountServices, IGetUserServices, BaseResult, Data, Errors, Success, AccountServices

### Community 42 - "TemplateCleanArchitecture.sln"
Cohesion: 0.20
Nodes (3): Template, Microsoft.VisualStudio.SDK, Microsoft.VSSDK.BuildTools

### Community 43 - "5. Target code standards"
Cohesion: 0.08
Nodes (23): 1. Evidence and assumptions, 2. Proposed AI workspace, 3. Dependency and integration update matrix, 4. Target Clean Architecture, 5. Target code standards, Application use cases, Controllers, Design principles (+15 more)

### Community 44 - "Relatório da Fase 10 — 2026-10-10"
Cohesion: 0.12
Nodes (15): Busca e indexação, Comandos executados e resultados, Comparação do OpenAPI com `docs/refactoring/baseline-fase-6/`, Confirmações, Decisão sobre o requisito ativo, Documento, índice e compatibilidade, Fake/no-op e rollback, Gates não executados (+7 more)

### Community 45 - "Template.Functional.Tests.csproj"
Cohesion: 0.20
Nodes (9): Microsoft.AspNetCore.Mvc.Testing, net10.0, coverlet.collector, Microsoft.Extensions.Hosting.Abstractions, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio (+1 more)

### Community 47 - "ProductEntity"
Cohesion: 0.18
Nodes (6): ProductEntity, BarCode, Name, Price, ProductConfiguration, ProductTests

### Community 48 - "Current configuration names"
Cohesion: 0.05
Nodes (28): Change and validation rules, Current configuration names, Current implementation conventions, Execution status, Repository purpose, TemplateCleanArchNET agent guide, Verified commands (2026-10-10, Phase 10 checkpoint), Time handling (+20 more)

### Community 50 - "Template.Infrastructure.Identity"
Cohesion: 0.25
Nodes (8): Microsoft.AspNetCore.Authentication.JwtBearer, Microsoft.AspNetCore.Identity.EntityFrameworkCore, System.IdentityModel.Tokens.Jwt, Template.Infrastructure.Identity, net10.0, Microsoft.EntityFrameworkCore.InMemory, Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.NET.Sdk

### Community 51 - "Phase 0 baseline — 2026-10-10"
Cohesion: 0.33
Nodes (5): Baseline validation before refactoring, Dependency and vulnerability baseline, Phase 0 baseline — 2026-10-10, Source state, Toolchain

### Community 52 - "UnitOfWork"
Cohesion: 0.17
Nodes (3): IUnitOfWork, UnitOfWork, ServiceRegistration

### Community 53 - "DeliveryDecision"
Cohesion: 0.29
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

### Community 61 - ".CreateIndex"
Cohesion: 0.05
Nodes (12): FakeElasticsearchServer, Handler, Port, Requests, Url, FakeResponse, RecordedRequest, PathAndQuery (+4 more)

### Community 62 - "IOrderEventPublisher"
Cohesion: 0.19
Nodes (3): IOrderEventPublisher, MessagingAdapterWithoutBrokerTests, NoOpHandler

### Community 63 - "Template.Integration.Tests.csproj"
Cohesion: 0.14
Nodes (12): Microsoft.Extensions.Caching.Memory, Microsoft.Extensions.Caching.StackExchangeRedis, net10.0, Microsoft.Extensions.Options.ConfigurationExtensions, Microsoft.NET.Sdk, net10.0, coverlet.collector, Microsoft.NET.Test.Sdk (+4 more)

### Community 64 - "ScriptedHandler"
Cohesion: 0.20
Nodes (6): IncomingMessageContext, CountingStore, Marks, OrderDeliveryProcessorTests, ScriptedHandler, Calls

### Community 69 - "Template.Architecture.Tests.csproj"
Cohesion: 0.29
Nodes (6): net10.0, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk

### Community 70 - "Program.cs"
Cohesion: 0.15
Nodes (7): Template.Infrastructure.Identity.Seeds, Template.Infrastructure.Persistence, Template.Infrastructure.Identity, Template.Infrastructure.Identity.Contexts, Template.Infrastructure.Persistence.Seeds, Template.WebApi.Infrastructure.Services, Template.Infrastructure.Identity.Models

### Community 71 - "Fases 6–7 — bloqueio de pré-condições (2026-10-10)"
Cohesion: 0.33
Nodes (5): Causa do bloqueio, Como desbloquear, Estado do Git registrado, Fases 6–7 — bloqueio de pré-condições (2026-10-10), Pré-condições

### Community 72 - "PersonUseCaseHandlerTests"
Cohesion: 0.11
Nodes (15): Template.Application.Features.Persons.Queries, Template.Unit.Tests.ApplicationTests.Features.Persons, Template.Application.Features.Persons.Commands, Port e contratos (Application), EnsurePersonIndexCommand, EnsurePersonIndexCommandHandler, IndexPersonCommand, IndexPersonCommandHandler (+7 more)

### Community 73 - "Current solution structure"
Cohesion: 0.09
Nodes (12): Current solution structure, Adapter (Infrastructure.Search), Classificação de falhas, IPersonSearchIndex, DisabledPersonSearchIndex, PersonSearchSettings, Enabled, IndexName (+4 more)

### Community 74 - "RabbitMqOrderConsumerService"
Cohesion: 0.15
Nodes (4): ReceivedDelivery, RabbitMqOrderConsumerService, OrderSubscription, DeadLetterQueue

### Community 81 - "PersonIndexStatus"
Cohesion: 0.15
Nodes (9): PersonIndexStatus, IncompatibleDocument, NotFound, Rejected, Succeeded, Unavailable, PersonFunctionalTests, Index (+1 more)

### Community 82 - ".AddMessagingInfrastructure"
Cohesion: 0.14
Nodes (5): Defeitos encontrados pelos testes com broker e corrigidos, Testes adicionados, OrderDeliveryProcessor, ServiceRegistration, ServiceRegistrationTests

### Community 85 - "handoff-fases-6-7.md"
Cohesion: 0.13
Nodes (12): Template.WebApi.Controllers, Entrega, Estado de partida (verificado em 2026-10-10 às 14:26), Execução de comandos, Fase 6 — controllers e contratos HTTP, Fase 7 — persistência e PostgreSQL (somente depois da Fase 6 aprovada), Fontes obrigatórias (ler antes de alterar qualquer arquivo), Regras gerais (+4 more)

### Community 102 - "ElasticsearchPersonSearchIndex"
Cohesion: 0.14
Nodes (5): ElasticsearchPersonSearchIndex, PersonDocument, FirstName, Id, LastName

### Community 104 - "Template.Application/ServiceRegistration.cs"
Cohesion: 0.09
Nodes (8): Template.Application, Template.Unit.Tests.ApplicationTests.Services, Template.Unit.Tests.ApplicationTests.Behaviors, Template.Application.Behaviors, Template.Application.Services.Time, SystemClock, UtcNow, SystemClockTests

### Community 105 - "ChangePasswordRequest"
Cohesion: 0.23
Nodes (4): ChangePasswordRequest, ConfirmPassword, Password, AccountController

### Community 106 - "Relatório das Fases 0–5 — 2026-10-10"
Cohesion: 0.22
Nodes (8): Comparação de contrato, Confirmação, Mudanças por fase, Relatório das Fases 0–5 — 2026-10-10, Revisão da migration e do SQL (Fase 4), Riscos e pendências, Testes e gates (execução de 2026-10-10, 14:25–14:26), Versões e fontes oficiais

### Community 107 - "PublishOrderCommand"
Cohesion: 0.18
Nodes (5): Template.Application.Features.Orders.Commands, PublishOrderCommand, PublishOrderCommandHandler, PublishOrderCommandHandlerTests, Handler

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

### Community 113 - "MessagingHarness"
Cohesion: 0.10
Nodes (5): BrokerAvailabilityTests, BrokerImage, MessagingHarness, Consumer, Publisher

### Community 118 - ".GetPagedUsers"
Cohesion: 0.18
Nodes (7): Template.Application.Parameters, GetAllUsersRequest, Name, PaginationRequestParameter, PageNumber, PageSize, GetUserServices

### Community 119 - "PersonIndexResult"
Cohesion: 0.11
Nodes (4): PersonIndexResult, Status, Succeeded, Value

### Community 120 - "RecordingHandler"
Cohesion: 0.18
Nodes (3): RecordingHandler, Behavior, Calls

### Community 121 - "RabbitMqFixture"
Cohesion: 0.13
Nodes (6): PostgreSqlCollection, RabbitMqCollection, RabbitMqFixture, Container, Host, Port

### Community 123 - "PersonBulkIndexResult"
Cohesion: 0.13
Nodes (9): Template.Unit.Tests.ApplicationTests.Search, Testes adicionados, PersonBulkIndexResult, FailedIds, IndexedIds, IsComplete, IsPartial, Status (+1 more)

### Community 124 - "CustomWebApplicationFactory"
Cohesion: 0.06
Nodes (16): Program, ExternalServicesUnavailableTests, MessageWithoutBrokerFunctionalTests, RecordingPublisherFactory, Publisher, RecordingSearchIndexFactory, Index, CacheDisabledFactory (+8 more)

### Community 125 - "Template.Infrastructure.Messaging.csproj"
Cohesion: 0.13
Nodes (13): RabbitMQ.Client, Testcontainers.RabbitMq, net10.0, Microsoft.Extensions.Hosting.Abstractions, Microsoft.Extensions.Options.ConfigurationExtensions, Microsoft.NET.Sdk, net10.0, Microsoft.Extensions.DependencyInjection (+5 more)

### Community 127 - "PaginationResponseDto"
Cohesion: 0.18
Nodes (5): PaginationResponseDto, Count, Data, PageNumber, PageSize

### Community 128 - "UserDto"
Cohesion: 0.20
Nodes (7): UserDto, Created, Email, Id, Name, PhoneNumber, UserName

### Community 130 - "TranslatorMessageDto"
Cohesion: 0.18
Nodes (6): TranslatorMessageDto, Args, Text, AccountMessages, ProductMessages, TranslatorMessages

### Community 133 - "ResourceGeneral"
Cohesion: 0.18
Nodes (9): ResourceGeneral, BarCode, ConfirmPassword, Culture, Id, Name, Password, ResourceManager (+1 more)

### Community 135 - "MessagePublishException"
Cohesion: 0.18
Nodes (8): Ports e contratos criados (Application), Reconexão, startup, shutdown e indisponibilidade, MessagePublishException, OrderDispatch, Broadcast, Direct, Topic, DisabledOrderEventPublisher

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

### Community 143 - "ElasticLogSinkDiagnostics"
Cohesion: 0.17
Nodes (3): ElasticLogSinkDiagnostics, Default, ManualTimeProvider

### Community 144 - "ErrorCodeEnum"
Cohesion: 0.25
Nodes (7): Template.WebApi.Infrastructure.Enums, ErrorCodeEnum, Conflict, Forbidden, Unauthorized, Unexpected, Validation

### Community 145 - "Required tests"
Cohesion: 0.25
Nodes (8): Adapter tests, Architecture tests, Functional tests, Health-check tests, Logging tests, Required tests, Tests with real Elasticsearch, Unit tests

### Community 146 - "Template.Application.Search.Persons"
Cohesion: 0.12
Nodes (8): Template.Integration.Tests.Search, Template.Tests.Shared, Template.Functional.Tests.Observability, Template.Application.Search.Persons, Template.Infrastructure.Search.Persons, Template.Infrastructure.Search, Template.Application.Interfaces.Search, SerialLoggingCollection

### Community 147 - "ElasticsearchFixture"
Cohesion: 0.21
Nodes (4): ElasticsearchFixture, Container, Http, Url

### Community 149 - "CountingProcessedMessageStore"
Cohesion: 0.33
Nodes (3): CountingProcessedMessageStore, Checks, Marks

### Community 151 - "ElasticsearchHealthCheckSettings"
Cohesion: 0.20
Nodes (6): ElasticsearchHealthCheckSettings, Enabled, FailureStatus, Name, Timeout, Url

### Community 152 - "PagedResponse"
Cohesion: 0.22
Nodes (5): PagedResponse, PageNumber, PageSize, TotalItems, TotalPages

### Community 153 - "UnreachableLogSinkFactory"
Cohesion: 0.20
Nodes (3): LoggingSinkUnavailableHostTests, UnreachableLogSinkFactory, AdditionalConfiguration

### Community 155 - "Relatório das Fases 6–7 — 2026-10-10"
Cohesion: 0.11
Nodes (17): Arquivos alterados nesta execução, Automação e documentação, Comandos e resultados, Confirmação de escopo, Contrato e WebApi, Diff de contrato e baseline, Migrations e SQL, Persistência e testes (+9 more)

### Community 156 - "PersonController"
Cohesion: 0.29
Nodes (4): Current state to characterize, HTTP compatibility, PersonController, NotFound

### Community 157 - "ITranslator"
Cohesion: 0.32
Nodes (3): ITranslator, ServiceRegistration, Translator

### Community 159 - "OrderPlacedMessageSerializer.cs"
Cohesion: 0.25
Nodes (7): DecodeResult, Incompatible, Invalid, DecodeStatus, Decoded, IncompatibleContract, InvalidPayload

### Community 162 - "LocalizationExtensions.cs"
Cohesion: 0.17
Nodes (3): Template.WebApi.Infrastructure.Extensions, Template.Application.Settings, CorsExtensions

### Community 163 - "Availability policy"
Cohesion: 0.50
Nodes (4): Availability policy, Health check, Logging, Search and indexing

### Community 165 - "ApplicationUser"
Cohesion: 0.10
Nodes (10): ApplicationUser, Created, Name, DefaultBasicUser, ServiceRegistration, JwtSettings, Audience, DurationInMinutes (+2 more)

### Community 166 - "Código, DI e ciclo de vida"
Cohesion: 0.50
Nodes (4): Comportamento HTTP legado (smoke da WebApi legada e caracterização do client), Código, DI e ciclo de vida, Inventário da integração anterior, Requisições e formato do documento (caracterizados contra um Elasticsearch 8.17.4 descartável)

### Community 167 - "Architecture boundaries"
Cohesion: 0.40
Nodes (5): Application may own, Architecture boundaries, Domain must remain free of, Infrastructure.Identity must own, WebApi may own

### Community 169 - "LegacyPerson"
Cohesion: 0.50
Nodes (4): LegacyPerson, FirstName, Id, LastName

## Knowledge Gaps
- **600 isolated node(s):** `Microsoft.VisualStudio.SDK`, `Microsoft.VSSDK.BuildTools`, `UserName`, `Password`, `Password` (+595 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 1151 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **64 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Current solution structure` connect `Current solution structure` to `Person`, `OrderPlacedEvent`, `IndexedPerson`, `Order`, `MessagePublishException`, `DistributedProductCacheTests`, `.DeclareAsync`, `IProductCache`, `ITranslator`, `Relatório da Fase 9 — 2026-10-10`, `Current configuration names`, `IOrderEventPublisher`, `RabbitMqOrderConsumerService`, `PersonIndexStatus`, `ElasticsearchPersonSearchIndex`, `RabbitMqOrderEventPublisher`, `PublishOrderCommand`, `RabbitMqSettings`, `PersonIndexResult`, `PersonBulkIndexResult`?**
  _High betweenness centrality (0.184) - this node is a cross-community bridge._
- **Are the 4 inferred relationships involving `BaseResult` (e.g. with `Current implementation conventions` and `Errors and API results`) actually correct?**
  _`BaseResult` has 4 INFERRED edges - model-reasoned connections that need verification._
- **What connects `Microsoft.VisualStudio.SDK`, `Microsoft.VSSDK.BuildTools`, `UserName` to the rest of the system?**
  _600 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `AccountServices.cs` be split into smaller, more focused modules?**
  _Cohesion score 0.10846560846560846 - nodes in this community are weakly interconnected._
- **Why does `BaseResult` connect `BaseResult` to `ApiResultMapper`, `.Handle`, `Error`, `UpdateProductCommand`, `ProductController`, `ProductFunctionalTests`, `ProductDto`, `PagedResponse`, `GetProductByIdQuery`, `Phase 3 — Incremental implementation plan`, `AuthenticationRequest`, `CreateProductCommand`, `AuthenticationResponse`, `Contexto técnico relevante`, `.ToActionResult`, `5. Target code standards`, `Current configuration names`, `Phase6HttpContractFunctionalTests`, `ChangePasswordRequest`, `CustomWebApplicationFactory`?**
  _High betweenness centrality (0.074) - this node is a cross-community bridge._
- **Are the 29 inferred relationships involving `IndexedPerson` (e.g. with `Current solution structure` and `Testes adicionados`) actually correct?**
  _`IndexedPerson` has 29 INFERRED edges - model-reasoned connections that need verification._
- **Should `Container (Dockerfile)` be split into smaller, more focused modules?**
  _Cohesion score 0.048625792811839326 - nodes in this community are weakly interconnected._