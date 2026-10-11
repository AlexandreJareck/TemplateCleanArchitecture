# Fase 15 — Consolidação do Redis no Redis 8

Data de execução e verificação: 2026-10-11 (America/Sao_Paulo). Checkout: `C:\Users\alexa\source\repos\TemplateCleanArchNET`, branch `refactor/phase-14-compose-baseline` em `836dd3e` (commit da Fase 14), árvore limpa antes das edições. Nada foi commitado, enviado ou mergeado.

## 1. Resumo executivo

- Os serviços `redis` (8.4.0), `redis-search` (RediSearch 2.6.13/Redis 6.2), `redis-timeseries` (RedisTimeSeries 1.6.19/Redis 6.2) e `redis-graph` (RedisGraph 2.10.16/Redis 6.2) foram consolidados em **um** serviço `redis` com `redis:8.10.2@sha256:2c2dff79…` (Search, Time Series, JSON, Bloom e Vector Sets integrados, verificados em runtime), num **volume novo** `redis8-data`.
- RedisGraph removido do Compose padrão depois de revalidar: nenhuma referência no repositório, nenhum dado de grafo no ambiente de ensaio, nenhum container de grafo no ambiente normal e **zero** conexões salvas no Redis Insight do usuário.
- Nenhum código da aplicação usa Search ou Time Series; o único consumidor de Redis continua sendo o cache de produtos da Fase 8. Chave, serialização, TTL e invalidação ficaram **inalterados**.
- Ensaio isolado de migração com **dados sintéticos** (RDB copiado para o volume novo + `MIGRATE … COPY` dos servidores de módulo + `FT.CREATE`/`TS.CREATERULE` recriados): **60/60** checks. Rollback demonstrado religando o ambiente de origem intocado (overlay `docker-compose.redis-rollback.yml`): stack completo **73/73**.
- Nova suíte opt-in `Tests/Template.Redis.Integration.Tests` contra Redis 8 real: **23/23**.
- Gate sequencial completo verde num único run (16:16–16:29). Contrato HTTP/OpenAPI inalterado. Nenhum pacote NuGet mudou. Nenhum volume ou dado do usuário foi lido ou apagado. Fase 16 não iniciada.
- **Compatibilidade com dados reais não verificada**: só dados sintéticos foram migrados.

## 2. Pré-requisitos verificados

| Fase | Evidência no código/relatórios |
|---|---|
| 8 | `IProductCache` em `Template.Application/Interfaces/Caching`; `DistributedProductCache`/`NoOpProductCache` e o pacote `Microsoft.Extensions.Caching.StackExchangeRedis` 10.0.12 só em `Template.Infrastructure.Caching`; chave `template:product:v1:{id}`, JSON web, TTL absoluto/deslizante, timeout, miss em falha; teste de arquitetura sem exceção para Application. |
| 13 | `/health/ready` classifica o cache como opcional/degradado; `LogEvents` estáveis (`CacheOperationFailed`, `CacheEntryUnreadable`); log sem payload; testes cross-cutting verdes (funcional 191/191). |
| 14 | `phase-14-report.md` tem resultados reais: baseline de volume limpo 65/65 e 67/67, health, restart não destrutivo, migrações/seeds idempotentes, pins `versão@sha256`, rollback. O Compose atual batia exatamente com o relatório (11 imagens pinadas, Redis 8.4.0 + três módulos legados, sem tags mutáveis). |
| Checkout | `git log`: `836dd3e` (Fase 14) sobre `d1a3ca0` (merge da Fase 13); árvore limpa; contém todas as fases anteriores. |

## 3. Inventário pré-mudança (sanitizado)

### Compose (Fase 14)

