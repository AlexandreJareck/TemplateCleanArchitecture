# Relatório da Fase 11 — 2026-10-11

Checkpoint de 2026-10-11 (America/Sao_Paulo). A Fase 11 atualizou a integração Elastic, já isolada na Fase 10, da linha 8.x para a 9.x: client .NET **9.5.3**, sink Serilog **9.0.0**, Elasticsearch e Kibana **9.5.5** (imagens oficiais fixadas por digest). O caminho oficial (8.17.4 → 8.19.23 → 9.5.5) foi ensaiado num ambiente paralelo descartável, com **dados sintéticos**, por snapshot/restore em clusters novos. O port da Application, o formato dos documentos e o contrato HTTP/OpenAPI não mudaram. **A compatibilidade com dados reais não foi verificada**: o volume `cleanarchitecture_v01` do usuário não foi lido. **Nada das Fases 12+ foi implementado.**

## Git e checkout

- Checkout autoritativo: `C:\Users\alexa\source\repos\TemplateCleanArchNET`, branch `refactor/fase-10-elastic`, HEAD `0ef646c` (commit da Fase 10). Nenhuma worktree foi usada; a worktree antiga `.claude/worktrees/fase-8-redis-adapter-f4fda8` não foi tocada.
- Antes de qualquer mudança, `git diff HEAD -- Src Tests Directory.Packages.props` estava vazio: o checkout continha todo o estado das Fases 0–10.
- Arquivos não versionados preexistentes (`docs/refactoring/handoff-phase-10..12.md` e o cache `graphify-out/cache`) foram preservados. Durante a fase surgiram outros arquivos não versionados que **não foram criados por esta fase** (`docs/refactoring/execute-phase-12..21.md`, `handoff-phase-13..21.md`, `prompt-create-handoffs-phases-14-21.md`); também foram preservados.
- Não houve reset, stash, clean, checkout destrutivo, commit, push, merge nem PR.

## Pré-condições verificadas (Fase 10)

| Verificação | Evidência no estado real |
|---|---|
| Relatório da Fase 10 | `docs/refactoring/phase-10-report.md` registra gate verde (22/77/136/5/17/11/86) e os gates não executados |
| Application sem pacote ou tipo Elastic | `Template.Application.csproj` sem `Elastic.*`; teste de arquitetura com lista de exceções vazia (22/22 no início e no fim) |
| Port na Application | `Interfaces/Search/IPersonSearchIndex` e contratos `Search/Persons/*` |
| Adapter em Infrastructure | `Template.Infrastructure.Search` é o único projeto com `Elastic.Clients.Elasticsearch` |
| Busca, sink e health check separados | `PersonSearchSettings`, `ElasticLoggingSettings` e `ElasticsearchHealthCheckSettings`; regras de arquitetura dedicadas |
| WebApi e funcionais sem Elasticsearch | Ambiente Test com `DisabledPersonSearchIndex`, sem sink nem check |
| Adapter 8.x verde | Suíte opt-in 11/11 contra 8.17.4 no gate da Fase 10 |
| Exceções da Fase 10 | Removidas; só resta `BuildServiceProvider()` em `LocalizationExtensions` (Fase 13) |

## Inventário antes do upgrade

### Pacotes e imagens

| Item | Antes | Onde |
|---|---|---|
| `Elastic.Clients.Elasticsearch` | 8.17.1 (transport 0.5.7) | `Template.Infrastructure.Search` |
| `Elastic.Serilog.Sinks` | 8.12.3 (`Elastic.Ingest.*` 0.7.5, ECS 8.12.3) | WebApi |
| Health check | Check próprio (`HEAD /` por `HttpClient`), sem pacote. `AspNetCore.HealthChecks.Elasticsearch` foi removido na Fase 10 | WebApi |
| Elasticsearch (compose) | `docker.elastic.co/elasticsearch/elasticsearch:8.17.4` (digest `sha256:413d8df6…cd56`), volume `v01` | `docker-compose.yml` |
| Kibana (compose) | `docker.elastic.co/kibana/kibana:8.17.4` (digest `sha256:45859e32…9f4b`) | `docker-compose.yml` |
| Testes reais | `elasticsearch:8.17.4` por digest | `Tests/Template.Elasticsearch.Integration.Tests` |

### Configuração e comportamento

- Client: `SingleNodePool`, `DefaultIndex`, `RequestTimeout` (10 s), sem autenticação nem TLS (a validação rejeita URL com credenciais); security desabilitada no compose.
- Operações do adapter: `HEAD`/`PUT /{index}`; `PUT /{index}/_doc/{id}?refresh=wait_for`; `POST /{index}/_bulk` com `update` + `doc_as_upsert`; `GET /{index}/_doc/{id}`; `POST /{index}/_search` com corpo `{}` (10 hits); `DELETE /{index}/_doc/{id}`. Não há delete-by-query (removido na Fase 10), agregações, paginação nem consultas próprias.
- Documento: `{"id":<int>,"firstName":…,"lastName":…}`, camelCase, nulos omitidos; não-ASCII escapado como `\uXXXX` (medido no ensaio, igual nas duas versões).
- Sink: data stream `logs-api-example-demo`, `BootstrapMethod.Silent`, timeout 5 s, diagnóstico em `stderr` com throttling.
- Health: `/health`, entrada `elasticsearch`, `Unhealthy` em falha, timeout 5 s, descrições fixas.
- Compose: rede `nw`, volume `v01` montado no Elasticsearch. Neste diretório o nome de projeto resolve para `templatecleanarchnet`. No Docker local, os dados 8.17.4 do usuário estão em `cleanarchitecture_v01`, de um nome de projeto anterior, com o container `elasticsearch` 8.17.4 parado há 8 meses. **Nenhum deles foi lido ou alterado.**

