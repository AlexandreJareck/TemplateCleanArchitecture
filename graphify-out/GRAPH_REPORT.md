# Graph Report - TemplateCleanArchNET  (2026-10-11)

## Corpus Check
- 308 files · ~139,531 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 14 file(s) not represented in the graph (top: (none) 4, .resx 4, .jsonl 2)

## Summary
- 3366 nodes · 7123 edges · 211 communities (133 shown, 78 thin omitted)
- Extraction: 89% EXTRACTED · 11% INFERRED · 0% AMBIGUOUS · INFERRED: 776 edges (avg confidence: 0.88)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `2842b5bc`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- IdentitySettings
- ResourceMessages
- Person
- Container (Dockerfile)
- ApiResultMapper
- .Handle
- ErrorCategoryTests
- DisabledOrderEventPublisher
- Relatório da Fase 11 — 2026-10-11
- shouldly
- DistributedProductCacheTests.cs
- UpdateProductCommand
- .GetRepository
- ValidationBehavior
- ApplicationDbContext
- Template.WebApi.csproj
- GetPagedListProductQuery
- Task
- ProductRepository
- Template.Application.Wrappers
- OrderMessagingBrokerTests
- ProductController
- JwtAuthenticationFunctionalTests
- ArchitectureRulesTests
- ProductDto
- IProductCache
- OrderPlacedEvent
- Phase 16 Execution Handoff
- Phase 11 Execution Handoff
- Template.Application.csproj
- AuditableBaseEntity
- CreateProductCommand
- OrderDispatch
- BaseResult
- .BuildModel
- Template.Application/ServiceRegistration.cs
- microsoft_extensions_dependencyinjection
- Relevant technical context
- ConfigureSwaggerOptions
- Phase 15 Execution Handoff
- RabbitMqTestSupport.cs
- ApplicationUser
- TemplateCleanArchitecture.sln
- 5. Target code standards
- Relatório da Fase 10 — 2026-10-10
- Template.Functional.Tests.csproj
- Phase 3 — Incremental implementation plan
- ProductEntity
- LocalizationExtensions
- HttpClientGetExtensionMethods
- Template.Infrastructure.Identity
- Phase 0 baseline — 2026-10-10
- UnitOfWork
- Phase 9 Execution Handoff
- .Decode
- Template.Infrastructure.Persistence.csproj
- Template.Unit.Tests.csproj
- .InitializeIdentityDatabaseAsync
- Product
- Template.Application.Search.Persons
- InitialPostgresMigration
- IndexedPerson
- IOrderEventPublisher
- Template.Integration.Tests.csproj
- ScriptedHandler
- InitialPostgresMigration
- Template.Functional.Tests.Common
- ProductPriceDecimal
- Phase6HttpContractFunctionalTests
- Template.Architecture.Tests.csproj
- Current solution structure
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
- handoff-phases-6-7.md
- Core/AGENTS.md
- Infrastructure/AGENTS.md
- Presentation/AGENTS.md
- Tests/AGENTS.md
- GenericRepository
- Phase 12 Execution Handoff
- ElasticsearchPersonSearchIndex
- RabbitMqOrderEventPublisher
- Program.cs
- ChangePasswordRequest
- Relatório das Fases 0–5 — 2026-10-10
- PublishOrderCommand
- Template.PostgreSql.Integration.Tests.csproj
- OrderPlacedEventHandlerTests
- RabbitMqSettings
- Phase 19 Handoff — Upgrade Test and Coverage Tooling
- PersonSearchIndexElasticsearchTests
- .Create
- MessageFunctionalTests
- Create Phase 14–21 Handoffs and Execution Prompts
- api-v1-lifecycle.md
- PersonController
- RecordingOrderEventPublisher
- PersonIndexResult
- RecordingHandler
- RabbitMqFixture
- Phase 13 Execution Handoff
- PersonBulkIndexResult
- CustomWebApplicationFactory
- Template.Infrastructure.Messaging.csproj
- .Supported_8_19_To_9_5_Path_PreservesSyntheticData_AndLeavesTheSourceUntouched
- system
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
- ElasticsearchFixture
- IdentityComponentTests
- ElasticLoggingSettings
- ElasticLogSinkDiagnostics
- ErrorCodeEnum
- Phase 14 Execution Handoff
- microsoft_extensions_configuration
- GetProductByIdQuery
- DeliveryDecision
- CountingProcessedMessageStore
- CapturingLogger
- ElasticsearchHealthCheckSettings
- ElasticsearchHealthCheck
- UnreachableLogSinkFactory
- DistributedProductCache
- Relatório das Fases 6–7 — 2026-10-10
- DistributedProductCacheTests
- ITranslator
- CapturingSink
- DecodeResult
- IClassFixture
- DisabledPersonSearchIndex
- CorsExtensions
- CacheSettings
- ConfiguredFactory
- PaginationResponseDto
- .AddIdentityInfrastructure
- ErrorCode
- DeliveryAttemptTracker
- MessagingHarness
- .SinkAssembly_BindsAgainstTheResolvedTransport
- NoOpProductCache
- IAuthenticatedUserService
- ManualTimeProvider
- ApiErrorItem
- PagedResponse
- .UnitOfWork_SaveChangesAsync_HonorsCancelledToken
- GetAllUsersRequest
- Required tests
- Required tests
- .ValidateAsync
- OrderMessagingTopology
- Required tests
- Architecture boundaries
- Error
- ProductValidationFunctionalTests
- IClock
- In-scope consistency work
- RecordingPublisherFactory
- Comandos executados e resultados
- Availability policy
- LegacyPerson
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
1. `BaseResult` - 77 edges
2. `IndexedPerson` - 63 edges
3. `ProductDto` - 50 edges
4. `OrderPlacedEvent` - 49 edges
5. `ArchitectureRulesTests` - 48 edges
6. `PersonIndexResult` - 45 edges
7. `ProductEntity` - 40 edges
8. `Template.Application.Wrappers` - 38 edges
9. `ElasticsearchPersonSearchIndexTests` - 36 edges
10. `Template.Application.Interfaces` - 33 edges