| Serviço | Imagem | Volume | Auth | Health | Portas (override) | Dependentes |
|---|---|---|---|---|---|---|
| redis | `redis:8.4.0@sha256:47200b04…` | `redis-volume-data:/data` | `--requirepass ${REDIS_PASSWORD}` | `redis-cli ping` PONG/NOAUTH | `${REDIS_PORT}:6379` | redis-insight |
| redis-search | `redislabs/redisearch:2.6.13@sha256:c0350c44…` | nenhum nomeado | **sem senha** | `redis-cli ping` | `${REDIS_SEARCH_PORT}:6379` | redis-insight |
| redis-timeseries | `redislabs/redistimeseries:1.6.19@sha256:5a15401e…` | nenhum nomeado | **sem senha** | `redis-cli ping` | `${REDIS_TIMESERIES_PORT}:6379` | redis-insight |
| redis-graph | `redislabs/redisgraph:2.10.16@sha256:bbf6c767…` | nenhum nomeado | **sem senha** | `redis-cli ping` | `${REDIS_GRAPH_PORT}:6379` | redis-insight |
| redis-insight | `redislabs/redisinsight:1.14.0@sha256:b03ab142…` | `redis-insight-volume_data:/db` | — | — | `${REDIS_INSIGHT_PORT}:8001` | — |

Todos na rede `nw`, `restart: always`. A WebApi não depende de nenhum Redis no `depends_on` (cache opcional); recebe `ConnectionStrings__RedisConnection=redis:6379,password=${REDIS_PASSWORD}`.

### Runtime (medido no ambiente de origem isolado, run `20261011162229`)

| Servidor | Versão | Módulos (`MODULE LIST`) | `save` | AOF | `maxmemory-policy` | Auth | Usuários ACL |
|---|---|---|---|---|---|---|---|
| redis | 8.4.0 | search 80402, timeseries 80400, ReJSON 80400, bf 80400, vectorset 1 | `3600 1 300 100 60 10000` | não | noeviction | sim | `default` |
| redis-search | 6.2.13 | search 20613, ReJSON 999999 | idem | não | noeviction | **não** | `default` |
| redis-timeseries | 6.2.7 | timeseries 10619 | idem | não | noeviction | **não** | `default` |
| redis-graph | 6.2.10 | graph 21016 | idem | não | noeviction | **não** | `default` |

Observação: o `redis:8.4.0` já trazia Search/Time Series integrados, então os três servidores de módulo eram redundantes. Banco lógico 0 apenas; TLS desligado; dados em `/data` (`dump.rdb`).

### Aplicação e referências

- Configuração: `ConnectionStrings:RedisConnection` (alvo host `redis`, porta 6379 no Compose), `CacheSettings` (`Enabled`, `AbsoluteExpiration` 10 min, `SlidingExpiration` 2 min, `OperationTimeout` 1 s). Teste: cache em memória; `Enabled=false` → `NoOpProductCache`.
- Cache: `HSET` com campos `absexp`, `sldexp`, `data` (JSON web v1: `id,name,price,barCode,createdDateTime`) + `EXPIRE`; leitura renova o TTL (deslizante) limitado ao absoluto; invalidação por `RemoveAsync` em `UpdateProductCommandHandler` e `DeleteProductCommandHandler`; payload ilegível → miss + remoção; falha/timeout → miss com log sem payload; só cancelamento do chamador propaga.
- Busca no repositório (código, testes, scripts, configs) por `GRAPH.*`, `FT.*`, `TS.*`, RediSearch, RedisTimeSeries, RedisGraph, NRedisStack e nomes/portas dos serviços: **nenhum uso de comando de módulo**. Referências apenas em `docker-compose*.yml`, `.env.example` (`REDIS_GRAPH_PORT`, `REDIS_SEARCH_PORT`, `REDIS_TIMESERIES_PORT`), `scripts/compose-baseline.ps1` (mapa de majors) e documentação histórica. Os termos "Search" de `Template.Infrastructure.Search` referem-se ao índice de pessoas no **Elasticsearch** (fora de escopo).
- Consumidores externos no escopo: o `.env` local do usuário contém as chaves `REDIS_GRAPH_PORT`, `REDIS_SEARCH_PORT`, `REDIS_TIMESERIES_PORT` (só nomes lidos; valores não lidos); agora são ignoradas. O Redis Insight do ambiente normal (`cleanarchitecture_redis-insight-volume_data`) foi lido **somente leitura** (montagem `:ro`, SQLite `immutable=1`, apenas a contagem/colunas host-porta): tabela `instances` com **0** linhas — nenhuma conexão salva.
- Ambiente normal do usuário: volumes `cleanarchitecture_redis-volume-data` e `cleanarchitecture_redis-insight-volume_data` (criados 2026-01-07), sem containers Redis existentes. Os **dados** desse volume não foram lidos (sem autorização para dados do usuário); por isso contagens/tipos reais não estão no relatório.
- Cobertura antes: adapter só contra backend em memória, fakes e Redis inalcançável; Compose smoke da Fase 14 (`/cache` + chave presente). Nenhum teste contra Redis real.

