# Relatório da Fase 21 — Consistência final e gate de release

Execução: 2026-10-13 a 2026-10-15 (America/Sao_Paulo). Checkout autoritativo: `C:\Users\alexa\source\repos\TemplateCleanArchNET`, branch `main` em `ec5bc0d` (Fase 20 mergeada), árvore limpa no início.

## 1. Resumo executivo e decisão

**Decisão de release: NOT READY.**

Bloqueador único: o gate **G-SEC-SECRETS** está `Failed`. A chave de assinatura JWT (`JwtSettings:Key`) está commitada em `appsettings.json`, `.Development.json`, `.Production.json` e `.Test.json`. Isso já existia antes da Fase 12, foi registrado como risco residual naquela fase e nunca foi rotacionado. Em 2026-10-15 o dono decidiu explicitamente classificar o problema como bloqueador, e não como risco aceito. Corrigir exige remover a chave do código-fonte, passá-la a um secret store e planejar a transição dos tokens emitidos. Isso fica para uma fase futura autorizada; a Fase 21 não muda esse comportamento.

Todos os demais gates obrigatórios foram executados contra o workspace final e passaram (§19). Resumo:

- Limpeza com prova: a exceção arquitetural temporária da Fase 7 foi removida (lote C1) e três tipos mortos foram apagados (lote C2). Nenhuma exceção temporária de arquitetura permanece.
- Patch de segurança do .NET 10.0.13 aplicado com autorização (decisão 1A de 2026-10-14): SDK 10.0.402, pacotes Microsoft 10.0.13 e imagens `aspnet:10.0.13`/`sdk:10.0.402` fixadas por digest. A release oficial de 2026-10-13 corrige 44 CVEs, entre eles RCE e EoP no ASP.NET Core e no SDK.
- Auditorias:
  - vulnerabilidades: 0 em todas as severidades, nos 17 projetos e no VSIX;
  - depreciação: só xunit 2.x, adiado;
  - licenças: 154/154 dentro da política aprovada.
- OpenAPI v1/v2 idêntico ao baseline aprovado. A v1 continua presente e deprecated.
- Matriz completa com Docker real: Compose 98/98, Redis 60/60 + 98/98, PostgreSQL 68/68 + 98/98, RabbitMQ 81/81 + 19/19 (4.2.2) + 97/97, Redis Insight 67/67 + 95/95, Elastic/Kibana 49/49.
- VSIX: build, instalação, **carga do shell** e desinstalação no hive experimental do VS 2026 (decisão 4A). Isso fecha a lacuna da Fase 20.

## 2. Checkout, árvore e mudanças concorrentes

- No início, `git status --short` estava vazio e o HEAD em `ec5bc0d`. Não havia mudança concorrente a preservar. A worktree antiga `.claude/worktrees/fase-8-redis-adapter-f4fda8` não foi tocada.
- Todas as mudanças desta fase são próprias (§6–§8 e §22). Nenhuma foi feita por outro agente.
- Eventos de ambiente durante a fase:
  - O Docker Desktop 4.47.0 caiu três vezes ao iniciar (socket obsoleto `%LOCALAPPDATA%\Docker\run\dockerInference`). O dono reiniciou a máquina e o Docker. Não houve "reset to factory defaults".
  - Antes de qualquer ação desta fase, o host já tinha **0 contêineres e só 6 imagens**. Os contêineres parados e as imagens de rollback das fases anteriores tinham sido removidos fora desta sessão. Os 274 volumes estavam intactos. As imagens de rollback foram baixadas de novo pelos digests fixados.

## 3. Reconciliação das Fases 0–20

| Fase | Status | Evidência atual |
|---|---|---|
| 0–5 | Adotada e evidenciada | `phases-0-5-report.md`; baseline OpenAPI original em `docs/refactoring/baseline/`; testes de arquitetura/unitários verdes |
| 6–7 | Adotada e evidenciada | `phases-6-7-report.md`; `baseline-phase-6` reproduzido byte a byte (CRLF normalizado); migração `20261010151729_ProductPriceDecimal` aplicada e testada no PostgreSQL |
| 8 | Adotada e evidenciada | `phase-8-report.md`; `Template.Infrastructure.Caching`; suíte Redis 23/23 |
| 9 | Adotada e evidenciada | `phase-9-report.md`; `Template.Infrastructure.Messaging`, RabbitMQ.Client 7.2.2; suíte broker 19/19 |
| 10–11 | Adotada e evidenciada | `phase-10-report.md`, `phase-11-report.md`; client 9.5.3/sink 9.0.0; suíte ES 13/13; ensaio 8.17.4→8.19.23→9.5.5 com Kibana 49/49 (WebApi 8.x publicada de `31b1225` via `git archive`) |
| 12 | Adotada e evidenciada (com risco residual) | `phase-12-report.md`; testes JWT funcionais verdes; a chave commitada continua (bloqueador desta fase) |
| 13 | Adotada e evidenciada | `phase-13-report.md`; testes funcionais de logging/health/localização |
| 14 | Adotada e evidenciada | `phase-14-report.md`; `compose-baseline.ps1` 98/98 |
| 15 | Adotada e evidenciada | `phase-15-report.md`; ensaio 60/60, rollback 98/98 |
| 16 | Adotada e evidenciada (checkpoint 16.x, decisão 6) | `phase-16-report.md`; ensaio 68/68, rollback 98/98 |
| 17 | Adotada e evidenciada | `phase-17-report.md`; ensaio 81/81, suíte 4.2.2 19/19, rollback 97/97 |
| 18 | Adotada e evidenciada | `phase-18-report.md`; ensaio 67/67, rollback 95/95 |
| 19 | Adotada e evidenciada | `phase-19-report.md`; descoberta = execução nos 8 projetos; cobertura OK |
| 20 | Adotada; a carga, antes não verificada, foi **comprovada nesta fase no VS 2026** | `phase-20-report.md` + §16 |

