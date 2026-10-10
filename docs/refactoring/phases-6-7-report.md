# Relatório das Fases 6–7 — 2026-10-10

Checkpoint fechado em 2026-10-10 às 15:40 (America/Sao_Paulo). As Fases 6 e 7 foram implementadas e validadas em sequência. **Nada das Fases 8+ foi implementado:** Redis, RabbitMQ e Elasticsearch permanecem nas exceções temporárias já registradas.

## Resultado por fase

### Fase 6 — controllers e contratos HTTP

- O service locator foi removido de `BaseApiController`; o controller de produto v1 recebe `ISender` por construtor e o v2 usa `ISender` diretamente.
- O versionamento migrou de `Microsoft.AspNetCore.Mvc.Versioning.ApiExplorer` 5.1.0 para `Asp.Versioning.Mvc.ApiExplorer` 10.2.1, com `AddApiVersioning().AddMvc().AddApiExplorer()` e leitura por segmento de URL.
- A v1 mantém métodos, rotas, parâmetros, status codes e envelopes legados. Todos os controllers v1 estão marcados deprecated; o ciclo de vida está em `api-v1-lifecycle.md` e não há data de remoção.
- `ApiResultMapper` é a fronteira única para resultados da Application na v2. Ele produz `ValidationProblemDetails`/`ProblemDetails` com `application/problem+json` e mapeia validation 400, unauthenticated 401, forbidden 403, not found 404, conflict 409 e unexpected 500.
- O middleware de exceções usa a mesma fábrica e não serializa mensagem/stack trace em 500. Autenticação e autorização usam Problem Details por `IAuthorizationMiddlewareResultHandler`.
- `FluentValidation.AspNetCore` 11.3.1 e `AddFluentValidationAutoValidation` foram removidos. Produtos usam o `ValidationBehavior` do MediatR; endpoints de Account executam seus validators explicitamente e de forma assíncrona, sem dupla validação.
- Binding de route/query/body ficou explícito. `CancellationToken` chega a produtos e contas; as APIs de Identity que não aceitam token recebem verificações antes e entre suas operações assíncronas.
- Na v2, create responde 201 com `Location` REST, delete responde 204 sem corpo e falhas usam Problem Details. A v1 continua com o comportamento publicado, inclusive falhas de negócio em envelope HTTP 200 onde já era assim.
- Os testes funcionais cobrem v1/v2, 400, 401, 403, 404, create/update/delete, Problem Details, ausência de detalhe interno no 500, 405 e versão de URL não suportada.

### Fase 7 — persistência e PostgreSQL

- O registro por reflexão foi substituído por registros scoped explícitos de `IGenericRepository<>` e `IProductRepository`.
- O repositório genérico foi mantido durante a transição porque os ports existentes ainda usam CRUD genérico e ele é exercitado junto com o repositório de produto. Nenhum `IQueryable` é exposto pelos ports; a composição de query permanece protegida dentro de Infrastructure.
- A paginação de produtos agora ordena por `Created` e desempata por `Id` antes de `Skip`/`Take`.
- `IUnitOfWork` define begin, commit e rollback assíncronos. O adapter EF mantém uma única transação ativa, salva e confirma no commit, faz rollback seguro em falha, limpa tracking no rollback e encerra transação pendente no dispose. O método síncrono residual foi removido.
- Não há referência a SQL Server em `Src`; a regra é coberta por teste de arquitetura com mutação controlada.
- Foi criado o projeto separado `Tests/Template.PostgreSql.Integration.Tests`, usando Testcontainers 4.16.0 e a imagem explícita `postgres:16.15`.
- Os 5 testes PostgreSQL aplicam primeiro a migration inicial, executam o preflight de `ProductPriceDecimal`, aplicam a migration pendente e verificam migrations/`numeric(18,2)`, CRUD, arredondamento 18,2, paginação determinística em empate, concorrência, cancelamento, commit e rollback e uma transação com `ProductRepository` e `GenericRepository<ProductEntity>`.

## Arquivos alterados nesta execução

### Contrato e WebApi