## 4. RedisGraph

| Evidência | Resultado |
|---|---|
| `GRAPH.*`, clientes/pacotes de grafo, DTOs, scripts, testes | nenhum |
| Variáveis/connection strings para o serviço de grafo | só `REDIS_GRAPH_PORT` (mapeamento de porta) |
| Conexões salvas no Redis Insight | 0 instâncias |
| Dados de grafo no ambiente de origem | `DBSIZE` 0, `GRAPH.LIST` vazio |
| Volume nomeado do serviço | nenhum (dados só no container) |
| Container de grafo do ambiente normal | inexistente |

Resultado: serviço `redis-graph`, sua porta, a dependência do Insight e `REDIS_GRAPH_PORT` removidos do Compose padrão e do `.env.example`. Imagem, overlay de rollback e containers de ensaio preservados. Nenhum banco de grafo substituto foi introduzido.

## 5. Versões, imagens e fontes (verificado em 2026-10-11)

| Papel | Referência | Versão runtime |
|---|---|---|
| Origem (principal) | `redis:8.4.0@sha256:47200b04138293fae39737e50878a238b13ec0781083126b1b0c63cf5d992e8d` | 8.4.0 |
| Origem (módulos) | `redislabs/redisearch:2.6.13@sha256:c0350c44…`, `redislabs/redistimeseries:1.6.19@sha256:5a15401e…`, `redislabs/redisgraph:2.10.16@sha256:bbf6c767…` | Redis 6.2.13 / 6.2.7 / 6.2.10 |
| **Alvo** | `redis:8.10.2@sha256:2c2dff791878316e3b083188be0d578423b24813cdda5de0e80ea4f7b3e6bfd6` (índice OCI; `8.10` aponta para o mesmo digest) | 8.10.2; search 81001, timeseries 81001, ReJSON 81000, bf 81001, vectorset 1 |
| Insight (inalterado) | `redislabs/redisinsight:1.14.0@sha256:b03ab142…` | 1.14.0 |

Seleção: 8.10.2 é o patch mais recente da linha estável mais nova (release GA 8.10, sem prerelease), publicado em 2026-09-17 com correções de segurança (ACL em `MULTI`, TimeSeries com RDB malformado, Search KNN, Vector Sets). Tags `8.10.3` e `8.12.0` inexistentes no Docker Hub na data.

Fontes oficiais:

- Releases do Redis Open Source: https://github.com/redis/redis/releases/tag/8.10.2 , https://github.com/redis/redis/releases/tag/8.10.0 , https://github.com/redis/redis/releases/tag/8.0.0
- Imagem oficial e tags: https://hub.docker.com/_/redis (manifest resolvido com `docker buildx imagetools inspect redis:8.10.2`)
- Módulos integrados / ciclo de vida: https://redis.io/docs/latest/operate/oss_and_stack/stack-with-enterprise/modules-lifecycle/ , https://redis.io/docs/latest/operate/oss_and_stack/stack-with-enterprise/deprecated-features/ , https://redis.io/docs/latest/develop/whats-new/8-10/ e notas do 8.0 ("Redis Query Engine and 8 new data structures are now an integral part of Redis 8 … included in all binary distributions")
- Persistência (backup/restauração por cópia de RDB): https://redis.io/docs/latest/operate/oss_and_stack/management/persistence/
- `MIGRATE`/`DUMP`/`RESTORE`: https://redis.io/docs/latest/commands/migrate/
- ACL: https://redis.io/docs/latest/operate/oss_and_stack/management/security/acl/ e `ACL DRYRUN`: https://redis.io/docs/latest/commands/acl-dryrun/
- Time Series `TS.CREATERULE`: https://redis.io/docs/latest/commands/ts.createrule/ ; Query Engine `FT.CREATE`: https://redis.io/docs/latest/commands/ft.create/
- RedisGraph EOL: https://redis.io/blog/redisgraph-eol/
- Cliente: `Microsoft.Extensions.Caching.StackExchangeRedis` 10.0.12 (StackExchange.Redis transitivo) — validado na prática pela suíte real; nenhum requisito de atualização encontrado.

Compatibilidade e mudanças relevantes:

- Redis 8.0: novas categorias ACL `@search`, `@json`, `@timeseries`, `@bloom`, `@cuckoo`, `@cms`, `@topk`, `@tdigest`, **e os comandos de módulo também entram em `@read`/`@write`** — regras amplas como `+@write` passam a liberar comandos de módulo; por isso a regra de privilégio mínimo validada nega `-@search -@timeseries -@json` explicitamente.
- Redis 8.10: novo encoding de hash compacto. Um RDB escrito pelo 8.10 **não deve** ser aberto por 8.4 — o rollback nunca reutiliza `redis8-data`.
- Caminho suportado: 8.4.0 → 8.10.2 por RDB (o 8.10.2 carrega RDB de versões anteriores; log `Loading RDB produced by version 8.4.0` + `Done loading RDB`); servidores 6.2 de módulo → 8.10.2 por `MIGRATE … COPY` (DUMP/RESTORE, preserva TTL, não altera a origem).
- **Achado do ensaio**: `RESTORE` de uma série temporal **não leva a regra de compactação** (`TS.INFO` sem `rules` no alvo); a regra deve ser recriada com `TS.CREATERULE`, e ela só compacta amostras novas (não faz backfill). Definições de índice Search também não são chaves: recriadas com `FT.CREATE`, que reindexa os hashes migrados.
- **Achado dos testes** (cliente, igual em 8.4 e 8.10): o `RedisCache` aplica TTL em **segundos inteiros** (`EXPIRE`, arredondado para cima) e cada leitura renova o TTL; expirações sub-segundo são aproximadas em até ~1 s. Não é regressão do Redis 8 e não muda a semântica da Fase 8 (valores reais são minutos).

## 6. Topologia alvo e configuração

| Item | Valor |
|---|---|
| Serviço | `redis` (nome mantido; WebApi e Insight continuam usando `redis:6379`) |
| Imagem | `redis:8.10.2@sha256:2c2dff79…` |
| Volume | `redis8-data:/data` (novo). `redis-volume-data` continua declarado, não montado, para rollback |
| Autenticação | usuário `default` com `--requirepass ${REDIS_PASSWORD}` (inalterado; sem ACL file) |
| Persistência | `--save 3600 1 300 100 60 10000` (padrão explicitado), `--appendonly no` |
| Eviction | `--maxmemory-policy noeviction` (sem `maxmemory`; o cache define TTL próprio) |
| Health | `redis-cli ping` → PONG/NOAUTH (sem senha no probe), 5s/5s/12 |
| Dependências | Insight `depends_on: [redis]`; WebApi continua **sem** dependência de Redis (fail-soft da Fase 13) |
| Portas | só `${REDIS_PORT}:6379` e `${REDIS_INSIGHT_PORT}:8001` no override |
| Chaves de config | inalteradas: `ConnectionStrings:RedisConnection`, `CacheSettings:*`, `REDIS_PASSWORD`, `REDIS_PORT`, `REDIS_INSIGHT_PORT` |