### Ativos Elastic do cluster (fonte do ensaio, criada pela WebApi 8.x real)

Capturados em `artifacts/validation/elastic-rehearsal/20261011100719/evidence/inventory-*` (git-ignored), sem documentos nem credenciais:

| Ativo | Encontrado |
|---|---|
| Índice da aplicação | `persons` (31 docs), mapping dinâmico: `id` long, `firstName`/`lastName` text + `keyword` (`ignore_above` 256) |
| Alias | `persons-read` (criado pelo ensaio, a aplicação não cria aliases) |
| Data stream | `logs-api-example-demo` |
| Index template do sink 8.12.3 | `logs-api-example-8.11.0`, padrão `logs-api-example-*`, prioridade 527104, `composed_of` 40 templates `ecs_8.11.0_*` + `data-streams-mappings` |
| Templates legados | Só os `.monitoring-*` embutidos |
| Ingest pipelines e ILM | Só os embutidos do Elasticsearch/APM; a aplicação não cria nenhum |
| Templates "próprios" (não gerenciados, sem ponto) | `logs-api-example-8.11.0` e `apm-source-map` (este, do APM do Kibana) |

O repositório não versiona templates, mappings, aliases, pipelines nem políticas próprias: os únicos ativos de templates são os que o sink instala no bootstrap.

## Fontes oficiais consultadas (2026-10-11, America/Sao_Paulo)

| Fonte | URL | O que estabelece |
|---|---|---|
| Índice NuGet do client | https://api.nuget.org/v3-flatcontainer/elastic.clients.elasticsearch/index.json | Última estável 9.5.3 (2026-09-28); sem deprecation nem vulnerabilidade no catálogo; depende de `Elastic.Transport` ≥ 1.1.0 |
| Índice NuGet do sink | https://api.nuget.org/v3-flatcontainer/elastic.serilog.sinks/index.json | Última estável 9.0.0 (2025-08-13); depende de `Elastic.Ingest.Elasticsearch.CommonSchema` 9.0.0 → `Elastic.Ingest.*` ≥ 0.14.0 → transport ≥ 0.10.0 |
| Releases do ecs-dotnet | https://github.com/elastic/ecs-dotnet/releases | 9.0.0 é a última release estável do sink |
| Releases do transport | https://github.com/elastic/elastic-transport-net/releases | 0.13.0 e 0.16.0 removeram/alteraram membros públicos; 1.0.0 e 1.1.0 sem mudança de API |
| Health check | https://api.nuget.org/v3/registration5-gz-semver2/aspnetcore.healthchecks.elasticsearch/9.0.0.json | Última 9.0.0 (2024-12-19), depende do client 8.x ≥ 8.14.3 |
| Compatibilidade do client | https://www.elastic.co/docs/reference/elasticsearch/clients/dotnet | Client 9.x funciona com servidores 9.x e 10.x; **não** com 8.x. Client 8.x funciona com 8.x e 9.x |
| Breaking changes do client | https://www.elastic.co/docs/release-notes/elasticsearch/clients/dotnet/breaking-changes | 9.0.0: descriptors viram `struct`, menos genéricos, construtores de índice removidos, `Query`/`Aggregation` sem fábricas estáticas; 9.3.5: transport ≥ 0.16.0 |
| Preparação do upgrade | https://www.elastic.co/docs/deploy-manage/upgrade/prepare-to-upgrade | Para 9.x é obrigatório partir de 8.19.x (e da última 8.19); Upgrade Assistant; snapshot antes; sem downgrade |
| Regra de versão/data | https://www.elastic.co/docs/deploy-manage/upgrade/deployment-or-cluster | Só se atualiza para 9.5.5 a partir de versão menor **e** lançada antes |
| Upgrade Assistant | https://www.elastic.co/docs/deploy-manage/upgrade/prepare-to-upgrade/upgrade-assistant | Rodar no 8.19 mais recente e resolver os itens críticos |
| Snapshot e restore | https://www.elastic.co/docs/deploy-manage/tools/snapshot-and-restore | Índices criados em 8.0–8.19 restauram em 9.0–9.5.5; cópia de diretório de dados não é backup; feature states para índices de sistema; Kibana 9 não migra `.kibana` escrito antes de 8.18 |
| Breaking changes do Elasticsearch | https://www.elastic.co/docs/release-notes/elasticsearch/breaking-changes | 9.0.0: LogsDB por padrão em `logs-*-*`, bulk mais estrito, valor legado de `discovery.type` removido; nada que afete as operações do adapter |
| Release notes | https://www.elastic.co/docs/release-notes/elasticsearch e https://www.elastic.co/docs/release-notes/kibana | 9.5.5 é a última 9.x (correções de segurança, ESA-2026-193 no Kibana) |
| Datas de release | https://github.com/elastic/elasticsearch/releases | v9.5.5 e v8.19.23 publicadas juntas em 2026-10-06T14:01:58Z; v8.19.22 em 2026-09-23 |
| Registro oficial de imagens | `https://docker.elastic.co/v2/{elasticsearch/elasticsearch,kibana/kibana}/tags/list` e manifests | Últimas tags estáveis 9.5.5 e 8.19.23; digests abaixo |
| Documentação do sink | https://www.elastic.co/docs/reference/ecs/logging/dotnet/serilog-data-shipper | Sink funciona com "8.x e superiores"; `BootstrapMethod`, `DataStreamName`, overload com transport |

