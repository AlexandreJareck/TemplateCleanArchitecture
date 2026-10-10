# Graph Report - TemplateCleanArchNET  (2026-10-10)

## Corpus Check
- 238 files · ~71,232 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 14 file(s) not represented in the graph (top: (none) 4, .resx 4, .jsonl 2)

## Summary
- 2255 nodes · 4647 edges · 144 communities (89 shown, 55 thin omitted)
- Extraction: 90% EXTRACTED · 10% INFERRED · 0% AMBIGUOUS · INFERRED: 450 edges (avg confidence: 0.89)
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
- Relatório da Fase 9 — 2026-10-10
- IClassFixture
- system
- Template.Application.DTOs.Products
- UpdateProductCommand
- .GetRepository
- ValidationBehavior
- ApplicationDbContext
- Template.WebApi.csproj
- PagedResponse
- DistributedProductCacheTests
- ProductRepository
- Template.Application.Interfaces
- OrderMessagingBrokerTests
- ProductController
- ProductFunctionalTests
- ArchitectureRulesTests
- ProductDto
- Application
- GetProductByIdQuery
- Phase 9 Execution Handoff
- Phase 3 — Incremental implementation plan
- Template.Application.csproj
- AuthenticationResponse
- CreateProductCommand
- Current solution structure
- BaseResult
- .BuildModel
- ElasticExtensions.cs
- Template.Application.Wrappers
- Fase 5 em detalhe
- SwaggerExtensions.cs
- .AbortQuietlyAsync
- RabbitMqTestSupport.cs
- AccountServices
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
- DeliveryDecision
- .Decode
- Template.Infrastructure.Persistence.csproj
- Template.Unit.Tests.csproj
- IncomingMessageContext
- Product
- Phase 11 Execution Handoff
- InitialPostgresMigration
- ApiResultFilterAttribute
- IOrderEventPublisher
- Template.Integration.Tests.csproj
- ScriptedHandler
- InitialPostgresMigration
- MessageFunctionalTests.cs
- ProductPriceDecimal
- Phase6HttpContractFunctionalTests
- Template.Architecture.Tests.csproj
- Template.Application.DTOs
- Fases 6–7 — bloqueio de pré-condições (2026-10-10)
- .GetGhostAccount
- ElasticSearchSettings
- RabbitMqOrderConsumerService
- contract-checklist.md
- boundaries.md
- verification-matrix.md
- postgresql-review.md
- adapter-checklist.md
- upgrade-record.md
- AuditableBaseEntity
- .AddMessagingInfrastructure
- init-db.sh
- skill-activation-report.md
- handoff-fases-6-7.md
- Core/AGENTS.md
- Infrastructure/AGENTS.md
- Presentation/AGENTS.md
- Tests/AGENTS.md
- GenericRepository
- DeliveryAttemptTracker
- ErrorCode
- RabbitMqOrderEventPublisher
- Contexto técnico relevante
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
- AccountController
- api-v1-lifecycle.md
- .AddPerson
- ApiResultMapper
- RecordingHandler
- RabbitMqFixture
- CustomWebApplicationFactory
- Template.Infrastructure.Messaging.csproj
- OrderPlacedEvent
- MessagingHarness
- UserDto
- IdentityContext
- TranslatorMessageDto
- Template.Infrastructure.Identity/ServiceRegistration.cs
- ResourceGeneral
- .Log
- DisabledOrderEventPublisher
- Order
- .HandleAsync
- InMemoryProcessedMessageStore
- ErrorCodeEnum
- Required tests
- ElasticService.cs
- .DeclareAsync
- CountingProcessedMessageStore
- IProcessedMessageStore
- Error
- OrderDispatch

## God Nodes (most connected - your core abstractions)
1. `BaseResult` - 76 edges
2. `ProductDto` - 50 edges
3. `OrderPlacedEvent` - 46 edges
4. `ProductEntity` - 40 edges
5. `Template.Application.Wrappers` - 36 edges
6. `ArchitectureRulesTests` - 32 edges
7. `Template.Application.Interfaces` - 30 edges
8. `RabbitMqSettings` - 29 edges
9. `OrderMessagingBrokerTests` - 27 edges
10. `Phase 12 Execution Handoff` - 27 edges