Privilégio mínimo: a aplicação continua no usuário `default` (introduzir `user=` exigiria nova variável/connection string — fora do escopo sem mudança de contrato). A regra mínima do cache foi **validada** (suíte real e `ACL DRYRUN` no ensaio) para adoção futura:
`on ~template:product:* &__Booksleeve_MasterChanged +@connection +@read +@write +@keyspace +@hash +@transaction +@scripting +info +subscribe +unsubscribe +client -@dangerous -@admin -@search -@timeseries -@json` (senha omitida).

## 7. Ensaio isolado de migração

Script: `scripts/redis8-migration-rehearsal.ps1` (gate `06g`). Dados **sintéticos**; credenciais aleatórias num env não versionado em `artifacts/validation/redis8-rehearsal/<RunId>/`.

| Identificador (run final `20261011162229`) | Valor |
|---|---|
| Projeto origem | `p15-src-20261011162229` (= `docker-compose.yml` + `docker-compose.baseline.yml` + `docker-compose.redis-rollback.yml`) |
| Containers origem | `p15-src-20261011162229-{redis,redis-search,redis-timeseries,redis-graph}-1` |
| Volume origem | `p15-src-20261011162229_redis-volume-data` |
| Projeto/container alvo | `p15-tgt-20261011162229`, `p15-tgt-20261011162229-redis-1` |
| Volume alvo | `p15-tgt-20261011162229_redis8-data` (novo, vazio antes da cópia) |
| Redes | `p15-src-20261011162229_nw`, `p15-tgt-20261011162229_nw` |
| Artefatos de recuperação (SHA-256) | `source-redis.rdb` `b0fbee41…6db2` (1447 B), `source-redis-search.rdb` `49d778cf…2e11` (735 B), `source-redis-timeseries.rdb` `78b4671c…f4ea` (644 B) |

Passos: (1) subir a origem e inventariar; (2) gerar dados sintéticos com os esquemas reais — 5 entradas de cache no layout do cliente (`HSET absexp/sldexp/data` + `PEXPIRE` ~12 h), índice `idx:p15:product` (TEXT SORTABLE, NUMERIC SORTABLE, TAG) com 5 hashes (1 com TTL), séries `raw` (retenção 1 ano, `DUPLICATE_POLICY LAST`, labels) e `avg` com regra `avg 60000`; (3) `SAVE` + cópia dos RDB; validação: `redis-check-rdb` OK (principal) e carga em servidor descartável da mesma imagem (`DBSIZE` 5 e 2); (4) `docker compose create` do alvo, cópia do RDB principal para o volume vazio, primeiro start; (5) `MIGRATE … COPY AUTH … KEYS …` dos servidores de módulo; (6) `FT.CREATE` e `TS.CREATERULE` no alvo; (7) paridade; (8) ACL; (9) restart; (10) rollback.

Resultados (60/60):

| Aspecto | Origem | Alvo (após migração e após restart) |
|---|---|---|
| Chaves | cache 5 (hash), search 5 (hash), series 2 (`TSDB-TYPE`) | idem; `DBSIZE` 12 |
| Valores | digest SHA-256 por chave | iguais (hash e `TS.RANGE`) |
| TTL | cache ~43 199 s; `p15:search:product:5` ~86 384 s; demais −1 | ≤ origem e dentro do tempo decorrido; sem TTL continua −1 |
| Search | `@category:{odd}` SORTBY price DESC → 3: 5,3,1; `widget` → 5; `@price:[15 35]` ASC → 2,3 | idênticos |
| Time Series | retention 31536000000, policy last, 4 amostras, regra→avg 1, labels via `TS.QUERYINDEX`, raw `10,30,50,70`, avg `20` | idênticos; regra ausente após `RESTORE` (0) e recriada; amostras novas compactadas (`avg` 150 no bucket novo) |
| Auth | — | sem `AUTH` → `NOAUTH`; só usuário `default` |
| ACL (`ACL DRYRUN`) | — | cache: HGETALL/HSET/EXPIRE/DEL OK; outro namespace, FLUSHALL, CONFIG SET, FT.SEARCH, TS.ADD negados. Leitor search: FT.SEARCH e TS.RANGE OK; TS.ADD e FT.DROPINDEX negados. Usuários de teste em memória somem no restart |
| Restart | — | `DB saved on disk` no stop; healthy; tudo igual |
| Isolamento | origem monta só `p15-src-…_redis-volume-data` | alvo monta só `p15-tgt-…_redis8-data` |
| Rollback | alvo parado; origem religada com `up --no-recreate` | origem 8.4.0, mesmo volume, cache/search/series iguais ao capturado |
| Usuário | volumes `cleanarchitecture_*`/`templatecleanarchnet_*` | inalterados (nome + `CreatedAt`) |

