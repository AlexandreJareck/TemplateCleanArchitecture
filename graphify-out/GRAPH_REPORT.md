# Graph Report - TemplateCleanArchNET  (2026-10-11)

## Corpus Check
- 294 files · ~136,056 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 14 file(s) not represented in the graph (top: (none) 4, .resx 4, .jsonl 2)

## Summary
- 3199 nodes · 6662 edges · 192 communities (116 shown, 76 thin omitted)
- Extraction: 89% EXTRACTED · 11% INFERRED · 0% AMBIGUOUS · INFERRED: 717 edges (avg confidence: 0.89)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `0ef646c1`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- Current configuration names
- ResourceMessages
- Person
- Container (Dockerfile)
- ApiResultMapper
- .Handle
- ErrorCategoryTests
- DisabledOrderEventPublisher
- Relatório da Fase 11 — 2026-10-11
- microsoft_entityframeworkcore
- DistributedProductCacheTests.cs
- UpdateProductCommand
- .GetRepository
- ValidationBehavior
- ApplicationDbContext
- Template.WebApi.csproj
- PagedResponse
- Task
- ProductRepository
- Template.Application.Wrappers
- OrderMessagingBrokerTests
- ProductController
- ProductFunctionalTests
- ArchitectureRulesTests
- ProductDto
- Application
- ProductServiceTests
- Phase 9 Execution Handoff
- Phase 3 — Incremental implementation plan
- Template.Application.csproj
- AuditableBaseEntity
- CreateProductCommand
- Relatório da Fase 9 — 2026-10-10
- BaseResult
- .BuildModel
- Template.Application/ServiceRegistration.cs
- Program.cs
- Contexto técnico relevante
- ConfigureSwaggerOptions
- Phase 16 Execution Handoff
- RabbitMqTestSupport.cs
- AccountServices
- TemplateCleanArchitecture.sln
- 5. Target code standards
- Relatório da Fase 10 — 2026-10-10
- Template.Functional.Tests.csproj
- Phase 17 Execution Handoff
- ProductEntity
- LocalizationExtensions
- HttpClientGetExtensionMethods
- Template.Infrastructure.Identity
- Phase 0 baseline — 2026-10-10
- UnitOfWork
- Phase 15 Execution Handoff
- .Decode
- Template.Infrastructure.Persistence.csproj
- Template.Unit.Tests.csproj
- .OnModelCreating
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
- IProcessedMessageStore
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
- handoff-fases-6-7.md
- Core/AGENTS.md
- Infrastructure/AGENTS.md
- Presentation/AGENTS.md
- Tests/AGENTS.md
- GenericRepository
- Phase 19 Handoff — Upgrade Test and Coverage Tooling
- ElasticsearchPersonSearchIndex
- RabbitMqOrderEventPublisher
- SwaggerExtensions.cs
- ChangePasswordRequest
- Relatório das Fases 0–5 — 2026-10-10
- OrderPlacedEvent
- Template.PostgreSql.Integration.Tests.csproj
- OrderPlacedEventHandlerTests
- RabbitMqSettings
- Phase 12 Execution Handoff
- Phase 10 Execution Handoff
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
- InMemoryProcessedMessageStore
- TranslatorMessageDto
- ElasticLoggingTests
- ResourceGeneral
- .Log
- Phase 18 Execution Handoff
- Phase 20 Handoff — Isolate and Modernize the VSIX Project
- Order
- elastic-upgrade-rehearsal.ps1
- Template.Infrastructure.Search.csproj
- Required tests
- Phase 21 Handoff — Final Consistency and Release Gate
- ElasticLoggingSettings
- ElasticLogSinkDiagnostics
- ErrorCodeEnum
- Required tests
- Template.Application.Search.Persons
- GetProductByIdQuery
- .ProcessAsync
- CountingProcessedMessageStore
- CapturingLogger
- ElasticsearchHealthCheckSettings
- Phase 14 Execution Handoff
- UnreachableLogSinkFactory
- DistributedProductCache
- Relatório das Fases 6–7 — 2026-10-10
- DistributedProductCacheTests
- ITranslator
- CapturingSink
- DecodeResult
- IClassFixture
- ThrowingWriter
- CorsExtensions
- Availability policy
- RabbitMqConnections
- ApplicationUser
- IdentityContext
- .AddHealthChecksService
- DeliveryAttemptTracker
- MessagingHarness
- .SinkAssembly_BindsAgainstTheResolvedTransport
- CacheSettings
- Current solution structure
- IAuthenticatedUserService
- ManualTimeProvider
- In-scope consistency work
- Required tests
- execute-fase-12.md
- execute-fase-13.md
- execute-fase-14.md
- execute-fase-15.md
- execute-fase-16.md
- execute-fase-17.md
- execute-fase-18.md
- execute-fase-19.md
- execute-fase-20.md
- execute-fase-21.md
- Error
- ProductValidationFunctionalTests
- IClock
- OrderDeliveryProcessor
- RecordingPublisherFactory