## Surprising Connections (you probably didn't know these)
- `Phase 4 — Clean the Domain model` --references--> `ProductDto`  [INFERRED]
  docs/phase-3-implementation-plan.md → Src/Core/Template.Application/DTOs/Products/ProductDto.cs
- `Riscos, pendências e rollback` --references--> `IUnitOfWork`  [INFERRED]
  docs/refactoring/phases-6-7-report.md → Src/Core/Template.Application/Interfaces/IUnitOfWork.cs
- `Execution status` --references--> `IPersonSearchIndex`  [INFERRED]
  AGENTS.md → Src/Core/Template.Application/Interfaces/Search/IPersonSearchIndex.cs
- `Errors and API results` --references--> `BaseResult`  [INFERRED]
  docs/phase-2-workspace.md → Src/Core/Template.Application/Wrappers/BaseResult.cs
- `Phase 5 — Standardize application use cases and results` --references--> `BaseResult`  [INFERRED]
  docs/phase-3-implementation-plan.md → Src/Core/Template.Application/Wrappers/BaseResult.cs

## Import Cycles
- None detected.

## Communities (211 total, 78 thin omitted)

### Community 0 - "IdentitySettings"
Cohesion: 0.15
Nodes (7): IdentitySettings, PasswordRequireDigit, PasswordRequiredLength, PasswordRequireLowercase, PasswordRequireNonAlphanumeric, PasswordRequireUppercase, IdentitySettingsValidator

### Community 1 - "ResourceMessages"
Cohesion: 0.17
Nodes (8): Template.Infrastructure.Resources.ProjectResources, ResourceMessages, Account_NotFound_with_UserName, Culture, Invalid_password, Product_NotFound_with_id, ResourceManager, Username_is_already_taken

### Community 2 - "Person"
Cohesion: 0.25
Nodes (7): Template.Domain.Person, Determining the active requirement, Sensitive-data redaction, Person, FirstName, Id, LastName

### Community 3 - "Container (Dockerfile)"
Cohesion: 0.05
Nodes (43): commandName, environmentVariables, launchBrowser, launchUrl, publishAllPorts, useSSL, ASPNETCORE_ENVIRONMENT, ASPNETCORE_HTTP_PORTS (+35 more)

### Community 4 - "ApiResultMapper"
Cohesion: 0.06
Nodes (5): Exception handling and log-once behavior, AccountController, ProductController, ApiResultMapper, ProblemDetailsAuthorizationMiddlewareResultHandler

### Community 5 - ".Handle"
Cohesion: 0.28
Nodes (4): DeleteProductCommand, Id, DeleteProductCommandHandler, DeleteProductCommandHandlerTests

### Community 8 - "Relatório da Fase 11 — 2026-10-11"
Cohesion: 0.09
Nodes (22): Ativos Elastic do cluster (fonte do ensaio, criada pela WebApi 8.x real), Caminho de upgrade adotado, Configuração e comportamento, Confirmações, Dados usados, Deprecations e Upgrade Assistant (no 8.19.23, dados do ensaio), Ensaio paralelo, Fontes oficiais consultadas (2026-10-11, America/Sao_Paulo) (+14 more)

### Community 9 - "shouldly"
Cohesion: 0.10
Nodes (11): Template.Infrastructure.Persistence.Repositories, Template.PostgreSql.Integration.Tests, Template.Unit.Tests.DomainTests.Products.Entities, Template.Unit.Tests.ApplicationTests.Wrappers, Template.Infrastructure.Persistence.Contexts, Template.Application.DTOs, Template.Domain.Products.Entities, Template.Integration.Tests.Data (+3 more)

### Community 10 - "DistributedProductCacheTests.cs"
Cohesion: 0.28
Nodes (3): Template.Integration.Tests.Caching, Template.Infrastructure.Caching, Template.Infrastructure.Caching.Products

### Community 11 - "UpdateProductCommand"
Cohesion: 0.22
Nodes (7): UpdateProductCommand, BarCode, Id, Name, Price, UpdateProductCommandHandler, UpdateProductCommandHandlerTests

### Community 12 - ".GetRepository"
Cohesion: 0.17
Nodes (5): BaseEfRepoTestFixture, EfRepositoryAdd, EfRepositoryDelete, EfRepositoryUpdate, ProductPersistencePrecisionTests