## Surprising Connections (you probably didn't know these)
- `Phase 8 — Move Redis caching behind an infrastructure adapter` --references--> `IProductCache`  [INFERRED]
  docs/fase-3-plano-implementacao.md → Src/Core/Template.Application/Interfaces/Caching/IProductCache.cs
- `Riscos, pendências e rollback` --references--> `IUnitOfWork`  [INFERRED]
  docs/refactoring/phases-6-7-report.md → Src/Core/Template.Application/Interfaces/IUnitOfWork.cs
- `Application port` --references--> `IElasticService`  [INFERRED]
  docs/refactoring/handoff-fase-10.md → Src/Core/Template.Application/Services/Elastic/IElasticService.cs
- `Não verificado e riscos` --references--> `ProductService`  [INFERRED]
  docs/refactoring/phase-8-report.md → Src/Core/Template.Application/Services/Product/ProductService.cs
- `Errors and API results` --references--> `BaseResult`  [INFERRED]
  docs/fase-2-workspace.md → Src/Core/Template.Application/Wrappers/BaseResult.cs

## Import Cycles
- None detected.

## Communities (144 total, 55 thin omitted)

### Community 0 - "shouldly"
Cohesion: 0.29
Nodes (4): Template.Unit.Tests.DomainTests.Products.Entities, Template.Unit.Tests.ApplicationTests.Wrappers, Template.Domain.Products.Entities, Template.Integration.Tests.Data

### Community 1 - "ResourceMessages"
Cohesion: 0.17
Nodes (8): Template.Infrastructure.Resources.ProjectResources, ResourceMessages, Account_NotFound_with_UserName, Culture, Invalid_password, Product_NotFound_with_id, ResourceManager, Username_is_already_taken

### Community 2 - "Person"
Cohesion: 0.14
Nodes (9): Current state to characterize, Determining the active requirement, ElasticService, IElasticService, Person, FirstName, Id, LastName (+1 more)

### Community 3 - "Container (Dockerfile)"
Cohesion: 0.05
Nodes (43): commandName, environmentVariables, launchBrowser, launchUrl, publishAllPorts, useSSL, ASPNETCORE_ENVIRONMENT, ASPNETCORE_HTTP_PORTS (+35 more)

### Community 5 - ".Handle"
Cohesion: 0.28
Nodes (4): DeleteProductCommand, Id, DeleteProductCommandHandler, DeleteProductCommandHandlerTests

### Community 7 - "Relatório da Fase 9 — 2026-10-10"
Cohesion: 0.14
Nodes (13): Adapters e hosted services (Infrastructure.Messaging), APIs 7.x usadas, Breaking changes tratados, Comandos e resultados, Confirmações, Contrato `order.placed` v1, Fontes oficiais, Gates não executados (+5 more)

### Community 8 - "IClassFixture"
Cohesion: 0.27
Nodes (3): ProductCacheDisabledFunctionalTests, ProductCacheFunctionalTests, ProductCacheScenario

### Community 9 - "system"
Cohesion: 0.24
Nodes (4): Template.Infrastructure.Identity.Migrations, Template.Infrastructure.Identity.Contexts, Template.Domain.Common, Template.Infrastructure.Persistence.Migrations

### Community 10 - "Template.Application.DTOs.Products"
Cohesion: 0.20
Nodes (6): Template.Application.DTOs.Products, Template.Application.Interfaces.Caching, Template.Integration.Tests.Caching, Template.Infrastructure.Caching, Template.Application.Services.Product, Template.Infrastructure.Caching.Products

### Community 11 - "UpdateProductCommand"
Cohesion: 0.22
Nodes (7): UpdateProductCommand, BarCode, Id, Name, Price, UpdateProductCommandHandler, UpdateProductCommandHandlerTests

### Community 12 - ".GetRepository"
Cohesion: 0.13
Nodes (6): BaseEfRepoTestFixture, EfCancellationPropagationTests, EfRepositoryAdd, EfRepositoryDelete, EfRepositoryUpdate, ProductPersistencePrecisionTests