## God Nodes (most connected - your core abstractions)
1. `BaseResult` - 76 edges
2. `IndexedPerson` - 63 edges
3. `ProductDto` - 50 edges
4. `OrderPlacedEvent` - 49 edges
5. `PersonIndexResult` - 45 edges
6. `ArchitectureRulesTests` - 41 edges
7. `ProductEntity` - 40 edges
8. `Template.Application.Wrappers` - 36 edges
9. `ElasticsearchPersonSearchIndexTests` - 36 edges
10. `Current solution structure` - 33 edges

## Surprising Connections (you probably didn't know these)
- `Phase 4 — Clean the Domain model` --references--> `ProductDto`  [INFERRED]
  docs/fase-3-plano-implementacao.md → Src/Core/Template.Application/DTOs/Products/ProductDto.cs
- `Phase 8 — Move Redis caching behind an infrastructure adapter` --references--> `IProductCache`  [INFERRED]
  docs/fase-3-plano-implementacao.md → Src/Core/Template.Application/Interfaces/Caching/IProductCache.cs
- `Prerequisites` --references--> `IProductCache`  [INFERRED]
  docs/refactoring/handoff-fase-15.md → Src/Core/Template.Application/Interfaces/Caching/IProductCache.cs
- `Riscos, pendências e rollback` --references--> `IUnitOfWork`  [INFERRED]
  docs/refactoring/phases-6-7-report.md → Src/Core/Template.Application/Interfaces/IUnitOfWork.cs
- `Execution status` --references--> `IPersonSearchIndex`  [INFERRED]
  AGENTS.md → Src/Core/Template.Application/Interfaces/Search/IPersonSearchIndex.cs

## Import Cycles
- None detected.

## Communities (192 total, 76 thin omitted)

### Community 0 - "Current configuration names"
Cohesion: 0.14
Nodes (13): Current configuration names, Template.Infrastructure.Identity.Settings, IdentitySettings, PasswordRequireDigit, PasswordRequiredLength, PasswordRequireLowercase, PasswordRequireNonAlphanumeric, PasswordRequireUppercase (+5 more)

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
Cohesion: 0.07
Nodes (4): AccountController, ProductController, ApiResultMapper, ProblemDetailsAuthorizationMiddlewareResultHandler

### Community 5 - ".Handle"
Cohesion: 0.28
Nodes (4): DeleteProductCommand, Id, DeleteProductCommandHandler, DeleteProductCommandHandlerTests

### Community 8 - "Relatório da Fase 11 — 2026-10-11"
Cohesion: 0.07
Nodes (27): Ativos Elastic do cluster (fonte do ensaio, criada pela WebApi 8.x real), Caminho de upgrade adotado, Comandos executados e resultados, Comparação do OpenAPI com `docs/refactoring/baseline-fase-6/`, Configuração e comportamento, Confirmações, Contagens finais, Dados usados (+19 more)

### Community 9 - "microsoft_entityframeworkcore"
Cohesion: 0.13
Nodes (7): Template.Infrastructure.Persistence.Repositories, Template.PostgreSql.Integration.Tests, Template.Infrastructure.Persistence.Contexts, Template.Domain.Products.Entities, Template.Infrastructure.Persistence.Contexts.Configurations, Template.Integration.Tests.Data, Template.Infrastructure.Persistence.Extensions

### Community 10 - "DistributedProductCacheTests.cs"
Cohesion: 0.29
Nodes (3): Template.Integration.Tests.Caching, Template.Infrastructure.Caching, Template.Infrastructure.Caching.Products

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
Cohesion: 0.09
Nodes (6): EF migrations and seeds, ApplicationDbContext, Products, ApplicationDbContextFactory, DefaultData, ServiceRegistration

### Community 15 - "Template.WebApi.csproj"
Cohesion: 0.12
Nodes (15): Asp.Versioning.Mvc.ApiExplorer, AspNetCore.HealthChecks.UI.Client, Elastic.Serilog.Sinks, Microsoft.AspNetCore.OpenApi, Microsoft.EntityFrameworkCore.Tools, Microsoft.VisualStudio.Azure.Containers.Tools.Targets, Serilog.AspNetCore, Serilog.Enrichers.ClientInfo (+7 more)

### Community 16 - "PagedResponse"
Cohesion: 0.10
Nodes (14): PaginationResponseDto, Count, Data, PageNumber, PageSize, GetPagedListProductQuery, Name, GetPagedListProductQueryHandler (+6 more)