## Matriz de compatibilidade selecionada

| Componente | Atual | Selecionado | Evidência de compatibilidade | Breaking changes relevantes | Ação de migração | Rollback |
|---|---|---|---|---|---|---|
| `Elastic.Clients.Elasticsearch` | 8.17.1 | **9.5.3** | Mesma major do servidor; minor igual a 9.5 (matriz oficial); 13/13 testes reais contra 9.5.5 | `_id` numérico do bulk passa a sair como número; delete de ausente (404 `not_found`) passa a ser resposta válida; `Search…Index()` obsoleto | 3 ajustes no adapter (abaixo) | Reverter para 8.17.1 junto com o adapter; client 9 **não** fala com 8.x |
| `Elastic.Transport` (transitivo) | 0.5.7 | **1.1.0** | Exigido pelo client; um único assembly no grafo | 0.13/0.16 mudaram API pública | Prova de binding do sink (abaixo) | Volta a 0.5.7 com o client |
| `Elastic.Serilog.Sinks` | 8.12.3 | **9.0.0** | Última estável; ECS 9.0.0; ingest real no 9.5.5 provado | Templates passam a `ecs_9.0.0_*` e `logs-api-example-9.0.0` (prioridade 589824) | Teste de logging atualizado | Reverter para 8.12.3 |
| `Elastic.Ingest.*`/`Elastic.Channels` (transitivos) | 0.7.5 | 0.14.0 | Mínimos exigidos pelo sink 9.0.0, compilados contra transport 0.10 | — | Ver risco de binding | Com o sink |
| Health check | Próprio (`HEAD /`) | **Mantido** | `HEAD /` é API estável; Healthy contra 9.5.5 | — | Nenhuma | — |
| `AspNetCore.HealthChecks.Elasticsearch` | Ausente | **Não readotado** | 9.0.0 depende do client 8.x e lança `TypeLoadException` com transport 1.1.0 (reproduzido) | — | — | — |
| Elasticsearch | 8.17.4 | **9.5.5** `sha256:13b4a40bb095774f5fda635e45fd5677cc6a26565e42b24997b0c72b88e75984` | Caminho oficial via 8.19.23; ensaio 49/49 | LogsDB por padrão em `logs-*-*` | Volume novo `es9-data` | Imagem 8.17.4 + `v01` intocado |
| Kibana | 8.17.4 | **9.5.5** `sha256:13b72ce7222c36e4213229004ae62a87aeb32fd372ebfa6fb38f7a3285fdbb78` | Mesma versão do Elasticsearch; data view do 8.17.4 migrada | — | Feature state `kibana` no snapshot | Imagem 8.17.4 |
| Elasticsearch/Kibana intermediário | — | 8.19.23 (`sha256:4d0724bb…14c1` / `sha256:920b24f6…e995`) | Salto obrigatório; Upgrade Assistant | — | Só no ensaio | — |

**Regra de versão/data.** 9.5.5 e 8.19.23 foram publicadas juntas (mesmo timestamp no GitHub, mesmos artefatos em 2026-10-06), como par de patches de segurança. A documentação não trata explicitamente o mesmo dia; o par foi aceito porque não há correção do 8.19.23 ausente no 9.5.5. Se uma leitura estrita for exigida, 8.19.22 (2026-09-23) → 9.5.5 satisfaz a regra literalmente.

**Por que o client 9.5.3 com o sink 9.0.0.** O sink mais novo foi compilado contra `Elastic.Transport` 0.10.x, e o client atual exige ≥ 1.1.0. Escolher o client 9.2.3 (transport 0.10.3) casaria as versões, mas deixaria o client quatro minors atrás do servidor. A decisão foi usar o 9.5.3 e **provar** o binding: um teste carrega todos os tipos e compila (JIT) todos os métodos não genéricos dos 7 assemblies do sink contra o transport resolvido. Mutação controlada: com `AspNetCore.HealthChecks.Elasticsearch` 9.0.0 temporariamente adicionado, o mesmo teste falhou com `TypeLoadException: Elastic.Transport.TransportConfigurationBase\`1` (o defeito da Fase 10); a mutação foi revertida.