### Community 13 - "ValidationBehavior"
Cohesion: 0.14
Nodes (11): ValidationBehavior, AuthenticationRequestValidator, ChangePasswordRequestValidator, CreateProductCommandValidator, UpdateProductCommandValidator, NameRequiredValidator, SampleRequest, Name (+3 more)

### Community 14 - "ApplicationDbContext"
Cohesion: 0.06
Nodes (14): Acceptance criteria, Current-state inventory before editing, Migrations and seed idempotency, Current-state inventory before editing, EF migrations and seeds, IdentityContext, ApplicationDbContext, Products (+6 more)

### Community 15 - "Template.WebApi.csproj"
Cohesion: 0.12
Nodes (15): Asp.Versioning.Mvc.ApiExplorer, AspNetCore.HealthChecks.UI.Client, Elastic.Serilog.Sinks, Microsoft.AspNetCore.OpenApi, Microsoft.EntityFrameworkCore.Tools, Microsoft.VisualStudio.Azure.Containers.Tools.Targets, Serilog.AspNetCore, Serilog.Enrichers.ClientInfo (+7 more)

### Community 16 - "GetPagedListProductQuery"
Cohesion: 0.29
Nodes (4): GetPagedListProductQuery, Name, GetPagedListProductQueryHandler, GetPagedListProductQueryHandlerTests

### Community 18 - "ProductRepository"
Cohesion: 0.18
Nodes (6): ProductRepository, PostgreSqlFixture, TestAuthenticatedUser, UserId, UserName, PostgreSqlPersistenceTests

### Community 19 - "Template.Application.Wrappers"
Cohesion: 0.09
Nodes (20): Template.Unit.Tests.ApplicationTests.Features.Products.Queries, Template.Application.Wrappers, Template.Application.Interfaces, Template.Application.Features.Products.Queries, Template.Functional.Tests.ApiEndpoints.v2, Template.WebApi.Controllers.v2, Template.Application.DTOs.Products, Template.Unit.Tests.ApplicationTests.Features.Products.Commands (+12 more)

### Community 22 - "JwtAuthenticationFunctionalTests"
Cohesion: 0.13
Nodes (5): JwtAuthenticationFunctionalTests, Settings, FixedClock, UtcNow, SecurityTestSupport

### Community 24 - "ProductDto"
Cohesion: 0.14
Nodes (6): ProductDto, BarCode, CreatedDateTime, Id, Name, Price

### Community 25 - "IProductCache"
Cohesion: 0.12
Nodes (16): Phase 8 — Move Redis caching behind an infrastructure adapter, Current-state inventory before editing, Prerequisites, Pré-condições verificadas (Fases 8 e 9), Application, Não verificado e riscos, O que mudou, OpenAPI e baseline (`docs/refactoring/baseline-phase-6`) (+8 more)

### Community 26 - "OrderPlacedEvent"
Cohesion: 0.06
Nodes (30): Acceptance criteria, Architecture and functional checks, Broker behavior tests, Checkout and working-tree safety, Consumption and delivery, Current-state inventory before editing, Cutover and rollback, Definitions and secret handling (+22 more)

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
Cohesion: 0.15
Nodes (8): Template.Domain.Common, AuditableBaseEntity, Created, CreatedBy, LastModified, LastModifiedBy, BaseEntity, Id

### Community 31 - "CreateProductCommand"
Cohesion: 0.26
Nodes (6): CreateProductCommand, BarCode, Name, Price, CreateProductCommandHandler, CreateProductCommandHandlerTests

### Community 32 - "OrderDispatch"
Cohesion: 0.33
Nodes (5): Ports e contratos criados (Application), OrderDispatch, Broadcast, Direct, Topic

### Community 33 - "BaseResult"
Cohesion: 0.10
Nodes (17): AuthenticationRequest, Password, UserName, AuthenticationResponse, Email, Id, IsVerified, JwToken (+9 more)

### Community 35 - "Template.Application/ServiceRegistration.cs"
Cohesion: 0.08
Nodes (9): Template.Application, Template.Unit.Tests.ApplicationTests.Services, Template.Functional.Tests.Observability, Template.Application.Behaviors, Template.Application.Services.Time, Template.Unit.Tests.ApplicationTests, SystemClock, UtcNow (+1 more)

### Community 36 - "microsoft_extensions_dependencyinjection"
Cohesion: 0.13
Nodes (9): Template.Infrastructure.Identity.Seeds, Template.Infrastructure.Identity.Authentication, Template.Infrastructure.Identity.Services, Template.Infrastructure.Identity.Contexts, Template.Functional.Tests.Security, Template.Infrastructure.Identity.Settings, Template.WebApi.Infrastructure.Services, Template.Infrastructure.Identity.Models (+1 more)

### Community 37 - "Relevant technical context"
Cohesion: 0.15
Nodes (13): Relevant technical context, Fase 5 em detalhe, IProductRepository, ErrorCategory, Conflict, Forbidden, NotFound, Unauthorized (+5 more)

### Community 39 - "Phase 15 Execution Handoff"
Cohesion: 0.08
Nodes (25): Acceptance criteria, ACL and configuration, Adapter and integration tests, Checkout and working-tree safety, Compose and removal checks, Cutover and rollback, Data, serialization, expiry, and command compatibility, Documentation and delivery (+17 more)

