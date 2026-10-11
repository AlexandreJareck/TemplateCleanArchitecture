# Graph Report - TemplateCleanArchNET  (2026-10-11)

## Corpus Check
- 329 files · ~151,925 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 14 file(s) not represented in the graph (top: (none) 4, .resx 4, .jsonl 2)

## Summary
- 3712 nodes · 7978 edges · 205 communities (132 shown, 73 thin omitted)
- Extraction: 89% EXTRACTED · 11% INFERRED · 0% AMBIGUOUS · INFERRED: 914 edges (avg confidence: 0.89)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `dc209429`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- Phase 13 Report — Observability, health, and localization
- ResourceMessages
- Person
- Container (Dockerfile)
- ApiResultMapper
- .Handle
- ErrorHandlerMiddleware
- DisabledOrderEventPublisher
- Relatório da Fase 11 — 2026-10-11
- Template.Domain.Products.Entities
- LoggingAndCorrelationTests
- UpdateProductCommand
- .GetRepository
- ValidationBehavior
- ApplicationDbContext
- Template.WebApi.csproj
- PagedResponse
- DistributedProductCacheTests
- UnitOfWork
- Template.Application.Wrappers
- OrderMessagingBrokerTests
- ProductController
- JwtAuthenticationFunctionalTests
- ArchitectureRulesTests
- IProductCache
- NoOpProductCache
- Phase 17 Execution Handoff
- Phase 16 Execution Handoff
- Phase 11 Execution Handoff
- Template.Application.csproj
- AuditableBaseEntity
- CreateProductCommand
- RecordingPersonSearchIndex
- BaseResult
- .BuildModel
- Template.Application/ServiceRegistration.cs
- microsoft_extensions_dependencyinjection
- ErrorCode
- ConfigureSwaggerOptions
- Phase 15 Execution Handoff
- RabbitMqTestSupport.cs
- AccountServices
- TemplateCleanArchitecture.sln
- 5. Target code standards
- Current solution structure
- Template.Functional.Tests.csproj
- Phase 3 — Incremental implementation plan
- ProductEntity
- LocalizationSettings
- HttpClientGetExtensionMethods
- Template.Infrastructure.Identity
- Phase 0 baseline — 2026-10-10
- IUnitOfWork
- Phase 9 Execution Handoff
- .Decode
- Template.Infrastructure.Persistence.csproj
- Template.Unit.Tests.csproj
- .InitializeIdentityDatabaseAsync
- Product
- Template.Application.Search.Persons
- IndexedPerson
- IOrderEventPublisher
- Template.Integration.Tests.csproj
- OrderDeliveryProcessorTests
- InitialPostgresMigration
- shouldly
- ProductPriceDecimal
- Phase6HttpContractFunctionalTests
- Template.Architecture.Tests.csproj
- Relatório da Fase 9 — 2026-10-10
- Fases 6–7 — bloqueio de pré-condições (2026-10-10)
- PersonIndexResult
- IPersonSearchIndex
- RabbitMqOrderConsumerService
- contract-checklist.md
- boundaries.md
- verification-matrix.md
- postgresql-review.md
- adapter-checklist.md
- upgrade-record.md
- PersonIndexStatus
- ServiceRegistrationTests
- init-db.sh
- skill-activation-report.md
- handoff-phases-6-7.md
- Core/AGENTS.md
- Infrastructure/AGENTS.md
- Presentation/AGENTS.md
- Tests/AGENTS.md
- Relevant technical context
- Phase 12 Execution Handoff
- ElasticsearchPersonSearchIndex
- OrderPlacedEvent
- Program.cs
- ChangeUserNameRequest
- Relatório das Fases 0–5 — 2026-10-10
- PublishOrderCommand
- Template.PostgreSql.Integration.Tests.csproj
- IProcessedMessageStore
- RabbitMqSettings
- Phase 19 Handoff — Upgrade Test and Coverage Tooling
- HealthCheckComponentTests
- .Create
- RecordingOrderEventPublisher
- Create Phase 14–21 Handoffs and Execution Prompts
- api-v1-lifecycle.md
- PersonController
- LocalizationFunctionalTests
- .AddDependencyHealthChecks
- RecordingHandler
- RabbitMqFixture
- Phase 13 Execution Handoff
- PersonBulkIndexResult
- CustomWebApplicationFactory
- Template.Infrastructure.Messaging.csproj
- .Supported_8_19_To_9_5_Path_PreservesSyntheticData_AndLeavesTheSourceUntouched
- Template.Infrastructure.Persistence.Contexts
- UserDto
- Phase 18 Execution Handoff
- TranslatorMessageDto
- Phase 20 Handoff — Isolate and Modernize the VSIX Project
- ElasticLoggingTests
- ResourceGeneral
- .Log
- Phase 21 Handoff — Final Consistency and Release Gate
- Phase 10 Execution Handoff
- Order
- elastic-upgrade-rehearsal.ps1
- Template.Infrastructure.Search.csproj
- .ToActionResult
- IdentityComponentTests
- ElasticLoggingSettings
- ElasticLogSinkDiagnostics
- ErrorCodeEnum
- ChangePasswordRequest
- Template.WebApi.Infrastructure.HealthChecks
- GetProductByIdQuery
- DeliveryDecision
- CountingProcessedMessageStore
- CapturingLogger
- ElasticsearchHealthCheckSettings
- DatabaseHealthCheck
- UnreachableLogSinkFactory
- HealthEndpointTests
- Relatório das Fases 6–7 — 2026-10-10
- OrderDeliveryProcessor
- ITranslator
- CapturingSink
- DecodeResult
- IClassFixture
- ScopeCapturingLogger
- CorsExtensions
- HangingCache
- .StopSessionAsync
- .WriteAsync
- .AddIdentityInfrastructure
- FailingCache
- InMemoryProcessedMessageStore
- MessagingHarness
- .SinkAssembly_BindsAgainstTheResolvedTransport
- GetProductByIdQueryHandler
- ListLogger
- IAuthenticatedUserService
- .Diagnostics_AreThrottled_AndNeverThrow
- system
- ProductDto
- DistributedCacheHealthCheck
- PostgreSqlFixture.cs
- Required tests
- Required tests
- SystemClock
- OrderMessagingTopology
- Required tests
- IncomingMessageContext
- Código, DI e ciclo de vida
- Phase 12 report — Harden Identity and JWT boundaries
- In-scope consistency work
- Availability policy
- execute-phase-12.md
- execute-phase-13.md
- execute-phase-14.md
- execute-phase-15.md
- execute-phase-16.md
- execute-phase-17.md
- execute-phase-18.md
- execute-phase-19.md
- execute-phase-20.md
- execute-phase-21.md