## Caminho de upgrade adotado

Justificativa oficial: para 9.x é obrigatório partir de 8.19.x (o mais recente); Kibana 9 não migra índices `.kibana` escritos antes de 8.18; um node 9.x não pode abrir dados 8.x de outra forma que não a suportada, e não há downgrade. O handoff proíbe montar 9.x em volume 8.x. Por isso, o caminho é **cluster novo + snapshot/restore**, com o salto obrigatório:

1. Fonte 8.17.4 (cluster atual) → snapshot **S0** (índices, data streams e feature state `kibana`, sem global state).
2. Cluster **novo** 8.19.23 → restaurar S0 → deprecation API, `system_features`, Upgrade Assistant e deprecations do Kibana 8.19.23.
3. Snapshot **S1** do 8.19.23.
4. Cluster **novo** 9.5.5 → copiar os templates próprios → restaurar S1 → Kibana 9.5.5 migra os saved objects.
5. Aplicação 9.x aponta para o 9.5.5. A fonte 8.17.4 e S0 ficam intactos para rollback.

Não houve índices 7.x, configurações deprecadas, pipelines nem ILM próprios a migrar, então nenhum reindex foi necessário: os índices criados em 8.x são lidos pelo 9.5.5 como estão (`index.version.created` 8.x confirmado).

**Global state.** Os restores usam `include_global_state: false` para não sobrescrever templates embutidos e configurações persistentes do cluster novo. Os templates não gerenciados (sem ponto e sem `_meta.managed`) são copiados explicitamente **antes** do restore, para o data stream restaurado continuar com template correspondente.

## Deprecations e Upgrade Assistant (no 8.19.23, dados do ensaio)

| Achado | Classificação |
|---|---|
| `_migration/deprecations`: nenhum item em `cluster_settings`, `node_settings`, `index_settings`, `data_streams`, `templates`, `ilm_policies`, `ml_settings` | Não aplicável |
| `_migration/system_features`: `NO_MIGRATION_NEEDED` | Não aplicável |
| Upgrade Assistant: `readyForUpgrade: true`, "All deprecation warnings have been resolved." | Não bloqueante |
| Log de deprecation: aviso `dot-prefix` para o índice `.apm-source-map` (criado pelo APM do Kibana) | Pode ser adiado: é aviso, não crítico, e não é ativo da aplicação |
| Kibana: `core.node_js_deprecations` "Detected legacy OpenSSL provider" (warning) | Pode ser adiado: aviso de runtime do Node do próprio Kibana 8.19, sem ação no cluster |
| Kibana: `enterpriseSearch` "Failed to fetch deprecations" (fetch_error) | Não aplicável: Enterprise Search não está instalado (o 9.x exige removê-lo, e ele nunca existiu aqui) |
| Configurações de security deprecadas | Não aplicável: security desabilitada, igual ao compose |
| APIs deprecadas do client | Resolvido: `SearchRequestDescriptor.Index()` → `Indices()` (único aviso CS0618) |

Nenhum item bloqueante ou crítico. Nada foi suprimido.

## Snapshot e restore (evidência)

| Snapshot | Cluster | Estado | Shards | Conteúdo |
|---|---|---|---|---|
| S0 `p11-s0-source-817` (repo `p11-repo-source`) | 8.17.4 | SUCCESS (versão 8.17.0–8.17.4, 0,8 s) | 11/11, 0 falhas | `persons`, data stream `logs-api-example-demo`, feature state `kibana` |
| S1 `p11-s1-hop-819` (repo `p11-repo-hop`) | 8.19.23 | SUCCESS | 12/12, 0 falhas | Idem, após o salto |

- Repositórios `fs` num diretório do host montado em `/snapshots` (`path.repo`), **um repositório por cluster escritor**; os demais registram como `readonly: true`. A primeira execução usou um repositório compartilhado com dois escritores; isso contraria a orientação da Elastic, e o script foi corrigido.
- Restore verificado: S0 → 8.19.23 (0 falhas, paridade total) e S1 → 9.5.5 (0 falhas). No teste automatizado, o snapshot também foi restaurado **no próprio 8.x** com renomeação (`rollback-check-*`), com contagem igual, provando o ponto de recuperação para rollback.
- Nenhuma cópia de diretório de dados foi usada como backup.

## Dados usados

**Somente sintéticos.** O volume `cleanarchitecture_v01` do usuário (8.17.4) existe, mas não foi lido, copiado, montado nem migrado; isso exige autorização explícita. A fonte do ensaio foi escrita pela **WebApi 8.x real** (build da Fase 10 publicado num diretório temporário): 31 pessoas pelos endpoints v1 (incluindo update, nulos e nomes não-ASCII), ~1.800 eventos de log pelo sink 8.12.3 com os templates reais, e uma data view no Kibana 8.17.4. **A compatibilidade com os dados reais do usuário continua não verificada.**