### Community 40 - "RabbitMqTestSupport.cs"
Cohesion: 0.13
Nodes (14): Template.Integration.Tests.Messaging, Template.Infrastructure.Messaging.Publishing, Template.Infrastructure.Messaging.Serialization, Template.Application.Features.Orders.EventHandlers, Template.Application.Interfaces.Messaging, Template.Application.Messaging.Orders, Template.Unit.Tests.ApplicationTests.Features.Orders, Template.Infrastructure.Messaging.Topology (+6 more)

### Community 41 - "ApplicationUser"
Cohesion: 0.20
Nodes (4): ApplicationUser, Created, Name, AccountServices

### Community 42 - "TemplateCleanArchitecture.sln"
Cohesion: 0.20
Nodes (3): Template, Microsoft.VisualStudio.SDK, Microsoft.VSSDK.BuildTools

### Community 43 - "5. Target code standards"
Cohesion: 0.08
Nodes (23): 1. Evidence and assumptions, 2. Proposed AI workspace, 3. Dependency and integration update matrix, 4. Target Clean Architecture, 5. Target code standards, Application use cases, Controllers, Design principles (+15 more)

### Community 44 - "Relatório da Fase 10 — 2026-10-10"
Cohesion: 0.10
Nodes (20): Busca e indexação, Comandos executados e resultados, Comparação do OpenAPI com `docs/refactoring/baseline-phase-6/`, Comportamento HTTP legado (smoke da WebApi legada e caracterização do client), Confirmações, Código, DI e ciclo de vida, Decisão sobre o requisito ativo, Documento, índice e compatibilidade (+12 more)

### Community 45 - "Template.Functional.Tests.csproj"
Cohesion: 0.20
Nodes (9): Microsoft.AspNetCore.Mvc.Testing, net10.0, coverlet.collector, Microsoft.Extensions.Hosting.Abstractions, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio (+1 more)

### Community 46 - "Phase 3 — Incremental implementation plan"
Cohesion: 0.08
Nodes (23): Decisions (recorded 2026-10-10), Global execution rules, Phase 0 — Freeze a reproducible baseline, Phase 10 — Move Elastic concerns behind infrastructure boundaries, Phase 11 — Upgrade the Elastic client and server stack, Phase 12 — Harden identity and JWT boundaries, Phase 14 — Establish a reproducible Compose baseline, Phase 15 — Consolidate Redis services on Redis 8 (+15 more)

### Community 47 - "ProductEntity"
Cohesion: 0.14
Nodes (7): Template.Infrastructure.Persistence.Contexts.Configurations, ProductEntity, BarCode, Name, Price, ProductConfiguration, ProductTests

### Community 48 - "LocalizationExtensions"
Cohesion: 0.15
Nodes (8): Localization registration and options, 2. Architecture exceptions and allowlists, Riscos residuais, Pré-condições verificadas (Fase 10), LocalizationSettings, DefaultRequestCulture, SupportedCultures, LocalizationExtensions

### Community 50 - "Template.Infrastructure.Identity"
Cohesion: 0.25
Nodes (8): Microsoft.AspNetCore.Authentication.JwtBearer, Microsoft.AspNetCore.Identity.EntityFrameworkCore, System.IdentityModel.Tokens.Jwt, Template.Infrastructure.Identity, net10.0, Microsoft.EntityFrameworkCore.InMemory, Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.NET.Sdk

### Community 51 - "Phase 0 baseline — 2026-10-10"
Cohesion: 0.33
Nodes (5): Baseline validation before refactoring, Dependency and vulnerability baseline, Phase 0 baseline — 2026-10-10, Source state, Toolchain

### Community 52 - "UnitOfWork"
Cohesion: 0.17
Nodes (3): IUnitOfWork, UnitOfWork, ServiceRegistration

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
Cohesion: 0.12
Nodes (4): ApplicationRole, DefaultBasicUser, DefaultRoles, ServiceRegistration

### Community 58 - "Product"
Cohesion: 0.24
Nodes (5): Account, ApiRoutes, Product, V1, V2

### Community 59 - "Template.Application.Search.Persons"
Cohesion: 0.16
Nodes (6): Template.Unit.Tests.ApplicationTests.Search, Template.Application.Search.Persons, Template.Application.Features.Persons.Queries, Template.Unit.Tests.ApplicationTests.Features.Persons, Template.Application.Features.Persons.Commands, Template.Application.Interfaces.Search

### Community 61 - "IndexedPerson"
Cohesion: 0.05
Nodes (13): IndexedPerson, FakeElasticsearchServer, Handler, Port, Requests, Url, FakeResponse, RecordedRequest (+5 more)

### Community 62 - "IOrderEventPublisher"
Cohesion: 0.19
Nodes (3): IOrderEventPublisher, MessagingAdapterWithoutBrokerTests, NoOpHandler

### Community 63 - "Template.Integration.Tests.csproj"
Cohesion: 0.14
Nodes (12): Microsoft.Extensions.Caching.Memory, Microsoft.Extensions.Caching.StackExchangeRedis, net10.0, Microsoft.Extensions.Options.ConfigurationExtensions, Microsoft.NET.Sdk, net10.0, coverlet.collector, Microsoft.NET.Test.Sdk (+4 more)