## 8. Testes adicionados/atualizados

- **Novo** `Tests/Template.Redis.Integration.Tests` (23 testes; Testcontainers genérico 4.16.0 já presente; Moq/Shouldly/xUnit já presentes; sem pacote novo): miss; round-trip com formato de hash/JSON v1; leitura de entrada no layout migrado; TTL no servidor; expiração absoluta; deslizante renovada e limitada pelo absoluto; deslizante ociosa; invalidação via handlers reais de update/delete; payload ilegível (2 casos) → miss + remoção sem payload no log; timeout (`CLIENT PAUSE`) → miss limitado; cancelamento propaga; senha errada → miss sem vazar segredo; comandos sem auth rejeitados; usuário de privilégio mínimo funciona e é negado no resto; categorias ACL de módulo; leitor search; módulos e versão em runtime; Search (índice, ordenação, limite, documento expirado sai do índice); Time Series (labels, retenção, duplicate policy, range, regra, `TS.MRANGE`); indisponibilidade inicial → miss → recuperação; queda → miss, restart preserva cache/índice/série. Classes executadas sequencialmente (servidores com portas fixas e `CLIENT PAUSE`); prontidão por comando (`redis-cli ping`), não por linha de log (sob WSL o log pode ter timestamp anterior ao start do container).
- `ArchitectureRulesTests.WebApi_Is_The_Only_Project_That_Composes_Infrastructure`: nova suíte adicionada à lista de projetos de teste de adapter (mesmo padrão de RabbitMQ/Elasticsearch).
- `scripts/compose-baseline.ps1`: checks da topologia Redis (imagem 8.10.2, volume `redis8-data`, sem serviços de módulo, Insight → só `redis`, nenhuma referência removida em Compose/`.env.example`/scripts/testes/src, versão runtime, Search/Time Series integrados, WebApi conectada ao alvo via `CLIENT LIST`, Insight alcança e autentica) e modo `-RedisRollback`.
- `scripts/run-phase-validation.ps1`: passo `06f-test-redis` (sempre) e `-RedisRehearsal` (`06g` ensaio, `06h` rollback em stack completo).
- Testes funcionais e de cache em memória inalterados (continuam determinísticos, sem Redis).

## 9. Cutover

Critérios go/no-go (todos atendidos antes de considerar o padrão aceito): suíte real 23/23; ensaio 60/60 sem erro inesperado de `redis-cli`; baseline do stack completo no Redis 8 com cache read-through, `/health/ready` 200 e OpenAPI igual; rollback em stack completo verde; volumes do usuário intocados.

Mudança única de configuração: `docker-compose.yml` (serviço `redis` → 8.10.2 em `redis8-data`, flags explícitas de persistência/eviction, serviços de módulo removidos, Insight → `redis`), `docker-compose.override.yml` (portas de módulo removidas), `.env.example` (três variáveis de porta removidas). Sem dual-write.

Última origem verificada: rollback do run `20261011162229` (8.4.0, dados iguais ao capturado). O ambiente normal `cleanarchitecture` não foi iniciado nem migrado.

## 10. Rollback

Procedimento exato (configuração, sem mexer no volume novo):

```powershell
docker compose -f docker-compose.yml -f docker-compose.override.yml -f docker-compose.redis-rollback.yml up -d
```