### Community 13 - "ValidationBehavior"
Cohesion: 0.06
Nodes (30): Arquivos alterados nesta execução, Automação e documentação, Comandos e resultados, Confirmação de escopo, Contrato e WebApi, Diff de contrato e baseline, Fase 6 — controllers e contratos HTTP, Fase 7 — persistência e PostgreSQL (+22 more)

### Community 14 - "ApplicationDbContext"
Cohesion: 0.12
Nodes (4): ApplicationDbContext, Products, ApplicationDbContextFactory, DefaultData

### Community 15 - "Template.WebApi.csproj"
Cohesion: 0.12
Nodes (16): Asp.Versioning.Mvc.ApiExplorer, AspNetCore.HealthChecks.Elasticsearch, AspNetCore.HealthChecks.UI.Client, Elastic.Serilog.Sinks, Microsoft.AspNetCore.OpenApi, Microsoft.EntityFrameworkCore.Tools, Microsoft.VisualStudio.Azure.Containers.Tools.Targets, Serilog.AspNetCore (+8 more)

### Community 16 - "PagedResponse"
Cohesion: 0.09
Nodes (14): PaginationResponseDto, Count, Data, PageNumber, PageSize, GetPagedListProductQuery, Name, GetPagedListProductQueryHandler (+6 more)

### Community 17 - "DistributedProductCacheTests"
Cohesion: 0.06
Nodes (9): CacheSettings, AbsoluteExpiration, Enabled, OperationTimeout, SlidingExpiration, DistributedProductCache, DistributedProductCacheTests, FailingDistributedCache (+1 more)

### Community 18 - "ProductRepository"
Cohesion: 0.15
Nodes (8): ProductRepository, PostgreSqlCollection, PostgreSqlFixture, TestAuthenticatedUser, UserId, UserName, PostgreSqlPersistenceTests, RabbitMqCollection

### Community 19 - "Template.Application.Interfaces"
Cohesion: 0.11
Nodes (15): Template.Unit.Tests.ApplicationTests.Features.Products.Queries, Template.Application.Interfaces, Template.Application, Template.Application.Features.Products.Queries, Template.Unit.Tests.ApplicationTests.Services, Template.Unit.Tests.ApplicationTests.Features.Products.Commands, Template.Application.Services.Elastic, Template.Unit.Tests.ApplicationTests.Behaviors (+7 more)

### Community 24 - "ProductDto"
Cohesion: 0.13
Nodes (7): Phase 4 — Clean the Domain model, ProductDto, BarCode, CreatedDateTime, Id, Name, Price

### Community 25 - "Application"
Cohesion: 0.18
Nodes (10): Application, Infrastructure — `Template.Infrastructure.Caching` (novo), Não verificado e riscos, O que mudou, OpenAPI e baseline (`docs/refactoring/baseline-fase-6`), Regras de arquitetura, Relatório da Fase 8 — 2026-10-10, Testes adicionados ou alterados (+2 more)

### Community 26 - "GetProductByIdQuery"
Cohesion: 0.25
Nodes (3): GetProductByIdQuery, Id, ProductServiceTests

### Community 27 - "Phase 9 Execution Handoff"
Cohesion: 0.08
Nodes (23): Acceptance criteria, Architecture tests, Asynchronous publishing, Availability and startup, Configuration and security, Consumption, acknowledgements, and delivery, Documentation and delivery, Event contracts (+15 more)

### Community 28 - "Phase 3 — Incremental implementation plan"
Cohesion: 0.08
Nodes (23): Decisions (recorded 2026-10-10), Global execution rules, Phase 0 — Freeze a reproducible baseline, Phase 10 — Move Elastic concerns behind infrastructure boundaries, Phase 11 — Upgrade the Elastic client and server stack, Phase 12 — Harden identity and JWT boundaries, Phase 14 — Establish a reproducible Compose baseline, Phase 15 — Consolidate Redis services on Redis 8 (+15 more)

### Community 29 - "Template.Application.csproj"
Cohesion: 0.14
Nodes (13): Elastic.Clients.Elasticsearch, FluentValidation.DependencyInjectionExtensions, MediatR, Microsoft.Extensions.Logging.Abstractions, Microsoft.Extensions.Options, net10.0, Microsoft.NET.Sdk, Template.Domain (+5 more)