## God Nodes (most connected - your core abstractions)
1. `BaseResult` - 79 edges
2. `IndexedPerson` - 63 edges
3. `ArchitectureRulesTests` - 56 edges
4. `ProductDto` - 50 edges
5. `OrderPlacedEvent` - 49 edges
6. `PersonIndexResult` - 45 edges
7. `ProductEntity` - 40 edges
8. `Template.Application.Wrappers` - 38 edges
9. `ElasticsearchPersonSearchIndexTests` - 36 edges
10. `Template.Application.Interfaces` - 34 edges

## Surprising Connections (you probably didn't know these)
- `Phase 4 — Clean the Domain model` --references--> `ProductDto`  [INFERRED]
  docs/phase-3-implementation-plan.md → Src/Core/Template.Application/DTOs/Products/ProductDto.cs
- `Riscos, pendências e rollback` --references--> `IUnitOfWork`  [INFERRED]
  docs/refactoring/phases-6-7-report.md → Src/Core/Template.Application/Interfaces/IUnitOfWork.cs
- `Acceptance criteria` --references--> `OrderPlacedEvent`  [INFERRED]
  docs/refactoring/handoff-phase-17.md → Src/Core/Template.Application/Messaging/Orders/OrderPlacedEvent.cs
- `Topology and message-contract compatibility` --references--> `OrderPlacedEvent`  [INFERRED]
  docs/refactoring/handoff-phase-17.md → Src/Core/Template.Application/Messaging/Orders/OrderPlacedEvent.cs
- `Não verificado e riscos` --references--> `ProductService`  [INFERRED]
  docs/refactoring/phase-8-report.md → Src/Core/Template.Application/Services/Product/ProductService.cs

## Import Cycles
- None detected.

## Communities (205 total, 73 thin omitted)

### Community 0 - "Phase 13 Report — Observability, health, and localization"
Cohesion: 0.06
Nodes (18): Commands executed and results, Correlation policy, Dashboard compatibility mapping, Executive summary, Final test counts, Gates not executed, Logging package review, OpenAPI comparison (+10 more)

### Community 1 - "ResourceMessages"
Cohesion: 0.22
Nodes (7): ResourceMessages, Account_NotFound_with_UserName, Culture, Invalid_password, Product_NotFound_with_id, ResourceManager, Username_is_already_taken

### Community 2 - "Person"
Cohesion: 0.25
Nodes (7): Template.Domain.Person, Determining the active requirement, Sensitive-data redaction, Person, FirstName, Id, LastName

### Community 3 - "Container (Dockerfile)"
Cohesion: 0.05
Nodes (43): commandName, environmentVariables, launchBrowser, launchUrl, publishAllPorts, useSSL, ASPNETCORE_ENVIRONMENT, ASPNETCORE_HTTP_PORTS (+35 more)

### Community 4 - "ApiResultMapper"
Cohesion: 0.10
Nodes (3): Pre-change inventory (no values copied), ApiResultMapper, ProblemDetailsAuthorizationMiddlewareResultHandler

### Community 5 - ".Handle"
Cohesion: 0.28
Nodes (4): DeleteProductCommand, Id, DeleteProductCommandHandler, DeleteProductCommandHandlerTests

### Community 6 - "ErrorHandlerMiddleware"
Cohesion: 0.13
Nodes (4): Log ownership and log-once policy, ApiResultFilterAttribute, ErrorHandlerMiddleware, ErrorCategoryTests

### Community 8 - "Relatório da Fase 11 — 2026-10-11"
Cohesion: 0.07
Nodes (27): Ativos Elastic do cluster (fonte do ensaio, criada pela WebApi 8.x real), Caminho de upgrade adotado, Comandos executados e resultados, Comparação do OpenAPI com `docs/refactoring/baseline-phase-6/`, Configuração e comportamento, Confirmações, Contagens finais, Dados usados (+19 more)

### Community 9 - "Template.Domain.Products.Entities"
Cohesion: 0.11
Nodes (6): Template.Infrastructure.Persistence.Repositories, Template.Unit.Tests.DomainTests.Products.Entities, Template.Application.DTOs, Template.Domain.Products.Entities, Template.Integration.Tests.Data, Template.Infrastructure.Persistence.Extensions

### Community 10 - "LoggingAndCorrelationTests"
Cohesion: 0.10
Nodes (8): Tests added or updated, CapturingSink, Events, LogCapturingFactory, Sink, LoggingAndCorrelationTests, Sink, LogEventsTests

### Community 11 - "UpdateProductCommand"
Cohesion: 0.22
Nodes (7): UpdateProductCommand, BarCode, Id, Name, Price, UpdateProductCommandHandler, UpdateProductCommandHandlerTests

### Community 12 - ".GetRepository"
Cohesion: 0.13
Nodes (6): BaseEfRepoTestFixture, EfCancellationPropagationTests, EfRepositoryAdd, EfRepositoryDelete, EfRepositoryUpdate, ProductPersistencePrecisionTests

### Community 13 - "ValidationBehavior"
Cohesion: 0.09
Nodes (13): ValidationBehavior, AuthenticationRequestValidator, CreateProductCommandValidator, UpdateProductCommandValidator, ConfiguredFactory, AdditionalConfiguration, IdentityOptionsStartupTests, NameRequiredValidator (+5 more)