### Community 18 - "ProductRepository"
Cohesion: 0.15
Nodes (8): ProductRepository, PostgreSqlCollection, PostgreSqlFixture, TestAuthenticatedUser, UserId, UserName, PostgreSqlPersistenceTests, RabbitMqCollection

### Community 19 - "Template.Application.Wrappers"
Cohesion: 0.13
Nodes (13): Template.Unit.Tests.ApplicationTests.Features.Products.Queries, Template.Application.Wrappers, Template.Application.Interfaces, Template.Application.Features.Products.Queries, Template.WebApi.Infrastructure.Filters, Template.Application.DTOs.Products, Template.Unit.Tests.ApplicationTests.Features.Products.Commands, Template.Application.DTOs (+5 more)

### Community 24 - "ProductDto"
Cohesion: 0.11
Nodes (6): ProductDto, BarCode, CreatedDateTime, Id, Name, Price

### Community 25 - "Application"
Cohesion: 0.18
Nodes (10): Application, Infrastructure — `Template.Infrastructure.Caching` (novo), Não verificado e riscos, O que mudou, OpenAPI e baseline (`docs/refactoring/baseline-fase-6`), Regras de arquitetura, Relatório da Fase 8 — 2026-10-10, Testes adicionados ou alterados (+2 more)

### Community 27 - "Phase 9 Execution Handoff"
Cohesion: 0.08
Nodes (23): Acceptance criteria, Architecture tests, Asynchronous publishing, Availability and startup, Configuration and security, Consumption, acknowledgements, and delivery, Documentation and delivery, Event contracts (+15 more)

### Community 28 - "Phase 3 — Incremental implementation plan"
Cohesion: 0.08
Nodes (24): Decisions (recorded 2026-10-10), Global execution rules, Phase 0 — Freeze a reproducible baseline, Phase 10 — Move Elastic concerns behind infrastructure boundaries, Phase 11 — Upgrade the Elastic client and server stack, Phase 12 — Harden identity and JWT boundaries, Phase 14 — Establish a reproducible Compose baseline, Phase 15 — Consolidate Redis services on Redis 8 (+16 more)

### Community 29 - "Template.Application.csproj"
Cohesion: 0.15
Nodes (12): FluentValidation.DependencyInjectionExtensions, MediatR, Microsoft.Extensions.Options, net10.0, Microsoft.Extensions.Logging.Abstractions, Microsoft.NET.Sdk, Template.Domain, net10.0 (+4 more)

### Community 30 - "AuditableBaseEntity"
Cohesion: 0.10
Nodes (14): Change and validation rules, Current implementation conventions, Execution status, Repository purpose, TemplateCleanArchNET agent guide, Verified commands (2026-10-11, Phase 11 checkpoint), Template.Domain.Common, AuditableBaseEntity (+6 more)

### Community 31 - "CreateProductCommand"
Cohesion: 0.17
Nodes (10): AuthenticationRequestValidator, ChangeUserNameRequestValidator, CreateProductCommand, BarCode, Name, Price, CreateProductCommandHandler, CreateProductCommandValidator (+2 more)

### Community 32 - "Relatório da Fase 9 — 2026-10-10"
Cohesion: 0.08
Nodes (24): Adapters e hosted services (Infrastructure.Messaging), APIs 7.x usadas, Breaking changes tratados, Comandos e resultados, Comparação do OpenAPI com `docs/refactoring/baseline-fase-6/`, Confirmações, Contrato `order.placed` v1, Defeitos encontrados pelos testes com broker e corrigidos (+16 more)

### Community 33 - "BaseResult"
Cohesion: 0.09
Nodes (19): AuthenticationRequest, Password, UserName, ChangeUserNameRequest, UserName, AuthenticationResponse, Email, Id (+11 more)

### Community 35 - "Template.Application/ServiceRegistration.cs"
Cohesion: 0.11
Nodes (10): Template.Application, Template.Unit.Tests.ApplicationTests.Services, Template.Unit.Tests.ApplicationTests.Behaviors, Template.Application.Behaviors, Template.Application.Services.Time, Template.Unit.Tests.ApplicationTests, ServiceRegistration, SystemClock (+2 more)

### Community 36 - "Program.cs"
Cohesion: 0.09
Nodes (13): Template.Infrastructure.Persistence, Template.Infrastructure.Identity, Template.WebApi.Infrastructure.Middlewares, Template.WebApi.Controllers.v2, Template.Infrastructure.Identity.Services, Template.Application.DTOs.Account.Request, Template.WebApi.Infrastructure.Http, Template.Application.Helpers (+5 more)

### Community 37 - "Contexto técnico relevante"
Cohesion: 0.08
Nodes (23): Contexto técnico relevante, Fase 5 em detalhe, Fase 7 — persistência e PostgreSQL, IUnitOfWork, IGenericRepository, IProductRepository, ErrorCategory, Conflict (+15 more)