### Community 30 - "AuthenticationResponse"
Cohesion: 0.16
Nodes (11): AuthenticationRequest, Password, UserName, AuthenticationResponse, Email, Id, IsVerified, JwToken (+3 more)

### Community 31 - "CreateProductCommand"
Cohesion: 0.26
Nodes (6): CreateProductCommand, BarCode, Name, Price, CreateProductCommandHandler, CreateProductCommandHandlerTests

### Community 32 - "Current solution structure"
Cohesion: 0.18
Nodes (6): Current solution structure, Pré-condição: Fase 8, IProductCache, ProductService, NoOpProductCache, ServiceRegistration

### Community 33 - "BaseResult"
Cohesion: 0.25
Nodes (5): BaseResult, Data, Errors, Success, AccountFunctionalTests

### Community 36 - "Template.Application.Wrappers"
Cohesion: 0.12
Nodes (11): Template.Application.Wrappers, Template.WebApi.Infrastructure.Filters, Template.Functional.Tests.ApiEndpoints.v2, Template.WebApi.Infrastructure.Middlewares, Template.WebApi.Controllers.v2, Template.Infrastructure.Identity.Services, Template.Application.DTOs.Account.Request, Template.WebApi.Infrastructure.Http (+3 more)

### Community 37 - "Fase 5 em detalhe"
Cohesion: 0.24
Nodes (9): Fase 5 em detalhe, ErrorCategory, Conflict, Forbidden, NotFound, Unauthorized, Unexpected, Validation (+1 more)

### Community 38 - "SwaggerExtensions.cs"
Cohesion: 0.08
Nodes (4): Template.WebApi.Infrastructure.Extensions, CorsExtensions, ConfigureSwaggerOptions, SwaggerExtensions

### Community 40 - "RabbitMqTestSupport.cs"
Cohesion: 0.12
Nodes (14): Template.Integration.Tests.Messaging, Template.Infrastructure.Messaging.Publishing, Template.Infrastructure.Messaging.Serialization, Template.Application.Features.Orders.EventHandlers, Template.Application.Interfaces.Messaging, Template.Application.Messaging.Orders, Template.Unit.Tests.ApplicationTests.Features.Orders, Template.Infrastructure.Messaging.Topology (+6 more)

### Community 41 - "AccountServices"
Cohesion: 0.16
Nodes (5): Phase 13 — Standardize observability, health, and localization, ITranslator, AccountServices, ServiceRegistration, Translator

### Community 42 - "TemplateCleanArchitecture.sln"
Cohesion: 0.20
Nodes (3): Template, Microsoft.VisualStudio.SDK, Microsoft.VSSDK.BuildTools

### Community 43 - "5. Target code standards"
Cohesion: 0.08
Nodes (23): 1. Evidence and assumptions, 2. Proposed AI workspace, 3. Dependency and integration update matrix, 4. Target Clean Architecture, 5. Target code standards, Application use cases, Controllers, Design principles (+15 more)

### Community 44 - "microsoft_entityframeworkcore"
Cohesion: 0.14
Nodes (6): Template.Infrastructure.Persistence.Repositories, Template.Infrastructure.Persistence, Template.PostgreSql.Integration.Tests, Template.Infrastructure.Persistence.Contexts, Template.Infrastructure.Persistence.Seeds, Template.Infrastructure.Persistence.Extensions

### Community 45 - "Template.Functional.Tests.csproj"
Cohesion: 0.20
Nodes (9): Microsoft.AspNetCore.Mvc.Testing, net10.0, coverlet.collector, Microsoft.Extensions.Hosting.Abstractions, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio (+1 more)

### Community 47 - "ProductEntity"
Cohesion: 0.14
Nodes (7): Template.Infrastructure.Persistence.Contexts.Configurations, ProductEntity, BarCode, Name, Price, ProductConfiguration, ProductTests

### Community 48 - "IdentitySettings"
Cohesion: 0.09
Nodes (17): Current configuration names, Template.Infrastructure.Identity.Settings, LocalizationSettings, DefaultRequestCulture, SupportedCultures, IdentitySettings, PasswordRequireDigit, PasswordRequiredLength (+9 more)

