# Phase 13 Report — Observability, health, and localization

Date: 2026-10-11 (America/Sao_Paulo). Checkout: `C:\Users\alexa\source\repos\TemplateCleanArchNET`, branch `main` at `dc20942` plus the Phase 13 working-tree changes.

## Executive summary

Phase 13 standardizes the cross-cutting behavior without changing the public API:

- A stable structured-log vocabulary (`Template.Application.Logging.LogEvents`, 37 `EventId`s with fixed ids and names) is applied to every existing operational log call in Application, Infrastructure and WebApi.
- Every HTTP request runs in a logging scope carrying `CorrelationId` = `HttpContext.TraceIdentifier`, the same value already returned as `traceId` in Problem Details; one `RequestCompleted` event is written per request. Message processing runs in a scope carrying a validated message id (`CorrelationId` helper: max 128 chars of `[A-Za-z0-9._:-]`).
- A Serilog redaction enricher masks secret-named properties (password, token, authorization, cookie, connection string, security stamp, query string, ...) and bearer/JWT/`password=` patterns in string values, for application and framework events.
- `ErrorHandlerMiddleware` now runs before routing/authentication, logs each unexpected exception exactly once (`UnhandledException`, with method, path and correlation), maps client cancellation to `RequestCanceled`/499 instead of 500, and aborts safely when the response already started.
- Health is split into legacy `/health` (unchanged checks and writer), `/health/live` (network-free) and `/health/ready` (required databases decide readiness; cache, broker and Elasticsearch only degrade it). Output is sanitized; outages are logged once per status change.
- `LocalizationSettings` is bound and validated through the options pipeline (`ValidateOnStart`); the `BuildServiceProvider()` call and its Phase 13 architecture exception are gone.
- `Serilog.Enrichers.ClientInfo`, `Serilog.Exceptions` and the direct `Serilog.Extensions.Logging` reference were removed after proving they were unused or supplied transitively at the same version.

OpenAPI v1/v2 remained identical to `docs/refactoring/baseline-phase-6`. No vendor server was upgraded and no Phase 14+ work was started.

## Prerequisite verification

Verified from the repository state before any change:

- `git log`: `dc20942 refactor: phase 12 harden Identity and JWT boundaries (#8)` on `main`, after `b9cfdc2` (Phase 11) and `31b1225` (Phase 10). `git status` showed only untracked `graphify-out/` cache files (preserved, not touched).
- `docs/refactoring/phase-11-report.md` and `phase-12-report.md` exist and record executed gate results (Phase 12: architecture 25/25, unit 77/77, integration 136/136, functional 147/147, PostgreSQL 5/5, RabbitMQ 17/17, Elasticsearch 13/13).
- Phase 11 final state present: `Elastic.Clients.Elasticsearch` 9.5.3 in Infrastructure.Search, `Elastic.Serilog.Sinks` 9.0.0 in WebApi, own `ElasticsearchHealthCheck`.
- Phase 12 state present: `JwtTokenFactory`/`SecurityStampTokenValidator` in Infrastructure.Identity, 401 bare `Bearer` challenge, 401/403 tests in `Phase6HttpContractFunctionalTests` and `JwtAuthenticationFunctionalTests`.
- Application has no Redis, RabbitMQ, Elastic, Identity, Serilog or health-check package or source (the Phase 12 architecture rules that assert this are present and pass in the Phase 13 run).

## Pre-change inventory