### Community 14 - "ApplicationDbContext"
Cohesion: 0.06
Nodes (26): Acceptance criteria, Acceptance criteria, Checkout and working-tree safety, Current-state inventory before editing, Documentation and delivery, Health checks and deterministic startup, Isolated baseline environment, Migrations and seed idempotency (+18 more)

### Community 15 - "Template.WebApi.csproj"
Cohesion: 0.15
Nodes (12): Asp.Versioning.Mvc.ApiExplorer, AspNetCore.HealthChecks.UI.Client, Elastic.Serilog.Sinks, Microsoft.AspNetCore.OpenApi, Microsoft.EntityFrameworkCore.Tools, Microsoft.VisualStudio.Azure.Containers.Tools.Targets, Serilog.AspNetCore, Swashbuckle.AspNetCore.SwaggerGen (+4 more)

### Community 16 - "PagedResponse"
Cohesion: 0.09
Nodes (14): PaginationResponseDto, Count, Data, PageNumber, PageSize, GetPagedListProductQuery, Name, GetPagedListProductQueryHandler (+6 more)

### Community 17 - "DistributedProductCacheTests"
Cohesion: 0.06
Nodes (10): Current-state inventory before editing, CacheSettings, AbsoluteExpiration, Enabled, OperationTimeout, SlidingExpiration, DistributedProductCache, DistributedProductCacheTests (+2 more)

### Community 18 - "UnitOfWork"
Cohesion: 0.15
Nodes (6): UnitOfWork, PostgreSqlFixture, TestAuthenticatedUser, UserId, UserName, PostgreSqlPersistenceTests

### Community 19 - "Template.Application.Wrappers"
Cohesion: 0.10
Nodes (21): Template.Unit.Tests.ApplicationTests.Features.Products.Queries, Template.Application.Wrappers, Template.Application.Interfaces, Template.Application.Features.Products.Queries, Template.WebApi.Infrastructure.Filters, Template.Functional.Tests.ApiEndpoints.v2, Template.WebApi.Controllers.v2, Template.Application.DTOs.Products (+13 more)

### Community 22 - "JwtAuthenticationFunctionalTests"
Cohesion: 0.13
Nodes (5): JwtAuthenticationFunctionalTests, Settings, FixedClock, UtcNow, SecurityTestSupport

### Community 24 - "IProductCache"
Cohesion: 0.13
Nodes (7): Phase 8 — Move Redis caching behind an infrastructure adapter, Prerequisites, Pré-condições verificadas (Fases 8 e 9), Pré-condição: Fase 8, IProductCache, IProductService, ProductService

### Community 25 - "NoOpProductCache"
Cohesion: 0.11
Nodes (12): Application, Infrastructure — `Template.Infrastructure.Caching` (novo), Não verificado e riscos, O que mudou, OpenAPI e baseline (`docs/refactoring/baseline-phase-6`), Regras de arquitetura, Relatório da Fase 8 — 2026-10-10, Testes adicionados ou alterados (+4 more)

### Community 26 - "Phase 17 Execution Handoff"
Cohesion: 0.07
Nodes (29): Acceptance criteria, Architecture and functional checks, Broker behavior tests, Checkout and working-tree safety, Configuration, security, and observability, Consumption and delivery, Cutover and rollback, Definitions and secret handling (+21 more)

### Community 27 - "Phase 16 Execution Handoff"
Cohesion: 0.06
Nodes (32): Acceptance criteria, Additional PostgreSQL 18 acceptance, only if authorized, Backup and restore rehearsal for both databases, Backup/restore tests, Checkout and working-tree safety, Cutover gate, Data safety and isolated environments, Documentation and delivery (+24 more)

### Community 28 - "Phase 11 Execution Handoff"
Cohesion: 0.06
Nodes (30): Acceptance criteria, Adapter contract tests, Checkout and working tree, Current-state inventory, Data safety and destructive-action boundaries, Deprecation and upgrade checks, Documentation and delivery, Elasticsearch and Kibana images (+22 more)

### Community 29 - "Template.Application.csproj"
Cohesion: 0.15
Nodes (12): FluentValidation.DependencyInjectionExtensions, MediatR, Microsoft.Extensions.Options, net10.0, Microsoft.Extensions.Logging.Abstractions, Microsoft.NET.Sdk, Template.Domain, net10.0 (+4 more)

### Community 30 - "AuditableBaseEntity"
Cohesion: 0.12
Nodes (13): Change and validation rules, Current implementation conventions, Repository language and file naming, Repository purpose, TemplateCleanArchNET agent guide, Verified commands (2026-10-11, Phase 12 checkpoint), AuditableBaseEntity, Created (+5 more)

### Community 31 - "CreateProductCommand"
Cohesion: 0.26
Nodes (6): CreateProductCommand, BarCode, Name, Price, CreateProductCommandHandler, CreateProductCommandHandlerTests

### Community 32 - "RecordingPersonSearchIndex"
Cohesion: 0.12
Nodes (11): RecordingPersonSearchIndex, BlockUntilCancelled, Calls, Listed, NextStatus, ObservedCancellation, Started, Stored (+3 more)

### Community 33 - "BaseResult"
Cohesion: 0.10
Nodes (17): AuthenticationRequest, Password, UserName, AuthenticationResponse, Email, Id, IsVerified, JwToken (+9 more)

### Community 35 - "Template.Application/ServiceRegistration.cs"
Cohesion: 0.09
Nodes (7): Template.Application, Template.Unit.Tests.ApplicationTests.Services, Template.Unit.Tests.ApplicationTests.Behaviors, Template.Application.Behaviors, Template.Application.Services.Time, Template.Unit.Tests.ApplicationTests, ServiceRegistration