### Community 64 - "ScriptedHandler"
Cohesion: 0.20
Nodes (6): IncomingMessageContext, CountingStore, Marks, OrderDeliveryProcessorTests, ScriptedHandler, Calls

### Community 66 - "Template.Functional.Tests.Common"
Cohesion: 0.13
Nodes (3): Template.Functional.Tests.ApiEndpoints, Template.WebApi.Infrastructure.Filters, Template.Functional.Tests.Common

### Community 69 - "Template.Architecture.Tests.csproj"
Cohesion: 0.29
Nodes (6): net10.0, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk

### Community 70 - "Current solution structure"
Cohesion: 0.12
Nodes (17): Current solution structure, Current state to characterize, Adapters e hosted services (Infrastructure.Messaging), APIs 7.x usadas, Configuração e DI, Confirmações, Contrato `order.placed` v1, Gates não executados (+9 more)

### Community 71 - "Fases 6–7 — bloqueio de pré-condições (2026-10-10)"
Cohesion: 0.33
Nodes (5): Causa do bloqueio, Como desbloquear, Estado do Git registrado, Fases 6–7 — bloqueio de pré-condições (2026-10-10), Pré-condições

### Community 72 - "PersonUseCaseHandlerTests"
Cohesion: 0.12
Nodes (10): Port e contratos (Application), EnsurePersonIndexCommand, EnsurePersonIndexCommandHandler, IndexPersonCommand, IndexPersonCommandHandler, RemovePersonCommand, RemovePersonCommandHandler, ListPersonsQuery (+2 more)

### Community 73 - "IPersonSearchIndex"
Cohesion: 0.12
Nodes (8): IPersonSearchIndex, PersonSearchSettings, Enabled, IndexName, RequestTimeout, Url, ServiceRegistration, SearchRegistrationTests

### Community 74 - "RabbitMqOrderConsumerService"
Cohesion: 0.10
Nodes (5): ReceivedDelivery, RabbitMqOrderConsumerService, RabbitMqConnections, OrderSubscription, DeadLetterQueue

### Community 81 - "RecordingPersonSearchIndex"
Cohesion: 0.10
Nodes (12): PersonFunctionalTests, Index, PersonWithoutSearchEngineFunctionalTests, RecordingPersonSearchIndex, BlockUntilCancelled, Calls, Listed, NextStatus (+4 more)

### Community 85 - "handoff-phases-6-7.md"
Cohesion: 0.18
Nodes (8): Template.WebApi.Controllers, Command execution, Delivery, General rules, Phase 6 — controllers and HTTP contracts, Required sources (read before changing any file), Starting state (verified on 2026-10-10 at 14:26), BaseApiController

### Community 100 - "GenericRepository"
Cohesion: 0.14
Nodes (3): IGenericRepository, GenericRepository, DbContext

### Community 101 - "Phase 12 Execution Handoff"
Cohesion: 0.09
Nodes (21): 401 and 403 behavior, Account-use-case standardization, Checkout and working tree, Documentation and delivery, HTTP and API compatibility, HTTPS metadata policy, Identity schema and migrations, JWT issuance and validation (+13 more)

### Community 102 - "ElasticsearchPersonSearchIndex"
Cohesion: 0.10
Nodes (12): Mudanças no adapter (`ElasticsearchPersonSearchIndex`), PersonIndexStatus, IncompatibleDocument, NotFound, Rejected, Succeeded, Unavailable, ElasticsearchPersonSearchIndex (+4 more)

### Community 103 - "RabbitMqOrderEventPublisher"
Cohesion: 0.15
Nodes (3): Reconexão, startup, shutdown e indisponibilidade, MessagePublishException, RabbitMqOrderEventPublisher

### Community 104 - "Program.cs"
Cohesion: 0.12
Nodes (8): Template.Infrastructure.Persistence, Template.Infrastructure.Identity, Template.WebApi.Infrastructure.Middlewares, Template.Infrastructure.Resources, Template.Infrastructure.Persistence.Seeds, Template.WebApi.Infrastructure.Extensions, Template.Application.Settings, Template.Infrastructure.Resources.Services

### Community 105 - "ChangePasswordRequest"
Cohesion: 0.16
Nodes (8): ChangePasswordRequest, ConfirmPassword, Password, ChangeUserNameRequest, UserName, ChangeUserNameRequestValidator, IAccountServices, AccountController

### Community 106 - "Relatório das Fases 0–5 — 2026-10-10"
Cohesion: 0.22
Nodes (8): Comparação de contrato, Confirmação, Mudanças por fase, Relatório das Fases 0–5 — 2026-10-10, Revisão da migration e do SQL (Fase 4), Riscos e pendências, Testes e gates (execução de 2026-10-10, 14:25–14:26), Versões e fontes oficiais

### Community 107 - "PublishOrderCommand"
Cohesion: 0.20
Nodes (4): PublishOrderCommand, PublishOrderCommandHandler, PublishOrderCommandHandlerTests, Handler

