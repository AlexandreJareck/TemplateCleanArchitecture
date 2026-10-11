# Phase 2 — Proposed AI workspace and refactoring target

Status: design only; no skills, custom agents, rules, package changes, or application refactors were implemented in this run.

Initial research date: **2026-10-08** (America/Sao_Paulo). The transitive-risk follow-up was checked on **2026-10-10**. Package versions were queried from official NuGet sources. Container versions were checked against vendor release documentation and official registry tags. “Latest” means latest stable release visible on the stated consultation date, not a prerelease.

## 1. Evidence and assumptions

The proposal is based on the generated Graphify graph (1,034 nodes, 1,946 edges, 74 communities), the project references and source code, successful restore/build/unit/integration validation, and official product documentation. The optional FullCycle repository was not needed because the application and primary vendor sources answered the architecture questions.

Reversible assumptions:

1. Keep .NET 10 because it is the current LTS and the projects already target `net10.0`; pin a stable SDK with `global.json` later.
2. Keep the application as a modular monolith, not microservices. Clean Architecture boundaries can be corrected without changing the deployment model.
3. Make API v2 the behavioral baseline and maintain v1 until an explicit deprecation decision is made.
4. Keep PostgreSQL as the single relational provider. SQL Server references appear unused.
5. Replace separate Redis Search and TimeSeries containers with Redis 8 integrated capabilities; retire RedisGraph unless a concrete academic requirement is confirmed.
6. Prefer explicit application ports plus infrastructure adapters for Redis, RabbitMQ, Elasticsearch, identity, and time. MediatR remains an implementation option, not a domain dependency.
7. Decisions adopted on 2026-10-10 are listed at the end of `phase-3-implementation-plan.md` (VSIX isolated, v1 kept and deprecated, RedisGraph removed, fakes for functional tests, Domain on net10.0, PostgreSQL 16 patch first).
8. Use stable releases only. Preview .NET 11 is not considered “latest usable” for this project.

## 2. Proposed AI workspace

### Design principles