| Area | State found |
|---|---|
| Logging packages (WebApi) | `Serilog.AspNetCore` 10.0.0, `Elastic.Serilog.Sinks` 9.0.0, `Serilog.Extensions.Logging` 10.0.0, `Serilog.Exceptions` 8.4.0, `Serilog.Enrichers.ClientInfo` 2.8.0. Application and Infrastructure.Search: `Microsoft.Extensions.Logging.Abstractions` 10.0.12. |
| Serilog bootstrap | Static `Log.Logger` (console in Test, `ReadFrom.Configuration` otherwise); `UseSerilog()`; outside Test `ConfigureLog()` = console + optional Elasticsearch data-stream sink + `Serilog` section; `Enrich.FromLogContext()` only. No request logging, no client-info or exception-details enrichers in code or configuration. `Log.CloseAndFlush` was registered after `app.Run()` (never reached before shutdown). |
| Log calls | 25 calls in Application (order handler, product service), Caching, Messaging (consumer, delivery processor, publisher), Search; one unhandled-exception log in `ErrorHandlerMiddleware`; demo `Log X capturado!` calls in both `AccountController`s (kept: public v1/v2 behavior). No `EventId` anywhere. Property names mostly consistent (`MessageId`, `Subscription`, `OrderId`, `Exchange`, `ExceptionType`); the exception log used `{Method}`/`{Path}`, the consumer start log `{Count}`. |
| Correlation | None explicit. Problem Details already returned `traceId` = `HttpContext.TraceIdentifier`. Messages used `MessageId` (fallback: order id) without validation. |
| Sensitive data | No explicit payload logging found. Risks: ASP.NET Core hosting `Request starting` logs the query string; exception logs could carry arbitrary messages; nothing prevented a future `{Password}` property. |
| Exception mapping | `ErrorHandlerMiddleware` after routing/auth/Swagger: `ValidationException` → 400, `KeyNotFoundException` → 404, others → logged + sanitized 500. Exceptions thrown by authentication or earlier middleware escaped it. No cancellation handling; a started response would have thrown again. |
| Problem Details | `ApiResultMapper` (single mapping boundary): 400 `ValidationProblemDetails`, 401/403/404/409/500 `ProblemDetails`, `application/problem+json`, `traceId` extension. 409 has no producing `ErrorCode` today. |
| Auth failures | `ProblemDetailsAuthorizationMiddlewareResultHandler` wrote 401/403 Problem Details; nothing logged. |
| Health | `/health` (`UseHealthChecks`, predicate `true`, `UIResponseWriter`); only the Elasticsearch ping (`HEAD /`, 5 s, failure `Unhealthy`, disabled in Test). No database, cache or broker checks; no liveness/readiness split. |
| Dependencies at startup | PostgreSQL app + Identity: migrated/seeded at startup (startup fails without them). Redis: lazy, fail-soft cache. RabbitMQ: lazy publisher, background consumers with reconnect. Elasticsearch index: lazy, search returns 500 when down. Elastic sink: silent bootstrap, fail-soft. |
| Localization | `ITranslator` (Application) implemented by `Translator` (Infrastructure.Resources, `ResourceManager` over `ResourceGeneral`/`ResourceMessages`, neutral + `pt-BR`). `LocalizationSettings` `DefaultRequestCulture=pt-BR`, `SupportedCultures=[pt-BR,en,fa]` (no `fa` resources). `AddCustomLocalization` called `BuildServiceProvider()` and set `ValidatorOptions.Global.LanguageManager.Culture` once to the default culture. No options validation. |
| `BuildServiceProvider()` in production | Only `LocalizationExtensions` (the named Phase 13 architecture exception). |
| Existing tests | `ElasticLoggingTests`, `ElasticsearchHealthCheckTests`, `ElasticDependencyCompatibilityTests`, `Phase6HttpContractFunctionalTests`, `ProductValidationFunctionalTests`, JWT/401/403 tests. No log-capture, correlation, redaction, readiness or culture tests. |

## Structured logging standard

Convention: `EventId(id, name)` from `LogEvents`; the name equals the field name and never contains dynamic values; templates use placeholders only; ranges 1xxx HTTP, 2xxx lifecycle, 3xxx cache, 4xxx messaging, 5xxx search, 6xxx health, 7xxx orders, 8xxx products. Stable property names: `CorrelationId`, `RequestMethod`, `RequestPath`, `StatusCode`, `ElapsedMilliseconds`, `MessageId`, `Subscription`, `OrderId`, `Exchange`, `Attempt`, `MaxAttempts`, `Reason`, `ExceptionType`, `QueueCount`, `ProductId`, `CacheKey`, `Operation`, `Outcome`, `HttpStatus`, `ErrorType`, `IndexName`, `HealthCheckName`, `HealthStatus`, `PreviousHealthStatus`, `EnvironmentName`, `FailureCategory`.