### Community 108 - "Template.PostgreSql.Integration.Tests.csproj"
Cohesion: 0.25
Nodes (7): Testcontainers.PostgreSql, net10.0, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk

### Community 110 - "RabbitMqSettings"
Cohesion: 0.07
Nodes (19): Configuration, security, and observability, Defeitos encontrados pelos testes com broker e corrigidos, Testes adicionados, OrderDeliveryProcessor, RabbitMqSettings, ConnectionTimeout, ConsumersEnabled, Enabled (+11 more)

### Community 111 - "Phase 19 Handoff — Upgrade Test and Coverage Tooling"
Cohesion: 0.09
Nodes (21): 1. Baseline and guardrails, 2. Microsoft.NET.Test.Sdk and Moq, 3. Coverlet and coverage pipeline, 4. xUnit Visual Studio runner decision, 5. Pragmatic coverage thresholds by layer, Acceptance criteria, Authoring-time checkpoint to verify, not assume, Dependencies and stop conditions (+13 more)

### Community 115 - "Create Phase 14–21 Handoffs and Execution Prompts"
Cohesion: 0.09
Nodes (21): Create Phase 14–21 Handoffs and Execution Prompts, Deliverables, Detailed handoff, Final response, Phase 14 — Reproducible Compose baseline, Phase 15 — Redis 8 consolidation, Phase 16 — PostgreSQL upgrade, Phase 17 — RabbitMQ server upgrade (+13 more)

### Community 117 - "PersonController"
Cohesion: 0.27
Nodes (3): Current state to characterize, HTTP compatibility, PersonController

### Community 118 - "RecordingOrderEventPublisher"
Cohesion: 0.19
Nodes (6): RecordingOrderEventPublisher, BlockUntilCancelled, FailWith, ObservedCancellation, Published, Started

### Community 119 - "PersonIndexResult"
Cohesion: 0.12
Nodes (6): GetPersonQuery, GetPersonQueryHandler, PersonIndexResult, Status, Succeeded, Value

### Community 120 - "RecordingHandler"
Cohesion: 0.18
Nodes (3): RecordingHandler, Behavior, Calls

### Community 121 - "RabbitMqFixture"
Cohesion: 0.23
Nodes (4): RabbitMqFixture, Container, Host, Port

### Community 122 - "Phase 13 Execution Handoff"
Cohesion: 0.10
Nodes (19): Acceptance criteria, Checkout and working tree, Correlation and tracing, Dependency-specific health behavior, Documentation and delivery, Health endpoint compatibility, Health model, HTTP and API compatibility (+11 more)

### Community 123 - "PersonBulkIndexResult"
Cohesion: 0.16
Nodes (7): PersonBulkIndexResult, FailedIds, IndexedIds, IsComplete, IsPartial, Status, PersonIndexResultTests

### Community 124 - "CustomWebApplicationFactory"
Cohesion: 0.09
Nodes (10): Program, ExternalServicesUnavailableTests, MessageWithoutBrokerFunctionalTests, RecordingSearchIndexFactory, Index, CacheDisabledFactory, AdditionalConfiguration, CustomWebApplicationFactory (+2 more)

### Community 125 - "Template.Infrastructure.Messaging.csproj"
Cohesion: 0.13
Nodes (13): RabbitMQ.Client, Testcontainers.RabbitMq, net10.0, Microsoft.Extensions.Hosting.Abstractions, Microsoft.Extensions.Options.ConfigurationExtensions, Microsoft.NET.Sdk, net10.0, Microsoft.Extensions.DependencyInjection (+5 more)

### Community 126 - ".Supported_8_19_To_9_5_Path_PreservesSyntheticData_AndLeavesTheSourceUntouched"
Cohesion: 0.17
Nodes (3): ClusterView, EsHttp, UpgradeRehearsalTests

### Community 128 - "UserDto"
Cohesion: 0.13
Nodes (10): Current-state characterization, UserDto, Created, Email, Id, Name, PhoneNumber, UserName (+2 more)

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
Cohesion: 0.11
Nodes (18): Template.Domain.Order, Breaking changes tratados, Comandos e resultados, Comparação do OpenAPI com `docs/refactoring/baseline-phase-6/`, Código e ciclo de vida, Fontes oficiais, Inventário anterior (estado de `f293cb9`), Mensagens, ack e falhas (+10 more)

### Community 138 - "elastic-upgrade-rehearsal.ps1"
Cohesion: 0.23
Nodes (16): Assert-Absent(), Copy-OwnedTemplates(), Es(), EsJson(), Get-OwnedTemplates(), Get-View(), Invoke-Http(), New-Snapshot() (+8 more)

### Community 139 - "Template.Infrastructure.Search.csproj"
Cohesion: 0.14
Nodes (12): Elastic.Clients.Elasticsearch, Testcontainers, net10.0, Microsoft.Extensions.Logging.Abstractions, Microsoft.Extensions.Options.ConfigurationExtensions, Microsoft.NET.Sdk, net10.0, Microsoft.NET.Test.Sdk (+4 more)

### Community 140 - "ElasticsearchFixture"
Cohesion: 0.15
Nodes (6): Current configuration names, ElasticsearchFixture, Container, Http, Url, ElasticsearchImage