### Community 39 - "Phase 16 Execution Handoff"
Cohesion: 0.06
Nodes (32): Acceptance criteria, Additional PostgreSQL 18 acceptance, only if authorized, Backup and restore rehearsal for both databases, Backup/restore tests, Checkout and working-tree safety, Cutover gate, Data safety and isolated environments, Documentation and delivery (+24 more)

### Community 40 - "RabbitMqTestSupport.cs"
Cohesion: 0.13
Nodes (14): Template.Integration.Tests.Messaging, Template.Infrastructure.Messaging.Publishing, Template.Infrastructure.Messaging.Serialization, Template.Application.Features.Orders.EventHandlers, Template.Application.Interfaces.Messaging, Template.Application.Messaging.Orders, Template.Unit.Tests.ApplicationTests.Features.Orders, Template.Infrastructure.Messaging.Topology (+6 more)

### Community 42 - "TemplateCleanArchitecture.sln"
Cohesion: 0.20
Nodes (3): Template, Microsoft.VisualStudio.SDK, Microsoft.VSSDK.BuildTools

### Community 43 - "5. Target code standards"
Cohesion: 0.08
Nodes (23): 1. Evidence and assumptions, 2. Proposed AI workspace, 3. Dependency and integration update matrix, 4. Target Clean Architecture, 5. Target code standards, Application use cases, Controllers, Design principles (+15 more)

### Community 44 - "Relatório da Fase 10 — 2026-10-10"
Cohesion: 0.09
Nodes (23): Adapter (Infrastructure.Search), Busca e indexação, Classificação de falhas, Comandos executados e resultados, Comparação do OpenAPI com `docs/refactoring/baseline-fase-6/`, Comportamento HTTP legado (smoke da WebApi legada e caracterização do client), Confirmações, Código, DI e ciclo de vida (+15 more)

### Community 45 - "Template.Functional.Tests.csproj"
Cohesion: 0.20
Nodes (9): Microsoft.AspNetCore.Mvc.Testing, net10.0, coverlet.collector, Microsoft.Extensions.Hosting.Abstractions, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio (+1 more)

### Community 46 - "Phase 17 Execution Handoff"
Cohesion: 0.07
Nodes (29): Acceptance criteria, Architecture and functional checks, Broker behavior tests, Checkout and working-tree safety, Configuration, security, and observability, Consumption and delivery, Cutover and rollback, Definitions and secret handling (+21 more)

### Community 47 - "ProductEntity"
Cohesion: 0.15
Nodes (7): Template.Unit.Tests.DomainTests.Products.Entities, ProductEntity, BarCode, Name, Price, ProductConfiguration, ProductTests

### Community 48 - "LocalizationExtensions"
Cohesion: 0.16
Nodes (8): Current-state inventory, Localization registration and options, Riscos residuais, Pré-condições verificadas (Fase 10), LocalizationSettings, DefaultRequestCulture, SupportedCultures, LocalizationExtensions

### Community 50 - "Template.Infrastructure.Identity"
Cohesion: 0.25
Nodes (8): Microsoft.AspNetCore.Authentication.JwtBearer, Microsoft.AspNetCore.Identity.EntityFrameworkCore, System.IdentityModel.Tokens.Jwt, Template.Infrastructure.Identity, net10.0, Microsoft.EntityFrameworkCore.InMemory, Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.NET.Sdk

### Community 51 - "Phase 0 baseline — 2026-10-10"
Cohesion: 0.33
Nodes (5): Baseline validation before refactoring, Dependency and vulnerability baseline, Phase 0 baseline — 2026-10-10, Source state, Toolchain

### Community 53 - "Phase 15 Execution Handoff"
Cohesion: 0.07
Nodes (26): Acceptance criteria, ACL and configuration, Adapter and integration tests, Checkout and working-tree safety, Compose and removal checks, Cutover and rollback, Data, serialization, expiry, and command compatibility, Documentation and delivery (+18 more)

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
Cohesion: 0.05
Nodes (13): IndexedPerson, FakeElasticsearchServer, Handler, Port, Requests, Url, FakeResponse, RecordedRequest (+5 more)

### Community 62 - "IOrderEventPublisher"
Cohesion: 0.19
Nodes (3): IOrderEventPublisher, MessagingAdapterWithoutBrokerTests, NoOpHandler

### Community 63 - "Template.Integration.Tests.csproj"
Cohesion: 0.14
Nodes (12): Microsoft.Extensions.Caching.Memory, Microsoft.Extensions.Caching.StackExchangeRedis, net10.0, Microsoft.Extensions.Options.ConfigurationExtensions, Microsoft.NET.Sdk, net10.0, coverlet.collector, Microsoft.NET.Test.Sdk (+4 more)