- `Directory.Packages.props`
- `Src/Presentation/Template.WebApi/Template.WebApi.csproj`
- `Src/Presentation/Template.WebApi/Program.cs`
- `Src/Presentation/Template.WebApi/Controllers/BaseApiController.cs`
- `Src/Presentation/Template.WebApi/Controllers/v1/{Account,Message,Person,Product}Controller.cs`
- `Src/Presentation/Template.WebApi/Controllers/v2/{Account,Product}Controller.cs`
- `Src/Presentation/Template.WebApi/Infrastructure/Extensions/SwaggerExtensions.cs`
- `Src/Presentation/Template.WebApi/Infrastructure/Http/ApiResultMapper.cs`
- `Src/Presentation/Template.WebApi/Infrastructure/Http/ProblemDetailsAuthorizationMiddlewareResultHandler.cs`
- `Src/Presentation/Template.WebApi/Infrastructure/Middlewares/ErrorHandlerMiddleware.cs`
- `Src/Core/Template.Application/Interfaces/UserInterfaces/IAccountServices.cs`
- `Src/Infrastructure/Template.Infrastructure.Identity/Services/AccountServices.cs`

### Persistência e testes

- `Src/Core/Template.Application/Interfaces/IUnitOfWork.cs`
- `Src/Infrastructure/Template.Infrastructure.Persistence/Contexts/UnitOfWork.cs`
- `Src/Infrastructure/Template.Infrastructure.Persistence/Repositories/{GenericRepository,ProductRepository}.cs`
- `Src/Infrastructure/Template.Infrastructure.Persistence/ServiceRegistration.cs`
- `Tests/Template.Architecture.Tests/ArchitectureRulesTests.cs`
- `Tests/Template.Functional.Tests/Common/ApiRoutes.cs`
- `Tests/Template.Functional.Tests/ApiEndpoints/ProductValidationFunctionalTests.cs`
- `Tests/Template.Functional.Tests/ApiEndpoints/Phase6HttpContractFunctionalTests.cs`
- `Tests/Template.PostgreSql.Integration.Tests/*`
- `TemplateCleanArchitecture.sln` e `TemplateCleanArchitecture.App.slnf`

### Automação e documentação

- `.agents/skills/architecture-boundary-check/scripts/check-boundaries.ps1`
- `scripts/run-phase-validation.ps1`
- `docs/refactoring/api-v1-lifecycle.md`
- `docs/refactoring/baseline-fase-6/*`
- `docs/refactoring/phases-6-7-report.md`
- `AGENTS.md`
- `graphify-out/*` (regenerado por Graphify, não editado à mão)

## Versões e fontes consultadas

Consulta em 2026-10-10 (America/Sao_Paulo), somente fontes oficiais:

| Componente | Antes | Selecionado | Fonte e observação |
|---|---:|---:|---|
| API Versioning | `Microsoft.AspNetCore.Mvc.Versioning.ApiExplorer` 5.1.0 | `Asp.Versioning.Mvc.ApiExplorer` 10.2.1 | [NuGet](https://www.nuget.org/packages/Asp.Versioning.Mvc.ApiExplorer), [guia oficial de migração](https://dotnet.github.io/aspnet-api-versioning/aspnet-core/quick-starts/migration.html), [releases](https://github.com/dotnet/aspnet-api-versioning/releases). O identificador/namespace antigo é servicing-only; 10.2.x é a linha estável para .NET 10. |
| FluentValidation ASP.NET | 11.3.1 | removido | [repositório oficial](https://github.com/FluentValidation/FluentValidation.AspNetCore). A integração MVC antiga não é suportada; `FluentValidation.DependencyInjectionExtensions` 12.1.1 permanece. |
| Testcontainers PostgreSQL | ausente | 4.16.0 | [NuGet](https://www.nuget.org/packages/Testcontainers.PostgreSql), [módulo PostgreSQL](https://dotnet.testcontainers.org/modules/postgres/). |
| PostgreSQL de teste | ausente | `postgres:16.15` | [imagem oficial](https://hub.docker.com/_/postgres/tags?name=16&page=1). Patch explícito dentro do major 16; nenhum upgrade major foi feito. |

Rollback de pacotes: restaurar em conjunto o PackageReference/PackageVersion de versionamento, os namespaces/configuração e os controllers; remover o projeto PostgreSQL e `Testcontainers.PostgreSql` como uma unidade separada. Nenhuma versão de Redis, RabbitMQ, Elastic ou dos respectivos containers foi alterada.

## Diff de contrato e baseline

O baseline original em `docs/refactoring/baseline/` foi preservado. O baseline aprovado está em `docs/refactoring/baseline-fase-6/`.

- v1: o diff estrutural contém apenas `info.description = "This API version has been deprecated."`; paths e status codes permanecem idênticos.
- v2: `ApiErrorResponse`/`ApiErrorItem` saem do OpenAPI; entram `ProblemDetails` e `ValidationProblemDetails` com `application/problem+json`. DELETE `/{id}` muda de 200 documentado para 204. O POST já documentava 201, e o comportamento real agora corresponde à documentação com `Location`.
- O gate final da Fase 7 produziu `openapi-v1.json` e `openapi-v2.json` byte a byte idênticos ao baseline da Fase 6. As respostas representativas também são estruturalmente idênticas após ignorar somente `createdDateTime` e `totalDuration`.

## Migrations e SQL

Não houve delta de modelo/schema na Fase 7; portanto nenhuma migration nova foi criada e nenhum Designer/snapshot foi editado. A migration existente `20261010151729_ProductPriceDecimal` foi aplicada em PostgreSQL 16.15 real depois do preflight e validada como `numeric(18,2)`.

O SQL versionado em `docs/refactoring/product-price-decimal.sql` foi relido: usa transação, altera `Price`, limita e torna `Name`/`BarCode` não nulos, e registra a migration. Continuam válidos os riscos já documentados: arredondamento acima de duas casas, overflow acima de 18,2, strings acima dos limites, conversão de nulos para vazio e lock durante `ALTER TABLE`. O rollback continua sendo voltar à migration inicial ou restaurar backup para recuperar dados transformados.

A tentativa de regenerar o script com `dotnet ef migrations script` não executou porque `dotnet-ef` não está instalado como ferramenta. Como não existe delta de schema nesta fase, nenhuma ferramenta global foi instalada e o gate foi feito pela aplicação real das migrations no container.

## Comandos e resultados

Gate sequencial executado por:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-phase-validation.ps1
```

| Gate | Resultado em 2026-10-10, 15:38–15:40 |
|---|---|
| Restore do `TemplateCleanArchitecture.App.slnf` | OK |
| Build WebApi | OK, 0 avisos, 0 erros |
| Arquitetura | 12/12 |
| Unitários | 38/38 |
| Integração EF InMemory | 11/11 |
| Integração PostgreSQL 16.15 | 5/5 |
| Funcionais HTTP | 27/27 |
| Audit NuGet direto/transitivo | nenhum pacote vulnerável nos 11 projetos |
| `docker compose config -q` | OK |
| Captura OpenAPI | OK; idêntica ao baseline da Fase 6 |
| Graphify | OK; 1.463 nós, 2.804 arestas, 117 comunidades |
| `git diff --check` | exit 2 somente pelos espaços preexistentes já listados no relatório das Fases 0–5; arquivos gerados/legados não foram “corrigidos” |

O scan determinístico de fronteiras também passou. O Graphify avisou que `tree_sitter_sql` não está instalado e que os rótulos de comunidade podem ser atualizados por `graphify label`; isso não impediu a regeneração do grafo.

Após o gate completo, uma revisão final fez o handler de 401/403 chamar o challenge/forbid do esquema JWT antes de escrever Problem Details, preservando `WWW-Authenticate: Bearer`. O build WebApi (0 avisos) e os funcionais (27/27) foram repetidos e permaneceram verdes; o teste agora fixa esse header.

## Riscos, pendências e rollback

- A v1 continua suportada sem data de remoção; consumidores devem migrar para v2 antes de qualquer sunset futuro.
- O `IUnitOfWork` transacional está disponível e testado, mas os handlers atuais continuam usando o save unitário existente; a adoção de uma transação explícita deve ser feita apenas quando um caso de uso exigir atomicidade entre múltiplas operações.
- As APIs de ASP.NET Core Identity usadas por `UserManager`/`SignInManager` não expõem `CancellationToken`; o token HTTP é verificado antes e entre essas chamadas, mas não pode interromper uma chamada de Identity já em andamento.
- O teste de concorrência prova writes concorrentes reais no PostgreSQL; o modelo ainda não declara token de concorrência otimista, portanto não promete detecção de lost update.
- O full stack `docker compose up` e um `dotnet run` contra Redis/RabbitMQ/Elastic reais continuam fora deste checkpoint. O PostgreSQL necessário à Fase 7 foi exercitado isoladamente.
- Rollback de persistência: reverter registro explícito/ordenação/port transacional como uma unidade; não há mudança de dados ou migration nova para desfazer. Os testes usam containers e bancos descartáveis.
- Não houve commit, push, PR, stash, reset, checkout ou clean. A working tree preexistente foi preservada.

## Confirmação de escopo

As Fases 6 e 7 estão concluídas com seus critérios verdes. Nenhum adapter foi movido e nenhuma versão de Redis, RabbitMQ ou Elasticsearch foi alterada; as exceções de arquitetura das Fases 8–10 permanecem exatamente nominais.
