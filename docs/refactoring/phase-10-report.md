# Relatório da Fase 10 — 2026-10-10

Checkpoint de 2026-10-10 (America/Sao_Paulo). A Fase 10 tirou o Elasticsearch da Application e colocou a indexação/consulta de pessoas atrás de um port da Application, implementado por um adapter no novo projeto `Template.Infrastructure.Search`. Busca, logging (sink Serilog) e health check passaram a ter opções, código e política de disponibilidade separados. O client continua no **8.17.1** e o servidor no **8.17.4**. **Nada das Fases 11+ foi implementado.**

## Git e checkout

- Checkout autoritativo: `C:\Users\alexa\source\repos\TemplateCleanArchNET`.
- O usuário pausou a execução, mergeou a Fase 9 (PR #2) e autorizou continuar e abrir um PR ao final.
- A branch `refactor/fase-10-elastic` foi criada a partir de `origin/main` (`8bd2548`, merge do PR #2). A árvore era idêntica à da branch da Fase 9.
- Os únicos arquivos não versionados preexistentes eram `docs/refactoring/handoff-fase-10.md`, `handoff-fase-11.md` e `handoff-fase-12.md`. Foram preservados.
- Não houve reset, stash, clean nem checkout destrutivo.

## Pré-condições verificadas (Fases 8 e 9)

Conferidas no estado real de `8bd2548`, antes de qualquer mudança:

| Verificação | Resultado |
|---|---|
| Application sem Redis | Sem pacote nem tipo Redis; `ProductService` usa `IProductCache` |
| Adapter Redis em Infrastructure | `Template.Infrastructure.Caching` |
| Exceção de arquitetura da Fase 8 | Removida |
| Application sem `RabbitMQ.Client` e sem hosted services | Confirmado; publisher e consumer estão em `Template.Infrastructure.Messaging` |
| `RabbitMQ.Client` | 7.2.2 |
| Exceções da Fase 9 | Removidas; restavam só as da Fase 10 (`Elastic.Clients.Elasticsearch` e `ElasticService.cs`) |
| Relatórios | `phase-8-report.md` e `phase-9-report.md` com resultados reais |
| Suítes das Fases 8 e 9 | Verdes no gate final desta fase (abaixo) |

## Inventário da integração anterior

### Código, DI e ciclo de vida

| Item | Antes da Fase 10 |
|---|---|
| Pacote | `Elastic.Clients.Elasticsearch` 8.17.1 referenciado pela **Application** |
| Interface | `Application/Services/Elastic/IElasticService`: `CreateIndexIfNotExistsAsync(indexName)`, `AddOrUpdate(Person)`, `AddOrUpdateBulk(IEnumerable<Person>, indexName)`, `Get(key)`, `GetAll()`, `Remove(key)` e `RemoveAll()`. Nenhum método recebia `CancellationToken`, e `Get`/`GetAll` devolviam `null` em qualquer falha. |
| Implementação | `Application/Services/Elastic/ElasticService`, registrada como singleton em `AddApplicationLayer` |
| Client | Um `ElasticsearchClient` criado no construtor do singleton (`SingleNodePool`, `DefaultIndex`), sem autenticação (a linha `BasicAuthentication` estava comentada) e com os defaults do client (timeout de 60 s, gzip) |
| Configuração | `Application/Settings/ElasticSearchSettings` (`Name`, `Url`, `DefaultIndex`, `Type`, `DataSet`, `Namespace`), compartilhada por busca, logging e health check e registrada com `Configure<ElasticSearchSettings>` no `Program.cs` |
| Health check | `ElasticExtensions.AddHealthChecksService` montava um `ServiceProvider` intermediário (`BuildServiceProvider()`) para ler `Url`/`Name` e chamava `AddElasticsearch(url, name)` de `AspNetCore.HealthChecks.Elasticsearch` 9.0.0; o check ficava fora do ambiente Test |
| Sink Serilog | `ElasticExtensions.ConfigureLog`: console mais `WriteTo.Elasticsearch([Url])`, data stream `Type-DataSet-Namespace` (`logs-api-example-demo`) e `BootstrapMethod.Failure`, seguido de `ReadFrom.Configuration`; fora do ambiente Test |
| Índices e aliases | Índice `persons` (de `DefaultIndex`); `create-index` aceita qualquer nome vindo da query string. Não há aliases nem templates próprios. |
| HTTP | v1 `PersonController` (deprecated, sem autenticação): `POST create-index?indexName=`, `POST add-person`, `POST update-person`, `GET get-person/{key}`, `GET get-all-persons` e `DELETE delete-person/{key}`. Corpo e resposta usam `Template.Domain.Person.Person`. |
| Outros consumidores | Nenhum. `AddOrUpdateBulk` e `RemoveAll` eram **código morto** (nenhum chamador); o resto era alcançável só pelo `PersonController`. |
| Testes | Nenhum teste de busca, logging ou health check do Elasticsearch |
| Compose | `elasticsearch` e `kibana` em `8.17.4` (security desabilitada, volume `v01`); a WebApi depende de `elasticsearch: service_healthy`. Não foram alterados. |

### Requisições e formato do documento (caracterizados contra um Elasticsearch 8.17.4 descartável)

| Operação | Requisição |
|---|---|
| Criar índice | `HEAD /{indexName}` e, se não existir, `PUT /{indexName}`. **Falhas eram ignoradas.** |
| Indexar | `PUT /persons/_doc/{id}?refresh=wait_for`, corpo `{"id":5,"firstName":"Eva"}` (camelCase, nulos omitidos) |
| Bulk | `POST /persons/_bulk` com `update` + `{"doc":…,"doc_as_upsert":true}`, sem refresh |
| Ler | `GET /persons/_doc/{key}` |
| Listar | `POST /persons/_search` com corpo `{}`: **página padrão do servidor (10 hits)** |
| Remover | `DELETE /persons/_doc/{key}`, sem refresh |
| Mapping dinâmico | `id: long`, `firstName`/`lastName: text` com subcampo `keyword` |

### Comportamento HTTP legado (smoke da WebApi legada e caracterização do client)

Smoke da WebApi legada em `Development` com `UseInMemoryDatabase=true`, RabbitMQ desabilitado e um Elasticsearch 8.17.4 descartável. O Elasticsearch disponível foi exercitado ao vivo; a coluna "indisponível" vem da caracterização do client contra uma porta fechada, porque a WebApi legada nem sobe sem Elasticsearch.

| Endpoint | Disponível | Ausente | Recusado (4xx) | Documento incompatível | Indisponível | Cancelado |
|---|---|---|---|---|---|---|
| create-index | 200 texto | — | **200 (mentira)** | — | **200 (mentira)** | **200** |
| add/update | 200 texto | — | 500 texto | — | 500 texto | 500 texto |
| get-person | 200 JSON | 404 "Person not found." | **404** | 500 Problem Details | **404 (mentira)** | **404** |
| get-all | 200 JSON (≤ 10) | índice ausente: 500 texto | 500 texto | 500 Problem Details | 500 texto | 500 texto |
| delete | 200 texto | **500** texto | 500 texto | — | 500 texto | 500 texto |

Outros fatos confirmados:

- **Startup:** fora do Test, com Elasticsearch indisponível, a WebApi **não sobe** ("Failure to create component template `ecs_8.11.0_agent`", causado por `BootstrapMethod.Failure`).
- **Health check quebrado:** `AspNetCore.HealthChecks.Elasticsearch` 9.0.0 foi compilado contra `Elastic.Transport` 0.4.x, mas o grafo resolve 0.5.7 (pelo client 8.17.1). O check lança `TypeLoadException` (`Elastic.Transport.TransportConfigurationBase`1`), e `/health` respondia **sempre** 503 Unhealthy, mesmo com o Elasticsearch disponível, expondo a mensagem da exceção.
- **Cancelamento:** o client 8.x não lança na transport; registra `TaskCanceledException` na resposta, e o serviço antigo convertia isso em 404 ou 500.
- **Sink duplicado vindo da configuração:** a entrada `Serilog:WriteTo:Elasticsearch` dos appsettings (args `NodeUris`/`IndexFormat`/`AutoRegisterTemplate`, do antigo `Serilog.Sinks.Elasticsearch`) se liga a `Elastic.Serilog.Sinks.ConfigSinkExtensions.Elasticsearch` com os args ignorados. Cria um **segundo** sink para `http://localhost:9200`, data stream `logs-dotnet-default`, `BootstrapMethod.None`. Ele existia antes e continua existindo (ver riscos).

## Decisão sobre o requisito ativo

- Os seis endpoints v1 de `PersonController` estão publicados no OpenAPI aprovado e foram tratados como **comportamento ativo**. A capacidade é **indexação CRUD de pessoas com leitura por chave e listagem**, não busca textual; não há busca de produto, e nenhuma foi criada.
- Os nomes passaram a refletir isso (índice/busca de pessoas), sem mudar rotas.
- `RemoveAll` (delete-by-query `match_all`, destrutivo e sem chamador) foi **removido**.
- `AddOrUpdateBulk` também não tinha chamador, mas foi **mantido** no port como `UpsertManyAsync`, porque o handoff exige o contrato de bulk (resultado parcial) testado. Não ganhou endpoint nem caso de uso. Fica como candidato a remoção numa fase futura.

## Port e contratos (Application)

| Tipo | Papel |
|---|---|
| `Interfaces/Search/IPersonSearchIndex` | `EnsureIndexAsync(indexName)`, `UpsertAsync(IndexedPerson)`, `UpsertManyAsync(persons)`, `GetAsync(key)`, `ListAsync()` e `RemoveAsync(key)`, todos com `CancellationToken` |
| `Search/Persons/IndexedPerson` | Contrato da aplicação: `Id`, `FirstName?` e `LastName?`. Não é a entidade de domínio nem o documento armazenado. |
| `Search/Persons/PersonIndexStatus` | `Succeeded`, `NotFound`, `Unavailable` (inacessível, timeout, 5xx/429 ou busca desabilitada), `Rejected` (outros 4xx) e `IncompatibleDocument` |
| `Search/Persons/PersonIndexResult` e `PersonIndexResult<T>` | Resultado sem `null` ambíguo: `Value` só existe em `Succeeded`, e falha com status de sucesso é proibida |
| `Search/Persons/PersonBulkIndexResult` | `IndexedIds`/`FailedIds`; `IsComplete` só com status de sucesso **e** nenhuma falha; `IsPartial` para resultado parcial |
| `Features/Persons/*` (MediatR) | `EnsurePersonIndexCommand`, `IndexPersonCommand` (add e update compartilham o upsert, como antes), `GetPersonQuery`, `ListPersonsQuery` e `RemovePersonCommand` |

Outras propriedades do port:

- Não expõe client, descriptors, queries, respostas, `Elastic.Transport`, JSON, URL, sink nem health check (há um teste de arquitetura para isso).
- Cancelamento do chamador lança `OperationCanceledException`.
- Erros de programação (argumento nulo ou vazio) não são convertidos em status.

## Adapter (Infrastructure.Search)

| Tipo | Responsabilidade |
|---|---|
| `PersonSearchSettings` | Opções tipadas e validadas no startup quando `Enabled` |
| `Persons/ElasticsearchPersonSearchIndex` | Adapter singleton e `IDisposable`: um `ElasticsearchClient` por processo (thread-safe, como recomenda a documentação do client 8.x), criado no construtor **sem chamada de rede** (`SingleNodePool` não faz sniff nem ping). Envia as mesmas requisições do legado. |
| `Persons/PersonDocument` | Documento armazenado com formato fixado por atributos (`id`, `firstName` e `lastName`, nulos omitidos) e leitura tolerante |
| `Persons/DisabledPersonSearchIndex` | Usado no Test ou com `Enabled=false`: sempre `Unavailable`, sem rede, respeitando o cancelamento |
| `ServiceRegistration.AddSearchInfrastructure(configuration, useSearchEngine)` | Composição |

### Classificação de falhas

- Sem resposta HTTP (conexão recusada, DNS, timeout) → `Unavailable`.
- 5xx e 429 → `Unavailable`.
- 404 → `NotFound` (documento ou índice).
- Outros 4xx → `Rejected`.
- `resource_already_exists_exception` no create → sucesso (idempotente sob concorrência).
- Corpo ilegível, ou servidor que não se identifica como Elasticsearch (`TransportException` e `UnsupportedProductException`) → `Unavailable`.
- `_source` que não vira pessoa → `IncompatibleDocument`. O adapter lê `_source` como `JsonElement` e faz o mapeamento ele mesmo; um documento ruim nunca vira exceção nem dado parcial (a lista inteira falha).
- O token do chamador é verificado antes de classificar, então cancelamento nunca vira `Unavailable` nem `NotFound`.

**Logs:** registram operação, status, código HTTP e o **tipo** do erro do Elasticsearch (por exemplo `invalid_index_name_exception`). Nunca registram `reason` (o Elasticsearch ecoa valores de campos nele), documentos, nomes, chaves, corpo nem `DebugInformation`. O endpoint é registrado uma vez no construtor, só esquema, host e porta.

## Documento, índice e compatibilidade

| Aspecto | Definição |
|---|---|
| Formato | JSON camelCase `{"id":<int>,"firstName":<string>,"lastName":<string>}`, nulos omitidos; **byte a byte igual** ao legado (teste com Elasticsearch real) |
| Identificador | `_id` = `Id` como texto; o campo `id` se repete no corpo |
| Índice | `PersonSearchSettings:IndexName`, com fallback para `ElasticSearchSettings:DefaultIndex` e default `persons`. O `create-index` continua aceitando qualquer nome. |
| Campos desconhecidos | Ignorados |
| Campos ausentes | Default (`Id` 0, nomes `null`) |
| `id` como string numérica | Aceito, como antes (`AllowReadingFromString`) |
| Documento incompatível | `id` fora de `int`, fracionário ou não numérico; nome não string; `_source` que não é objeto → `IncompatibleDocument` |
| Documentos já indexados | Lidos sem migração: teste grava com o código legado e lê com o adapter, e vice-versa |
| Refresh | Upsert com `wait_for`; bulk e delete sem refresh (iguais ao legado) |
| Bulk parcial | Item com status fora de 2xx ou com erro → `FailedIds`; o status geral fica `Succeeded`, mas `IsComplete=false` |
| Listagem | Mantém a página padrão de 10 hits do servidor, sem ordenação. É limitação herdada, documentada no port. |
| Impacto | Nenhuma mudança de formato; não há migração de índice |

## Configuração e DI

Os nomes antigos continuam valendo. Cada responsabilidade tem sua seção nova, opcional. Precedência: **chave da seção nova > chave legada em `ElasticSearchSettings` > default no código**.

| Responsabilidade | Seção nova | Chaves legadas lidas | Chaves novas e defaults |
|---|---|---|---|
| Busca | `PersonSearchSettings` | `Url`, `DefaultIndex` | `Enabled` (true), `Url`, `IndexName` (`persons`), `RequestTimeout` (10 s) |
| Logging | `ElasticLoggingSettings` | `Url`, `Type`, `DataSet`, `Namespace` | `Enabled` (true), `RequestTimeout` (5 s) |
| Health check | `ElasticsearchHealthCheckSettings` | `Url`, `Name` | `Enabled` (true), `Timeout` (5 s), `FailureStatus` (`Unhealthy`) |

Detalhes:

- Uma URL com credenciais embutidas é **rejeitada** pela validação; não há autenticação, igual ao legado.
- Nenhum appsettings foi alterado.
- Diagnóstico seguro: o adapter registra o endpoint sem userinfo, path nem query.

Registros:

- `Program.cs` remove o `Configure<ElasticSearchSettings>` e chama `AddSearchInfrastructure(configuration, useSearchEngine: !isTestEnvironment)` e `AddHealthChecksService(configuration, includeElasticsearch: !isTestEnvironment)`. O `ConfigureLog` continua fora do Test.
- O health check lê as próprias opções do `IConfiguration` no registro. **Nenhum `BuildServiceProvider()`** sobra nos caminhos de Elastic.
- `ElasticExtensions.cs` foi dividido em `Infrastructure/HealthChecks/*` e `Infrastructure/Logging/*`.

## Separação entre busca, logging e health check

- **Busca:** só `Template.Infrastructure.Search` usa `Elastic.Clients.Elasticsearch`. Controller → MediatR → port.
- **Logging (WebApi):** `ElasticLoggingSettings`, `LoggingExtensions.ConfigureLogger` e `ElasticLogSinkDiagnostics`. Não lê opções de busca nem referencia o port (teste de arquitetura e teste funcional).
- **Health check (WebApi):** `ElasticsearchHealthCheck` próprio faz `HEAD /` (o mesmo probe padrão do pacote anterior) por um `HttpClient` nomeado. Só compartilha o endpoint físico.

### Pacote do health check removido

O pacote anterior estava **quebrado em runtime** pela incompatibilidade de `Elastic.Transport` descrita acima. Não há versão do `AspNetCore.HealthChecks.Elasticsearch` que resolva o problema sem trocar a linha do client, e a Fase 10 não troca versões. Por isso `AspNetCore.HealthChecks.Elasticsearch` 9.0.0 foi removido do WebApi e do `Directory.Packages.props`, em vez de fazer upgrade. Rota `/health`, writer (`UIResponseWriter`), nome da entrada (`elasticsearch`) e status de falha (`Unhealthy`) foram preservados.

## Política de disponibilidade

### Busca e indexação

- O host sobe sem Elasticsearch: nada é contatado no startup.
- Endpoints não relacionados funcionam normalmente.
- Os endpoints de pessoa respondem com as falhas da tabela abaixo.
- A indisponibilidade aparece em log de aviso com a categoria.
- Cada operação é limitada por `RequestTimeout` (10 s).
- Não há fallback para PostgreSQL.

**Mapeamento HTTP.** Onde a resposta legada era verdadeira, ela foi mantida. Três respostas mentiam e foram corrigidas, seguindo a convenção v1 existente de "500 + texto":

| Endpoint | Sucesso | `NotFound` | Falha (`Unavailable`, `Rejected` ou `IncompatibleDocument`) |
|---|---|---|---|
| create-index | 200, mesmo texto | — | **500 "Error creating index."** (antes: 200 falso) |
| add/update | 200, mesmo texto | — | 500 "Error adding or updating person." (igual) |
| get-person | 200, mesmo JSON | 404 "Person not found." (igual) | **500 "Error retrieving person."** (antes: 404 falso; documento incompatível era 500 Problem Details) |
| get-all | 200, mesmo JSON | 500 "Error retrieving persons." (igual) | 500 "Error retrieving persons." (documento incompatível era 500 Problem Details) |
| delete | 200, mesmo texto | 500 "Error deleting person." (**legado preservado**) | 500 "Error deleting person." (igual) |

As três correções são exigidas pelo handoff ("não transformar indisponibilidade em item não encontrado", "não retornar dados incorretos"). Não mudam rotas, métodos, parâmetros, códigos de sucesso nem o OpenAPI, e estão cobertas por testes funcionais. Cancelamento do cliente propaga como `OperationCanceledException`, que é o mesmo caminho do `ThrowIfCancellationRequested` legado.

### Logging

- Console sempre ativo.
- O sink usa `BootstrapMethod.Silent`: um Elasticsearch indisponível **nunca derruba o startup** (antes derrubava).
- Com o Elasticsearch disponível, o bootstrap instala os templates ECS e o data stream `logs-api-example-demo`.
- Eventos ficam em buffer e são exportados em background, em **best effort**: sem garantia de entrega, e eventos não entregues podem ser descartados.
- Falhas de export vão para `stderr` por `ElasticLogSinkDiagnostics`: no máximo uma linha por minuto, só com a categoria (tipo da exceção ou HTTP). Ela não passa pelo Serilog, então não há recursão possível.
- `ElasticLoggingSettings:Enabled=false` remove o sink adicionado por código e mantém o console.
- **Startup medido** (`dotnet Template.WebApi.dll`, Development):

  | Cenário | Tempo até o Swagger responder |
  |---|---|
  | Elasticsearch disponível | 1,8 s |
  | Endpoint recusando conexão | ~5,5 s (recusa em loopback no Windows leva ~2 s por tentativa) |
  | Endpoint não roteável (`10.255.255.1`), timeout 5 s | 18,6 s |
  | Endpoint não roteável, timeout 1 s | 3,7 s |
  | Endpoint não roteável, sink desabilitado | 1,8 s |

  O atraso é limitado e proporcional a `ElasticLoggingSettings:RequestTimeout`.

### Health check

- Disponível → `Healthy`, HTTP 200.
- Inacessível, status de erro ou timeout → `FailureStatus` (padrão `Unhealthy`, HTTP 503; configurável para `Degraded`).
- Timeout de 5 s aplicado pelo `HealthCheckService` e pelo `HttpClient`. Cancelamento do chamador propaga.
- Descrições fixas ("Elasticsearch is reachable.", "Elasticsearch is unreachable.", "Elasticsearch answered with an error status.") e nenhuma exceção ou dado: não vaza URL, credencial nem detalhe interno. Em timeout, o próprio `HealthCheckService` escreve a mensagem genérica "A task was canceled.".
- Os logs de requisição do `HttpClient` do check foram desligados (`RemoveAllLoggers`) para não repetir cada falha com a exceção.
- O check roda só quando `/health` é chamado, então não bloqueia o shutdown.
- No Test não há entrada Elasticsearch, e `/health` continua idêntico ao baseline.

## Fake/no-op e rollback

**Test:**

- `DisabledPersonSearchIndex`, sem health check do Elasticsearch e sem sink do Elasticsearch.
- Os testes funcionais substituem o port por um fake gravador.
- Os testes de adapter, health e logging usam um `FakeElasticsearchServer` (Kestrel em loopback, sem pacote novo).

**Rollback sem reverter código:**

- `PersonSearchSettings:Enabled=false` desliga a busca (endpoints 500, nenhuma escrita).
- `ElasticLoggingSettings:Enabled=false` desliga o sink.
- `ElasticsearchHealthCheckSettings:Enabled=false` desliga o check.

**Rollback completo:** reverter o commit da fase de forma coerente (Application, projeto Search, WebApi, testes, solução, scripts e `Directory.Packages.props`). Não há migração de dados: o formato é o mesmo nos dois sentidos, e o adapter antigo lê os documentos novos (testado).

O handoff permitia manter o adapter antigo atrás de um flag. Isso **não foi feito**, por três motivos:

- os formatos e as requisições são idênticos (provado contra o Elasticsearch real);
- dois adapters não podem escrever no mesmo índice;
- o estado final não deve carregar código morto.

## Testes adicionados

- **Unitários (+20):**
  - `PersonUseCaseHandlerTests`: port correto para cada caso de uso; mapeamento comando → `IndexedPerson`; update com o mesmo upsert; `NotFound` diferente de `Unavailable`; listagem; remoção; repasse de token; cancelamento propagado em todos.
  - `PersonIndexResultTests`: sem valor em falha; falha com status de sucesso proibida; bulk parcial não é completo.
  - `ServiceRegistrationTests`: a Application não registra o port nem nada do Elastic.
- **Arquitetura (+6 regras; exceções da Fase 10 removidas):**
  - Application sem pacote ou fonte de implementação (lista de exceções **vazia**).
  - Domain sem Elastic, Serilog ou health check.
  - `Elastic.Clients.Elasticsearch` e `ElasticsearchClient` só em Infrastructure.Search; `ElasticSearchSettings` não existe mais como tipo.
  - Search referencia só a Application e implementa o port, que não vaza tipos.
  - Logging e health check não dependem do port nem das opções de busca.
  - WebApi só compõe a busca, e os controllers não conhecem o port.
  - Nenhum `BuildServiceProvider(` em `Src`, com uma única exceção nominal: `LocalizationExtensions.cs`, que é pré-existente e pertence à Fase 13.
  - Mutações controladas novas para cada marcador.
  - Também houve mutação real e temporária dos arquivos (pacote Elastic na Application, tipo `ElasticSearchSettings`, port no logging, `BuildServiceProvider` no health): 5 regras falharam como esperado, e os arquivos foram restaurados idênticos byte a byte.
- **Integração sem Elasticsearch (+58), contra o servidor fake:**
  - create idempotente e concorrente; nome inválido → `Rejected`;
  - upsert novo e existente com requisição e corpo exatos; leitura;
  - 4 formatos atuais e 5 incompatíveis; documento e índice ausentes;
  - listagem com corpo `{}`, vazia, índice ausente e um documento incompatível;
  - remoção e remoção de ausente;
  - bulk completo (NDJSON exato), parcial, falha geral e vazio;
  - 5xx/429 → `Unavailable`; 4xx → `Rejected`;
  - 3 respostas inválidas; servidor inacessível; timeout limitado; cancelamento antes e durante a chamada;
  - recuperação após restart; índice configurado em todas as operações;
  - **nenhum nome, chave, documento ou corpo nos logs**; erros de programação;
  - registro, precedência de opções, validação, flag de rollback, construção sem rede e log de endpoint sem path ou query.
- **Funcionais (+45):**
  - Os seis endpoints preservam status, texto, `text/plain` e JSON, e chamam o port com o argumento e o token certos.
  - `create-index` sem nome → 400.
  - `NotFound` diferente de falha.
  - Cancelamento chega ao port.
  - Composição Test sem busca: endpoints de pessoa 500 e produtos e Swagger 200; `/health` igual ao baseline.
  - Health check: disponível, erro, inacessível (rápido, sem detalhes), `Degraded`, timeout, cancelamento, precedência, desabilitado e validação.
  - `/health` com Elasticsearch inacessível: 503 sanitizado, com produtos e Swagger 200.
  - Logging:
    - console com o sink indisponível, sem atraso de startup;
    - diagnóstico em `stderr` sem endpoint nem payload;
    - bootstrap de templates e export para `logs-api-example-demo` com o Elasticsearch disponível;
    - seção nova sobrepondo a legada;
    - `Enabled=false` sem tráfego;
    - opções de logging que não leem as de busca;
    - throttling do diagnóstico, que nunca lança;
    - host com sink inacessível servindo endpoints.
- **Elasticsearch real, opt-in (`Tests/Template.Elasticsearch.Integration.Tests`, 11):**
  - imagem `docker.elastic.co/elasticsearch/elasticsearch:8.17.4@sha256:413d8df6…cd56` (mesma versão do compose, fixada por digest), security desabilitada como no compose, sem volumes;
  - índices exclusivos `phase10-test-<guid>`, apagados no fim; wait strategy com limite de 3 min;
  - versão e saúde do cluster; create idempotente e nome inválido; round-trip de upsert, leitura, update, listagem e remoção;
  - página padrão de 10; bulk com merge e falha parcial real (mapping `integer`); índice ausente; documento incompatível real;
  - **compatibilidade nos dois sentidos com o código legado** (`_source` byte a byte);
  - health check real;
  - log no data stream real (`_data_stream` confirmado e removido no fim);
  - disponibilidade: adapter e health check criados antes do container subir, depois uso, parada (falha rápida) e restart em porta fixa com recuperação e dados preservados.

## Comandos executados e resultados

Gate final em sequência, com o SDK fixado `C:\Users\alexa\.dotnet\dotnet.exe` (10.0.401):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-phase-validation.ps1
```

O script ganhou o passo `06c-test-elasticsearch`.

O gate final rodou de 19:06:07 a 19:08:22, e todos os passos terminaram com exit=0:

| Passo | Resultado |
|---|---|
| `restore TemplateCleanArchitecture.App.slnf` | OK (16 projetos) |
| `build Template.WebApi` | OK, 0 avisos, 0 erros. Builds não incrementais do WebApi e dos projetos de teste afetados também deram 0 avisos. |
| Arquitetura | **22/22** (16 + 6 regras novas) |
| Unitários | **77/77** (57 + 20) |
| Integração EF InMemory + adapters | **136/136** (78 + 58) |
| Integração PostgreSQL 16.15 | **5/5** |
| RabbitMQ real (`rabbitmq:4.2.2`, opt-in) | **17/17** |
| **Elasticsearch real (`8.17.4`, opt-in)** | **11/11** (cerca de 40 s; também 11/11 em três execuções isoladas seguidas) |
| Funcionais | **86/86** (41 + 45) |
| `list package --vulnerable --include-transitive` | Nenhum pacote vulnerável nos 16 projetos, incluindo `Template.Infrastructure.Search` e `Template.Elasticsearch.Integration.Tests` |
| `docker compose config -q` | OK (compose não alterado) |
| Captura do OpenAPI | OK |
| `graphify update . --force` | OK: 2.792 nós, 6.064 arestas e 169 comunidades no gate; 2.821 nós, 6.137 arestas e 171 comunidades na nova execução, depois de escrever este relatório e o `AGENTS.md`. Avisos: `tree_sitter_sql` ausente e rótulos de comunidade desatualizados (`graphify label`). |
| `git diff --check` | exit 0. Só avisos informativos de normalização LF/CRLF. |

O `.agents/skills/architecture-boundary-check/scripts/check-boundaries.ps1` também passou ("Architecture boundary scan passed."). Ele foi atualizado: a lista de exceções da Application agora é vazia, e há varreduras novas para Elastic no Core, para o client fora do projeto Search e para `BuildServiceProvider(`. Uma mutação temporária fez o script falhar nas três regras.

Antes desse resultado, houve duas falhas, ambas investigadas e corrigidas:

1. O primeiro disparo do gate pelo Git Bash não executou nada (caminho `.\scripts` mal interpretado) e foi refeito pelo PowerShell.
2. Numa rodada intermediária, só o passo `06c` falhou: logo depois do restart do container, a leitura voltou `Unavailable`, porque os shards do índice ainda estavam se recuperando (o Elasticsearch responde 503). A classificação estava certa; o teste é que considerava a porta HTTP como sinal de prontidão. Agora o teste espera o cluster ficar `yellow` (com limite) antes de verificar a recuperação.

### Comparação do OpenAPI com `docs/refactoring/baseline-fase-6/`

- `openapi-v1.json` e `openapi-v2.json`: **idênticos ao blob versionado**. Com as quebras de linha normalizadas, o SHA-256 é igual ao do baseline (`521a4ff0…` e `75127aba…`), e a comparação JSON estrutural tem zero diferenças. O baseline não foi alterado.
- `health.json`: difere só em `totalDuration`. No Test não há entrada Elasticsearch, como antes.
- O OpenAPI não documenta corpos de resposta dos endpoints de pessoa. Por isso as três correções de falha (create-index, get-person e o caso de documento incompatível) não alteram o documento publicado. Elas estão descritas na política de disponibilidade e cobertas por testes funcionais.

### Smoke da WebApi real (Development, banco em memória, RabbitMQ desabilitado)

| Cenário | Resultado |
|---|---|
| Elasticsearch 8.17.4 disponível | `/health` 200 Healthy. Os seis endpoints respondem como o legado: create-index válido 200, add/update 200, get 200 com o mesmo JSON, get ausente 404, get-all 200, delete 200 e delete ausente 500. A diferença é create-index com `Bad_Name`: agora 500 (antes 200 falso). O data stream `logs-api-example-demo` recebeu eventos. |
| Elasticsearch recusando conexão | **A WebApi sobe** (antes não subia). `/health` 503 com "Elasticsearch is unreachable.". Endpoints de pessoa 500 com os textos v1; produtos 200; uma única linha de diagnóstico do sink em `stderr`. |
| Endpoint não roteável | A WebApi sobe (tempos medidos na seção de logging); `/health` 503 por timeout de 5 s; operações de pessoa limitadas a 10 s; produtos 200. |

O container descartável `charact-es-phase10`, usado na caracterização e no smoke, foi parado e removido (`--rm`). Os containers `elasticsearch`/`kibana` do compose e o volume `v01` **não foram tocados**.

## Pacotes

| Pacote | Mudança |
|---|---|
| `Elastic.Clients.Elasticsearch` 8.17.1 | **Movido** da Application para `Template.Infrastructure.Search`; versão inalterada |
| `AspNetCore.HealthChecks.Elasticsearch` 9.0.0 | **Removido** do WebApi e do CPM (incompatível em runtime; substituído por um check próprio sem pacote) |
| `Elastic.Serilog.Sinks` 8.12.3 | Inalterado, continua no WebApi |
| `Testcontainers` 4.16.0 | **Adicionado** ao CPM para a suíte opt-in. É a mesma versão que já entrava transitivamente por `Testcontainers.RabbitMq`/`PostgreSql`; consultada no [índice oficial do NuGet](https://api.nuget.org/v3-flatcontainer/testcontainers/index.json) em 2026-10-10 (última estável, publicada em 2026-10-08). |
| `Microsoft.Extensions.Logging.Abstractions`, `Options.ConfigurationExtensions` 10.0.12 | Referenciados pelo projeto Search; família já fixada |

Com a busca no grafo do WebApi, `Elastic.Transport` voltou a resolver **0.5.7**, a mesma versão do legado. **Nenhuma versão major foi alterada**: client 8.17.1, sink 8.12.3, servidor e Kibana 8.17.4 continuam iguais, e o compose não foi alterado.

## Riscos residuais

- **Sink vindo da configuração:** `Serilog:WriteTo:Elasticsearch` nos appsettings continua criando um segundo sink (`localhost:9200`, `logs-dotnet-default`, sem bootstrap), fora do controle de `ElasticLoggingSettings:Enabled`. É preexistente e foi mantido para não mudar o logging com o Elasticsearch disponível. Fica como item da Fase 13: remover a entrada ou corrigir os argumentos.
- **Bootstrap síncrono do sink:** com um endpoint que não responde, o startup atrasa de forma limitada e proporcional a `RequestTimeout` (medido acima).
- **Listagem limitada a 10 hits** e sem ordem: comportamento herdado.
- **Delete de ausente continua 500**, porque é o contrato legado; o port distingue o caso, e uma futura versão da API pode responder 404.
- **`/health` passa a refletir o Elasticsearch de verdade:** antes era sempre 503 por causa do bug; agora fica 200 com o Elasticsearch disponível e 503 sem ele. Orquestradores que dependam de `/health` passam a ver o estado real. Readiness e liveness ficam para a Fase 13.
- **Fallback das chaves legadas:** busca, logging e health check ainda leem `ElasticSearchSettings` quando as seções novas faltam; mudar só `ElasticSearchSettings:Url` move os três.
- **`LocalizationExtensions` usa `BuildServiceProvider()`:** preexistente, de localização, registrado como exceção nominal da Fase 13.
- **Bulk sem caso de uso:** `UpsertManyAsync` existe só no port e no adapter.
- **Sem autenticação:** igual ao legado; URL com credenciais é rejeitada.
- **Ambiente completo:** `docker compose up` completo e `dotnet run` contra os serviços do compose não foram executados.

## Gates não executados

- **`docker compose up` completo** com os serviços e volumes do usuário: não executado, para não tocar em dados. A compatibilidade com o 8.17.4 foi coberta por containers descartáveis da mesma versão.
- **`dotnet run` contra o compose:** não executado. O smoke usou banco em memória e Elasticsearch descartável.
- **VSIX:** fora do gate, sem mudança.
- **Redis real:** pendência da Fase 8, fora do escopo.

## Confirmações

- O contrato HTTP de sucesso e o OpenAPI v1/v2 foram preservados (comparação acima). As três respostas de falha que mentiam foram corrigidas como descrito e estão cobertas por testes.
- As exceções de arquitetura da Fase 10 foram removidas. As das Fases 8 e 9 não voltaram, e a lista de pacotes e fontes de exceção da Application está **vazia**.
- Nenhuma versão major da stack Elastic foi alterada.
- Nenhum trabalho das Fases 11+ foi iniciado: sem client ou servidor 9, sem migração de índice, sem templates próprios, sem OpenTelemetry, sem mudanças em RabbitMQ ou Redis.