O overlay volta `redis` para `redis:8.4.0@sha256:47200b04…` com `redis-volume-data` e restaura `redis-search`, `redis-timeseries`, `redis-graph` e as dependências do Insight exatamente como na Fase 14 (portas dos módulos: readicionar ao override se necessário). Nunca montar `redis8-data` num servidor mais antigo. Para desfazer a mudança de código: `git revert` do commit desta fase.

Demonstrado: ensaio (seção 7) e stack completo `p15-rollback-20261011162510` — 73/73 (8.4.0 em runtime, volume de origem, três serviços legados presentes, WebApi conectada, cache read-through, Insight autentica).

## 11. Gate de validação

Comando: `powershell -ExecutionPolicy Bypass -File .\scripts\run-phase-validation.ps1 -ComposeBaseline -RedisRehearsal` (SDK `C:\Users\alexa\.dotnet\dotnet.exe` 10.0.401), 2026-10-11 16:16–16:29, sequencial.

| Passo | Resultado |
|---|---|
| 01 restore | exit 0 |
| 02 build WebApi | exit 0, 0 avisos, 0 erros |
| 03 arquitetura | 30/30 |
| 04 unit | 107/107 |
| 05 integration (EF InMemory + adapters) | 141/141 |
| 06 PostgreSQL | 5/5 |
| 06b RabbitMQ | 17/17 |
| 06c Elasticsearch (9.5.5) | 13/13 |
| 06f **Redis 8 real** | **23/23** |
| 06e Compose baseline (Redis 8) | 73/73 (`p14-baseline-20261011162004`) |
| 06g **ensaio de migração** | **60/60** (`p15-src/tgt-20261011162229`) |
| 06h **rollback (stack completo)** | **73/73** (`p15-rollback-20261011162510`) |
| 07 functional | 191/191 |
| 08 vulnerabilidades (diretas + transitivas) | nenhum pacote vulnerável (17 projetos) |
| 09 `docker compose config -q` | exit 0 |
| 10 OpenAPI | v1/v2 idênticos a `docs/refactoring/baseline-phase-6` normalizando CRLF (diferença só de fim de linha, como na Fase 14) |
| 11 Graphify | 3 851 nós, 8 326 arestas, 210 comunidades (novo projeto de teste = mudança estrutural) |
| 12 `git diff --check` | exit 0 |

Histórico honesto das execuções anteriores:

- Ensaio: runs `a`–`g` falharam por defeitos do próprio script (stdin não repassado; BOM injetado pelo Windows PowerShell 5.1; `--scan` enviado como comando; desempacotamento escalar do PowerShell; regra TS não migrada — virou achado e passo do procedimento; janela de TTL). Run `h` 60/60.
- Gate 1 (13:30): arquitetura 29/30 (lista de projetos), baseline 71/73 e rollback 72/73 (checks do próprio script). Corrigidos.
- Gate interrompido (13:42): suíte Redis travou — espera por log perdida por desvio de relógio; corrigido com prontidão por comando.
- Flakiness da suíte Redis rastreada até o arredondamento de TTL em segundos do cliente; janelas redimensionadas; 8/8 execuções seguidas verdes.
- Gate 2 (14:29): tudo verde exceto 1 teste de TTL (corrigido). Gate 3 (14:42): RabbitMQ, ensaio e rollback falharam com `all predefined address pools have been fully subnetted` (redes Docker esgotadas pelos projetos retidos). Com **autorização explícita do usuário**, foram removidas apenas 20 **redes** dos runs obsoletos (sem containers, volumes ou imagens). Gate final (16:16) verde.

## 12. Pacotes e imagens