- Keep always-loaded instructions small. OpenAI recommends concise skill descriptions and progressive disclosure; Claude Code similarly distinguishes persistent instructions from on-demand skills and isolated subagents ([OpenAI guidance](https://developers.openai.com/blog/rethinking-skills-and-prompts-for-gpt-6-astra), [Claude Code extension guide](https://code.claude.com/docs/en/features-overview)).
- Use the portable Agent Skills shape: one `SKILL.md` with required `name` and `description`, plus optional `scripts/`, `references/`, and `assets/` ([Agent Skills specification](https://agentskills.io/specification), [OpenAI Skills documentation](https://developers.openai.com/api/docs/guides/tools-skills)).
- Put deterministic enforcement in build tooling or hooks, not prompt prose. Use skills for reasoned workflows, rules for durable scope-specific conventions, and agents for isolated reviews.
- Add workspace pieces only after their trigger recurs. Do not preload a large catalog “just in case.”

### Proposed skills

Canonical skill sources live under `.agents/skills/`, which Codex reads directly. Claude Code only discovers project skills under `.claude/skills/`, so `scripts/link-agent-skills.ps1` links that path to `.agents/skills/` (a directory junction on Windows, a symbolic link elsewhere). The link is git-ignored and created once per checkout; never copy skills into `.claude/skills/`.

| Skill | Purpose and trigger | Inputs | Outputs | Proposed location |
|---|---|---|---|---|
| `dotnet-change-verification` | Select and run proportionate restore/build/test checks after a .NET code or project-file change. Trigger when C# or `.csproj` files change. | Changed paths, requested scope, dependency availability | Commands run, pass/fail counts, warnings, unverified checks | `.agents/skills/dotnet-change-verification/SKILL.md` |
| `package-upgrade` | Research and stage one coherent package family upgrade. Trigger for NuGet, SDK, or container version changes. | Package family, target stability, current TFM | Official-source version matrix, breaking-change notes, rollback and validation checklist | `.agents/skills/package-upgrade/SKILL.md` |
| `api-contract-change` | Protect routes, versions, status codes, Problem Details, auth, and OpenAPI during controller changes. Trigger when controllers, request/response contracts, or versioning change. | Affected endpoints and desired behavior | Contract delta, compatibility decision, required functional tests | `.agents/skills/api-contract-change/SKILL.md` |
| `ef-migration` | Create and validate additive EF migrations safely. Trigger only when entity mapping or schema changes require a migration. | Context, intended schema delta, target database | Migration files, generated SQL review, forward/backward rollout notes | `.agents/skills/ef-migration/SKILL.md` |
| `integration-adapter` | Implement or modify Redis, RabbitMQ, Elastic, identity, or other external adapters behind application ports. Trigger on an integration implementation or configuration change. | Port contract, vendor client, resilience and observability requirements | Adapter, DI registration, options validation, integration tests, operational notes | `.agents/skills/integration-adapter/SKILL.md` |
| `architecture-boundary-check` | Detect forbidden project/package dependencies. Trigger on project references, new packages, or moves across layers. | Solution/project graph and changed references | Boundary report and minimal corrective actions | `.agents/skills/architecture-boundary-check/SKILL.md` |

Each skill should keep its root instructions short, link one level deep to focused references, and place repeatable checks in `scripts/`. Skills must never read or print secret values. Before enabling networked skills, review them as privileged instructions, as required by the [OpenAI Skills safety guidance](https://developers.openai.com/api/docs/guides/tools-skills#risks-and-safety).

### Proposed agents

Agents are reviewers or specialists with isolated context. Store canonical prompts under `.agents/agents/`; project them to `.claude/agents/` only for Claude Code. Codex orchestration can use the same prompts explicitly; no assumption is made that Codex auto-discovers this custom agent directory.

| Agent | Purpose and trigger | Inputs | Outputs | Proposed location |
|---|---|---|---|---|
| `architecture-reviewer` | Review a proposed slice for dependency direction and responsibility placement. Trigger before merging cross-layer changes. | Diff, project graph, target architecture | Findings ranked by impact; no edits unless requested | `.agents/agents/architecture-reviewer.md` |
| `dependency-auditor` | Independently verify package/container versions, advisories, deprecations, and license/support status. Trigger for an upgrade batch. | Dependency inventory and target batch | Source-linked update report with date, risk, and unknowns | `.agents/agents/dependency-auditor.md` |
| `api-contract-reviewer` | Compare endpoint behavior and OpenAPI before/after a change. Trigger for controller/version/result changes. | Baseline contract, diff, functional-test results | Compatibility report and missing test cases | `.agents/agents/api-contract-reviewer.md` |
| `integration-test-reviewer` | Inspect test isolation and external service assumptions. Trigger when integration/functional tests or adapters change. | Test fixtures, configuration, logs | Determinism risks, fixture improvements, validation matrix | `.agents/agents/integration-test-reviewer.md` |

Use agents only where isolated context or parallel independent review adds value. Claude’s official guide recommends subagents for focused work that reads many files and returns a summary, while skills hold reusable content ([Claude Code subagent guide](https://code.claude.com/docs/en/sub-agents)).

### Proposed rules

| Rule | Purpose and trigger | Inputs | Outputs/enforcement | Proposed location |
|---|---|---|---|---|
| Core dependency rule | Always applies in Domain/Application: no infrastructure/framework clients in Domain; Application depends only on Domain and abstractions. | Changed Core files/project refs | Agent guidance plus architecture test | `Src/Core/AGENTS.md`; Claude projection `.claude/rules/core.md` |
| HTTP/API rule | Applies to controllers, middleware, filters, and contracts. | `Src/Presentation/**/*.cs` | Consistent routes, cancellation, result mapping, OpenAPI, auth | `Src/Presentation/AGENTS.md`; `.claude/rules/api.md` |
| Infrastructure rule | Applies to EF, identity, Redis, RabbitMQ, Elastic, migrations. | `Src/Infrastructure/**/*.cs` | Adapter-only implementations, options validation, resilience, no secret logging | `Src/Infrastructure/AGENTS.md`; `.claude/rules/infrastructure.md` |
| Test rule | Applies to test projects. | `Tests/**/*.cs` | Deterministic test categories, fixture ownership, no accidental live-service dependency | `Tests/AGENTS.md`; `.claude/rules/tests.md` |
| Mechanical C# style | Deterministic whitespace, naming, analyzers, nullable policy. | C# source | IDE/build enforcement | `.editorconfig`, `Directory.Build.props` |
| Package centralization | One source of direct NuGet versions and lock policy. | Project files | Restore/build enforcement | `Directory.Packages.props`, optionally lock files |

Root `AGENTS.md` remains the concise always-on contract. `CLAUDE.md` imports it using only `@AGENTS.md`, so the content has one source of truth. Claude Code documents `CLAUDE.md` as persistent context and path-scoped rules/skills as better homes for conditional detail ([Claude Code extension guide](https://code.claude.com/docs/en/features-overview)).

## 3. Dependency and integration update matrix

### Runtime and container integrations

| Integration | Current | Latest usable verified | Official source consulted 2026-10-08 | Breaking changes / disposition | Risk |
|---|---:|---:|---|---|---|
| .NET application runtime | TFM `net10.0`; installed runtime 10.0.11; active CLI is SDK `10.0.300-preview` | Runtime 10.0.12; SDK 10.0.401 | [.NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy) | Patch runtime update; pin stable SDK. Do not move to .NET 11 RC. | Medium |
| Domain target | `netstandard2.1` | Retarget to `net10.0` (decision 5, 2026-10-10) | [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy) | Retargeting simplifies modern APIs; broad netstandard compatibility is dropped (accepted). | Medium |
| VSIX target | .NET Framework 4.7.2 | Isolated; stay on VS SDK 17.x (decision 1) | [Visual Studio SDK NuGet](https://www.nuget.org/packages/Microsoft.VisualStudio.SDK) | Visual Studio 18/VSSDK 18 may require project/tooling changes. | High |
| PostgreSQL image | `postgres:16` (major-only tag) | 18.6; supported 16 patch is 16.15 | [PostgreSQL releases](https://www.postgresql.org/docs/release/), [18.6 notes](https://www.postgresql.org/docs/release/18.6/) | Major upgrade needs backup/restore or `pg_upgrade`, extension review, and migration rehearsal. Prefer first pinning 16.15, then a separate 18.x phase. | High |
| Elasticsearch image | 8.17.4 | 9.5.5 | [Elastic release notes](https://www.elastic.co/docs/release-notes/elasticsearch) | Major server upgrade; run Elastic upgrade/deprecation checks and align client/sink versions. | High |
| Kibana image | 8.17.4 | 9.5.5 | [Kibana release notes](https://www.elastic.co/docs/release-notes/kibana) | Must match the Elastic Stack compatibility policy; upgrade with Elasticsearch. | High |
| RabbitMQ server image | `rabbitmq:latest` (unresolved/mutable) | 4.3.6 | [RabbitMQ downloads](https://www.rabbitmq.com/docs/download), [release information](https://www.rabbitmq.com/release-information) | Pin explicit management image/tag. Review broker upgrade path and feature flags; client 7 migration is separate. | High |
| Redis server image | `redis:latest` (unresolved/mutable) | 8.10.2 | [Redis 8.10 notes](https://redis.io/docs/latest/develop/whats-new/8-10/), [Redis 8 breaking changes](https://redis.io/docs/latest/operate/rc/changelog/version-release-notes/8-0/) | Redis 8 integrates Search/JSON/TimeSeries and changes ACL categories. Pin tag/digest and retest auth/data. | High |
| Redis Insight image | 1.14.0 | 3.8.0 | [Redis Insight release notes](https://redis.io/docs/latest/develop/tools/insight/release-notes/) | Multi-major UI/data-volume change; treat as tooling, migrate its volume separately. | Medium |
| RedisGraph image | `redislabs/redisgraph` floating tag | No supported successor in Redis; Docker registry’s final line is 2.12.x | [RedisGraph EOL](https://redis.io/blog/redisgraph-eol/), [deprecated features](https://redis.io/docs/latest/operate/oss_and_stack/stack-with-enterprise/deprecated-features/) | End of support was 2025-01-31. Remove unless requirements prove it is used; otherwise select another graph database explicitly. | Critical |
| RediSearch image | `redislabs/redisearch:latest` | Use Redis 8.10.2 integrated Search | [Redis module lifecycle](https://redis.io/docs/latest/operate/oss_and_stack/stack-with-enterprise/modules-lifecycle/) | Separate modules are replaced by Redis 8 integrated capabilities. Validate command/ACL behavior. | High |
| RedisTimeSeries image | `redislabs/redistimeseries` floating tag | Use Redis 8.10.2 integrated Time Series | [Redis module lifecycle](https://redis.io/docs/latest/operate/oss_and_stack/stack-with-enterprise/modules-lifecycle/) | Replace separate container; migrate data and retest commands/ACLs. | High |

The compose file mounts `./init-db.sh` and passes it `POSTGRES_DB_NAME` and `POSTGRES_IDENTITY_DB_NAME`. The script creates the application and identity databases idempotently without embedded credentials. On 2026-10-10 its syntax, Compose wiring, clean-volume execution, connections to both created databases, and a second idempotent execution were verified against PostgreSQL 16. Full-stack startup and the WebApi HTTP smoke test remain pending.

### Direct NuGet packages

All “latest stable” values below came from the official NuGet V3 index and link to the official NuGet package page. A same-version result does not mean the package is strategically appropriate.

| Package | Current | Latest stable | Official source consulted 2026-10-08 | Breaking changes / recommended handling | Risk |
|---|---:|---:|---|---|---|
| AspNetCore.HealthChecks.Elasticsearch | 9.0.0 | 9.0.0 | [NuGet](https://www.nuget.org/packages/AspNetCore.HealthChecks.Elasticsearch) | No newer stable package found; verify compatibility with Elastic 9 before server upgrade. | Medium |
| AspNetCore.HealthChecks.UI.Client | 9.0.0 | 9.0.0 | [NuGet](https://www.nuget.org/packages/AspNetCore.HealthChecks.UI.Client) | No version change; validate response contract. | Low |
| coverlet.collector | 6.0.4 | 10.1.0 | [NuGet](https://www.nuget.org/packages/coverlet.collector) | Multi-major jump; review collector/MSBuild/test-platform release notes and coverage output. | High |
| Elastic.Clients.Elasticsearch | 8.17.1 | 9.5.3 | [NuGet](https://www.nuget.org/packages/Elastic.Clients.Elasticsearch), [9.x breaking changes](https://www.elastic.co/docs/release-notes/elasticsearch/clients/dotnet/breaking-changes) | 9.0 changes query/aggregation containers, descriptors, date/time types, fields, and serialization. Upgrade with server. | High |
| Elastic.Serilog.Sinks | 8.12.3 | 9.0.0 | [NuGet](https://www.nuget.org/packages/Elastic.Serilog.Sinks) | Major jump and server-template bootstrap behavior; isolate logging startup and test unavailable Elastic. | High |
| FluentValidation.AspNetCore | 11.3.1 | 11.3.1 | [NuGet](https://www.nuget.org/packages/FluentValidation.AspNetCore), [official repository](https://github.com/FluentValidation/FluentValidation.AspNetCore) | Package is unsupported; replace auto-validation with explicit/core FluentValidation integration. | High |
| FluentValidation.DependencyInjectionExtensions | 12.1.1 | 12.1.1 | [NuGet](https://www.nuget.org/packages/FluentValidation.DependencyInjectionExtensions) | Keep; align all validation packages on supported core 12.x behavior. | Low |
| MediatR | 14.0.0 | 14.2.0 | [NuGet](https://www.nuget.org/packages/MediatR) | Minor update; verify registration and pipeline behaviors. | Medium |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.1 | 10.0.12 | [NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.Authentication.JwtBearer) | Patch-family update with .NET 10 servicing. | Low |
| Microsoft.AspNetCore.Hosting | 2.3.9 | 2.3.13 | [NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.Hosting) | Legacy direct reference in functional tests; remove and use shared framework rather than updating. | Medium |
| Microsoft.AspNetCore.Identity | 2.3.9 | 2.3.13 | [NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.Identity) | Legacy metapackage mixed with Identity EF 10; remove after confirming no unique asset is needed. | High |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 10.0.1 | 10.0.12 | [NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.Identity.EntityFrameworkCore) | Patch-family update; align with all EF/ASP.NET packages. | Low |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.1 | 10.0.12 | [NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.Mvc.Testing) | Patch-family update; retest host customization. | Low |
| Microsoft.AspNetCore.Mvc.Versioning.ApiExplorer | 5.1.0 | 5.1.0 | [NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.Mvc.Versioning.ApiExplorer), [migration guide](https://github.com/dotnet/aspnet-api-versioning/wiki/Migration/00b9bedee183235c9e158d1f6ce6fe9db236d1a8) | Legacy package is servicing-only. Replace with `Asp.Versioning.Mvc.ApiExplorer` 10.2.0; namespaces and some routing/error behavior change. | High |
| Microsoft.AspNetCore.OpenApi | 10.0.1 | 10.0.12 | [NuGet](https://www.nuget.org/packages/Microsoft.AspNetCore.OpenApi) | Patch-family update; align with runtime. | Low |
| Microsoft.EntityFrameworkCore | 10.0.1 | 10.0.12 | [NuGet](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore) | Patch-family update as one EF batch. | Low |
| Microsoft.EntityFrameworkCore.Design | 10.0.1 | 10.0.12 | [NuGet](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Design) | Patch-family update; validate migrations tooling. | Low |
| Microsoft.EntityFrameworkCore.InMemory | 10.0.1 | 10.0.12 | [NuGet](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.InMemory) | Patch-family update; do not treat InMemory behavior as PostgreSQL equivalence. | Low |
| Microsoft.EntityFrameworkCore.SqlServer | 10.0.1 | 10.0.12 | [NuGet](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.SqlServer) | No SQL Server code path found; remove from Identity and Persistence instead of updating. | Medium |
| Microsoft.EntityFrameworkCore.Tools | 10.0.1 | 10.0.12 | [NuGet](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Tools) | Patch-family update; keep private tooling assets. | Low |
| Microsoft.Extensions.Caching.StackExchangeRedis | 10.0.1 | 10.0.12 | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Caching.StackExchangeRedis) | Patch-family update; move reference/registration to Infrastructure. | Medium |
| Microsoft.Extensions.Configuration | 10.0.1 | 10.0.12 | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Configuration) | Patch-family update; may be redundant through framework references. | Low |
| Microsoft.Extensions.DependencyInjection | 10.0.1 | 10.0.12 | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection) | Patch-family update; may be redundant through framework references. | Low |
| Microsoft.Extensions.Hosting.Abstractions | 10.0.1 | 10.0.12 | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Hosting.Abstractions) | Patch-family update; direct Application use is tied to hosted RabbitMQ consumers and should move outward. | Medium |
| Microsoft.NET.Test.Sdk | 18.0.1 | 18.10.1 | [NuGet](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk) | Same major; update all test projects together and verify discovery. | Medium |
| Microsoft.VisualStudio.Azure.Containers.Tools.Targets | 1.21.0 | 1.23.0 | [NuGet](https://www.nuget.org/packages/Microsoft.VisualStudio.Azure.Containers.Tools.Targets) | Minor tooling update; validate Visual Studio container workflow separately. | Medium |
| Microsoft.VisualStudio.SDK | 17.0.32112.339 | 17.14.40265 | [NuGet](https://www.nuget.org/packages/Microsoft.VisualStudio.SDK) | Same major; prefer latest 17.x unless the VSIX is deliberately migrated to Visual Studio 18. | Medium |
| Microsoft.VSSDK.BuildTools | 17.12.2069 | 18.9.820 | [NuGet](https://www.nuget.org/packages/Microsoft.VSSDK.BuildTools) | Major toolchain jump. Keep VSIX update isolated; `dotnet build` is unsupported for deployment. | High |
| Moq | 4.20.72 | 4.21.0 | [NuGet](https://www.nuget.org/packages/Moq) | Minor update; run unit suite. | Low |
| Newtonsoft.Json | 13.0.4 | 13.0.4 | [NuGet](https://www.nuget.org/packages/Newtonsoft.Json) | No update. Consider `System.Text.Json` later, as a behavior change rather than package cleanup. | Medium |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.0 | 10.0.3 | [NuGet](https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL) | Patch update; align with EF 10.0.x and validate migrations/queries. | Low |
| RabbitMQ.Client | 6.8.1 | 7.2.2 | [NuGet](https://www.nuget.org/packages/RabbitMQ.Client), [RabbitMQ .NET client guide](https://www.rabbitmq.com/client-libraries/dotnet-api-guide) | Major API change toward async operations and `ReadOnlyMemory<byte>` payloads; rewrite publisher/consumers behind ports. | High |
| Serilog.AspNetCore | 10.0.0 | 10.0.0 | [NuGet](https://www.nuget.org/packages/Serilog.AspNetCore) | No update. | Low |
| Serilog.Enrichers.ClientInfo | 2.8.0 | 2.9.0 | [NuGet](https://www.nuget.org/packages/Serilog.Enrichers.ClientInfo) | Minor update; verify property names and privacy policy. | Low |
| Serilog.Exceptions | 8.4.0 | 8.4.0 | [NuGet](https://www.nuget.org/packages/Serilog.Exceptions) | No update; review whether destructuring could expose sensitive data. | Low |
| Serilog.Extensions.Logging | 10.0.0 | 10.0.0 | [NuGet](https://www.nuget.org/packages/Serilog.Extensions.Logging) | No update; confirm direct reference is required by the chosen Serilog setup. | Low |
| Shouldly | 4.3.0 | 4.3.0 | [NuGet](https://www.nuget.org/packages/Shouldly) | No update. | Low |
| Swashbuckle.AspNetCore.SwaggerGen | 10.1.0 | 10.3.0 | [NuGet](https://www.nuget.org/packages/Swashbuckle.AspNetCore.SwaggerGen) | Minor update; regenerate and compare OpenAPI. | Medium |
| Swashbuckle.AspNetCore.SwaggerUI | 10.1.0 | 10.3.0 | [NuGet](https://www.nuget.org/packages/Swashbuckle.AspNetCore.SwaggerUI) | Update with SwaggerGen; smoke-test UI. | Medium |
| System.IdentityModel.Tokens.Jwt | 8.15.0 | 8.23.0 | [NuGet](https://www.nuget.org/packages/System.IdentityModel.Tokens.Jwt) | Minor update; verify claim mapping, token validation, and expiry tests. | Medium |
| xunit | 2.9.3 | 2.9.3 | [NuGet](https://www.nuget.org/packages/xunit) | No update. | Low |
| xunit.runner.visualstudio | 3.1.5 | 4.0.0 | [NuGet](https://www.nuget.org/packages/xunit.runner.visualstudio) | Major runner update; verify compatibility with xUnit v2 and IDE/CLI discovery before adoption. | High |

NuGet restore also reported high-severity vulnerabilities in transitive `Microsoft.OpenApi` 2.3.0 and `System.Security.Cryptography.Xml` 8.0.2. The implementation must identify their parent dependency paths with a vulnerability audit and eliminate or override them through supported top-level upgrades; do not blindly add transitive pins.

### Security-relevant transitive packages

These are not direct project references. Their resolved versions came from the successful 2026-10-08 restore/audit. The preferred remediation is to update the owning direct package family; use an explicit transitive override only when the parent cannot resolve a safe compatible version and validation proves the override supported.

| Transitive package | Resolved | Latest stable usable verified | Official source consulted | Breaking changes / recommended handling | Risk |
|---|---:|---:|---|---|---|
| Microsoft.OpenApi | 2.3.0 | 2.12.2 for the ASP.NET Core 10-compatible 2.x line; 3.10.2 is the latest overall | [NuGet 2.12.2](https://www.nuget.org/packages/Microsoft.OpenApi/2.12.2), [NuGet latest](https://www.nuget.org/packages/Microsoft.OpenApi), [official v2 upgrade guide](https://github.com/microsoft/OpenAPI.NET/blob/main/docs/upgrade-guide-2.md), consulted 2026-10-10 | Resolve through the ASP.NET Core OpenAPI/Swashbuckle family. Version 2 introduced async parsing, nullable model changes, lazy references, `JsonNode` replacements, and removed APIs; version 3 is not the target line for the current ASP.NET Core 10 integration. Regenerate and compare OpenAPI. | High |
| System.Security.Cryptography.Xml | 8.0.2 | 10.0.12 for the current .NET 10 target | [NuGet](https://www.nuget.org/packages/System.Security.Cryptography.Xml/10.0.12), consulted 2026-10-10 | Prefer a supported parent-package update. If an explicit override is necessary, align it with .NET 10 and retest authentication, XML signature/encryption paths, restore audit, and runtime loading. | High |

## 4. Target Clean Architecture

Microsoft’s guidance places business logic and the application model at the center; infrastructure implementations depend inward on application abstractions, while UI and infrastructure are composed at runtime ([Microsoft Clean Architecture guidance](https://learn.microsoft.com/en-us/dotnet/standard/modern-web-apps-azure-architecture/common-web-application-architectures)).

### Target projects and responsibilities

| Layer/project | Responsibilities | May depend on | Must not depend on |
|---|---|---|---|
| `Template.Domain` | Entities, value objects, domain events, invariants, domain services, domain errors | BCL only | MediatR, EF, ASP.NET, Redis, RabbitMQ, Elastic, Serilog, DTOs tied to transport |
| `Template.Application` | Use cases, commands/queries, validators, application DTOs, ports, authorization policies, transaction abstractions | Domain, small application abstractions | Vendor clients, EF implementations, ASP.NET controllers, concrete infrastructure |
| `Template.Infrastructure.Persistence` | EF Core DbContext, mappings, migrations, repositories, transaction adapter, PostgreSQL | Application, Domain | Presentation |
| `Template.Infrastructure.Identity` | Identity models/store, JWT issuance/validation adapter, current-user adapter | Application, Domain | Presentation contracts except composition extensions |
| `Template.Infrastructure.Integrations` (new or split projects if justified) | Redis cache, RabbitMQ publisher/consumers, Elastic search/log transport, resilience | Application, Domain | Controllers |
| `Template.Infrastructure.Resources` | Localization adapter/resources | Application | Presentation behavior |
| `Template.WebApi` | HTTP transport, authentication middleware, versioning, Problem Details, OpenAPI, composition root | Application plus infrastructure registrations | Business rules or direct data access |
| Test projects | Unit, infrastructure integration, contract/functional, architecture tests | Subject under test | Live services unless fixture explicitly owns them |

Dependency rule: `Domain <- Application <- Infrastructure`; `WebApi -> Application` and references Infrastructure only to compose adapters. Runtime flow may go from Application ports to Infrastructure implementations, but compile-time dependencies remain inward.

### Immediate boundary corrections

- Move `ProductDto` from Domain to Application contracts.
- Move `ElasticService`, `RabbitMqService`, RabbitMQ hosted consumers, Redis registration, and vendor package references out of Application.
- Replace direct external concepts with ports such as `IProductCache`, `IProductSearch`, `IEventPublisher`, and idempotent message-handler interfaces.
- Keep account use cases in Application and ASP.NET Identity implementation in Infrastructure.Identity.
- Make Elastic logging optional/fail-soft at startup, especially under the Test environment.
- Replace reflection-based repository registration with explicit registrations unless a measured need justifies scanning.

## 5. Target code standards

Examples are illustrative design targets, not code added in this run.

### Controllers

Controllers are thin transport adapters: explicit version/route, injected sender, cancellation token, correct status codes, typed contracts, no business logic, and no service-location from `HttpContext`.

```csharp
[ApiController]
[ApiVersion(2.0)]
[Route("api/v{version:apiVersion}/products")]
public sealed class ProductsController(ISender sender) : ControllerBase
{
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IResult> Get(Guid id, CancellationToken ct)
        => (await sender.Send(new GetProduct(id), ct)).ToHttpResult();
}
```

### Application use cases

One request and one handler per use case. Depend on ports, pass cancellation, and use explicit outcome types. Validation is explicit or a tested MediatR pipeline behavior.

```csharp
public sealed record CreateProduct(string Name, decimal Price, string BarCode)
    : IRequest<Result<ProductResponse>>;

internal sealed class CreateProductHandler(IProductRepository products, IUnitOfWork unitOfWork)
    : IRequestHandler<CreateProduct, Result<ProductResponse>>
{
    public async Task<Result<ProductResponse>> Handle(CreateProduct request, CancellationToken ct)
    {
        var product = Product.Create(request.Name, request.Price, request.BarCode);
        await products.AddAsync(product, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return ProductResponse.From(product);
    }
}
```

### Entities and value objects

Entities guard invariants and expose behavior. Use `decimal` for money, controlled construction, UTC through an application clock where current time affects behavior, and no public setters.

```csharp
public sealed class Product : AuditableEntity
{
    private Product() { }
    private Product(string name, decimal price, string barCode) => RenameAndReprice(name, price, barCode);

    public string Name { get; private set; } = null!;
    public decimal Price { get; private set; }
    public string BarCode { get; private set; } = null!;

    public static Product Create(string name, decimal price, string barCode)
        => new(name, price, barCode);

    public void RenameAndReprice(string name, decimal price, string barCode)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Product name is required.");
        if (price < 0) throw new DomainException("Product price cannot be negative.");
        Name = name.Trim(); Price = price; BarCode = barCode;
    }
}
```

### Repositories and infrastructure adapters

Expose use-case-oriented ports, accept cancellation tokens, keep `IQueryable` inside Infrastructure, and avoid a generic repository when EF Core already supplies the required abstraction.

```csharp
public interface IProductRepository
{
    Task<Product?> FindAsync(Guid id, CancellationToken ct);
    Task AddAsync(Product product, CancellationToken ct);
}

internal sealed class EfProductRepository(ApplicationDbContext db) : IProductRepository
{
    public Task<Product?> FindAsync(Guid id, CancellationToken ct)
        => db.Products.SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task AddAsync(Product product, CancellationToken ct)
        => await db.Products.AddAsync(product, ct);
}
```

### External integrations

Use typed options with startup validation, bounded timeouts, cancellation, structured logs without payload secrets, health checks, idempotency where messages can redeliver, and adapter tests. Do not make observability sinks a prerequisite for test-host construction.

```csharp
services.AddOptions<RabbitMqOptions>()
    .BindConfiguration("RabbitMq")
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

### Errors and API results

Use one application result model for expected failures and RFC Problem Details at the HTTP boundary. Do not return `BaseResult` directly from controllers. Map NotFound to 404, validation to 400, conflict to 409, unauthenticated to 401, forbidden to 403, and unexpected exceptions to 500 with no internal details.

### Tests

- Domain tests cover invariants without mocks.
- Handler tests mock only ports and assert state/outcome plus cancellation propagation.
- Persistence integration tests use PostgreSQL (for provider semantics) where it matters; EF InMemory remains suitable only for simple collaboration tests.
- Functional tests replace or own every external dependency. Test startup must not contact Elastic/RabbitMQ/Redis unless the fixture explicitly provisions it.
- Architecture tests enforce forbidden references and package boundaries.

```csharp
[Fact]
public void Create_rejects_negative_price()
{
    var act = () => Product.Create("Book", -1m, "ABC");
    act.ShouldThrow<DomainException>();
}
```

### General C# and repository standards

- Enable nullable reference types consistently; no new `#nullable disable`.
- Use file-scoped namespaces and one primary public type per file, except tightly coupled private/internal helpers.
- Suffix async I/O methods with `Async`; accept `CancellationToken` and pass it through.
- Use `DateTimeOffset`/UTC and an injected clock for observable time-dependent behavior.
- Prefer explicit registrations, immutable request/response records, structured logging, and analyzers.
- Keep secrets out of source, documentation, test output, and generated artifacts. Document configuration names only.
- Pin SDK, package families, and container tags/digests; avoid `latest`.