### Community 50 - "Template.Infrastructure.Identity"
Cohesion: 0.25
Nodes (8): Microsoft.AspNetCore.Authentication.JwtBearer, Microsoft.AspNetCore.Identity.EntityFrameworkCore, System.IdentityModel.Tokens.Jwt, Template.Infrastructure.Identity, net10.0, Microsoft.EntityFrameworkCore.InMemory, Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.NET.Sdk

### Community 51 - "Phase 0 baseline — 2026-10-10"
Cohesion: 0.25
Nodes (5): Baseline validation before refactoring, Dependency and vulnerability baseline, Phase 0 baseline — 2026-10-10, Source state, Toolchain

### Community 52 - "UnitOfWork"
Cohesion: 0.17
Nodes (3): IUnitOfWork, UnitOfWork, ServiceRegistration

### Community 53 - "DeliveryDecision"
Cohesion: 0.29
Nodes (7): DeliveryAction, Ack, DeadLetter, Requeue, DeliveryDecision, Ack, Requeue

### Community 54 - ".Decode"
Cohesion: 0.09
Nodes (14): DecodeResult, Incompatible, Invalid, DecodeStatus, Decoded, IncompatibleContract, InvalidPayload, OrderPlacedMessageSerializer (+6 more)

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

### Community 62 - "IOrderEventPublisher"
Cohesion: 0.17
Nodes (3): IOrderEventPublisher, MessagingAdapterWithoutBrokerTests, NoOpHandler

### Community 63 - "Template.Integration.Tests.csproj"
Cohesion: 0.14
Nodes (12): Microsoft.Extensions.Caching.Memory, Microsoft.Extensions.Caching.StackExchangeRedis, net10.0, Microsoft.Extensions.Options.ConfigurationExtensions, Microsoft.NET.Sdk, net10.0, coverlet.collector, Microsoft.NET.Test.Sdk (+4 more)

### Community 64 - "ScriptedHandler"
Cohesion: 0.32
Nodes (3): OrderDeliveryProcessorTests, ScriptedHandler, Calls

### Community 69 - "Template.Architecture.Tests.csproj"
Cohesion: 0.29
Nodes (6): net10.0, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk

### Community 70 - "Template.Application.DTOs"
Cohesion: 0.17
Nodes (3): Template.Infrastructure.Resources, Template.Application.DTOs, Template.Infrastructure.Resources.Services

### Community 71 - "Fases 6–7 — bloqueio de pré-condições (2026-10-10)"
Cohesion: 0.33
Nodes (5): Causa do bloqueio, Como desbloquear, Estado do Git registrado, Fases 6–7 — bloqueio de pré-condições (2026-10-10), Pré-condições

### Community 73 - "ElasticSearchSettings"
Cohesion: 0.22
Nodes (9): Configuration and security, Separation between search, logging, and health checks, ElasticSearchSettings, DataSet, DefaultIndex, Name, Namespace, Type (+1 more)

### Community 74 - "RabbitMqOrderConsumerService"
Cohesion: 0.15
Nodes (4): ReceivedDelivery, RabbitMqOrderConsumerService, OrderSubscription, DeadLetterQueue

### Community 81 - "AuditableBaseEntity"
Cohesion: 0.12
Nodes (13): Change and validation rules, Current implementation conventions, Execution status, Repository purpose, TemplateCleanArchNET agent guide, Verified commands (2026-10-10, Phase 8 checkpoint), AuditableBaseEntity, Created (+5 more)

### Community 82 - ".AddMessagingInfrastructure"
Cohesion: 0.20
Nodes (4): Defeitos encontrados pelos testes com broker e corrigidos, Testes adicionados, ServiceRegistration, ServiceRegistrationTests

### Community 85 - "handoff-fases-6-7.md"
Cohesion: 0.17
Nodes (9): Template.WebApi.Controllers, Entrega, Estado de partida (verificado em 2026-10-10 às 14:26), Execução de comandos, Fase 6 — controllers e contratos HTTP, Fase 7 — persistência e PostgreSQL (somente depois da Fase 6 aprovada), Fontes obrigatórias (ler antes de alterar qualquer arquivo), Regras gerais (+1 more)