### Community 36 - "microsoft_extensions_dependencyinjection"
Cohesion: 0.12
Nodes (7): Template.Infrastructure.Identity.Seeds, Template.Infrastructure.Identity.Authentication, Template.Infrastructure.Identity.Contexts, Template.Functional.Tests.Security, Template.Infrastructure.Identity.Settings, Template.WebApi.Infrastructure.Services, Template.Infrastructure.Identity.Models

### Community 37 - "ErrorCode"
Cohesion: 0.09
Nodes (21): Exception and Problem Details behavior, Fase 5 em detalhe, Error, Description, ErrorCode, FieldName, ErrorCategory, Conflict (+13 more)

### Community 39 - "Phase 15 Execution Handoff"
Cohesion: 0.08
Nodes (25): Acceptance criteria, ACL and configuration, Adapter and integration tests, Checkout and working-tree safety, Compose and removal checks, Cutover and rollback, Data, serialization, expiry, and command compatibility, Documentation and delivery (+17 more)

### Community 40 - "RabbitMqTestSupport.cs"
Cohesion: 0.11
Nodes (17): Template.Application.Logging, Template.Integration.Tests.Messaging, Template.Infrastructure.Messaging.Publishing, Template.Infrastructure.Messaging.Serialization, Template.Application.Features.Orders.EventHandlers, Template.Application.Interfaces.Messaging, Template.Application.Messaging.Orders, Template.Unit.Tests.ApplicationTests.Features.Orders (+9 more)

### Community 41 - "AccountServices"
Cohesion: 0.14
Nodes (4): ApplicationUser, Created, Name, AccountServices

### Community 42 - "TemplateCleanArchitecture.sln"
Cohesion: 0.20
Nodes (3): Template, Microsoft.VisualStudio.SDK, Microsoft.VSSDK.BuildTools

### Community 43 - "5. Target code standards"
Cohesion: 0.08
Nodes (23): 1. Evidence and assumptions, 2. Proposed AI workspace, 3. Dependency and integration update matrix, 4. Target Clean Architecture, 5. Target code standards, Application use cases, Controllers, Design principles (+15 more)

### Community 44 - "Current solution structure"
Cohesion: 0.10
Nodes (20): Current solution structure, Adapter (Infrastructure.Search), Busca e indexação, Classificação de falhas, Comandos executados e resultados, Comparação do OpenAPI com `docs/refactoring/baseline-phase-6/`, Confirmações, Decisão sobre o requisito ativo (+12 more)

### Community 45 - "Template.Functional.Tests.csproj"
Cohesion: 0.20
Nodes (9): Microsoft.AspNetCore.Mvc.Testing, net10.0, coverlet.collector, Microsoft.Extensions.Hosting.Abstractions, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio (+1 more)

### Community 46 - "Phase 3 — Incremental implementation plan"
Cohesion: 0.08
Nodes (23): Decisions (recorded 2026-10-10), Global execution rules, Phase 0 — Freeze a reproducible baseline, Phase 10 — Move Elastic concerns behind infrastructure boundaries, Phase 11 — Upgrade the Elastic client and server stack, Phase 12 — Harden identity and JWT boundaries, Phase 14 — Establish a reproducible Compose baseline, Phase 15 — Consolidate Redis services on Redis 8 (+15 more)

### Community 47 - "ProductEntity"
Cohesion: 0.12
Nodes (8): Template.Infrastructure.Persistence.Contexts.Configurations, ProductEntity, BarCode, Name, Price, ProductConfiguration, DefaultData, ProductTests

### Community 48 - "LocalizationSettings"
Cohesion: 0.11
Nodes (10): Localization registration and options, Riscos residuais, Confirmations, `BuildServiceProvider()` removal, Confirmations, LocalizationSettings, DefaultRequestCulture, SupportedCultures (+2 more)

### Community 50 - "Template.Infrastructure.Identity"
Cohesion: 0.25
Nodes (8): Microsoft.AspNetCore.Authentication.JwtBearer, Microsoft.AspNetCore.Identity.EntityFrameworkCore, System.IdentityModel.Tokens.Jwt, Template.Infrastructure.Identity, net10.0, Microsoft.EntityFrameworkCore.InMemory, Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.NET.Sdk

### Community 51 - "Phase 0 baseline — 2026-10-10"
Cohesion: 0.33
Nodes (5): Baseline validation before refactoring, Dependency and vulnerability baseline, Phase 0 baseline — 2026-10-10, Source state, Toolchain

### Community 53 - "Phase 9 Execution Handoff"
Cohesion: 0.08
Nodes (23): Acceptance criteria, Architecture tests, Asynchronous publishing, Availability and startup, Configuration and security, Consumption, acknowledgements, and delivery, Documentation and delivery, Event contracts (+15 more)

### Community 54 - ".Decode"
Cohesion: 0.14
Nodes (7): OrderPlacedMessageSerializer, OrderPlacedMessageV1, Id, Price, ProductName, Quantity, OrderPlacedMessageSerializerTests

### Community 55 - "Template.Infrastructure.Persistence.csproj"
Cohesion: 0.22
Nodes (8): Microsoft.EntityFrameworkCore, Microsoft.Extensions.Configuration, net10.0, Microsoft.EntityFrameworkCore.Design, Microsoft.EntityFrameworkCore.InMemory, Microsoft.Extensions.DependencyInjection, Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.NET.Sdk

### Community 56 - "Template.Unit.Tests.csproj"
Cohesion: 0.22
Nodes (8): Moq, net10.0, coverlet.collector, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk

### Community 57 - ".InitializeIdentityDatabaseAsync"
Cohesion: 0.07
Nodes (5): Health model, ApplicationRole, DefaultBasicUser, DefaultRoles, ServiceRegistration

### Community 58 - "Product"
Cohesion: 0.24
Nodes (5): Account, ApiRoutes, Product, V1, V2