| Event (id) | Level | Owner | Exception attached | Template properties / redaction |
|---|---|---|---|---|
| `RequestCompleted` (1000) | Information (Debug for `/health*`) | `RequestLoggingMiddleware` | No | `RequestMethod`, `RequestPath` (no query string), `StatusCode`, `ElapsedMilliseconds`; scope `CorrelationId` |
| `UnhandledException` (1001) | Error | `ErrorHandlerMiddleware` | Yes, once | `RequestMethod`, `RequestPath`, `CorrelationId` |
| `RequestCanceled` (1002) | Information | `ErrorHandlerMiddleware` | No | same as above |
| `AuthenticationChallenged` (1003) / `AuthorizationForbidden` (1004) | Information | `ProblemDetailsAuthorizationMiddlewareResultHandler` | No | method, path, correlation; never token, claims, user id or policy |
| `ApplicationStarted` (2000) / `ApplicationStopping` (2001) | Information | `Program` | No | `EnvironmentName` |
| `ApplicationStartupFailed` (2002) | Critical | `Program` | Yes | `FailureCategory` (exception type); covers invalid localization/JWT/cache/messaging options |
| `CacheOperationFailed` (3000) / `CacheEntryUnreadable` (3001) | Warning | `DistributedProductCache` | No (type only) | `Operation`, `CacheKey`, `ExceptionType` |
| `MessagingConsumersDisabled/Started/Stopped` (4000–4002) | Information | `RabbitMqOrderConsumerService` | No | `QueueCount` (was `Count`) |
| `MessagingBrokerUnreachable` (4003), `MessagingConnectionLost` (4004), `MessagingChannelClosed` (4017) | Warning | consumer service | No | `ExceptionType`, `Delay`, `ReplyCode` |
| `MessagingConnectionRecovered` (4005), `MessagingPublisherConnected` (4006) | Information | consumer / publisher | No | — |
| `MessagePublished` (4007) | Debug | publisher | No | `OrderId`, `Exchange` |
| `MessagePublishFailed` (4008) / `MessagePublishTimedOut` (4009) | Warning | publisher | No (type only; the exception is logged once by `UnhandledException`) | `OrderId`, `Exchange`, `ExceptionType` |
| `MessageRejected` (4010) | Warning | `OrderDeliveryProcessor` | No | validated `MessageId`, `Reason`, `BodyLength`, `MessageType`; never the body |
| `MessageRetryScheduled` (4011) / `MessageRetriesExhausted` (4012) | Warning / Error | `OrderDeliveryProcessor` | Yes (handler failure, once per attempt) | `MessageId`, `Subscription`, `Attempt`, `MaxAttempts` |
| `MessageDeadLettered` (4013), `MessageDeadLetterFailed` (4014), `MessageSettleFailed` (4015), `MessageProcessingCanceled` (4016) | Warning / Error / Warning / Information | consumer / processor | No | `MessageId`, `Subscription`, `Reason`, `ExceptionType` |
| `SearchIndexConfigured` (5000) | Information | `ElasticsearchPersonSearchIndex` | No | `IndexName`, endpoint authority only (credentials rejected by options validation) |
| `SearchOperationFailed` (5001), `SearchBulkPartiallyFailed` (5002), `SearchResponseUnreadable` (5003) | Warning (Debug for not-found) | search adapter | No | `Operation`, `Outcome`, `HttpStatus`, `ErrorType`, counts |
| `HealthDependencyStatusChanged` (6000) | Warning (Information on recovery) | `HealthStatusTracker` | No | `HealthCheckName`, `PreviousHealthStatus`, `HealthStatus` |
| `OrderReceived` (7000) / `OrderDuplicateSkipped` (7001) | Information | `OrderPlacedEventHandler` | No | `OrderId`, `Subscription`, `MessageId`, `IsRedelivery` |
| `ProductCacheHit` (8000) / `ProductCached` (8001) | Debug | `ProductService` | No | `ProductId` |

Elastic sink failures are deliberately **not** Serilog events: `ElasticLogSinkDiagnostics` (Phase 11) writes at most one category-only line per minute to standard error, so the sink can never log about itself. Localization configuration failures surface as an `OptionsValidationException` naming only `LocalizationSettings:*` keys (and `ApplicationStartupFailed` when the process runs outside a test host).

### Dashboard compatibility mapping

| Before | After |
|---|---|
| `{Method}`, `{Path}` on the unhandled-exception event | `{RequestMethod}`, `{RequestPath}` (+ `{CorrelationId}`), event `UnhandledException`/1001 |
| `{Count}` on "Order consumers started" | `{QueueCount}`, event `MessagingConsumersStarted`/4001 |
| no `EventId` | `EventId.Id`/`EventId.Name` on every standardized event (Serilog structure property `EventId`) |

All other message templates and property names are unchanged, so existing text or property queries keep matching.

## Correlation policy