## Mudanças no adapter (`ElasticsearchPersonSearchIndex`)

| Ponto | 8.x | 9.x | Mudança |
|---|---|---|---|
| Construção/DI | `ElasticsearchClientSettings` + `SingleNodePool`, singleton, `IDisposable` | Sem mudança de API | Nenhuma |
| Índice: exists/create | `Indices.ExistsAsync`/`CreateAsync` | Iguais | Nenhuma |
| Indexar | `IndexAsync(doc, r => r.Index().Id().Refresh(WaitFor))` | Igual | Nenhuma |
| Bulk | `UpdateMany(..., u.Id(int))` → `"_id":"1"` | `Id(int)` vira `"_id":1` | `_id` passado como texto invariante: NDJSON byte a byte igual ao 8.x |
| Get | `GetAsync<JsonElement>` | Igual | Nenhuma |
| Search | `search.Index(name)` | `Index()` obsoleto | `Indices(name)`; corpo continua `{}` (10 hits) |
| Delete | 404 `not_found` = resposta inválida → `NotFound` | 404 `not_found` = resposta **válida** | `Result.NotFound` → `NotFound` (antes reportaria sucesso) |
| Erros/cancelamento | `ElasticsearchServerError`, `ApiCallDetails`, `TransportException`/`UnsupportedProductException` | Iguais | Nenhuma |
| Serialização | `PersonDocument` com atributos STJ | Igual (não-ASCII `\uXXXX` nas duas) | Nenhuma |

Query/aggregation containers, field inference, datas, delete-by-query e paginação não são usados pelo adapter. O port, os DTOs, a classificação de falhas e os logs não mudaram. Nenhum tipo Elastic 9 vazou para Application, Domain, controllers ou contratos.

## Mudanças no sink Serilog

- Código inalterado (`LoggingExtensions`, `ElasticLoggingSettings`, `ElasticLogSinkDiagnostics`): a API usada (`WriteTo.Elasticsearch(nodes, options, transport)`, `DataStreamName`, `BootstrapMethod.Silent`, `ConfigureChannel`, callbacks) é a mesma no 9.0.0.
- Templates instalados: 41 component templates `ecs_9.0.0_*` e o index template `logs-api-example-9.0.0` (padrão `logs-api-example-*`, prioridade 589824, `data_stream`, `composed_of` inclui `data-streams-mappings`). O 8.12.3 instalava `logs-api-example-8.11.0` (prioridade 527104).
- **Coexistência:** com o template 8.11.0 migrado, o bootstrap do 9.0.0 instala o seu sem colisão (prioridades diferentes). O data stream existente continua escrevendo no backing index antigo; o próximo rollover usa o template 9.0.0. Provado no teste automatizado e no ensaio (rollover para `.ds-logs-api-example-demo-…-000002`).
- **LogsDB:** no 9.5.5, o backing index criado depois do rollover tem `index.mode: logsdb`, `index.mapping.source.mode: STORED` (licença basic) e `index.logsdb.sort_on_host_name: true` (breaking change oficial do 9.0.0 para `logs-*-*`). A ingestão continua funcionando; o impacto é de armazenamento e ordenação, não de API.
- Bootstrap idempotente (dois loggers seguidos, sem diagnóstico de falha); fail-soft sem Elasticsearch; console contínuo; uma linha de diagnóstico em `stderr` sem endpoint nem payload.
- A entrada `Serilog:WriteTo:Elasticsearch` dos appsettings continua resolvendo para o sink 9.0.0 e não impediu o startup (smoke em Development). Continua sendo o risco residual da Fase 10 (sink duplicado), para a Fase 13.

## Health check

O check próprio da Fase 10 (`HEAD /`, timeout 5 s, descrições fixas) foi **mantido**: depende só de HTTP e do endpoint raiz, estável no 9.x. Ficou Healthy contra o 9.5.5 real e no ensaio, e 503 sanitizado com o Elasticsearch inacessível. O pacote `AspNetCore.HealthChecks.Elasticsearch` não foi readotado: a última versão (9.0.0) depende do client 8.x e quebra com o transport 1.1.0, como a mutação reproduziu. Rota, writer, nome da entrada e status foram preservados. Novos testes cobrem 401/403 sem vazar o corpo nem o desafio.

## Templates, mappings, aliases e data streams no 9.5.5

| Item | Resultado |
|---|---|
| Templates próprios migrados (`logs-api-example-8.11.0`, `apm-source-map`) | Iguais aos do salto 8.19.23 |
| Templates do sink 9.0.0 | Instalados e idempotentes; data stream reporta `logs-api-example-9.0.0` depois do bootstrap |
| Templates de teste (`phase11-component`/`phase11-template`, prioridade 200) | Migrados; índice criado por eles mantém `number_of_replicas: 0` |
| Mapping de `persons` | JSON idêntico ao da fonte (210 caracteres comparados) |
| Alias `persons-read` | Restaurado; contagem igual; leitura pelo adapter via alias |
| Data stream | Restaurado com o template 8.11.0, gravável, rollover OK |
| Pipelines/ILM próprios | Não existem |
| Colisões | Nenhuma: os padrões sobrepostos têm prioridades distintas (527104, 589824 e o embutido `logs` 100) |