### Community 141 - "IdentityComponentTests"
Cohesion: 0.21
Nodes (3): IdentityComponentTests, StubCurrentUser, UserName

### Community 142 - "ElasticLoggingSettings"
Cohesion: 0.08
Nodes (12): Configuração e DI, Pacote do health check removido, Separação entre busca, logging e health check, Mudanças no sink Serilog, ElasticLoggingSettings, DataSet, Enabled, Namespace (+4 more)

### Community 143 - "ElasticLogSinkDiagnostics"
Cohesion: 0.16
Nodes (4): ElasticLogSinkDiagnostics, Default, ThrowingWriter, Encoding

### Community 144 - "ErrorCodeEnum"
Cohesion: 0.22
Nodes (8): Template.WebApi.Infrastructure.Enums, ErrorCodeEnum, Conflict, Forbidden, NotFound, Unauthorized, Unexpected, Validation

### Community 145 - "Phase 14 Execution Handoff"
Cohesion: 0.12
Nodes (16): Acceptance criteria, Checkout and working-tree safety, Documentation and delivery, Health checks and deterministic startup, Isolated baseline environment, Phase 14 Execution Handoff, Phase 14 scope, PostgreSQL bootstrap and both databases (+8 more)

### Community 146 - "microsoft_extensions_configuration"
Cohesion: 0.11
Nodes (8): Template.Integration.Tests.Search, Template.Tests.Shared, Template.WebApi.Infrastructure.Logging, Template.Infrastructure.Search.Persons, Template.WebApi.Infrastructure.HealthChecks, Template.Infrastructure.Search, Template.Elasticsearch.Integration.Tests, SerialLoggingCollection

### Community 147 - "GetProductByIdQuery"
Cohesion: 0.14
Nodes (5): GetProductByIdQuery, Id, GetProductByIdQueryHandler, GetProductByIdQueryHandlerTests, ProductServiceTests

### Community 148 - "DeliveryDecision"
Cohesion: 0.29
Nodes (7): DeliveryAction, Ack, DeadLetter, Requeue, DeliveryDecision, Ack, Requeue

### Community 149 - "CountingProcessedMessageStore"
Cohesion: 0.33
Nodes (3): CountingProcessedMessageStore, Checks, Marks

### Community 151 - "ElasticsearchHealthCheckSettings"
Cohesion: 0.11
Nodes (7): ElasticsearchHealthCheckSettings, Enabled, FailureStatus, Name, Timeout, Url, HealthCheckExtensions

### Community 153 - "UnreachableLogSinkFactory"
Cohesion: 0.20
Nodes (3): LoggingSinkUnavailableHostTests, UnreachableLogSinkFactory, AdditionalConfiguration

### Community 155 - "Relatório das Fases 6–7 — 2026-10-10"
Cohesion: 0.14
Nodes (13): Arquivos alterados nesta execução, Automação e documentação, Comandos e resultados, Confirmação de escopo, Contrato e WebApi, Fase 6 — controllers e contratos HTTP, Fase 7 — persistência e PostgreSQL, Migrations e SQL (+5 more)

### Community 157 - "ITranslator"
Cohesion: 0.20
Nodes (7): Phase 13 — Standardize observability, health, and localization, Current-state inventory, Localization boundary, Scope of Phase 13, ITranslator, ServiceRegistration, Translator

### Community 159 - "DecodeResult"
Cohesion: 0.29
Nodes (7): DecodeResult, Incompatible, Invalid, DecodeStatus, Decoded, IncompatibleContract, InvalidPayload

### Community 160 - "IClassFixture"
Cohesion: 0.15
Nodes (5): ProductCacheDisabledFunctionalTests, ProductCacheFunctionalTests, ProductCacheScenario, ProductFunctionalTests, RandomDataExtensionMethods

### Community 161 - "DisabledPersonSearchIndex"
Cohesion: 0.28
Nodes (3): Adapter (Infrastructure.Search), Classificação de falhas, DisabledPersonSearchIndex

### Community 163 - "CacheSettings"
Cohesion: 0.15
Nodes (6): CacheSettings, AbsoluteExpiration, Enabled, OperationTimeout, SlidingExpiration, ServiceRegistration

### Community 164 - "ConfiguredFactory"
Cohesion: 0.23
Nodes (3): ConfiguredFactory, AdditionalConfiguration, IdentityOptionsStartupTests

### Community 165 - "PaginationResponseDto"
Cohesion: 0.18
Nodes (5): PaginationResponseDto, Count, Data, PageNumber, PageSize

### Community 166 - ".AddIdentityInfrastructure"
Cohesion: 0.10
Nodes (7): JwtTokenFactory, JwtSettings, Audience, DurationInMinutes, Issuer, Key, JwtSettingsValidator

### Community 167 - "ErrorCode"
Cohesion: 0.18
Nodes (7): ErrorCode, AccessDenied, ErrorInIdentity, Exception, FieldDataInvalid, ModelStateNotValid, NotFound

### Community 169 - "MessagingHarness"
Cohesion: 0.15
Nodes (4): CapturedLog, MessagingHarness, Consumer, Publisher