- Imagens: `redis` 8.4.0 → 8.10.2 (por digest); `redislabs/redisearch`, `redislabs/redistimeseries`, `redislabs/redisgraph` fora do Compose padrão (só no overlay de rollback). Nenhuma outra imagem mudou (PostgreSQL, RabbitMQ, Elastic, Kibana, Insight, .NET iguais).
- NuGet: `Directory.Packages.props` sem alteração; nenhum pacote de produção mudou; cliente Redis inalterado (compatível com 8.10.2 conforme a suíte real).
- `.env` continua ignorado (`.gitignore:368`) e não rastreado; `.env.example` só com placeholders.
- Nenhuma tag flutuante de Redis restou (todas `versão@sha256`).

## 13. Riscos residuais e limitações

- **Migração com dados reais não verificada**: o volume `cleanarchitecture_redis-volume-data` não foi lido; a migração real (RDB 8.4.0 → volume novo) exige autorização. Como os servidores de módulo não tinham volume nomeado, não há dados persistentes de Search/Time Series/Graph a migrar no ambiente normal.
- Ao subir o Compose normal agora, o Redis começa vazio em `redis8-data` (cache frio, comportamento aceito pelo fail-soft).
- App ainda no usuário `default`; a regra de privilégio mínimo foi validada mas não adotada.
- O `docker compose up` normal com override/HTTPS não foi executado (igual à Fase 14).
- TTL do cliente em segundos inteiros (documentado acima).
- Redis Insight 1.14 (EOL) só foi verificado como alcançando/autenticando no Redis 8 por socket; conexões são configuradas pela UI (v1 não provisiona por ambiente). Upgrade é da Fase 18.
- O `.env` local ainda tem `REDIS_GRAPH_PORT`/`REDIS_SEARCH_PORT`/`REDIS_TIMESERIES_PORT` (agora ignoradas); não editado.

## 14. Gates não executados

- Migração do volume real do usuário (sem autorização).
- `docker compose up` do ambiente normal (protegido/fora de escopo).
- VSIX (não-gate conhecido).

## 15. Recursos retidos (exclusão só com aprovação explícita)

Containers parados e volumes (nenhum container em execução ao final):

- Ensaio: projetos `p15-src-20261011{a..h,133542,143540,144917,162229}` (containers `-{redis,redis-search,redis-timeseries,redis-graph}-1`, volume `_redis-volume-data`) e `p15-tgt-20261011{a..h,133542,143540,144917,162229}` (container `-redis-1` exceto `144917`; volume `_redis8-data`); RDB e evidências em `artifacts/validation/redis8-rehearsal/<RunId>/`.
- Baseline: `p14-baseline-20261011{133325,143328,144709,162004}`; rollback: `p15-rollback-20261011{133813,143805,145019,162510}` (`145019` só tem volumes); cada um com volumes `_es9-data`, `_postgres-data`, `_rabbitmq-lib`, `_rabbitmq-log`, `_redis-insight-volume_data` e `_redis8-data` ou `_redis-volume-data`; imagens `<projeto>-webapi`.
- Redes mantidas dos runs de referência: `p15-src/tgt-20261011{h,143540,162229}_nw`, `p14-baseline-20261011{143328,162004}_nw`, `p15-rollback-20261011{143805,162510}_nw`.
- Redes removidas com autorização (2026-10-11): `p15-src/tgt-20261011{a..g,133542}_nw`, `p15-src-20261011144917_nw`, `p14-baseline-20261011{133325,144709}_nw`, `p15-rollback-20261011133813_nw` (20 redes, vazias). Um container parado desses projetos volta com `docker compose up`, que recria a rede.
- Imagem `redis:8.10.2` puxada; imagens legadas preservadas.
- Do usuário, intocados: `cleanarchitecture_redis-volume-data`, `cleanarchitecture_redis-insight-volume_data` e todos os demais.

## 16. Confirmações

- Nenhum volume, RDB, chave, índice, série, grafo, backup, conexão salva ou outro dado do usuário foi apagado, sobrescrito ou montado; os servidores 8.x antigo e novo nunca compartilharam volume.
- Rotas, status, contratos, autenticação, health e OpenAPI inalterados.
- Nenhum trabalho da Fase 16 ou posterior foi implementado (PostgreSQL, RabbitMQ, Elastic, Insight, tooling e VSIX intocados).