Nenhum template ou mapping da aplicação precisou mudar; não há reindex.

## Ensaio paralelo

Duas camadas, ambas com containers, volumes e redes novos, imagens fixadas por digest, limite de memória, timeouts e nomes exclusivos (`p11-*`).

### Teste automatizado (`UpgradeRehearsalTests`, no gate `06c`)

Dois clusters Testcontainers (8.19.23 e 9.5.5) que só compartilham um diretório temporário de snapshots. Ele cobre:

- seed sintético no formato 8.x (50 pessoas, alias, templates de teste, réplica do layout do sink 8.12.3 com 20 eventos);
- deprecations sem crítico e `NO_MIGRATION_NEEDED`;
- o adapter 9.x contra o 8.19 é **rejeitado** (400), comprovando a matriz oficial;
- S0 com sucesso e verificado por restore no próprio 8.x;
- restore no 9.5.5, com paridade de contagens, documentos, consultas `match`/`term`, página padrão, alias, mapping, réplicas do template e versão de criação 8.x;
- adapter (ensure, get, list 10, upsert, update, bulk, remove, ausente), leitura por alias e health check;
- coexistência e rollover do sink;
- restart com persistência;
- fonte intacta, com snapshot ainda SUCCESS.

### Script (`scripts/elastic-upgrade-rehearsal.ps1`)

Executa o caminho completo com Kibana e com as duas WebApis reais. Resultado final (`RunId 20261011100719`): **49/49 checks**.

| Passo | Resultado |
|---|---|
| Fonte 8.17.4 + Kibana 8.17.4, data view criada | OK |
| Seed pela WebApi 8.x (client 8.17.1, sink 8.12.3) | 31 pessoas, 1.841 eventos, templates reais `logs-api-example-8.11.0` |
| S0 | SUCCESS, 0 falhas |
| Salto 8.19.23: restore, paridade, deprecations ES/Kibana, Upgrade Assistant | Tudo OK |
| S1 | SUCCESS |
| 9.5.5: templates, restore, saúde, paridade de 7 propriedades | Tudo igual |
| Kibana 9.5.5 | Disponível; data view criada no 8.17.4 migrada (200) |
| WebApi 9.x no 9.5.5 | get, get-all, create-index idempotente, add, update, delete, 404 de ausente, `/health` 200, sink no data stream migrado, rollover com template 9.0.0 |
| Formato gravado | Byte a byte igual ao do 8.x (ASCII e não-ASCII) |
| Restart do 9.5.5 | 7 propriedades iguais antes e depois |
| Rollback: WebApi 8.x na fonte 8.17.4 | Dados originais, `/health` 200 |
| Fonte | Ainda 8.17.4, dados iguais; S0 e S1 SUCCESS |

Execuções anteriores, registradas por transparência:

- `20261011095638`: 38/41. Houve dois defeitos do script e uma divergência não reproduzida.
  - O check de formato comparava com um literal, e não com o documento gravado pelo 8.x.
  - Dois clusters tinham o mesmo repositório como gravável.
  - "restart persistence" divergiu; a comparação ainda era agregada e não registrou qual propriedade mudou. Com a comparação por propriedade, as duas execuções seguintes passaram.
- `20261011100245`: 49/49, mas os nomes não-ASCII viraram mojibake, porque o PowerShell 5.1 lê `.ps1` sem BOM como ANSI. O script passou a usar escapes JSON ASCII.

## Testes adicionados ou atualizados

- **Funcionais (+10):**
  - `ElasticDependencyCompatibilityTests`: grafo com client e sink 9 e um único transport; binding/JIT dos 7 assemblies do sink (8 casos).
  - Health check com 401/403 sem vazamento (2 casos).
  - `ElasticLoggingTests` passa a esperar `ecs_9.0.0_*` e `logs-api-example-9.0.0`.
- **Elasticsearch real (opt-in, 11 → 13):**
  - imagem 9.5.5 por digest; versão esperada 9.5.5;
  - compatibilidade com o formato 8.x por HTTP cru (o client 8.x não pode coexistir no grafo), incluindo bulk e mapping;
  - cancelamento sem escrita;
  - sink com templates 9.0.0, prioridade, idempotência e diagnóstico vazio;
  - `UpgradeRehearsalTests` (migração completa, acima).
- **Integração com o servidor fake:** sem mudança. Os 136 testes existentes detectaram as duas mudanças semânticas do client 9 (`_id` numérico no bulk e delete de ausente) antes da correção.
- **Arquitetura e unitários:** sem mudança; continuam verdes.

## Comandos executados e resultados

SDK fixado `C:\Users\alexa\.dotnet\dotnet.exe` (10.0.401), sempre em sequência.

### Verificações pontuais durante a implementação