Nenhuma fase adotada está ausente ou materialmente incompleta. A cronologia confere com o histórico `--first-parent` de `main` (PRs #1–#17).

## 4. Decisões registradas

As seis decisões de 2026-10-10 seguem válidas:

- VSIX 17.x isolado, sem VSSDK 18;
- v1 deprecated e sem remoção;
- RedisGraph removido;
- funcionais com fakes;
- Domain em `net10.0`;
- PostgreSQL 16.x.

Novas decisões do dono nesta fase:

| Data | Decisão |
|---|---|
| 2026-10-14 | 1A: aplicar o patch de segurança .NET 10.0.13 na Fase 21 |
| 2026-10-14 | 2B: política de severidade de vulnerabilidades = bloqueiam **high e critical**; low/moderate são registradas como risco |
| 2026-10-14 | 3A: aceitar todas as licenças encontradas, incluindo MediatR 14 (RPL-1.5 ou comercial Lucky Penny) e a EULA do VS Container Tools Targets (tooling de design-time) |
| 2026-10-14 | 4A: autorizado instalar/carregar/desinstalar o VSIX no hive `Exp` do VS 2026 Insiders |
| 2026-10-14 | Ajuste do gate de cobertura (§9) |
| 2026-10-15 | Remoção de 11 redes Docker vazias criadas pela 1ª rodada do gate desta fase (§17) |
| 2026-10-15 | Chave JWT commitada classificada como **bloqueador** (Not ready) |

## 5. Inventário pré-limpeza e ledger de candidatos

Estado pré-limpeza (`artifacts/validation/phase-21/pre-cleanup`): SDK 10.0.401, restore OK, build da WebApi com 0 avisos e 0 erros, arquitetura 30/30, grafo de pacotes com 246 entradas de produção e 469 de teste (`package-graph/phase21-before`).

| Candidato | Categoria | Evidência / consumidores procurados | Risco runtime | Teste alvo | Decisão |
|---|---|---|---|---|---|
| `Program.cs` resolvendo `ApplicationDbContext` para migrar e semear (exceção nomeada "Phase 7" em `Presentation_Has_Only_Named_Temporary_Persistence_Exception`) | Exceção arquitetural temporária | Só `Program.cs`; a Identity já usava `InitializeIdentityDatabaseAsync` (Fase 12) | DI/EF: mesma ordem (Identity migra e semeia, depois Application migra e semeia) | Integração em memória (seed idempotente via DI) + PostgreSQL real (migração e seed idempotentes, 0 pendentes) + arquitetura sem exceção + 2 mutações | **Removida (C1)** |
| `Template.Application.Wrappers.ApiErrorResponse`/`ApiErrorItem` | Código morto | 0 referências em código, testes, scripts e docs; ausente do OpenAPI aprovado `baseline-phase-6`; último uso removido na Fase 6 (`4421793`) | Serialização: nenhum serializer o recebe; não aparece no OpenAPI | OpenAPI idêntico; funcionais 191/191 | **Removido (C2)** |
| `Template.WebApi.Infrastructure.Enums.ErrorCodeEnum` | Código morto | 0 referências; último uso removido nas Fases 0–7 | Nenhum (enum sem uso) | Build + funcionais | **Removido (C2)** |
| `Template.WebApi.Infrastructure.Filters.ApiResultFilterAttribute` | Código morto | Nunca aplicado (`[ApiResultFilter]` removido em `74ea17f`) nem registrado em `MvcOptions.Filters`; v1/v2 usam `ApiResultMapper.FromModelState` | Filtro MVC só ativa se registrado | Funcionais de validação v1/v2 191/191; OpenAPI idêntico | **Removido (C2)** |
| Fallback de chaves `ElasticSearchSettings` (Search, health, logging) | Shim de compatibilidade | Ainda lido pelos 4 `appsettings*.json` | Binding de configuração | — | **Mantido** (consumidor ativo) |
| `Microsoft.EntityFrameworkCore.Relational` (entrada central sem `PackageReference`) | Entrada central | Pin transitivo (`CentralPackageTransitivePinningEnabled`) que alinha a versão trazida pelo Npgsql | Mudaria o grafo | Grafo de pacotes | **Mantido** (pin ativo) |
| `MessagePack`/`MessagePack.Annotations` 2.5.307 | Entrada central | Pin transitivo só do VSIX (advisories da Fase 20) | Reabriria 11 advisories | `vsix-validation` audit 0 | **Mantido** |
| `/health` legado, contrato `OrderPlacedEvent` v1, formato de documento 8.x, chaves de cache v1 | Formatos de fio/compatibilidade | Contratos públicos documentados | Altos | — | **Mantidos** |
| Controllers v1 duplicados | Código v1 | Sem sunset aprovado (decisão 2) | Contrato público | — | **Mantidos** |
| `#nullable disable` (5 arquivos) | Consistência | Comportamento de nulabilidade | Mudança de comportamento | — | **Mantido** (fora do escopo de limpeza) |
| Tags de imagem mutáveis | Imagens | Toda imagem em Compose, overlays, Dockerfile, scripts e testes está como `versão@sha256` | — | `docker compose config -q`; checks de pin nos ensaios | Nenhuma encontrada |
| `AspNetCore.HealthChecks.UI.Client`, `Microsoft.EntityFrameworkCore.Design`/`Tools`, `Microsoft.VisualStudio.Azure.Containers.Tools.Targets` | Pacotes diretos | Usado (`UIResponseWriter`) / tooling de migração / tooling Docker do VS | Design-time | — | **Mantidos** |

## 6. Lote C1: exceção temporária da Fase 7

- Antes: `Program.cs` com `using Microsoft.EntityFrameworkCore`, `...Persistence.Contexts`, `...Persistence.Seeds` e `GetRequiredService<ApplicationDbContext>()`.
- Depois: `Template.Infrastructure.Persistence.ServiceRegistration.InitializeApplicationDatabaseAsync(applyMigrations)`, espelho de `InitializeIdentityDatabaseAsync`. `Program.cs` chama as duas inicializações na mesma ordem de antes.
- Arquivos alterados:
  - `Src/Infrastructure/Template.Infrastructure.Persistence/ServiceRegistration.cs`
  - `Src/Presentation/Template.WebApi/Program.cs`
  - `Tests/Template.Architecture.Tests/ArchitectureRulesTests.cs`: a regra virou `Presentation_Has_No_Persistence_Context_Or_Repository_References`, que exige zero violações, e ganhou 2 mutações controladas
  - `Tests/Template.Integration.Tests/Data/ApplicationDatabaseInitializationTests.cs` (novo)
  - `Tests/Template.PostgreSql.Integration.Tests/PostgreSqlPersistenceTests.cs`
  - `Tests/Template.PostgreSql.Integration.Tests/PostgreSqlFixture.cs`: `ConnectionString` exposta e `TestAuthenticatedUser` internal
- Prova:
  - arquitetura 30/30;
  - integração 141 → 142;
  - PostgreSQL 5 → 6 (migração e seed idempotentes num PostgreSQL 16.15 real, 0 migrações pendentes, 5 produtos);
  - funcionais 191/191, que sobem a WebApi com seed em memória;
  - Compose baseline 98/98, que sobe a WebApi com migrações reais, reinicia e verifica migrações e seeds idempotentes;
  - Graphify: `Program.cs` → Persistence caiu de `{Persistence, Persistence.Contexts, Persistence.Seeds}` para `{Persistence}`.
- Rollback: reverter os 6 arquivos como um lote, sem Git destrutivo, e rodar `Template.Architecture.Tests`, que volta ao estado pré-limpeza 30/30 (`pre-cleanup/architecture.log`).

## 7. Lote C2: tipos mortos

- Removidos:
  - `Src/Core/Template.Application/Wrappers/ApiErrorResponse.cs`
  - `Src/Presentation/Template.WebApi/Infrastructure/Enums/ErrorCodeEnum.cs`
  - `Src/Presentation/Template.WebApi/Infrastructure/Filters/ApiResultFilterAttribute.cs`
  - os dois diretórios que ficaram vazios
- Cópia dos originais em `artifacts/validation/phase-21/c2-before/` (git-ignored).
- Prova: build da solução com 0 avisos e 0 erros; arquitetura 30, unit 107, integração 142, PostgreSQL 6 e funcionais 191; OpenAPI v1/v2 idêntico.
- Rollback: restaurar os 3 arquivos e repetir build e funcionais.

## 8. Patch .NET 10.0.13 (segurança)

- Fonte oficial: https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json e https://github.com/dotnet/core/blob/main/release-notes/10.0/10.0.13/10.0.13.md. A 10.0.13 saiu em 2026-10-13 com `security: true` e 44 CVEs, entre eles CVE-2026-70174 (RCE), CVE-2026-96230 (ASP.NET Core RCE), CVE-2026-70175 (ASP.NET Core EoP) e CVE-2026-70161 (SDK EoP). A linha 10.0 está em suporte ativo até 2028-11-14.
- O SDK 10.0.402 foi instalado com o `dotnet-install.ps1` oficial (assinatura Authenticode válida) em `C:\Users\alexa\.dotnet`. O SDK 10.0.401 e o runtime 10.0.12 ficaram instalados para rollback.
- Arquivos alterados:
  - `global.json`: 10.0.401 → 10.0.402
  - `Directory.Packages.props`: os 17 pacotes Microsoft de 10.0.12 para 10.0.13
  - `Src/Presentation/Template.WebApi/Dockerfile`:
    - `aspnet:10.0.13@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac`
    - `sdk:10.0.402@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa`
    - os dois digests foram resolvidos com `docker buildx imagetools inspect` em 2026-10-14.
- Grafo (`package-graph/phase21-after-patch` vs `phase21-before`): mesma quantidade de entradas (246/469). Só mudaram versões: 92 entradas de produção e 123 de teste de 10.0.12 para 10.0.13, e o transitivo `Microsoft.OpenApi` 2.12.0 → 2.12.2 trazido por `Microsoft.AspNetCore.OpenApi` 10.0.13. Nenhum pacote foi adicionado ou removido. O OpenAPI gerado ficou idêntico.
- Os designers e snapshots de migração mantêm `ProductVersion 10.0.12`, porque são gerados e não se editam à mão.
- Rollback: voltar `global.json`, as 17 versões e o Dockerfile para 10.0.12/10.0.401 (os digests anteriores estão no histórico Git) e rodar `run-phase-validation.ps1`.

## 9. Gate de cobertura e ajuste aprovado

Na 1ª rodada, o gate de cobertura falhou só por `minimumSources`: Application 40 < 41 e WebApi 31 < 32. A queda corresponde exatamente aos arquivos mortos removidos; `ErrorCodeEnum` não é instrumentável. As porcentagens subiram:

| Módulo | Linha antes → depois |
|---|---|
| Application | 94,24 → 95,12% |
| WebApi | 68,33 → 68,93% |

Com aprovação do dono (2026-10-14), `Tests/test-coverage-policy.json` ficou assim:

- Application `minimumSources` 41 → 40 e linha 93 → **94** (ratchet `floor(medido-1)`);
- WebApi `minimumSources` 32 → 31;
- nenhum outro mínimo foi alterado;
- a justificativa ficou registrada na descrição da política.

## 10. Retidos e justificativas

- Shims de configuração `ElasticSearchSettings`: têm consumidor ativo nos `appsettings*.json`.
- Pins transitivos `Microsoft.EntityFrameworkCore.Relational` e `MessagePack*`.
- `/health` legado, contrato `OrderPlacedEvent` v1, formato de documento 8.x, chaves de cache v1.
- API v1 inteira: deprecated, sem sunset.
- Overlays de rollback: Redis 8.4.0 com módulos, PostgreSQL 16.11, RabbitMQ 4.2.2, Redis Insight 1.14.0.

**Nenhuma exceção arquitetural temporária permanece.** A regra `WebApi_Is_The_Only_Http_Context_Adapter_For_The_Current_User` aceita `ApplicationDbContextFactory.cs`, que é a factory de design-time do EF dentro da Infrastructure (adapter de usuário de design-time). Essa disposição é permanente e justificada, não temporária.

## 11. Grafo de projetos e pacotes

- Projetos: os 17 do `App.slnf` (9 de produção em `net10.0` e 8 de teste), mais o VSIX `net472` isolado. Referências: `Domain ← Application ← Infrastructure`, com a WebApi como única raiz de composição (teste `WebApi_Is_The_Only_Project_That_Composes_Infrastructure`).
- Pacotes: 38 diretos e 154 distintos no total; 102 no escopo de produção.
- O grafo resolvido muda só nas versões do patch (§8).

## 12. Auditorias de pacotes (2026-10-14, SDK 10.0.402)

| Auditoria | Comando | Resultado |
|---|---|---|
| Vulnerabilidades (aplicação + testes) | `dotnet list TemplateCleanArchitecture.App.slnf package --vulnerable --include-transitive` | 17/17 projetos sem pacote vulnerável (`audits-final/vulnerable.txt` e passo `08` do gate) |
| Vulnerabilidades (VSIX) | `scripts/vsix-validation.ps1` (restore isolado com `NuGetAuditMode=all`, nível low) | 0 NU190x |
| Política de severidade | Decisão 2B | Bloqueiam high/critical; achados: **0 em qualquer severidade** |
| Depreciados | `... --deprecated --include-transitive` | Só `xunit`, `xunit.assert`, `xunit.core` e `xunit.extensibility.*` 2.9.3, marcados "Legacy", sucessor xunit.v3. Escopo de teste. Migração major adiada |
| Desatualizados | `... --outdated [--include-transitive]` | Ver §13 |
| Licenças | `scripts/license-inventory.ps1 -Label phase21-final` (novo, lê os `.nuspec` do cache NuGet local, sem rede) | 154 pacotes: 114 MIT, 32 Apache-2.0, 2 BSD-3-Clause, 2 PostgreSQL (Npgsql), 1 Apache-2.0 OR MPL-2.0, 1 Apache-2.0 por URL (xunit.abstractions), exceções aceitas MediatR 14.0.0 (arquivo `LICENSE.md`: RPL-1.5 ou comercial) e VS Container Tools Targets 1.21.0 (EULA Microsoft). Política 3A: **0 fora da política** (exit 0) |

## 13. Atualizações disponíveis e adiadas

| Item | Atual | Disponível | Classificação |
|---|---|---|---|
| Pacotes Microsoft 10.0.x | 10.0.13 | 10.0.13 | Atual (patch aplicado) |
| MediatR | 14.0.0 | 14.2.0 | Minor suportada, adiada (sem correção de segurança; licença) |
| FluentValidation | 12.1.1 | 12.2.0 | Minor suportada, adiada |
| Microsoft.NET.Test.Sdk | 18.10.1 | 18.11.0 | Minor de teste, adiada |
| xunit.runner.visualstudio | 3.1.5 | 4.0.1 | Major; mantida pela Fase 19 (Test Explorer não verificado) |
| xunit 2.x | 2.9.3 | xunit.v3 | Major/comportamental, fase futura |
| VS Container Tools Targets | 1.21.0 | 1.23.0 | Tooling, adiada |
| Transitivos Elastic.Ingest/Channels | 0.14.0 | 0.53.0 | Controlados pelo sink 9.0.0; não se atualiza transitivo à força |
| StackExchange.Redis (transitivo) | 2.7.27 | 3.4.5 | Major, controlado por `Microsoft.Extensions.Caching.StackExchangeRedis` |
| Microsoft.OpenApi (transitivo) | 2.12.2 | 3.10.2 | Major, controlado pelo ASP.NET Core |

## 14. Imagens e suporte (verificado em 2026-10-14)

| Serviço | Imagem fixada | Situação |
|---|---|---|
| PostgreSQL | `postgres:16.15@sha256:ca0bd484…` | Último patch 16.x; 16 suportada até 2028-11-09 |
| RabbitMQ | `rabbitmq:4.3.6-management@sha256:8dd6e357…` | Último 4.3; 4.2 encerrada em 2026-07-31 (só rollback) |
| Redis | `redis:8.10.2@sha256:2c2dff79…` | Último 8.10 |
| Elasticsearch / Kibana | `9.5.5@sha256:13b4a40b…` / `@sha256:13b72ce7…` | Último 9.5 |
| Redis Insight | `redis/redisinsight:3.8.0@sha256:b5e19ee2…` | Último 3.8 |
| .NET runtime / SDK | `aspnet:10.0.13@sha256:d5633778…` / `sdk:10.0.402@sha256:0d99ebf1…` | Patch de segurança atual |
| Rollback | `postgres:16.11`, `rabbitmq:4.2.2`, `redis:8.4.0` + `redislabs/*`, `redislabs/redisinsight:1.14.0` (todos com digest) | Só overlays de rollback; RedisGraph em EOL, removido pela decisão 3 |

Fontes:

- tags: Docker Hub API (`hub.docker.com/v2/repositories/...`) e `mcr.microsoft.com` via `docker buildx imagetools inspect`;
- ciclos de vida: https://www.postgresql.org/support/versioning/, https://www.rabbitmq.com/release-information, https://www.elastic.co/support/eol, https://endoflife.date/api/{postgresql,rabbitmq,redis,elasticsearch}.json;
- .NET: release metadata oficial (§8).

Nenhuma tag mutável restante.

## 15. Arquitetura e Graphify

- `Template.Architecture.Tests`: 30/30 antes e depois. A exceção temporária foi removida e a regra endurecida para zero violações, com mutações controladas.
- `graphify update . --force` (Graphify 0.9.80) rodou no passo `11-graphify` da 1ª rodada (2026-10-14 23:09). A 2ª rodada respondeu "No code-graph topology changes detected"; depois disso só mudaram scripts e JSON de política. `graphify-out/` continua git-ignored e não foi editado à mão.

| Métrica | Fase 20 (`graphify-out/2026-10-14/graph.json`, gerado em 2026-10-12 19:07) | Final |
|---|---|---|
| Nós / arestas / comunidades | 4.060 / 8.577 / 231 | 4.047 / 8.578 / 224 |
| Arestas EXTRACTED Domain/Application → camadas externas | 0 | **0** |
| INFERRED `dispatches_to` Application → Infrastructure (port → adapter) | 17 | 17 |
| Presentation → Application / Infrastructure (EXTRACTED) | 91 / 20 | 89 / 18 |
| `Program.cs` → namespaces da Persistence | Persistence, Contexts, Seeds | **Persistence** |
| God nodes (top 5) | BaseResult 79, IndexedPerson 63, ArchitectureRulesTests 56, OrderPlacedEvent 54, ProductDto 51 | iguais |

As quedas de nós e arestas vêm dos três tipos removidos e das referências do `Program.cs` à Persistence. Não houve mudança inesperada de hub.

## 16. Gate separado do VSIX (decisão 4A)

| Passo | Comando | Resultado |
|---|---|---|
| Build e inspeção (VS 2026 Insiders 18.11, MSBuild 18.11.0) | `vsix-validation.ps1 -Label phase21-vs2026-build -VisualStudioVersionRange '[18.0,19.0)' -Prerelease` | exit 0; audit 0; 0 avisos e 0 erros; todos os checks PASS; só pacotes 17.x |
| Instalação + smoke (hive `18.0_ab47ee71Exp`) | `... -Label phase21-vs2026-exp -Install -Smoke` | exit 0; VSIXInstaller exit 0; estado inicial **Enabled** ("Per-user extension enabled"); devenv /RootSuffix Exp saiu com 0 em 522 s; ActivityLog com 519 entradas, shell carregado (`Begin package load`, `PrimeMainWindowPackage`, `ComponentModelCache` criado), identidade em `ExtensionMetadata2.0.mpack`, **0 entradas referentes à extensão**. Os 4 erros são de login MSAL (`AADSTS700082`), sem relação |
| Desinstalação | `... -Label phase21-vs2026-uninstall -Uninstall` | exit 0; manifesto removido |

- O manifesto continua `[17.0,18.0)`. O VS 2026 aceitou a extensão pela política de compatibilidade com extensões do VS 2022.
- O pacote é vazio: não há funcionalidade para exercitar, só a carga.
- O perfil normal do VS 2026, que estava aberto, não foi tocado. O hive experimental do VS 2026 permanece (§21).
- O VSIX não faz parte do artefato de release da aplicação, que é a WebApi e a imagem.

## 17. Matriz de validação: execuções

1ª rodada (`artifacts/validation/phase-21/gate-run1-*`, 2026-10-14 22:36–23:09). Falhas e correções:

- **06e/06j/06m/06o**, 1 check cada (`redis.no-removed-service-references`): falso positivo. `scripts/redis-insight-upgrade-rehearsal.ps1:171` é um check *negativo* de ausência dos serviços removidos. O script foi commitado às 17:43 de 2026-10-12, depois do baseline da Fase 18 (17:28), quando ainda não era rastreado e o `git grep` não o via. Correção em `scripts/compose-baseline.ps1`: exclusão desse script, igual às duas já existentes pelo mesmo motivo.
- **06d**: a imagem 8.17.4 tinha sumido do host, e com `ErrorActionPreference=Stop` a mensagem "Unable to find image" em stderr abortava a execução. Correção em `scripts/elastic-upgrade-rehearsal.ps1`: pré-pull das imagens fixadas.
- **07b**: ver §9.

2ª rodada, final (`run-phase-validation.ps1 -ComposeBaseline -RedisRehearsal -PostgresRehearsal -RabbitMqRehearsal -RedisInsightRehearsal -ElasticRehearsalWebApi8xPath … -ElasticRehearsalWebApi9xPath …`, 2026-10-14 23:21–23:51). 06m, 06n e 06o falharam ao subir porque o Docker esgotou os pools de endereço de rede (`all predefined address pools have been fully subnetted`, 32 redes). Com autorização, foram removidas as 11 redes vazias criadas pela 1ª rodada desta fase (0 endpoints cada; log em `phase-21/network-removal.log`). Os três passos foram reexecutados isoladamente em 2026-10-15 00:00–00:06 (`phase-21/rerun/`), todos com exit 0.

## 18. Resultados por perfil

| Perfil | Resultado |
|---|---|
| Testes (descoberta = execução = aprovação, `test-tooling-evidence.ps1 -Label phase21-discovery`, adapter 3.1.5) | Arquitetura 30, Unit 107, Integração 142, PostgreSQL 6, RabbitMQ 19, Elasticsearch 13, Redis 23, Funcionais 191 |
| Cobertura (`07b`) | "Coverage report: OK"; por módulo: Domain 100%, Application 95,12/89,61, Caching 96,55/93,75, Identity 90,29/80,56, Messaging 59,5/66,43, Persistence 67,38/58,7, Resources 100/100, Search 96,99/87,5, WebApi 68,93/51,86 (linha/branch) |
| Compose baseline (Fase 14) | `p14-baseline-20261014233058`: 98/98 (volumes limpos, os dois bancos, idempotência de init/migração/seed, health, ordem de subida, restart, smoke HTTP da WebApi) |
| Redis 8 (Fase 15) | Ensaio `p15-*-20261014233345` 60/60; rollback `p15-rollback-20261014233626` 98/98; suíte 23/23 |
| PostgreSQL 16 (Fase 16) | Ensaio `20261014233909` 68/68 (backup 2,9 s, restore 2 s, rollback 3,4 s, dois bancos); rollback 16.11 `p16-rollback-20261014234051` 98/98; suíte 6/6 |
| RabbitMQ (Fase 17) | Ensaio `20261014234324` 81/81 (rollback 7,8 s); suíte 19/19 em 4.3.6 e 19/19 em 4.2.2; rollback `p17-rollback-20261015000010` 97/97 |
| Elastic/Kibana (Fases 11–12) | Ensaio `20261014232457` 49/49 (8.17.4 → 8.19.23 → 9.5.5 com Kibana, WebApi 8.x e 9.x reais, restart, rollback); suíte 13/13 |
| Redis Insight (Fase 18) | Ensaio `p18-20261015000311` 67/67; rollback 1.14.0 `p18-rollback-20261015000409` 95/95 |
| OpenAPI | `openapi-v1.json` e `openapi-v2.json` **idênticos** a `docs/refactoring/baseline-phase-6` após normalizar CRLF (passo `10` e `phase-21/openapi`). v1 `info.description` = "This API version has been deprecated."; v2 sem depreciação. `health.json` e `product-v*-get.json` diferem só em `totalDuration`/`createdDateTime`. Baseline não sobrescrito |
| `docker compose config -q` | exit 0 |
| `git diff --check` | exit 0 (só avisos LF/CRLF) |

## 19. Tabela de gates

Ambiente de todos os gates:

- Windows 11, Docker Desktop 4.47.0 (Engine 28.4.0, 16 CPUs, 16 GB);
- SDK `C:\Users\alexa\.dotnet\dotnet.exe` 10.0.402;
- logs em `artifacts/validation/` (git-ignored).

| ID | Gate | Obrig. | Comando / procedimento | Data | Exit / resultado | Status |
|---|---|---|---|---|---|---|
| G-SDK | SDK fixado e restore limpo | Sim | `dotnet --version`; passo `01-restore` | 2026-10-14 | 10.0.402; exit 0 | Passed |
| G-BUILD | Build WebApi | Sim | passo `02` | 2026-10-14 | 0 avisos, 0 erros | Passed |
| G-ARCH | Arquitetura, sem exceção temporária | Sim | passo `03` | 2026-10-14 | 30/30 | Passed |
| G-UNIT | Unitários | Sim | passo `04` | 2026-10-14 | 107/107 | Passed |
| G-INT | Integração geral | Sim | passo `05` | 2026-10-14 | 142/142 | Passed |
| G-PG | PostgreSQL 16.15 real | Sim | passo `06` | 2026-10-14 | 6/6 | Passed |
| G-RMQ | RabbitMQ 4.3.6 real | Sim | passo `06b` | 2026-10-14 | 19/19 | Passed |
| G-ES | Elasticsearch 9.5.5 real | Sim | passo `06c` | 2026-10-14 | 13/13 | Passed |
| G-REDIS | Redis 8.10.2 real | Sim | passo `06f` | 2026-10-14 | 23/23 | Passed |
| G-FUNC | Funcionais | Sim | passo `07` | 2026-10-14 | 191/191 | Passed |
| G-DISC | Descoberta = execução | Sim | `test-tooling-evidence.ps1 -Label phase21-discovery` | 2026-10-15 | 8 projetos OK | Passed |
| G-COV | Cobertura e limites | Sim | passo `07b` | 2026-10-14 | OK (após ajuste aprovado) | Passed |
| G-COMPOSE | Compose limpo, full-stack, smoke HTTP | Sim | passo `06e` | 2026-10-14 | 98/98 | Passed |
| G-R15 | Redis 8 ensaio + rollback | Sim | passos `06g`/`06h` | 2026-10-14 | 60/60, 98/98 | Passed |
| G-R16 | PostgreSQL backup/restore + rollback | Sim | passos `06i`/`06j` | 2026-10-14 | 68/68, 98/98 | Passed |
| G-R17 | RabbitMQ ensaio, suíte 4.2.2, rollback | Sim | passos `06k`/`06l` + `phase-21/rerun` 06m | 2026-10-14/15 | 81/81, 19/19, 97/97 | Passed |
| G-R11 | Elastic/Kibana ensaio + rollback | Sim | passo `06d` | 2026-10-14 | 49/49 | Passed |
| G-R18 | Redis Insight ensaio + rollback | Sim | `phase-21/rerun` 06n/06o | 2026-10-15 | 67/67, 95/95 | Passed |
| G-VSIX | VSIX build/inspeção/instalação/carga/desinstalação (separado) | Separado | §16 | 2026-10-14 | exit 0 nos 3 | Passed |
| G-VULN | Vulnerabilidades (política 2B) | Sim | §12 | 2026-10-14 | 0 achados | Passed |
| G-DEPR | Depreciados | Sim | §12 | 2026-10-14 | xunit 2.x (teste), adiado | Passed |
| G-OUTD | Desatualizados | Sim | §12–§13 | 2026-10-14 | classificados | Passed |
| G-LIC | Licenças (política 3A) | Sim | `license-inventory.ps1 -Label phase21-final` | 2026-10-14 | 0 fora da política | Passed |
| G-IMG | Imagens/digests/suporte | Sim | §14 | 2026-10-14 | sem tag mutável; todas suportadas | Passed |
| G-CCFG | `docker compose config -q` | Sim | passo `09` | 2026-10-14 | exit 0 | Passed |
| G-OPENAPI | OpenAPI v1/v2 exato | Sim | passo `10` + comparação | 2026-10-14 | idênticos | Passed |
| G-GRAPH | Graphify + direção | Sim | passo `11` + §15 | 2026-10-14 | 0 dependências externas | Passed |
| G-SEC-SECRETS | Segredos | Sim | varredura do diff + revisão do repositório | 2026-10-15 | diff limpo; **chave JWT commitada nos `appsettings*.json` (pré-existente)** | **Failed** (dono, 2026-10-15) |
| G-DOCS | Comandos documentados verificados | Sim | `AGENTS.md` atualizado só com o que foi executado | 2026-10-15 | — | Passed |
| G-DIFF | `git diff --check` | Sim | passo `12` | 2026-10-14 | exit 0 | Passed |

Próxima ação para G-SEC-SECRETS (dono do repositório, fase futura autorizada):

1. Fornecer `JwtSettings__Key` por secret store ou user secrets.
2. Remover o valor dos `appsettings*.json`, com plano de invalidação dos tokens emitidos com a chave atual.
3. Rotacionar a chave.

## 20. Limitações e riscos residuais

- **G-SEC-SECRETS: Failed** (§19).
- Dados reais do usuário (`cleanarchitecture_postgres-data`, `_rabbitmq-lib`, `_redis-volume-data`, `_redis-insight-volume_data`, `_v01`) continuam sem leitura nem migração. Todos os ensaios usaram dados sintéticos.
- `docker compose up` normal, com `docker-compose.override.yml` (HTTPS, user secrets), e `dotnet run` local contra serviços reais continuam sem verificação. O full-stack foi verificado com `docker-compose.baseline.yml`.
- Não há CI.
- O VSIX não foi exercitado no VS 2022 nesta fase: a Fase 20 deixou o hive 17.0 com "Erro de Configuração", e o dono preferiu o VS 2026.
- MediatR 14 tem licença RPL-1.5 ou comercial (aceita pelo dono). O uso comercial pode exigir chave Lucky Penny.
- O Docker Desktop do host fica instável com o socket `dockerInference` obsoleto.
- Os ensaios acumulam recursos e podem esgotar os pools de rede. Limpeza só com autorização.

## 21. Recursos retidos e limpeza que exige autorização

- Volumes: os 274 pré-existentes estão intactos (inventário antes/depois em `phase-21/docker-inventory-*`, nenhum faltando), mais os novos volumes dos ensaios desta fase (`p11-*-20261014232457`, `p14-baseline-20261014*`, `p15-*-20261014*`, `p16-*-20261014*`, `p17-*-2026101*`, `p18-*-2026101*`).
- Contêineres parados dos ensaios desta fase e as redes da 2ª rodada.
- Imagens de rollback rebaixadas.
- Hive experimental do VS 2026 `18.0_ab47ee71Exp` (sem a extensão) e o do VS 2022 da Fase 20.
- Artefatos em `artifacts/validation/phase-21/`, `vsix/phase21-*`, `licenses/`, `package-graph/phase21-*` e `scratchpad` (WebApis 8.x/9.x publicadas).
- SDK 10.0.401 e runtime 10.0.12 instalados.

Remover qualquer um desses itens exige autorização explícita. Os ensaios imprimem os comandos de limpeza sem executá-los.

## 22. Arquivos alterados nesta fase

- Código: `Src/Infrastructure/Template.Infrastructure.Persistence/ServiceRegistration.cs`, `Src/Presentation/Template.WebApi/Program.cs`.
- Removidos: `ApiErrorResponse.cs`, `ErrorCodeEnum.cs`, `ApiResultFilterAttribute.cs`.
- Testes: `ArchitectureRulesTests.cs`, `ApplicationDatabaseInitializationTests.cs` (novo), `PostgreSqlPersistenceTests.cs`, `PostgreSqlFixture.cs`.
- Versões: `global.json`, `Directory.Packages.props`, `Dockerfile`.
- Política: `Tests/test-coverage-policy.json`.
- Scripts: `scripts/license-inventory.ps1` (novo), `scripts/compose-baseline.ps1`, `scripts/elastic-upgrade-rehearsal.ps1`.
- Docs: `AGENTS.md` e este relatório.

## 23. Confirmações

- A API v1 **não** foi removida. Rotas, status codes e contrato estão inalterados, e ela continua deprecated no OpenAPI.
- Não houve feature nova, upgrade major, PostgreSQL 18, VSSDK 18, reescrita de migração aplicada nem edição manual de Graphify. Nenhum trabalho da Fase 22 foi iniciado.
- Nenhum volume, banco, fila, índice, backup ou estado de tooling pré-existente foi apagado. A única remoção foram as 11 redes vazias criadas nesta própria fase, com autorização.
- Nenhum segredo foi exposto nesta fase. Os valores de `.env` e das chaves não aparecem neste relatório.
- **Por que não está pronto:** todos os gates obrigatórios passaram, menos G-SEC-SECRETS. O dono classificou a chave JWT commitada em `appsettings.Production.json` (e nos demais) como bloqueador de release.