- HTTP: the correlation id is the server-generated `HttpContext.TraceIdentifier`. `RequestLoggingMiddleware` (outermost) opens `ILogger` scope `{CorrelationId}`; Serilog's provider attaches it to every event written during the request, including Application/Infrastructure services (tested with `ProductService`). The same value is the `traceId` of every Problem Details response. W3C `traceparent` continues to be handled by ASP.NET Core `Activity` (Serilog also records `TraceId`/`SpanId`).
- No inbound HTTP correlation header is accepted and no response header was added: the existing observable behavior is preserved, so there is no public contract change.
- Messaging: `OrderDeliveryProcessor` opens scope `{CorrelationId, Subscription}` around decoding, handling and retry decisions. The inbound `MessageId` is untrusted: it is used only if `CorrelationId.IsValid` (≤128 chars, `[A-Za-z0-9._:-]`); otherwise the deterministic order id is used (keeps deduplication stable across redeliveries); for an undecodable message a new GUID is generated. Invalid or oversized values are never logged.
- Nothing in the correlation context contains tokens, credentials, user identifiers or payloads.

## Redaction policy

Never logged: passwords, JWTs/refresh tokens/API keys/authorization headers, cookies/session ids, signing keys, security stamps, password hashes, reset tokens, complete connection strings, service credentials, request/response bodies, full `Person`/`Order`/account/indexed documents, exception `Data`, query strings.

Enforcement:

1. Call sites log only bounded identifiers and categories (reviewed for all 37 events; bodies, routing keys of users, documents and exceptions of lower layers are not logged).
2. `RequestLoggingMiddleware` logs the path only.
3. `SensitiveDataRedactionEnricher` (all events, including framework ones such as hosting's `QueryString`) replaces properties whose names contain `password, passwd, pwd, token, jwt, authorization, cookie, secret, apikey, api_key, connectionstring, securitystamp, signingkey, privatekey, querystring, credential` and masks `Bearer …`, JWT-shaped strings and `password=/pwd=/user id=/username=/api_key=/token=` assignments inside string values. It works on log properties only and never mutates application data.
4. `Serilog.Exceptions` was removed, so exception `Data` dictionaries are not destructured.
5. Health output and health logs use fixed descriptions; exceptions from checks are replaced by `"The check failed."`.
6. Framework per-probe health logs (`Microsoft.Extensions.Diagnostics.HealthChecks`) are raised to `Fatal`; `HealthStatusTracker` logs changes instead.

## Log ownership and log-once policy

- An unexpected exception is logged with its exception object only by `ErrorHandlerMiddleware` (HTTP) or `OrderDeliveryProcessor` (messages). Lower layers that rethrow (`RabbitMqOrderEventPublisher`) log a category-only warning with operational context (exchange, order id) and no exception object.
- Expected failures (validation, not found, 401, 403, business errors through `BaseResult`) are never logged as `UnhandledException` (tested).
- Cancellation by the client is `RequestCanceled`, not an error.
- Health outages are logged once per status transition.

## Exception and Problem Details behavior

| Case | Result (unchanged unless noted) |
|---|---|
| Validation (FluentValidation / model state) | 400 `ValidationProblemDetails`, `application/problem+json`, `traceId` |
| Unauthenticated | 401 Problem Details + `WWW-Authenticate: Bearer` |
| Forbidden | 403 Problem Details |
| Not found | 404 Problem Details (v2); v1 legacy 200 envelope preserved |
| Conflict | 409 mapping exists in `ApiResultMapper`; no `ErrorCode` produces it today, so no endpoint returns it (documented, not invented) |
| Unexpected exception | sanitized 500 Problem Details (`title` "An unexpected error occurred.", no message, type or stack), logged once with the same `traceId` |
| Client cancellation (`RequestAborted`) | **new**: no 500; logged `RequestCanceled`; status 499 recorded when the response had not started (the client is already gone) |
| Response already started | **new**: logged once, connection aborted instead of rethrowing |
| Exception in authentication/routing | **new**: now caught (middleware moved before `UseRouting`), mapped to the same sanitized 500 |

`ApiResultMapper` remains the single HTTP mapping boundary; the Problem Details schema, titles, types, content type and v1 behavior are unchanged.

## Health model

- **Liveness** `/health/live`: the process can execute its pipeline. Runs only `process` (tag `live`, no I/O). Never fails because a remote dependency is down.
- **Readiness** `/health/ready`: the process can serve its core capabilities. Runs tag `ready`. Unready (503) only when a `required` check is Unhealthy; any other non-healthy check reports `Degraded` with 200.
- **Legacy** `/health`: unchanged route, `UIResponseWriter` shape and status mapping; runs only checks tagged `legacy` (the Elasticsearch ping, exactly what it ran before; empty and Healthy in Test).

| Dependency | Classification | Check | Failure status | Timeout | Notes |
|---|---|---|---|---|---|
| Application PostgreSQL | Required | `application-database` (`CanConnectToApplicationDatabaseAsync`, owned by Infrastructure.Persistence) | Unhealthy | 5 s | startup also migrates it |
| Identity PostgreSQL | Required | `identity-database` (`CanConnectToIdentityDatabaseAsync`, owned by Infrastructure.Identity) | Unhealthy | 5 s | authentication depends on it |
| Redis cache | Optional/degraded; feature-disabled when `CacheSettings:Enabled=false` | `cache` (`IDistributedCache.GetAsync` of a fixed probe key, `WaitAsync`-bounded) | Degraded | 2 s | in-memory backend in Test |
| RabbitMQ | Optional/degraded; feature-disabled when `RabbitMqSettings:Enabled=false`; not registered in Test | `message-broker` (TCP connect to host:port, no AMQP credentials sent) | Degraded | 3 s | reachability only |
| Elasticsearch person index | Optional/degraded; feature-disabled when `ElasticsearchHealthCheckSettings:Enabled=false`; not registered in Test | `elasticsearch` (`HEAD /`, unchanged) | configured (`Unhealthy` default, kept for legacy `/health`) | configured (5 s) | in readiness it only degrades |
| Elasticsearch logging sink | Optional, not probed | — | — | — | fail-soft; failures visible through `ElasticLogSinkDiagnostics` on stderr |
| Localization resources | Startup-validated, not probed | — | — | — | embedded resources; options validated at start |
| In-memory EF provider | Test/development only | same database checks (`CanConnectAsync` returns true) | — | — | |

Endpoint details: `/health/live` and `/health/ready` return `application/json` `{status,totalDurationMs,checks:[{name,status,description,required,durationMs}]}`, `Cache-Control: no-store, no-cache` (framework default), anonymous, not part of OpenAPI. Degraded → 200, Unhealthy → 503. Entries that failed with an exception (including the framework timeout) report `"The check failed."`. Every remote check is bounded by its registration timeout and honors caller cancellation. No check runs at startup.

## Localization

- Boundary kept: Application depends only on `ITranslator`; `Translator`/`ResourceManager` stay in Infrastructure.Resources; no `IStringLocalizer`, request-culture or `.resx` types in Core (architecture tests). Translation keys and resource files unchanged; designers not edited.
- Options: `AddCustomLocalization(configuration)` binds `LocalizationSettings` (same section), registers `LocalizationSettingsValidator` with `ValidateOnStart`, and derives `RequestLocalizationOptions` through `Configure<IOptions<LocalizationSettings>>`. Validation: default culture present and predefined; supported set non-empty, predefined, no case-insensitive duplicates (rejected deterministically); default must be supported. Messages name keys only.
- Culture selection: ASP.NET Core default providers (query string `culture`, cookie, `Accept-Language`), parent-culture fallback (`en-US` → `en`), unsupported → `pt-BR`. `fa` is supported without resources and falls back to the neutral (English) resources — documented existing behavior.
- `ValidatorOptions.Global.LanguageManager.Culture` is still process-global: it is set once from the validated default culture inside `UseCustomLocalization`, never per request, so there is no cross-request race. This preserves the current built-in FluentValidation sentences (pt-BR) while field names come from `ITranslator` in the request culture.
- Background work (consumers) has no request culture; `Translator` uses the thread culture (tested).
- Missing keys: the key itself (`translator[...]`, `GetString(string)`), or the key with `_` replaced by spaces and formatted (`GetString(TranslatorMessageDto)`).

## `BuildServiceProvider()` removal

`LocalizationExtensions` no longer builds a provider. `Registration_Does_Not_Build_Intermediate_Service_Providers` now asserts an empty result (no replacement exception); its controlled mutation (`using var provider = services.BuildServiceProvider();`) is still detected.

## Logging package review

| Package | Finding | Action |
|---|---|---|
| `Serilog.AspNetCore` 10.0.0 | directly used (`UseSerilog`, `ReadFrom.Services`) | kept |
| `Elastic.Serilog.Sinks` 9.0.0 | directly used | kept |
| `Serilog.Extensions.Logging` 10.0.0 | no direct API use; supplied by `Serilog.AspNetCore` → `Serilog.Extensions.Hosting` 10.0.0 at the same version | direct reference removed |
| `Serilog.Exceptions` 8.4.0 | no code or configuration use | removed (also from `Directory.Packages.props`) |
| `Serilog.Enrichers.ClientInfo` 2.8.0 | no code or configuration use (`Enrich` section absent) | removed (also from `Directory.Packages.props`) |
| `Microsoft.Extensions.Logging.Abstractions` (Application, Search) | directly used (`ILogger`, `EventId`) | kept |
| `AspNetCore.HealthChecks.UI.Client` 9.0.0 | legacy `/health` writer | kept |

No version changed. Runtime logging verified by the functional log-capture tests and the existing `ElasticLoggingTests`. Rollback: re-add the three `PackageVersion`/`PackageReference` lines.

## Tests added or updated

- Unit: `LogEventsTests` (unique ids/names, published ids of critical events), `CorrelationIdTests` (valid, invalid, oversized, new ids).
- Integration: `OrderDeliveryProcessorTests` + correlation scope, unusable inbound ids replaced and never logged, rejected messages get a new safe id.
- Functional: `LoggingAndCorrelationTests` (event id/name/properties, correlation = Problem Details `traceId`, nested services inherit correlation, unexpected exception logged exactly once and sanitized, expected failures not logged as unexpected, 401/403 logged without tokens, client cancellation → `RequestCanceled`/499, secrets and personal payload markers absent); `HealthEndpointTests` (legacy shape, liveness with all dependencies down, readiness healthy/unready per required dependency/degraded optional, sanitized output, once-per-change logging, anonymous and absent from OpenAPI); `HealthCheckComponentTests` (classification, disabled features, readiness policy, broker healthy/unavailable/recovered, cache healthy/unavailable/timeout bound, caller cancellation, invalid configuration without details, transition logging incl. recovery); `LocalizationFunctionalTests` (default, each supported culture, parent and unsupported fallback, query-string precedence, localized validation field names, 40 concurrent mixed-culture requests, missing keys, background culture, valid registration, five invalid configurations failing startup without echoing values, empty supported set).
- Architecture: Phase 13 exception removed; Domain free of logging/health/HTTP/localization frameworks; Application only on the localization port; concrete localization only in Infrastructure.Resources; logging/health folders free of Application ports and DbContexts; removed logging packages stay removed; synthetic mutations for each new marker set.
- Controlled mutation executed: removing the redaction enricher from `LoggingExtensions` made `RepresentativeSecretsAndPersonalPayloads_NeverReachTheLogs` fail ("should not contain"); the line was restored.

## Commands executed and results

All with `C:\Users\alexa\.dotnet\dotnet.exe` (SDK 10.0.401), sequentially, on 2026-10-11.

| Command | Result |
|---|---|
| `dotnet restore TemplateCleanArchitecture.App.slnf --verbosity minimal --disable-parallel` | Succeeded |
| `dotnet build Src\Presentation\Template.WebApi\Template.WebApi.csproj --no-restore --verbosity minimal --maxcpucount:1 -p:UseSharedCompilation=false` | Succeeded, 0 warnings, 0 errors |
| `dotnet test Tests\Template.Architecture.Tests\...` | 30/30 passed |
| `dotnet test Tests\Template.Unit.Tests\...` | 107/107 passed |
| `dotnet test Tests\Template.Integration.Tests\...` | 141/141 passed |
| `dotnet test Tests\Template.Functional.Tests\...` | 191/191 passed |
| Mutation: functional `--filter RepresentativeSecrets` without the enricher | 1/1 failed as expected; restored |
| `powershell -ExecutionPolicy Bypass -File .\scripts\run-phase-validation.ps1` | All 13 steps exit 0 (Docker 28.4.0): restore, WebApi build, architecture, unit, integration, PostgreSQL, RabbitMQ, Elasticsearch, functional, vulnerability audit, `docker compose config -q`, OpenAPI capture, `graphify update . --force` (3,712 nodes, 7,978 edges, 205 communities), `git diff --check` |

## Final test counts

Architecture 30/30, unit 107/107, integration 141/141, PostgreSQL 5/5 (disposable PostgreSQL), RabbitMQ 17/17 (disposable `rabbitmq:4.2.2`), Elasticsearch 13/13 (disposable `elasticsearch:9.5.5`/`8.19.23`, ~1 min 41 s), functional 191/191.

## OpenAPI comparison

`scripts\capture-api-baseline.ps1` (gate step 10) output for v1 and v2 is identical to `docs/refactoring/baseline-phase-6` after line-ending normalization. The new health routes are middleware, not part of OpenAPI. The baseline was not modified.

## Vulnerability audit

`dotnet list TemplateCleanArchitecture.App.slnf package --vulnerable --include-transitive`: no vulnerable packages in any of the 16 projects.

## Residual risks

- The message-broker readiness check proves TCP reachability, not AMQP authentication; credential errors surface through `MessagingBrokerUnreachable` logs and the publish path.
- FluentValidation built-in sentences stay in the default culture (pt-BR) for every request (pre-existing behavior, preserved deliberately); only field names and business messages follow the request culture. Making them per request would change response text for `en`/`fa` clients and needs a contract decision.
- `fa` is a supported culture without resources (falls back to English).
- The redaction enricher is pattern-based defense in depth; exception messages and stack traces are still logged for unexpected failures (operationally necessary) and could contain secrets if a library put them there.
- EF Core logs connection errors of `CanConnectAsync` at Debug; with `Verbose` minimum level they are visible on every failing readiness probe.
- Readiness is not yet wired into Compose or an orchestrator (Phase 14 scope).
- The committed JWT key (Phase 12 residual risk) is unchanged.

## Rollback procedure

Revert the Phase 13 commit as a whole (`git revert <commit>`), or selectively:

1. Event names/ids: delete `Src/Core/Template.Application/Logging/` and the `LogEvents.*` arguments; restore `{Method}`/`{Path}`/`{Count}`.
2. Correlation: remove `app.UseMiddleware<RequestLoggingMiddleware>()` and the `BeginCorrelationScope` calls in `OrderDeliveryProcessor`.
3. Redaction: remove `.Enrich.With<SensitiveDataRedactionEnricher>()` (bootstrap and `ConfigureLogger`) and the health-check level override.
4. Exception middleware: move `app.UseMiddleware<ErrorHandlerMiddleware>()` back before `MapControllers` and drop the cancellation/started-response branches.
5. Health routing: remove the `/health/live` and `/health/ready` `UseHealthChecks` calls and `AddDependencyHealthChecks`; set the legacy predicate back to `_ => true` (legacy output is identical either way).
6. Classification: change a registration's tags (`required` ↔ `optional`) or failure status in `HealthCheckExtensions`.
7. Localization: restore `builder.Services.Configure<LocalizationSettings>(...)` and the previous `AddCustomLocalization()`; architecture test must then list the exception again.
8. Packages: re-add `Serilog.Enrichers.ClientInfo` 2.8.0, `Serilog.Exceptions` 8.4.0 and `Serilog.Extensions.Logging` 10.0.0 to `Directory.Packages.props` and the WebApi project.

No data, index, dashboard, alert, log or volume was created or deleted, so no data rollback is needed.

## Gates not executed

- Optional gate step `06d` (Elastic upgrade rehearsal) was not run; it requires published 8.x/9.x WebApi paths and is unrelated to this phase.
- Legacy VSIX build (known non-gate) not run.
- No live `docker compose up` or `dotnet run` against real Redis/RabbitMQ was performed; readiness against live Redis and the compose broker remains unverified (covered by fakes, loopback sockets and the Docker-based suites).

## Confirmations

- No vendor server (Redis, PostgreSQL, RabbitMQ, Elasticsearch, Kibana, Redis Insight) was upgraded; no container image was pinned or changed; no package version changed.
- No EF migration was created; no migration designer/snapshot or `.resx` designer was edited.
- Public HTTP compatibility: routes, methods, bindings, status codes (except client-cancelled requests, which no client observes), v1 envelopes, v2 Problem Details, 401/403 and `WWW-Authenticate: Bearer`, DTOs, versioning and OpenAPI preserved; only `/health/live` and `/health/ready` were added (outside OpenAPI).
- The Phase 13 architecture exception (`BuildServiceProvider()` in `LocalizationExtensions`) was removed and no replacement exception was introduced.
- No Phase 14+ work (Compose baseline, image pinning, service upgrades, OpenTelemetry) was started.