### Community 59 - "Template.Application.Search.Persons"
Cohesion: 0.13
Nodes (8): Template.Integration.Tests.Search, Template.Tests.Shared, Template.Unit.Tests.ApplicationTests.Search, Template.Application.Search.Persons, Template.Infrastructure.Search.Persons, Template.Unit.Tests.ApplicationTests.Features.Persons, Template.Infrastructure.Search, Template.Application.Interfaces.Search

### Community 61 - "IndexedPerson"
Cohesion: 0.05
Nodes (13): IndexedPerson, FakeElasticsearchServer, Handler, Port, Requests, Url, FakeResponse, RecordedRequest (+5 more)

### Community 62 - "IOrderEventPublisher"
Cohesion: 0.17
Nodes (3): IOrderEventPublisher, MessagingAdapterWithoutBrokerTests, NoOpHandler

### Community 63 - "Template.Integration.Tests.csproj"
Cohesion: 0.14
Nodes (12): Microsoft.Extensions.Caching.Memory, Microsoft.Extensions.Caching.StackExchangeRedis, net10.0, Microsoft.Extensions.Options.ConfigurationExtensions, Microsoft.NET.Sdk, net10.0, coverlet.collector, Microsoft.NET.Test.Sdk (+4 more)

### Community 64 - "OrderDeliveryProcessorTests"
Cohesion: 0.30
Nodes (3): OrderDeliveryProcessorTests, ScriptedHandler, Calls

### Community 66 - "shouldly"
Cohesion: 0.15
Nodes (4): Template.Functional.Tests.ApiEndpoints, Template.Unit.Tests.ApplicationTests.Wrappers, Template.Functional.Tests.Localization, Template.Functional.Tests.Common

### Community 69 - "Template.Architecture.Tests.csproj"
Cohesion: 0.29
Nodes (6): net10.0, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk

### Community 70 - "Relatório da Fase 9 — 2026-10-10"
Cohesion: 0.08
Nodes (22): Adapters e hosted services (Infrastructure.Messaging), APIs 7.x usadas, Breaking changes tratados, Comandos e resultados, Comparação do OpenAPI com `docs/refactoring/baseline-phase-6/`, Confirmações, Contrato `order.placed` v1, Fontes oficiais (+14 more)

### Community 71 - "Fases 6–7 — bloqueio de pré-condições (2026-10-10)"
Cohesion: 0.33
Nodes (5): Causa do bloqueio, Como desbloquear, Estado do Git registrado, Fases 6–7 — bloqueio de pré-condições (2026-10-10), Pré-condições

### Community 72 - "PersonIndexResult"
Cohesion: 0.07
Nodes (18): Template.Application.Features.Persons.Queries, Template.Application.Features.Persons.Commands, Testes adicionados, EnsurePersonIndexCommand, EnsurePersonIndexCommandHandler, IndexPersonCommand, IndexPersonCommandHandler, RemovePersonCommand (+10 more)

### Community 73 - "IPersonSearchIndex"
Cohesion: 0.12
Nodes (8): IPersonSearchIndex, PersonSearchSettings, Enabled, IndexName, RequestTimeout, Url, ServiceRegistration, SearchRegistrationTests

### Community 74 - "RabbitMqOrderConsumerService"
Cohesion: 0.20
Nodes (4): ReceivedDelivery, RabbitMqOrderConsumerService, OrderSubscription, DeadLetterQueue

### Community 81 - "PersonIndexStatus"
Cohesion: 0.15
Nodes (9): PersonIndexStatus, IncompatibleDocument, NotFound, Rejected, Succeeded, Unavailable, PersonFunctionalTests, Index (+1 more)

### Community 85 - "handoff-phases-6-7.md"
Cohesion: 0.14
Nodes (11): Template.WebApi.Controllers, Command execution, Delivery, General rules, Phase 6 — controllers and HTTP contracts, Phase 7 — persistence and PostgreSQL (only after Phase 6 is approved), Required sources (read before changing any file), Starting state (verified on 2026-10-10 at 14:26) (+3 more)

### Community 100 - "Relevant technical context"
Cohesion: 0.10
Nodes (8): Relevant technical context, Fase 7 — persistência e PostgreSQL, IGenericRepository, IProductRepository, GenericRepository, DbContext, ProductRepository, ServiceRegistration

### Community 101 - "Phase 12 Execution Handoff"
Cohesion: 0.07
Nodes (26): 401 and 403 behavior, Account-use-case standardization, Application may own, Architecture boundaries, Checkout and working tree, Documentation and delivery, Domain must remain free of, HTTP and API compatibility (+18 more)

### Community 102 - "ElasticsearchPersonSearchIndex"
Cohesion: 0.13
Nodes (6): Mudanças no adapter (`ElasticsearchPersonSearchIndex`), ElasticsearchPersonSearchIndex, PersonDocument, FirstName, Id, LastName

### Community 104 - "Program.cs"
Cohesion: 0.09
Nodes (10): Template.Infrastructure.Persistence, Template.Infrastructure.Identity, Template.WebApi.Infrastructure.Middlewares, Template.WebApi.Infrastructure.Http, Template.Infrastructure.Resources, Template.Infrastructure.Persistence.Seeds, Template.WebApi.Infrastructure.Extensions, Template.Infrastructure.Caching (+2 more)

### Community 105 - "ChangeUserNameRequest"
Cohesion: 0.17
Nodes (5): ChangeUserNameRequest, UserName, ChangeUserNameRequestValidator, IAccountServices, AccountController

### Community 106 - "Relatório das Fases 0–5 — 2026-10-10"
Cohesion: 0.22
Nodes (8): Comparação de contrato, Confirmação, Mudanças por fase, Relatório das Fases 0–5 — 2026-10-10, Revisão da migration e do SQL (Fase 4), Riscos e pendências, Testes e gates (execução de 2026-10-10, 14:25–14:26), Versões e fontes oficiais

### Community 107 - "PublishOrderCommand"
Cohesion: 0.16
Nodes (4): PublishOrderCommand, PublishOrderCommandHandler, PublishOrderCommandHandlerTests, Handler