### Community 64 - "ScriptedHandler"
Cohesion: 0.31
Nodes (3): OrderDeliveryProcessorTests, ScriptedHandler, Calls

### Community 66 - "shouldly"
Cohesion: 0.19
Nodes (5): Template.Functional.Tests.ApiEndpoints, Template.Unit.Tests.ApplicationTests.Wrappers, Template.Unit.Tests.ApplicationTests.Search, Template.Functional.Tests.ApiEndpoints.v2, Template.Functional.Tests.Common

### Community 69 - "Template.Architecture.Tests.csproj"
Cohesion: 0.29
Nodes (6): net10.0, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk

### Community 70 - "IProcessedMessageStore"
Cohesion: 0.15
Nodes (8): Current state to characterize, Configuração e DI, Política de ack, retry, poison e idempotência, Resumo do design, OrderPlacedEventHandler, IOrderPlacedEventHandler, IProcessedMessageStore, ServiceRegistration

### Community 71 - "Fases 6–7 — bloqueio de pré-condições (2026-10-10)"
Cohesion: 0.33
Nodes (5): Causa do bloqueio, Como desbloquear, Estado do Git registrado, Fases 6–7 — bloqueio de pré-condições (2026-10-10), Pré-condições

### Community 72 - "PersonUseCaseHandlerTests"
Cohesion: 0.10
Nodes (13): Template.Application.Features.Persons.Queries, Template.Unit.Tests.ApplicationTests.Features.Persons, Template.Application.Features.Persons.Commands, Port e contratos (Application), IndexPersonCommand, IndexPersonCommandHandler, RemovePersonCommand, RemovePersonCommandHandler (+5 more)

### Community 73 - "IPersonSearchIndex"
Cohesion: 0.11
Nodes (8): IPersonSearchIndex, PersonSearchSettings, Enabled, IndexName, RequestTimeout, Url, ServiceRegistration, SearchRegistrationTests

### Community 81 - "RecordingPersonSearchIndex"
Cohesion: 0.08
Nodes (18): PersonIndexStatus, IncompatibleDocument, NotFound, Rejected, Succeeded, Unavailable, PersonFunctionalTests, Index (+10 more)

### Community 85 - "handoff-fases-6-7.md"
Cohesion: 0.15
Nodes (10): Template.WebApi.Controllers, Entrega, Estado de partida (verificado em 2026-10-10 às 14:26), Execução de comandos, Fase 6 — controllers e contratos HTTP, Fontes obrigatórias (ler antes de alterar qualquer arquivo), Regras gerais, Fase 6 — controllers e contratos HTTP (+2 more)

### Community 101 - "Phase 19 Handoff — Upgrade Test and Coverage Tooling"
Cohesion: 0.09
Nodes (21): 1. Baseline and guardrails, 2. Microsoft.NET.Test.Sdk and Moq, 3. Coverlet and coverage pipeline, 4. xUnit Visual Studio runner decision, 5. Pragmatic coverage thresholds by layer, Acceptance criteria, Authoring-time checkpoint to verify, not assume, Dependencies and stop conditions (+13 more)

### Community 102 - "ElasticsearchPersonSearchIndex"
Cohesion: 0.13
Nodes (6): Mudanças no adapter (`ElasticsearchPersonSearchIndex`), ElasticsearchPersonSearchIndex, PersonDocument, FirstName, Id, LastName

### Community 104 - "SwaggerExtensions.cs"
Cohesion: 0.10
Nodes (5): Template.Functional.Tests.Observability, Template.Infrastructure.Resources, Template.WebApi.Infrastructure.Extensions, Template.Application.Settings, Template.Infrastructure.Resources.Services

### Community 105 - "ChangePasswordRequest"
Cohesion: 0.16
Nodes (6): ChangePasswordRequest, ConfirmPassword, Password, ChangePasswordRequestValidator, IAccountServices, AccountController

### Community 106 - "Relatório das Fases 0–5 — 2026-10-10"
Cohesion: 0.22
Nodes (8): Comparação de contrato, Confirmação, Mudanças por fase, Relatório das Fases 0–5 — 2026-10-10, Revisão da migration e do SQL (Fase 4), Riscos e pendências, Testes e gates (execução de 2026-10-10, 14:25–14:26), Versões e fontes oficiais

### Community 107 - "OrderPlacedEvent"
Cohesion: 0.15
Nodes (5): PublishOrderCommand, PublishOrderCommandHandler, OrderPlacedEvent, PublishOrderCommandHandlerTests, Handler

### Community 108 - "Template.PostgreSql.Integration.Tests.csproj"
Cohesion: 0.25
Nodes (7): Testcontainers.PostgreSql, net10.0, Microsoft.NET.Test.Sdk, Shouldly, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk

### Community 110 - "RabbitMqSettings"
Cohesion: 0.08
Nodes (17): Current-state inventory before editing, RabbitMqSettings, ConnectionTimeout, ConsumersEnabled, Enabled, HostName, MaxDeliveryAttempts, MaxReconnectDelay (+9 more)

### Community 111 - "Phase 12 Execution Handoff"
Cohesion: 0.07
Nodes (26): 401 and 403 behavior, Account-use-case standardization, Application may own, Architecture boundaries, Checkout and working tree, Documentation and delivery, Domain must remain free of, HTTP and API compatibility (+18 more)

### Community 112 - "Phase 10 Execution Handoff"
Cohesion: 0.11
Nodes (17): Acceptance criteria, Application port, Checkout and working tree, Configuration and security, Contracts and indexed documents, Documentation and delivery, Feature flag and rollback, Infrastructure adapter (+9 more)

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
Nodes (6): EnsurePersonIndexCommand, EnsurePersonIndexCommandHandler, PersonIndexResult, Status, Succeeded, Value

### Community 120 - "RecordingHandler"
Cohesion: 0.16
Nodes (4): IncomingMessageContext, RecordingHandler, Behavior, Calls

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
Cohesion: 0.09
Nodes (10): Program, ExternalServicesUnavailableTests, MessageWithoutBrokerFunctionalTests, RecordingSearchIndexFactory, Index, CacheDisabledFactory, AdditionalConfiguration, CustomWebApplicationFactory (+2 more)

### Community 125 - "Template.Infrastructure.Messaging.csproj"
Cohesion: 0.13
Nodes (13): RabbitMQ.Client, Testcontainers.RabbitMq, net10.0, Microsoft.Extensions.Hosting.Abstractions, Microsoft.Extensions.Options.ConfigurationExtensions, Microsoft.NET.Sdk, net10.0, Microsoft.Extensions.DependencyInjection (+5 more)

### Community 126 - ".Supported_8_19_To_9_5_Path_PreservesSyntheticData_AndLeavesTheSourceUntouched"
Cohesion: 0.05
Nodes (15): ElasticsearchHealthCheck, ElasticsearchAvailabilityTests, ElasticsearchFixture, Container, Http, Url, ElasticsearchImage, LegacyPerson (+7 more)

### Community 127 - "system"
Cohesion: 0.33
Nodes (3): Template.Infrastructure.Identity.Migrations, Template.Infrastructure.Identity.Contexts, Template.Infrastructure.Persistence.Migrations

### Community 128 - "UserDto"
Cohesion: 0.10
Nodes (13): Template.Application.Parameters, GetAllUsersRequest, Name, UserDto, Created, Email, Id, Name (+5 more)

### Community 129 - "InMemoryProcessedMessageStore"
Cohesion: 0.24
Nodes (3): InMemoryProcessedMessageStore, CountingStore, Marks

### Community 130 - "TranslatorMessageDto"
Cohesion: 0.18
Nodes (6): TranslatorMessageDto, Args, Text, AccountMessages, ProductMessages, TranslatorMessages

### Community 133 - "ResourceGeneral"
Cohesion: 0.18
Nodes (9): ResourceGeneral, BarCode, ConfirmPassword, Culture, Id, Name, Password, ResourceManager (+1 more)

### Community 135 - "Phase 18 Execution Handoff"
Cohesion: 0.10
Nodes (19): Acceptance criteria, Application and release independence, Checkout and working-tree safety, Compose and configuration changes, Current-state inventory before editing, Disposable-tooling boundary, Documentation and delivery, New target volume and isolated rehearsal (+11 more)

### Community 136 - "Phase 20 Handoff — Isolate and Modernize the VSIX Project"
Cohesion: 0.10
Nodes (19): 1. Make support policy explicit, 2. Create a VSIX-specific build path, 3. Update within the Visual Studio SDK 17.x line, 4. Verify installation and runtime behavior, 5. Keep the application gate independent, Acceptance criteria, Adopted decision and prerequisites, Authoring-time checkpoint to verify, not assume (+11 more)

### Community 137 - "Order"
Cohesion: 0.16
Nodes (11): Template.Domain.Order, Código e ciclo de vida, Inventário anterior (estado de `f293cb9`), Mensagens, ack e falhas, Topologia, Order, Id, Price (+3 more)

### Community 138 - "elastic-upgrade-rehearsal.ps1"
Cohesion: 0.23
Nodes (16): Assert-Absent(), Copy-OwnedTemplates(), Es(), EsJson(), Get-OwnedTemplates(), Get-View(), Invoke-Http(), New-Snapshot() (+8 more)