### Community 100 - "GenericRepository"
Cohesion: 0.14
Nodes (3): IGenericRepository, GenericRepository, DbContext

### Community 102 - "ErrorCode"
Cohesion: 0.18
Nodes (7): ErrorCode, AccessDenied, ErrorInIdentity, Exception, FieldDataInvalid, ModelStateNotValid, NotFound

### Community 104 - "Contexto técnico relevante"
Cohesion: 0.11
Nodes (10): Time handling, Contexto técnico relevante, IClock, UtcNow, IProductRepository, SystemClock, UtcNow, EntityFrameworkExtensions (+2 more)

### Community 105 - "ChangePasswordRequest"
Cohesion: 0.15
Nodes (9): ChangePasswordRequest, ConfirmPassword, Password, ChangePasswordRequestValidator, ChangeUserNameRequest, UserName, ChangeUserNameRequestValidator, IAccountServices (+1 more)

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
Cohesion: 0.04
Nodes (46): 401 and 403 behavior, Account functional tests, Account-use-case standardization, Application may own, Architecture boundaries, Architecture tests, Authorization tests, Checkout and working tree (+38 more)

### Community 112 - "Phase 10 Execution Handoff"
Cohesion: 0.10
Nodes (20): Acceptance criteria, Application port, Availability policy, Checkout and working tree, Contracts and indexed documents, Documentation and delivery, Feature flag and rollback, Health check (+12 more)

### Community 114 - "MessageFunctionalTests"
Cohesion: 0.14
Nodes (4): Reconexão, startup, shutdown e indisponibilidade, MessagePublishException, MessageFunctionalTests, Publisher

### Community 120 - "RecordingHandler"
Cohesion: 0.21
Nodes (3): RecordingHandler, Behavior, Calls

### Community 121 - "RabbitMqFixture"
Cohesion: 0.14
Nodes (5): Eventually, RabbitMqFixture, Container, Host, Port

### Community 124 - "CustomWebApplicationFactory"
Cohesion: 0.08
Nodes (10): Program, ExternalServicesUnavailableTests, MessageWithoutBrokerFunctionalTests, RecordingPublisherFactory, Publisher, CacheDisabledFactory, AdditionalConfiguration, ProductValidationFunctionalTests (+2 more)

### Community 125 - "Template.Infrastructure.Messaging.csproj"
Cohesion: 0.13
Nodes (13): RabbitMQ.Client, Testcontainers.RabbitMq, net10.0, Microsoft.Extensions.Hosting.Abstractions, Microsoft.Extensions.Options.ConfigurationExtensions, Microsoft.NET.Sdk, net10.0, Microsoft.Extensions.DependencyInjection (+5 more)

### Community 126 - "OrderPlacedEvent"
Cohesion: 0.18
Nodes (7): OrderPlacedEvent, RecordingOrderEventPublisher, BlockUntilCancelled, FailWith, ObservedCancellation, Published, Started

### Community 127 - "MessagingHarness"
Cohesion: 0.15
Nodes (4): CapturedLog, MessagingHarness, Consumer, Publisher

### Community 128 - "UserDto"
Cohesion: 0.10
Nodes (14): Template.Application.Parameters, GetAllUsersRequest, Name, UserDto, Created, Email, Id, Name (+6 more)

### Community 129 - "IdentityContext"
Cohesion: 0.07
Nodes (11): Acceptance criteria, Current-state characterization, IGetUserServices, IdentityContext, ApplicationRole, ApplicationUser, Created, Name (+3 more)

### Community 130 - "TranslatorMessageDto"
Cohesion: 0.18
Nodes (6): TranslatorMessageDto, Args, Text, AccountMessages, ProductMessages, TranslatorMessages

### Community 131 - "Template.Infrastructure.Identity/ServiceRegistration.cs"
Cohesion: 0.18
Nodes (4): Template.Infrastructure.Identity.Seeds, Template.Infrastructure.Identity, Template.WebApi.Infrastructure.Services, Template.Infrastructure.Identity.Models

### Community 133 - "ResourceGeneral"
Cohesion: 0.18
Nodes (9): ResourceGeneral, BarCode, ConfirmPassword, Culture, Id, Name, Password, ResourceManager (+1 more)