### Community 108 - "Template.PostgreSql.Integration.Tests.csproj"
Cohesion: 0.25
Nodes (7): Testcontainers.PostgreSql, net10.0, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk

### Community 109 - "IProcessedMessageStore"
Cohesion: 0.11
Nodes (9): Current state to characterize, Configuração e DI, Política de ack, retry, poison e idempotência, Resumo do design, OrderPlacedEventHandler, IOrderPlacedEventHandler, IProcessedMessageStore, OrderPlacedEventHandlerTests (+1 more)

### Community 110 - "RabbitMqSettings"
Cohesion: 0.09
Nodes (17): Defeitos encontrados pelos testes com broker e corrigidos, Testes adicionados, RabbitMqSettings, ConnectionTimeout, ConsumersEnabled, Enabled, HostName, MaxDeliveryAttempts (+9 more)

### Community 111 - "Phase 19 Handoff — Upgrade Test and Coverage Tooling"
Cohesion: 0.09
Nodes (21): 1. Baseline and guardrails, 2. Microsoft.NET.Test.Sdk and Moq, 3. Coverlet and coverage pipeline, 4. xUnit Visual Studio runner decision, 5. Pragmatic coverage thresholds by layer, Acceptance criteria, Authoring-time checkpoint to verify, not assume, Dependencies and stop conditions (+13 more)

### Community 114 - "RecordingOrderEventPublisher"
Cohesion: 0.11
Nodes (8): MessageFunctionalTests, Publisher, RecordingOrderEventPublisher, BlockUntilCancelled, FailWith, ObservedCancellation, Published, Started

### Community 115 - "Create Phase 14–21 Handoffs and Execution Prompts"
Cohesion: 0.09
Nodes (21): Create Phase 14–21 Handoffs and Execution Prompts, Deliverables, Detailed handoff, Final response, Phase 14 — Reproducible Compose baseline, Phase 15 — Redis 8 consolidation, Phase 16 — PostgreSQL upgrade, Phase 17 — RabbitMQ server upgrade (+13 more)

### Community 117 - "PersonController"
Cohesion: 0.25
Nodes (4): Current state to characterize, HTTP compatibility, PersonController, NotFound

### Community 118 - "LocalizationFunctionalTests"
Cohesion: 0.16
Nodes (3): ConfiguredFactory, AdditionalConfiguration, LocalizationFunctionalTests

### Community 119 - ".AddDependencyHealthChecks"
Cohesion: 0.11
Nodes (4): Rollback procedure, HealthCheckExtensions, HealthStatusTracker, ProcessHealthCheck

### Community 120 - "RecordingHandler"
Cohesion: 0.21
Nodes (3): RecordingHandler, Behavior, Calls

### Community 121 - "RabbitMqFixture"
Cohesion: 0.19
Nodes (4): RabbitMqFixture, Container, Host, Port

### Community 122 - "Phase 13 Execution Handoff"
Cohesion: 0.10
Nodes (20): Acceptance criteria, Checkout and working tree, Correlation and tracing, Dependency-specific health behavior, Documentation and delivery, Exception handling and log-once behavior, Health endpoint compatibility, Health model (+12 more)

### Community 123 - "PersonBulkIndexResult"
Cohesion: 0.16
Nodes (7): PersonBulkIndexResult, FailedIds, IndexedIds, IsComplete, IsPartial, Status, PersonIndexResultTests

### Community 124 - "CustomWebApplicationFactory"
Cohesion: 0.08
Nodes (12): Pre-change inventory, Program, ExternalServicesUnavailableTests, MessageWithoutBrokerFunctionalTests, RecordingPublisherFactory, Publisher, CacheDisabledFactory, AdditionalConfiguration (+4 more)

### Community 125 - "Template.Infrastructure.Messaging.csproj"
Cohesion: 0.13
Nodes (13): RabbitMQ.Client, Testcontainers.RabbitMq, net10.0, Microsoft.Extensions.Hosting.Abstractions, Microsoft.Extensions.Options.ConfigurationExtensions, Microsoft.NET.Sdk, net10.0, Microsoft.Extensions.DependencyInjection (+5 more)

### Community 126 - ".Supported_8_19_To_9_5_Path_PreservesSyntheticData_AndLeavesTheSourceUntouched"
Cohesion: 0.05
Nodes (16): Current configuration names, ElasticsearchHealthCheck, ElasticsearchAvailabilityTests, ElasticsearchFixture, Container, Http, Url, ElasticsearchImage (+8 more)

### Community 127 - "Template.Infrastructure.Persistence.Contexts"
Cohesion: 0.29
Nodes (4): Template.Infrastructure.Identity.Migrations, Template.Infrastructure.Persistence.Contexts, Template.Infrastructure.Persistence.Migrations, InitialPostgresMigration

### Community 128 - "UserDto"
Cohesion: 0.09
Nodes (16): Template.Application.Parameters, Current-state characterization, GetAllUsersRequest, Name, UserDto, Created, Email, Id (+8 more)

### Community 129 - "Phase 18 Execution Handoff"
Cohesion: 0.10
Nodes (19): Acceptance criteria, Application and release independence, Checkout and working-tree safety, Compose and configuration changes, Current-state inventory before editing, Disposable-tooling boundary, Documentation and delivery, New target volume and isolated rehearsal (+11 more)

### Community 130 - "TranslatorMessageDto"
Cohesion: 0.18
Nodes (6): TranslatorMessageDto, Args, Text, AccountMessages, ProductMessages, TranslatorMessages

### Community 131 - "Phase 20 Handoff — Isolate and Modernize the VSIX Project"
Cohesion: 0.10
Nodes (19): 1. Make support policy explicit, 2. Create a VSIX-specific build path, 3. Update within the Visual Studio SDK 17.x line, 4. Verify installation and runtime behavior, 5. Keep the application gate independent, Acceptance criteria, Adopted decision and prerequisites, Authoring-time checkpoint to verify, not assume (+11 more)