### Community 139 - "Template.Infrastructure.Search.csproj"
Cohesion: 0.14
Nodes (12): Elastic.Clients.Elasticsearch, Testcontainers, net10.0, Microsoft.Extensions.Logging.Abstractions, Microsoft.Extensions.Options.ConfigurationExtensions, Microsoft.NET.Sdk, net10.0, Microsoft.NET.Test.Sdk (+4 more)

### Community 140 - "Required tests"
Cohesion: 0.25
Nodes (8): Account functional tests, Architecture tests, Authorization tests, JWT validation tests, Options and startup tests, Persistence and migration tests, Required tests, Unit tests

### Community 141 - "Phase 21 Handoff — Final Consistency and Release Gate"
Cohesion: 0.11
Nodes (18): Acceptance criteria, Binding decisions and boundaries, Cleanup evidence standard, Complete final validation matrix, Current version, security, and license research, Dependencies and hard stop conditions, Documentation reconciliation, Final response (+10 more)

### Community 142 - "ElasticLoggingSettings"
Cohesion: 0.10
Nodes (12): Configuração e DI, Pacote do health check removido, Separação entre busca, logging e health check, Mudanças no sink Serilog, ElasticLoggingSettings, DataSet, Enabled, Namespace (+4 more)

### Community 144 - "ErrorCodeEnum"
Cohesion: 0.22
Nodes (8): Template.WebApi.Infrastructure.Enums, ErrorCodeEnum, Conflict, Forbidden, NotFound, Unauthorized, Unexpected, Validation

### Community 145 - "Required tests"
Cohesion: 0.25
Nodes (8): Adapter tests, Architecture tests, Functional tests, Health-check tests, Logging tests, Required tests, Tests with real Elasticsearch, Unit tests

### Community 146 - "Template.Application.Search.Persons"
Cohesion: 0.07
Nodes (10): Template.Integration.Tests.Search, Template.Tests.Shared, Template.Application.Search.Persons, Template.WebApi.Infrastructure.Logging, Template.Infrastructure.Search.Persons, Template.WebApi.Infrastructure.HealthChecks, Template.Infrastructure.Search, Template.Application.Interfaces.Search (+2 more)

### Community 147 - "GetProductByIdQuery"
Cohesion: 0.22
Nodes (4): GetProductByIdQuery, Id, GetProductByIdQueryHandler, GetProductByIdQueryHandlerTests

### Community 148 - ".ProcessAsync"
Cohesion: 0.17
Nodes (9): DeliveryAction, Ack, DeadLetter, Requeue, DeliveryDecision, Ack, Requeue, OrderSubscription (+1 more)

### Community 149 - "CountingProcessedMessageStore"
Cohesion: 0.33
Nodes (3): CountingProcessedMessageStore, Checks, Marks

### Community 151 - "ElasticsearchHealthCheckSettings"
Cohesion: 0.20
Nodes (6): ElasticsearchHealthCheckSettings, Enabled, FailureStatus, Name, Timeout, Url

### Community 152 - "Phase 14 Execution Handoff"
Cohesion: 0.12
Nodes (16): Acceptance criteria, Checkout and working-tree safety, Documentation and delivery, Health checks and deterministic startup, Isolated baseline environment, Phase 14 Execution Handoff, Phase 14 scope, PostgreSQL bootstrap and both databases (+8 more)

### Community 153 - "UnreachableLogSinkFactory"
Cohesion: 0.20
Nodes (3): LoggingSinkUnavailableHostTests, UnreachableLogSinkFactory, AdditionalConfiguration

### Community 155 - "Relatório das Fases 6–7 — 2026-10-10"
Cohesion: 0.11
Nodes (17): Arquivos alterados nesta execução, Automação e documentação, Comandos e resultados, Confirmação de escopo, Contrato e WebApi, Diff de contrato e baseline, Migrations e SQL, Persistência e testes (+9 more)

### Community 157 - "ITranslator"
Cohesion: 0.22
Nodes (6): Phase 13 — Standardize observability, health, and localization, Localization boundary, Scope of Phase 13, ITranslator, ServiceRegistration, Translator

### Community 159 - "DecodeResult"
Cohesion: 0.29
Nodes (7): DecodeResult, Incompatible, Invalid, DecodeStatus, Decoded, IncompatibleContract, InvalidPayload

### Community 160 - "IClassFixture"
Cohesion: 0.27
Nodes (3): ProductCacheDisabledFunctionalTests, ProductCacheFunctionalTests, ProductCacheScenario

### Community 163 - "Availability policy"
Cohesion: 0.50
Nodes (4): Availability policy, Health check, Logging, Search and indexing

### Community 165 - "ApplicationUser"
Cohesion: 0.13
Nodes (7): Template.Infrastructure.Identity.Seeds, Template.Infrastructure.Identity.Models, ApplicationUser, Created, Name, DefaultBasicUser, DefaultRoles