### Community 173 - "IAuthenticatedUserService"
Cohesion: 0.13
Nodes (8): Current-user and claims boundary, IAuthenticatedUserService, UserId, UserName, AuthenticatedUserService, UserId, UserName, AuthenticatedUserService

### Community 175 - "ApiErrorItem"
Cohesion: 0.28
Nodes (7): Diff de contrato e baseline, ApiErrorItem, Code, Field, Message, ApiErrorResponse, Errors

### Community 176 - "PagedResponse"
Cohesion: 0.22
Nodes (5): PagedResponse, PageNumber, PageSize, TotalItems, TotalPages

### Community 178 - "GetAllUsersRequest"
Cohesion: 0.29
Nodes (6): Template.Application.Parameters, GetAllUsersRequest, Name, PaginationRequestParameter, PageNumber, PageSize

### Community 179 - "Required tests"
Cohesion: 0.25
Nodes (8): Adapter tests, Architecture tests, Functional tests, Health-check tests, Logging tests, Required tests, Tests with real Elasticsearch, Unit tests

### Community 180 - "Required tests"
Cohesion: 0.25
Nodes (8): Account functional tests, Architecture tests, Authorization tests, JWT validation tests, Options and startup tests, Persistence and migration tests, Required tests, Unit tests

### Community 183 - "Required tests"
Cohesion: 0.33
Nodes (6): Architecture tests, Exception and Problem Details tests, Health tests, Localization tests, Logging and correlation tests, Required tests

### Community 186 - "Architecture boundaries"
Cohesion: 0.40
Nodes (5): Application may own, Architecture boundaries, Domain must remain free of, Infrastructure.Identity must own, WebApi may own

### Community 187 - "Error"
Cohesion: 0.33
Nodes (4): Error, Description, ErrorCode, FieldName

### Community 189 - "IClock"
Cohesion: 0.15
Nodes (10): Change and validation rules, Current implementation conventions, Execution status, Repository language and file naming, Repository purpose, TemplateCleanArchNET agent guide, Verified commands (2026-10-11, Phase 11 checkpoint), Time handling (+2 more)

### Community 190 - "In-scope consistency work"
Cohesion: 0.40
Nodes (5): 1. Compatibility shims and temporary adapters, 3. Dead or redundant packages and code, 4. API and duplicate code, 5. Images, configuration, and documentation, In-scope consistency work

### Community 192 - "Comandos executados e resultados"
Cohesion: 0.40
Nodes (5): Comandos executados e resultados, Comparação do OpenAPI com `docs/refactoring/baseline-phase-6/`, Contagens finais, Gate final, Verificações pontuais durante a implementação

### Community 194 - "Availability policy"
Cohesion: 0.50
Nodes (4): Availability policy, Health check, Logging, Search and indexing

### Community 195 - "LegacyPerson"
Cohesion: 0.50
Nodes (4): LegacyPerson, FirstName, Id, LastName

## Knowledge Gaps
- **838 isolated node(s):** `Microsoft.VisualStudio.SDK`, `Microsoft.VSSDK.BuildTools`, `UserName`, `Password`, `Password` (+833 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 1475 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **78 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Current solution structure` connect `Current solution structure` to `Person`, `DisabledOrderEventPublisher`, `Order`, `IProductCache`, `DistributedProductCache`, `OrderPlacedEvent`, `ITranslator`, `DisabledPersonSearchIndex`, `CacheSettings`, `NoOpProductCache`, `LocalizationExtensions`, `OrderMessagingTopology`, `IndexedPerson`, `IOrderEventPublisher`, `IClock`, `IPersonSearchIndex`, `RabbitMqOrderConsumerService`, `ElasticsearchPersonSearchIndex`, `RabbitMqOrderEventPublisher`, `PublishOrderCommand`, `RabbitMqSettings`, `PersonIndexResult`, `PersonBulkIndexResult`, `.Supported_8_19_To_9_5_Path_PreservesSyntheticData_AndLeavesTheSourceUntouched`?**
  _High betweenness centrality (0.195) - this node is a cross-community bridge._
- **Are the 4 inferred relationships involving `BaseResult` (e.g. with `Current implementation conventions` and `Errors and API results`) actually correct?**
  _`BaseResult` has 4 INFERRED edges - model-reasoned connections that need verification._
- **What connects `Microsoft.VisualStudio.SDK`, `Microsoft.VSSDK.BuildTools`, `UserName` to the rest of the system?**
  _838 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Container (Dockerfile)` be split into smaller, more focused modules?**
  _Cohesion score 0.048625792811839326 - nodes in this community are weakly interconnected._
- **Why does `ITranslator` connect `ITranslator` to `.Handle`, `Current solution structure`, `ApplicationUser`, `UpdateProductCommand`, `IAuthenticatedUserService`, `Template.Application.Wrappers`, `GetProductByIdQuery`?**
  _High betweenness centrality (0.079) - this node is a cross-community bridge._
- **Are the 31 inferred relationships involving `IndexedPerson` (e.g. with `Current solution structure` and `Testes adicionados`) actually correct?**
  _`IndexedPerson` has 31 INFERRED edges - model-reasoned connections that need verification._
- **Should `ApiResultMapper` be split into smaller, more focused modules?**
  _Cohesion score 0.06442307692307692 - nodes in this community are weakly interconnected._