### Community 133 - "ResourceGeneral"
Cohesion: 0.18
Nodes (9): ResourceGeneral, BarCode, ConfirmPassword, Culture, Id, Name, Password, ResourceManager (+1 more)

### Community 135 - "Phase 21 Handoff — Final Consistency and Release Gate"
Cohesion: 0.11
Nodes (18): Acceptance criteria, Binding decisions and boundaries, Cleanup evidence standard, Complete final validation matrix, Current version, security, and license research, Dependencies and hard stop conditions, Documentation reconciliation, Final response (+10 more)

### Community 136 - "Phase 10 Execution Handoff"
Cohesion: 0.11
Nodes (17): Acceptance criteria, Application port, Checkout and working tree, Configuration and security, Contracts and indexed documents, Documentation and delivery, Feature flag and rollback, Infrastructure adapter (+9 more)

### Community 137 - "Order"
Cohesion: 0.16
Nodes (11): Template.Domain.Order, Código e ciclo de vida, Inventário anterior (estado de `f293cb9`), Mensagens, ack e falhas, Topologia, Order, Id, Price (+3 more)

### Community 138 - "elastic-upgrade-rehearsal.ps1"
Cohesion: 0.23
Nodes (16): Assert-Absent(), Copy-OwnedTemplates(), Es(), EsJson(), Get-OwnedTemplates(), Get-View(), Invoke-Http(), New-Snapshot() (+8 more)

### Community 139 - "Template.Infrastructure.Search.csproj"
Cohesion: 0.14
Nodes (12): Elastic.Clients.Elasticsearch, Testcontainers, net10.0, Microsoft.Extensions.Logging.Abstractions, Microsoft.Extensions.Options.ConfigurationExtensions, Microsoft.NET.Sdk, net10.0, Microsoft.NET.Test.Sdk (+4 more)

### Community 141 - "IdentityComponentTests"
Cohesion: 0.10
Nodes (3): IdentityComponentTests, StubCurrentUser, UserName

### Community 142 - "ElasticLoggingSettings"
Cohesion: 0.10
Nodes (12): Configuração e DI, Pacote do health check removido, Separação entre busca, logging e health check, Mudanças no sink Serilog, ElasticLoggingSettings, DataSet, Enabled, Namespace (+4 more)

### Community 144 - "ErrorCodeEnum"
Cohesion: 0.25
Nodes (7): Template.WebApi.Infrastructure.Enums, ErrorCodeEnum, Conflict, Forbidden, Unauthorized, Unexpected, Validation

### Community 145 - "ChangePasswordRequest"
Cohesion: 0.19
Nodes (5): ChangePasswordRequest, ConfirmPassword, Password, ChangePasswordRequestValidator, AccountController

### Community 146 - "Template.WebApi.Infrastructure.HealthChecks"
Cohesion: 0.08
Nodes (7): Template.Functional.Tests.Observability, Template.WebApi.Infrastructure.Logging, Template.WebApi.Infrastructure.HealthChecks, Template.Integration.Tests.Caching, Template.Elasticsearch.Integration.Tests, HealthTags, SerialLoggingCollection

### Community 147 - "GetProductByIdQuery"
Cohesion: 0.25
Nodes (3): GetProductByIdQuery, Id, ProductServiceTests

### Community 148 - "DeliveryDecision"
Cohesion: 0.29
Nodes (7): DeliveryAction, Ack, DeadLetter, Requeue, DeliveryDecision, Ack, Requeue

### Community 149 - "CountingProcessedMessageStore"
Cohesion: 0.33
Nodes (3): CountingProcessedMessageStore, Checks, Marks

### Community 151 - "ElasticsearchHealthCheckSettings"
Cohesion: 0.15
Nodes (7): Pré-condições verificadas (Fase 10), ElasticsearchHealthCheckSettings, Enabled, FailureStatus, Name, Timeout, Url

### Community 152 - "DatabaseHealthCheck"
Cohesion: 0.11
Nodes (3): DatabaseHealthCheck, TcpEndpointHealthCheck, ThrowingCheck

### Community 153 - "UnreachableLogSinkFactory"
Cohesion: 0.20
Nodes (3): LoggingSinkUnavailableHostTests, UnreachableLogSinkFactory, AdditionalConfiguration

### Community 154 - "HealthEndpointTests"
Cohesion: 0.27
Nodes (3): CheckView, HealthEndpointTests, HealthFactory

### Community 155 - "Relatório das Fases 6–7 — 2026-10-10"
Cohesion: 0.11
Nodes (17): Arquivos alterados nesta execução, Automação e documentação, Comandos e resultados, Confirmação de escopo, Contrato e WebApi, Diff de contrato e baseline, Migrations e SQL, Persistência e testes (+9 more)

### Community 157 - "ITranslator"
Cohesion: 0.22
Nodes (8): Phase 13 — Standardize observability, health, and localization, Current-state inventory, Localization boundary, Scope of Phase 13, Localization, ITranslator, ServiceRegistration, Translator

### Community 159 - "DecodeResult"
Cohesion: 0.29
Nodes (7): DecodeResult, Incompatible, Invalid, DecodeStatus, Decoded, IncompatibleContract, InvalidPayload

### Community 160 - "IClassFixture"
Cohesion: 0.15
Nodes (5): ProductCacheDisabledFunctionalTests, ProductCacheFunctionalTests, ProductCacheScenario, ProductFunctionalTests, RandomDataExtensionMethods

### Community 161 - "ScopeCapturingLogger"
Cohesion: 0.15
Nodes (3): Restore, ScopeCapturingLogger, Entries

### Community 166 - ".AddIdentityInfrastructure"
Cohesion: 0.08
Nodes (13): IdentitySettings, PasswordRequireDigit, PasswordRequiredLength, PasswordRequireLowercase, PasswordRequireNonAlphanumeric, PasswordRequireUppercase, IdentitySettingsValidator, JwtSettings (+5 more)