| Comando | Resultado |
|---|---|
| `restore` + `list package --include-transitive` antes e depois | Antes: client 8.17.1, transport 0.5.7, sink 8.12.3. Depois: client 9.5.3, transport 1.1.0, sink 9.0.0, `Elastic.Ingest.*`/`Channels` 0.14.0, ECS 9.0.0, `Elastic.Esql` 0.11.0. Nenhum client ou sink 8.x restante; nenhum pacote pré-release. |
| `build` WebApi após a troca de versões | 0 erros, 1 aviso (CS0618 `Index()` → corrigido) |
| `test` Integration (fake) | 134/136 antes dos ajustes (bulk `_id`, delete ausente) → 136/136 |
| `test` Functional | 85/86 (nomes de template 9.0.0) → 96/96 com os testes novos |
| Mutação do teste de binding | Falhou como esperado com `TypeLoadException`; arquivos restaurados |
| `test` Elasticsearch (opt-in) | 1ª rodada 12/13 (`_refresh` com corpo rejeitado pelo 8.19; `\uXXXX` comparado com texto) → corrigido → 13/13 |
| `scripts/elastic-upgrade-rehearsal.ps1` | 38/41 → 49/49 → **49/49** (detalhes acima) |
| Smoke WebApi 9.x, Development, Elasticsearch recusando conexão | Sobe em 7,3 s; `/health` 503 "Elasticsearch is unreachable."; `get-person` 500 "Error retrieving person."; `GET /api/v2/product/get-paged-list` 200; uma linha `[elastic-log-sink]` em `stderr`; console contínuo |
| `.agents/skills/architecture-boundary-check/scripts/check-boundaries.ps1` | "Architecture boundary scan passed." |
| `docker run … elasticsearch:9.5.5 … command -v curl` | `/usr/bin/curl` presente (RHEL 9.8): o healthcheck do compose continua válido |