### Community 137 - "Order"
Cohesion: 0.16
Nodes (11): Template.Domain.Order, Código e ciclo de vida, Inventário anterior (estado de `f293cb9`), Mensagens, ack e falhas, Topologia, Order, Id, Price (+3 more)

### Community 140 - "InMemoryProcessedMessageStore"
Cohesion: 0.24
Nodes (3): InMemoryProcessedMessageStore, CountingStore, Marks

### Community 144 - "ErrorCodeEnum"
Cohesion: 0.25
Nodes (7): Template.WebApi.Infrastructure.Enums, ErrorCodeEnum, Conflict, Forbidden, Unauthorized, Unexpected, Validation

### Community 145 - "Required tests"
Cohesion: 0.25
Nodes (8): Adapter tests, Architecture tests, Functional tests, Health-check tests, Logging tests, Required tests, Tests with real Elasticsearch, Unit tests

### Community 149 - "CountingProcessedMessageStore"
Cohesion: 0.33
Nodes (3): CountingProcessedMessageStore, Checks, Marks

### Community 151 - "IProcessedMessageStore"
Cohesion: 0.15
Nodes (8): Current state to characterize, Configuração e DI, Política de ack, retry, poison e idempotência, Resumo do design, OrderPlacedEventHandler, IOrderPlacedEventHandler, IProcessedMessageStore, ServiceRegistration

### Community 152 - "Error"
Cohesion: 0.33
Nodes (4): Error, Description, ErrorCode, FieldName

### Community 155 - "OrderDispatch"
Cohesion: 0.33
Nodes (5): Ports e contratos criados (Application), OrderDispatch, Broadcast, Direct, Topic

## Knowledge Gaps
- **526 isolated node(s):** `Microsoft.VisualStudio.SDK`, `Microsoft.VSSDK.BuildTools`, `UserName`, `Password`, `Password` (+521 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 956 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **55 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Current solution structure` connect `Current solution structure` to `Person`, `DisabledOrderEventPublisher`, `RabbitMqOrderEventPublisher`, `AccountServices`, `Order`, `.Handle`, `RabbitMqOrderConsumerService`, `RabbitMqSettings`, `DistributedProductCacheTests`, `AuditableBaseEntity`, `.DeclareAsync`, `IProcessedMessageStore`, `IOrderEventPublisher`, `OrderPlacedEvent`?**
  _High betweenness centrality (0.122) - this node is a cross-community bridge._
- **Are the 4 inferred relationships involving `BaseResult` (e.g. with `Current implementation conventions` and `Errors and API results`) actually correct?**
  _`BaseResult` has 4 INFERRED edges - model-reasoned connections that need verification._
- **What connects `Microsoft.VisualStudio.SDK`, `Microsoft.VSSDK.BuildTools`, `UserName` to the rest of the system?**
  _526 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Person` be split into smaller, more focused modules?**
  _Cohesion score 0.13793103448275862 - nodes in this community are weakly interconnected._
- **Why does `BaseResult` connect `BaseResult` to `.ToActionResult`, `.Handle`, `ErrorCategoryTests`, `IClassFixture`, `UpdateProductCommand`, `PagedResponse`, `ProductController`, `ProductFunctionalTests`, `ProductDto`, `Error`, `GetProductByIdQuery`, `Phase 3 — Incremental implementation plan`, `AuthenticationResponse`, `CreateProductCommand`, `Fase 5 em detalhe`, `AccountServices`, `5. Target code standards`, `GetProductByIdQueryHandler`, `Phase6HttpContractFunctionalTests`, `.GetGhostAccount`, `AuditableBaseEntity`, `Contexto técnico relevante`, `ChangePasswordRequest`, `ApiResultMapper`?**
  _High betweenness centrality (0.094) - this node is a cross-community bridge._
- **Are the 6 inferred relationships involving `ProductDto` (e.g. with `Immediate boundary corrections` and `Phase 4 — Clean the Domain model`) actually correct?**
  _`ProductDto` has 6 INFERRED edges - model-reasoned connections that need verification._
- **Should `Container (Dockerfile)` be split into smaller, more focused modules?**
  _Cohesion score 0.048625792811839326 - nodes in this community are weakly interconnected._