### Community 168 - "InMemoryProcessedMessageStore"
Cohesion: 0.24
Nodes (3): InMemoryProcessedMessageStore, CountingStore, Marks

### Community 169 - "MessagingHarness"
Cohesion: 0.17
Nodes (4): CapturedLog, MessagingHarness, Consumer, Publisher

### Community 173 - "IAuthenticatedUserService"
Cohesion: 0.10
Nodes (14): Current-user and claims boundary, Current-user policy, IAuthenticatedUserService, UserId, UserName, ApplicationDbContextFactory, DesignTimeAuthenticatedUser, Instance (+6 more)

### Community 174 - ".Diagnostics_AreThrottled_AndNeverThrow"
Cohesion: 0.20
Nodes (3): ManualTimeProvider, ThrowingWriter, Encoding

### Community 176 - "ProductDto"
Cohesion: 0.25
Nodes (6): ProductDto, BarCode, CreatedDateTime, Id, Name, Price

### Community 178 - "PostgreSqlFixture.cs"
Cohesion: 0.33
Nodes (3): Template.PostgreSql.Integration.Tests, PostgreSqlCollection, RabbitMqCollection

### Community 179 - "Required tests"
Cohesion: 0.25
Nodes (8): Adapter tests, Architecture tests, Functional tests, Health-check tests, Logging tests, Required tests, Tests with real Elasticsearch, Unit tests

### Community 180 - "Required tests"
Cohesion: 0.25
Nodes (8): Account functional tests, Architecture tests, Authorization tests, JWT validation tests, Options and startup tests, Persistence and migration tests, Required tests, Unit tests

### Community 181 - "SystemClock"
Cohesion: 0.33
Nodes (3): SystemClock, UtcNow, SystemClockTests

### Community 182 - "OrderMessagingTopology"
Cohesion: 0.25
Nodes (3): Current-state inventory before editing, OrderMessagingTopology, Subscriptions

### Community 183 - "Required tests"
Cohesion: 0.33
Nodes (6): Architecture tests, Exception and Problem Details tests, Health tests, Localization tests, Logging and correlation tests, Required tests

### Community 186 - "Código, DI e ciclo de vida"
Cohesion: 0.50
Nodes (4): Comportamento HTTP legado (smoke da WebApi legada e caracterização do client), Código, DI e ciclo de vida, Inventário da integração anterior, Requisições e formato do documento (caracterizados contra um Elasticsearch 8.17.4 descartável)

### Community 189 - "Phase 12 report — Harden Identity and JWT boundaries"
Cohesion: 0.07
Nodes (23): Execution status, Time handling, 401/403 and HTTPS metadata, Boundary decisions, Commands executed and results, Executive summary, Gates not executed, JWT issuance and validation policy (+15 more)

### Community 190 - "In-scope consistency work"
Cohesion: 0.33
Nodes (6): 1. Compatibility shims and temporary adapters, 2. Architecture exceptions and allowlists, 3. Dead or redundant packages and code, 4. API and duplicate code, 5. Images, configuration, and documentation, In-scope consistency work

### Community 194 - "Availability policy"
Cohesion: 0.50
Nodes (4): Availability policy, Health check, Logging, Search and indexing

## Knowledge Gaps
- **857 isolated node(s):** `Microsoft.VisualStudio.SDK`, `Microsoft.VSSDK.BuildTools`, `UserName`, `Password`, `Password` (+852 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 1584 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **73 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Current solution structure` connect `Current solution structure` to `Person`, `DisabledOrderEventPublisher`, `Order`, `DistributedProductCacheTests`, `IProductCache`, `NoOpProductCache`, `ITranslator`, `AuditableBaseEntity`, `LocalizationSettings`, `OrderMessagingTopology`, `IndexedPerson`, `IOrderEventPublisher`, `PersonIndexResult`, `IPersonSearchIndex`, `RabbitMqOrderConsumerService`, `PersonIndexStatus`, `ElasticsearchPersonSearchIndex`, `OrderPlacedEvent`, `PublishOrderCommand`, `IProcessedMessageStore`, `RabbitMqSettings`, `PersonBulkIndexResult`, `.Supported_8_19_To_9_5_Path_PreservesSyntheticData_AndLeavesTheSourceUntouched`?**
  _High betweenness centrality (0.127) - this node is a cross-community bridge._
- **Are the 5 inferred relationships involving `BaseResult` (e.g. with `Current implementation conventions` and `Errors and API results`) actually correct?**
  _`BaseResult` has 5 INFERRED edges - model-reasoned connections that need verification._
- **What connects `Microsoft.VisualStudio.SDK`, `Microsoft.VSSDK.BuildTools`, `UserName` to the rest of the system?**
  _857 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Phase 13 Report — Observability, health, and localization` be split into smaller, more focused modules?**
  _Cohesion score 0.06294326241134751 - nodes in this community are weakly interconnected._
- **Why does `Pre-change inventory` connect `CustomWebApplicationFactory` to `Phase 13 Report — Observability, health, and localization`, `ResourceMessages`, `ApiResultMapper`, `ErrorCode`, `ResourceGeneral`, `ErrorHandlerMiddleware`, `Phase6HttpContractFunctionalTests`, `ElasticLoggingTests`, `.SinkAssembly_BindsAgainstTheResolvedTransport`, `ElasticLoggingSettings`, `LocalizationSettings`, `IndexedPerson`, `ITranslator`?**
  _High betweenness centrality (0.061) - this node is a cross-community bridge._
- **Are the 31 inferred relationships involving `IndexedPerson` (e.g. with `Current solution structure` and `Testes adicionados`) actually correct?**
  _`IndexedPerson` has 31 INFERRED edges - model-reasoned connections that need verification._
- **Should `Container (Dockerfile)` be split into smaller, more focused modules?**
  _Cohesion score 0.048625792811839326 - nodes in this community are weakly interconnected._