### Community 166 - "IdentityContext"
Cohesion: 0.16
Nodes (9): Acceptance criteria, Current-state characterization, Current-state inventory before editing, Migrations and seed idempotency, Current-state inventory before editing, IGetUserServices, IdentityContext, ServiceRegistration (+1 more)

### Community 169 - "MessagingHarness"
Cohesion: 0.15
Nodes (4): CapturedLog, MessagingHarness, Consumer, Publisher

### Community 171 - "CacheSettings"
Cohesion: 0.12
Nodes (6): Current-state inventory before editing, CacheSettings, AbsoluteExpiration, Enabled, OperationTimeout, SlidingExpiration

### Community 172 - "Current solution structure"
Cohesion: 0.16
Nodes (7): Current solution structure, Pré-condições verificadas (Fases 8 e 9), Pré-condição: Fase 8, IProductCache, ProductService, NoOpProductCache, ServiceRegistration

### Community 173 - "IAuthenticatedUserService"
Cohesion: 0.14
Nodes (12): Current-user and claims boundary, IAuthenticatedUserService, UserId, UserName, DesignTimeAuthenticatedUser, Instance, UserId, UserName (+4 more)

### Community 175 - "In-scope consistency work"
Cohesion: 0.33
Nodes (6): 1. Compatibility shims and temporary adapters, 2. Architecture exceptions and allowlists, 3. Dead or redundant packages and code, 4. API and duplicate code, 5. Images, configuration, and documentation, In-scope consistency work

### Community 176 - "Required tests"
Cohesion: 0.33
Nodes (6): Architecture tests, Exception and Problem Details tests, Health tests, Localization tests, Logging and correlation tests, Required tests

### Community 187 - "Error"
Cohesion: 0.33
Nodes (4): Error, Description, ErrorCode, FieldName

### Community 189 - "IClock"
Cohesion: 0.40
Nodes (3): Time handling, IClock, UtcNow

## Knowledge Gaps
- **833 isolated node(s):** `Microsoft.VisualStudio.SDK`, `Microsoft.VSSDK.BuildTools`, `UserName`, `Password`, `Password` (+828 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 1422 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **76 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Current solution structure` connect `Current solution structure` to `Person`, `DisabledOrderEventPublisher`, `Order`, `DistributedProductCache`, `ITranslator`, `AuditableBaseEntity`, `CacheSettings`, `Relatório da Fase 10 — 2026-10-10`, `LocalizationExtensions`, `IndexedPerson`, `IOrderEventPublisher`, `IProcessedMessageStore`, `IPersonSearchIndex`, `RabbitMqOrderConsumerService`, `RecordingPersonSearchIndex`, `ElasticsearchPersonSearchIndex`, `RabbitMqOrderEventPublisher`, `OrderPlacedEvent`, `RabbitMqSettings`, `PersonIndexResult`, `PersonBulkIndexResult`, `.Supported_8_19_To_9_5_Path_PreservesSyntheticData_AndLeavesTheSourceUntouched`?**
  _High betweenness centrality (0.206) - this node is a cross-community bridge._
- **Are the 4 inferred relationships involving `BaseResult` (e.g. with `Current implementation conventions` and `Errors and API results`) actually correct?**
  _`BaseResult` has 4 INFERRED edges - model-reasoned connections that need verification._
- **What connects `Microsoft.VisualStudio.SDK`, `Microsoft.VSSDK.BuildTools`, `UserName` to the rest of the system?**
  _833 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Current configuration names` be split into smaller, more focused modules?**
  _Cohesion score 0.14285714285714285 - nodes in this community are weakly interconnected._
- **Why does `BaseResult` connect `BaseResult` to `ApiResultMapper`, `.Handle`, `ErrorCategoryTests`, `UpdateProductCommand`, `PagedResponse`, `GetProductByIdQuery`, `ProductController`, `ProductFunctionalTests`, `ProductDto`, `Phase 3 — Incremental implementation plan`, `AuditableBaseEntity`, `CreateProductCommand`, `IClassFixture`, `Contexto técnico relevante`, `AccountServices`, `5. Target code standards`, `Error`, `Phase6HttpContractFunctionalTests`, `ChangePasswordRequest`?**
  _High betweenness centrality (0.061) - this node is a cross-community bridge._
- **Are the 31 inferred relationships involving `IndexedPerson` (e.g. with `Current solution structure` and `Testes adicionados`) actually correct?**
  _`IndexedPerson` has 31 INFERRED edges - model-reasoned connections that need verification._
- **Should `Container (Dockerfile)` be split into smaller, more focused modules?**
  _Cohesion score 0.048625792811839326 - nodes in this community are weakly interconnected._