### Gate final

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-phase-validation.ps1
```

Execução de 10:11:58 a 10:18:17 (2026-10-11). Todos os passos terminaram com exit=0:

| Passo | Resultado |
|---|---|
| `01-restore` | OK (16 projetos) |
| `02-build-webapi` | OK, **0 avisos**, 0 erros |
| `03` Arquitetura | **22/22** |
| `04` Unitários | **77/77** |
| `05` Integração EF InMemory + adapters (fake) | **136/136** |
| `06` PostgreSQL 16.15 real | **5/5** |
| `06b` RabbitMQ 4.2.2 real | **17/17** |
| `06c` Elasticsearch **9.5.5** real + ensaio 8.19.23 → 9.5.5 | **13/13** (1 min 42 s) |
| `07` Funcionais | **96/96** (86 + 10) |
| `08` `list package --vulnerable --include-transitive` | Nenhum pacote vulnerável nos 16 projetos |
| `09` `docker compose config -q` | OK; também executado diretamente (exit 0), com as imagens 9.5.5 por digest e o volume `es9-data` |
| `10` Captura do OpenAPI | OK |
| `11` `graphify update . --force` | OK: 3.167 nós, 6.616 arestas e 187 comunidades. Rodado de novo depois deste relatório e do `AGENTS.md`: 3.199 nós, 6.662 arestas e 192 comunidades; `git diff --check` exit 0 em seguida. |
| `12` `git diff --check` | exit 0; só avisos de normalização LF/CRLF |

O passo opcional `06d-elastic-upgrade-rehearsal` (novo, ativado por `-ElasticRehearsalWebApi8xPath`/`-ElasticRehearsalWebApi9xPath`) **não** foi incluído no gate final, para não criar um quarto conjunto de recursos `p11-*`. O script foi executado à parte, com o resultado registrado acima.

### Comparação do OpenAPI com `docs/refactoring/baseline-phase-6/`

- `openapi-v1.json` e `openapi-v2.json`: **idênticos**. Com as quebras de linha normalizadas, o SHA-256 é igual ao do baseline (`521a4ff0…` e `75127aba…`), e a comparação JSON estrutural é igual. O baseline não foi alterado.
- `health.json`: igual ao baseline, exceto `totalDuration`. No ambiente Test não há entrada Elasticsearch.

### Contagens finais

Arquitetura 22, unitários 77, integração 136, PostgreSQL 5, RabbitMQ 17, Elasticsearch real 13 e funcionais 96, mais 49 checks do script de ensaio.

## Gates não executados

- **Migração de dados reais:** o volume `cleanarchitecture_v01` (8.17.4, de outro nome de projeto compose) não foi lido nem migrado. Falta autorização explícita, e a compatibilidade dos dados reais **não foi verificada**.
- **`docker compose up` completo** e `dotnet run` contra o compose: não executados, para não criar/alterar recursos do projeto do usuário. Imagens e healthcheck foram validados isoladamente.
- **Suíte de contrato do adapter 9.x contra o 8.x:** não aplicável. A matriz oficial diz que o client 9 não suporta servidor 8, e o teste comprova a rejeição (400). A linha de base 8.x é o resultado 11/11 da Fase 10 com o client 8.17.1.
- **TLS/autenticação reais:** não há no repositório (security desabilitada). Só o 401/403 do health check foi testado com fake.
- **VSIX:** fora do escopo.

## Riscos residuais

- **Dados reais não migrados nem ensaiados:** `cleanarchitecture_v01` não foi lido. Antes de migrar, é preciso autorização explícita e o procedimento abaixo.
- **Binding sink × transport:** o sink 9.0.0 foi compilado contra `Elastic.Transport` 0.10 e roda com 1.1.0. O teste de JIT cobre métodos não genéricos; instanciações genéricas só são exercitadas pelos testes de bootstrap/export reais. Uma versão futura do sink pode exigir revisão.
- **LogsDB** nos novos backing indices de `logs-*-*`: muda armazenamento e ordenação dos logs (source armazenado na licença basic).
- **Templates do sink acumulam:** `logs-api-example-8.11.0` e os `ecs_8.11.0_*` migrados continuam no cluster (prioridade menor). A remoção fica para depois de os backing indices antigos expirarem, e só com autorização.
- **Regra de data do par 8.19.23 → 9.5.5:** lançados juntos; ver a interpretação acima.
- **`apm-source-map`:** template do APM do Kibana 8.17 copiado como "próprio" pela heurística do script; inofensivo no ensaio, mas numa migração real convém revisar a lista antes de copiar.
- **Herdados da Fase 10:** sink duplicado vindo de `Serilog:WriteTo` (Fase 13), sem autenticação/TLS, listagem limitada a 10 hits, delete de ausente 500 no v1.
- **Compose:** o serviço passa a usar o volume novo `es9-data`. Um `docker compose up` sobe um Elasticsearch 9.5.5 **vazio**, e os dados 8.17.4 continuam em `v01` até uma migração autorizada. `docker compose up` completo não foi executado.

## Procedimento de rollback (executável)

O rollback nunca monta dados atualizados com um binário mais antigo.

1. **Aplicação:** reverter de forma coerente `Directory.Packages.props` (client 8.17.1, sink 8.12.3), as 3 mudanças do adapter, os testes e o compose (imagens 8.17.4 e volume `v01`). Antes do commit, `git diff` mostra exatamente essas mudanças; depois, basta reverter o commit da fase. O client 9 **não** fala com o servidor 8.x, então trocar só a URL não basta.
2. **Conexão:** apontar `PersonSearchSettings:Url`, `ElasticLoggingSettings:Url` e `ElasticsearchHealthCheckSettings:Url` (ou o legado `ElasticSearchSettings:Url`) para o cluster 8.x preservado. No compose, o serviço `elasticsearch` volta a montar `v01` (nunca tocado por 9.x).
3. **Dados:** se o 8.x de origem tiver sido perdido, restaurar S0 (tirado no 8.17.4 antes de qualquer upgrade) num cluster 8.17.4 **novo**. Escritas feitas só no 9.x depois do corte não voltam automaticamente; reexportá-las para o 8.x é decisão operacional.
4. **Alternativa parcial sem rollback de dados:** o client 8.x é compatível com servidor 9.x (matriz oficial), então reverter só o código mantendo o 9.5.5 também funciona; reverter só o servidor não funciona.
5. **Ensaiado:** a WebApi 8.x publicada voltou a servir os dados originais da fonte 8.17.4 intacta (ensaio, passo 7).

### Migração dos dados reais (requer autorização explícita; não executada)

1. Adicionar `path.repo` e um diretório/volume de snapshot ao 8.17.4 atual, e criar um snapshot de `persons`, `logs-api-example-*` e do feature state `kibana`, com `include_global_state:false`.
2. Restaurar num 8.19.23 **novo**; rodar `_migration/deprecations`, `_migration/system_features` e o Upgrade Assistant; resolver itens críticos.
3. Fazer o snapshot do 8.19.23 e restaurar no 9.5.5 do compose (`es9-data`), copiando antes os templates próprios.
4. Validar contagens, documentos e consultas como no script.
5. Manter `v01` e os snapshots até a aceitação.

O script de ensaio é o roteiro executável (trocar o seed sintético pelo snapshot real).

## Confirmações

- O contrato HTTP e o OpenAPI v1/v2 foram preservados (comparação acima). Rotas, status, textos e health inalterados.
- Application e Domain continuam sem pacotes e tipos Elastic; o port da Fase 10 não mudou; nenhuma exceção de arquitetura nova.
- **Nenhum dado 8.x, volume ou snapshot foi apagado:**
  - `cleanarchitecture_v01` e os containers `elasticsearch`/`kibana` do compose não foram tocados;
  - os recursos do ensaio (`p11-*`: 18 containers parados, 9 volumes, 3 redes, snapshots em `artifacts/validation/elastic-rehearsal/*/snapshots`) foram **mantidos**;
  - a limpeza exige autorização (comando impresso pelo script).
- Nenhum cluster de produção foi trocado.
- **Nenhum trabalho das Fases 12+ foi iniciado:** sem mudanças em identidade/JWT, localização, OpenTelemetry, Redis, RabbitMQ, PostgreSQL ou VSIX.
