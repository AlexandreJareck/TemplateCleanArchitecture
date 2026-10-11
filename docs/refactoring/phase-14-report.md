# Fase 14 — Baseline reproduzível do Docker Compose

Data de execução e verificação: 2026-10-11 (America/Sao_Paulo). Checkout: `C:\Users\alexa\source\repos\TemplateCleanArchNET`, branch `main` em `d1a3ca0` (merge da Fase 13), árvore limpa antes das edições.

## 1. Resumo executivo

- Todas as 11 referências de imagem (9 serviços do Compose e 2 estágios do Dockerfile) agora têm **tag de versão explícita + digest imutável**. Nenhum `latest`, tag flutuante ou tag sem versão restou. **Nenhum major mudou**: cada imagem foi pinada no artefato que a tag mutável já resolvia localmente antes da fase (ou, no .NET, no digest atual da mesma linha 10.0, idêntico ao que `10.0` resolvia).
- Health checks adicionados/corrigidos em PostgreSQL (as duas bases), Elasticsearch, Kibana, RabbitMQ, Redis e nos três módulos Redis legados. A WebApi passa a esperar o PostgreSQL **healthy** (antes era `service_started`).
- Dois defeitos de wiring do Compose comprovados e corrigidos: chaves `RabbitMQ__*` (não lidas; a seção adotada é `RabbitMqSettings`, e a porta usava a porta do host) e ausência de `ConnectionStrings__RedisConnection` (a WebApi apontava para `localhost`). Um defeito do Dockerfile também: o `dotnet restore` falhava com NU1015 por não copiar `Directory.Packages.props`/`global.json` nem os csproj de Caching/Messaging/Search — **a imagem da WebApi não buildava antes desta fase**.
- Novo `scripts/compose-baseline.ps1` + `docker-compose.baseline.yml`: baseline isolado e não destrutivo (projeto único, volumes novos, credenciais sintéticas, nunca remove nada). Execução final verde: **65/65** checks (run `20261011d`) e **67/67** dentro do gate completo (run `20261011120235`, com duas checagens de ordem de start adicionadas).
- Contrato HTTP/OpenAPI inalterado (v1/v2 iguais byte a byte, normalizando CRLF, e estruturalmente a `baseline-phase-6`).
- Gate sequencial completo verde. Nenhum volume do usuário foi tocado; nada foi apagado. Fase 15 não iniciada.

## 2. Pré-requisitos e checkout autoritativo

| Verificação | Evidência |
|---|---|
| Fases 0–13 concluídas | Relatórios `phases-0-5`, `phases-6-7`, `phase-8` … `phase-13-report.md` presentes; `git log` mostra o merge da Fase 13 (#10) no topo de `main`. |
| Fase 1 (isolamento do host de teste) | Ambiente `Test` em `Program.cs` (cache em memória, sem broker/Elasticsearch); suíte funcional 191/191. |
| Fase 7 (persistência/PostgreSQL) | `Tests/Template.PostgreSql.Integration.Tests` 5/5 contra PostgreSQL 16.15 descartável. |
| Último relatório registra validação real | `phase-13-report.md` registra execuções e contagens. |
| Compose coerente com as fases | Elastic 9.5.5 por digest (Fase 11) já presente; divergências RabbitMQ/Redis encontradas e corrigidas (seção 6). |

## 3. Inventário pré-mudança

Cópias exatas preservadas (git-ignored) em `artifacts/validation/phase-14/pre-change/` (`docker-compose.yml`, `docker-compose.override.yml`, `Dockerfile`, `effective-config.placeholders.yml` renderizado com `.env.example`). Docker Engine 28.4.0, `amd64`, Compose v2.39.4. O image store local usa digests de índice (ID = digest).

| Serviço | Referência original | Resolvido localmente antes da fase | Versão real | Health / dependência original |
|---|---|---|---|---|
| db | `postgres:16` | `sha256:056b54f0…` | PostgreSQL 16.11 | sem healthcheck; WebApi `service_started` |
| elasticsearch | `…/elasticsearch:9.5.5@sha256:13b4a40b…` | idem | 9.5.5 | `curl -f`, 30s×5 |
| kibana | `…/kibana:9.5.5@sha256:13b72ce7…` | idem | 9.5.5 | sem healthcheck |
| rabbitmq | `rabbitmq:latest` | `sha256:18cc2597…` | 4.2.2 | `rabbitmq-diagnostics ping`, 30s×5 |
| redis | `redis:latest` | `sha256:47200b04…` | Redis 8.4.0 | sem healthcheck |
| redis-insight | `redislabs/redisinsight:1.14.0` | `sha256:b03ab142…` | 1.14.0 | sem healthcheck |
| redis-graph | `redislabs/redisgraph` | `sha256:bbf6c767…` | Redis 6.2.10 + RedisGraph 2.10.16 | sem healthcheck |
| redis-search | `redislabs/redisearch:latest` | `sha256:c0350c44…` | Redis 6.2.13 + RediSearch 2.6.13 (+ReJSON) | sem healthcheck |
| redis-timeseries | `redislabs/redistimeseries` | `sha256:5a15401e…` | Redis 6.2.7 + RedisTimeSeries 1.6.19 | sem healthcheck |
| Dockerfile base | `mcr.microsoft.com/dotnet/aspnet:10.0` | não presente localmente; registry → `sha256:222759b3…` | ASP.NET Core 10.0.12 | — |
| Dockerfile build | `mcr.microsoft.com/dotnet/sdk:10.0` | não presente localmente; registry → `sha256:e70cdb7f…` | SDK 10.0.401 | — |

Observação: a imagem antiga do compose-db `postgres:16` local é 16.11; a imagem 16.15 local é a dos testes Testcontainers. Versões de módulo lidas com `MODULE LIST` em containers efêmeros.

Outros itens inventariados: volumes nomeados (`postgres-data`, `v01` reservado, `es9-data`, `rabbitmq-lib`, `rabbitmq-log`, `redis-volume-data`, `redis-insight-volume_data`); rede `nw`; config `rabbitmq-plugins`; `restart: always` em db/rabbitmq/redis/insight/módulos; `container_name` fixo em sete serviços. `init-db.sh`: LF (`i/lf w/lf`, `eol=lf`), montado em `/docker-entrypoint-initdb.d/`, exige `POSTGRES_USER`/`POSTGRES_DB_NAME`/`POSTGRES_IDENTITY_DB_NAME`, identificadores com `format('%I')`, `ON_ERROR_STOP`, criação condicional (idempotente), sem credenciais. Migrações: `IdentityContext` (`InitializeIdentityDatabaseAsync`) e depois `ApplicationDbContext.MigrateAsync()` no startup; seeds de produtos (5 IDs fixos, só se a tabela estiver vazia), role `Admin` e usuário `Admin` (só se não houver usuários). Saúde: `/health` (legado), `/health/live`, `/health/ready` (bancos obrigatórios; cache, broker e Elasticsearch opcionais) da Fase 13.

## 4. Matriz de imagens (verificada em 2026-10-11)

| Serviço | Selecionado | Linha / major | Status oficial | Fase dona da atualização |
|---|---|---|---|---|
| db | `postgres:16.11@sha256:056b54f00419b49289227ab12d09df508543883f407fe9935a2cec430ef8aa8d` | 16 | 16 suportado até 2028-11-09; minor atual 16.15 | Fase 16 (patch 16.x) |
| elasticsearch | `…/elasticsearch:9.5.5@sha256:13b4a40bb095774f5fda635e45fd5677cc6a26565e42b24997b0c72b88e75984` | 9 | inalterado (Fase 11) | — |
| kibana | `…/kibana:9.5.5@sha256:13b72ce7222c36e4213229004ae62a87aeb32fd372ebfa6fb38f7a3285fdbb78` | 9 | inalterado (Fase 11) | — |
| rabbitmq | `rabbitmq:4.2.2@sha256:18cc2597e9b07bc29f5f991ca2836555245b4f94563773c19b662944f4b6d870` | 4.2 | série 4.2 listada; patch atual 4.2.9 | Fase 17 |
| redis | `redis:8.4.0@sha256:47200b04138293fae39737e50878a238b13ec0781083126b1b0c63cf5d992e8d` | 8 | `latest` já era 8 localmente | Fase 15 |
| redis-insight | `redislabs/redisinsight:1.14.0@sha256:b03ab1426d0d861b0370e05ee670a8819c8ab36b987ca59e4a3e92e0038d089c` | 1 | linha v1 EOL | Fase 18 |
| redis-graph | `redislabs/redisgraph:2.10.16@sha256:bbf6c767f0ba80606e7bacf9bcf75966e97838c40dd5b6b685091a0c1632bdc3` | 2.10 / Redis 6.2 | EOL | Fase 15 (remoção adotada) |
| redis-search | `redislabs/redisearch:2.6.13@sha256:c0350c445d0c27ad91ffe5a1aaee1a2db3eb911222ff549e9066985ea94500a7` | 2.6 / Redis 6.2 | EOL | Fase 15 |
| redis-timeseries | `redislabs/redistimeseries:1.6.19@sha256:5a15401e18bede614ca5d0e90c35ee3b51b27d43f8fe44922e34c5e35bc33b89` | 1.6 / Redis 6.2 | EOL | Fase 15 |
| Dockerfile base | `mcr.microsoft.com/dotnet/aspnet:10.0.12@sha256:222759b391a1aaf241166672c8f99b2d4ada452e7b5319f3c6e8f265a37b5ad4` | 10.0 | runtime atual 10.0.12 | — |
| Dockerfile build | `mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317` | 10.0 | SDK atual 10.0.401 (= `global.json`) | — |

Critério: onde trocar o patch seria upgrade com dono em fase posterior (PostgreSQL, RabbitMQ, Redis, módulos, Insight), foi pinado o artefato **exatamente** já resolvido localmente; no .NET, `10.0`/`10.0.12` e `10.0`/`10.0.401` apontavam para o mesmo digest, então o pin não muda bits. Proveniência: todas as referências foram resolvidas com `docker buildx imagetools inspect` nos registries oficiais (Docker Hub library/`redislabs`, `docker.elastic.co`, `mcr.microsoft.com`); para PostgreSQL, RabbitMQ e Redis o digest local original continua obtenível por referência imutável; as tags de versão dos módulos (`2.10.16`, `2.6.13`, `1.6.19`) apontam hoje para os mesmos digests. Todos os manifests incluem `linux/amd64` (arquitetura do host).

Fontes oficiais consultadas em 2026-10-11:

- PostgreSQL versioning policy: https://www.postgresql.org/support/versioning/
- RabbitMQ release information: https://www.rabbitmq.com/release-information
- .NET 10 downloads: https://dotnet.microsoft.com/en-us/download/dotnet/10.0
- Imagens oficiais: https://hub.docker.com/_/postgres , https://hub.docker.com/_/rabbitmq , https://hub.docker.com/_/redis , https://hub.docker.com/r/redislabs/redisinsight , https://hub.docker.com/r/redislabs/redisgraph , https://hub.docker.com/r/redislabs/redisearch , https://hub.docker.com/r/redislabs/redistimeseries , https://mcr.microsoft.com/en-us/artifact/mar/dotnet/aspnet , https://mcr.microsoft.com/en-us/artifact/mar/dotnet/sdk , https://www.docker.elastic.co/

**Nenhum major de serviço mudou** (checks `images.major-unchanged.*`, todos PASS).

## 5. Modelo de health e ordem de startup

| Serviço | "Healthy" significa | Probe (bounded) | Dependentes |
|---|---|---|---|
| db | as **duas** bases aceitam `SELECT 1` via TCP loopback (o servidor temporário do init só escuta socket Unix, então não passa antes do `init-db.sh` terminar); usa a regra trust do loopback, sem senha | 5s / 5s / 12 retries / start 30s | webapi (`service_healthy`, novo) |
| elasticsearch | `_cluster/health?wait_for_status=yellow&timeout=5s` | 10s / 10s / 12 / 30s | kibana, webapi (condição já existente) |
| kibana | `/api/status` com `"level":"available"` | 10s / 10s / 18 / 60s | — |
| rabbitmq | `rabbitmq-diagnostics -q ping` | 10s / 10s / 12 / 30s | webapi (condição já existente) |
| redis | `redis-cli ping` responde `PONG` ou `NOAUTH` (prova servidor ativo sem passar a senha) | 5s / 5s / 12 | — (opcional; cache faz miss) |
| redis-graph/search/timeseries | `redis-cli ping` | 5s / 5s / 12 | — |
| redis-insight | sem healthcheck (UI sem dependentes; mantido `depends_on` simples) | — | — |
| webapi | sem healthcheck de container (a imagem aspnet não tem curl/wget); prontidão verificada pelo script via `/health/ready` | — | — |

Decisões: nenhuma dependência opcional nova virou obrigatória (Redis continua fora do `depends_on`; Elasticsearch e RabbitMQ mantêm as condições `service_healthy` que já existiam). Probes são somente leitura, não criam filas/índices/bases. Ordem observada no gate: `redis > elasticsearch > redis-timeseries > redis-graph > redis-search > rabbitmq > db > redis-insight > kibana > webapi` (kibana e webapi só após os pré-requisitos healthy); sem nenhum erro de conexão ao banco no primeiro start.

## 6. Mudanças

| Arquivo | Mudança |
|---|---|
| `docker-compose.yml` | Pins tag+digest; health checks; `db` `service_healthy`; `init-db.sh` montado `:ro`; `RabbitMQ__*` → `RabbitMqSettings__HostName/Port(5672)/UserName/Password`; `ConnectionStrings__RedisConnection=redis:6379,password=${REDIS_PASSWORD}`. |
| `Src/Presentation/Template.WebApi/Dockerfile` | Pins `aspnet:10.0.12@…`/`sdk:10.0.401@…`; copia `global.json`, `Directory.Packages.props` e os csproj de Caching/Messaging/Search antes do restore (corrige NU1015). |
| `docker-compose.baseline.yml` (novo) | Overlay só para o baseline isolado: `container_name: !reset null`, WebApi HTTP em `127.0.0.1:${P14_WEBAPI_PORT}`, certificado padrão limpo (o Kestrel o carregava mesmo sem HTTPS e derrubava a WebApi). Não carrega `docker-compose.override.yml`. |
| `scripts/compose-baseline.ps1` (novo) | Baseline completo e não destrutivo (seção 8). |
| `scripts/run-phase-validation.ps1` | Passo opcional `06e-compose-baseline` (`-ComposeBaseline`). |

Nenhum arquivo .NET, csproj, pacote, migração, seed, rota ou contrato mudou.

## 7. Configuração efetiva (redigida)

`docker compose --env-file .env.example config -q` e a renderização do projeto isolado passam. Credenciais aparecem apenas como referências `${POSTGRES_PASSWORD}`, `${RABBITMQ_DEFAULT_PASS}`, `${REDIS_PASSWORD}`, `${ASPNETCORE_CERTIFICATE_PASSWORD}`. `.env` continua ignorado e não rastreado (`git check-ignore` → `.gitignore:368`); `.env.example` só tem placeholders. O baseline gera um env sintético aleatório em `artifacts/validation/compose-baseline/<RunId>/baseline.env` (git-ignored), nunca impresso.

## 8. Baseline isolado executado

Comando (executado):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\compose-baseline.ps1 -RunId <id>
```

| Run | Resultado | Observação |
|---|---|---|
| `20261011a` | FALHOU no build | `dotnet restore` NU1015 no Dockerfile (defeito pré-existente, corrigido). Containers ficaram em `Created`, nunca iniciados. |
| `20261011b` | 35/63 | WebApi saiu (139) com `DirectoryNotFoundException '/https/aspnetcore.pfx'` (certificado eager; corrigido no overlay) + bug do script ao enviar SQL por pipe (corrigido). Init, health, restart e isolamento já verdes. |
| `20261011c` | 61/65 | Consulta da Identity usava schema errado: o histórico EF da Identity fica em `public.__EFMigrationsHistory` (corrigido no script). |
| `20261011d` | **65/65** | Execução limpa completa. |
| `20261011120235` | **67/67** | Dentro do gate completo, com as checagens `startup.webapi-started-after-prerequisites` e `startup.webapi-no-database-connection-errors` e `create-index?indexName=persons` → 200 (no run `d`, sem `indexName`, o 400 é a validação documentada na Fase 10). |

Resultados do run final (`20261011120235`):

- **Estático:** config renderiza; 11/11 imagens `versão@sha256`; majors iguais; `init-db.sh`: `bash -n` dentro da imagem PostgreSQL pinada OK, sem `POSTGRES_DB_NAME` falha com `POSTGRES_DB_NAME must be set` (sem valores), sem credenciais embutidas, LF.
- **Startup limpo:** `up --build --wait` em 56 s, todos os serviços com healthcheck `healthy`, WebApi `running`; `/health/ready` 200.
- **PostgreSQL:** log mostra `running /docker-entrypoint-initdb.d/init-db.sh` + dois `CREATE DATABASE`; as duas bases existem e pertencem ao usuário configurado (`count=2`); reexecutar o `init-db.sh` no container em execução é idempotente (exit 0, ainda 2 bases).
- **Migrações:** aplicação `20250326224009_InitialPostgresMigration`, `20261010151729_ProductPriceDecimal` (2 = arquivos de migração); Identity `20250326223511_InitialPostgresMigration` (1). Sem cross-wiring (0 tabelas `Identity` na base de aplicação, 0 `Products` na Identity).
- **Seeds (run 1 = run 2 = após restart):** produtos 5 / 5 distintos (`80f099ae…`, `818d3815…`, `b3c87ba8…`, `c9770569…`, `e9e9743f…`); roles 1 (`ADMIN`); usuários 1 (`ADMIN`); user_roles 0 (o seed atual não associa o usuário à role — comportamento existente, não alterado).
- **Smoke:** `/health`, `/health/live`, `/health/ready` 200 (ready: bancos `required`, cache/broker/elasticsearch opcionais, todos Healthy); OpenAPI v1/v2 idênticos ao baseline; produto v1 `get-by-id` e v2 `{id}` e `get-paged-list` 200; POST v1 sem token 401; Redis: `/api/v2/product/{id}/cache` 200 e chave `template:product:v1:{id}` presente; Identity: `authenticate` v2 com o usuário seed 200 + JWT, senha errada 400; RabbitMQ: `create-order-direct` 200, filas duráveis + `*.dead_letter` declaradas, todas com 0 mensagens pendentes e 0 dead-lettered (consumidas e confirmadas); Elasticsearch: `create-index` 200, `add-person` 200, `get-person` 200 (dado sintético id 9014); sink de log: data stream `logs-*` existe; Kibana healthy; Redis Insight running.
- **Segundo startup da aplicação** (`restart webapi`): ready de novo; snapshot do banco idêntico; `Applying migration` 6 linhas no primeiro start e **0** no segundo.
- **Restart completo não destrutivo** (`stop` + `up --no-build --no-recreate --wait`): todos healthy de novo; log do db com `Skipping initialization` e sem rodar `init-db.sh`; dados PostgreSQL idênticos; pessoa 9014 persiste no Elasticsearch; filas duráveis persistem; produto v2 200.
- **Shutdown:** `docker compose stop`; todos `exited` (Elasticsearch 143 = SIGTERM do JVM; demais 0, WebApi 0 com `Application stopping` no log); volumes do projeto mantidos; volumes normais (`cleanarchitecture_*`) idênticos (nome + `CreatedAt`) antes e depois.

## 9. Gate de validação sequencial

Executado: `powershell -ExecutionPolicy Bypass -File .\scripts\run-phase-validation.ps1 -ComposeBaseline -SkipGraphify` (2026-10-11 11:59–12:06, SDK pinado `C:\Users\alexa\.dotnet\dotnet.exe` 10.0.401).

| Passo | Resultado |
|---|---|
| 01 restore | exit 0 |
| 02 build WebApi | exit 0, 0 avisos, 0 erros |
| 03 arquitetura | 30/30 |
| 04 unit | 107/107 |
| 05 integration (InMemory/adapters) | 141/141 |
| 06 PostgreSQL | 5/5 |
| 06b RabbitMQ (`rabbitmq:4.2.2`) | 17/17 |
| 06c Elasticsearch (9.5.5 + rehearsal 8.19.23→9.5.5) | 13/13 |
| 06e Compose baseline | 67/67 (`p14-baseline-20261011120235`) |
| 07 functional | 191/191 |
| 08 vulnerabilidades (diretas + transitivas) | nenhum pacote vulnerável (16 projetos) |
| 09 `docker compose config -q` | exit 0 |
| 10 OpenAPI | capturado; v1/v2 iguais a `baseline-phase-6` (normalizando CRLF) |
| 12 `git diff --check` | exit 0 |

Graphify (11) **não executado** de propósito: só mudaram Compose, Dockerfile, scripts e docs; nenhum projeto, referência ou código.

## 10. Riscos residuais e limitações

- PostgreSQL 16.11, RabbitMQ 4.2.2 e Redis 8.4.0 estão atrás dos patches atuais (16.15, 4.2.9); são pins de reprodutibilidade, a atualização é das Fases 16, 17 e 15.
- Módulos Redis 6.2 e Redis Insight 1.14 são EOL; preservados e pinados, sem remoção (Fases 15 e 18).
- A WebApi não tem healthcheck de container (a imagem aspnet não traz curl/wget); prontidão é provada por HTTP pelo script.
- O `docker compose up` normal (com `docker-compose.override.yml`, HTTPS e user-secrets do `APPDATA`) não foi executado; só o overlay isolado. Os volumes normais não foram usados.
- Persistência do Redis após restart não foi exigida (cache).
- O seed não cria vínculo usuário-role; registrado, não alterado.

## 11. Rollback

1. Reverter o commit desta fase (`git revert <commit>`), que restaura `docker-compose.yml`, `Dockerfile` e o script de validação e remove os dois arquivos novos. As referências originais também estão em `artifacts/validation/phase-14/pre-change/`.
2. Nenhum volume precisa ser apagado ou remontado: os pins são os mesmos artefatos (mesmos binários e formatos de dados) que as tags mutáveis resolviam; nenhum binário mais antigo abre dados gravados por um major mais novo (major igual em todos os serviços).
3. Observação: ao reverter, voltam também os defeitos corrigidos (build NU1015, `RabbitMQ__*`, Redis sem connection string).

## 12. Recursos isolados retidos (sem autorização para apagar)

Projetos Compose `p14-baseline-20261011a`, `…b`, `…c`, `…d`, `…20261011120235`: containers parados (`<projeto>-<serviço>-1`), rede `<projeto>_nw`, imagem `<projeto>-webapi` e volumes `<projeto>_es9-data`, `_postgres-data`, `_rabbitmq-lib`, `_rabbitmq-log`, `_redis-insight-volume_data`, `_redis-volume-data`. Também ficaram imagens puxadas (`sdk:10.0.401`) e containers efêmeros `--rm` de inspeção já removidos automaticamente.

Parar (não destrutivo):

```powershell
docker compose -p p14-baseline-<RunId> --env-file artifacts\validation\compose-baseline\<RunId>\baseline.env -f docker-compose.yml -f docker-compose.baseline.yml stop
```

Limpeza destrutiva (**opcional, exige aprovação explícita**): `docker compose -p p14-baseline-<RunId> … down --volumes` por projeto.

## 13. Gates não executados

- Graphify (motivo na seção 9).
- Validação do VSIX (não-gate conhecido).
- `docker compose up` do ambiente normal (fora do escopo e protegido).

## 14. Confirmações

- Nenhum volume, banco, fila, índice, snapshot ou dado do usuário foi apagado ou montado.
- Nenhum major de serviço, rota, status, contrato OpenAPI, comportamento de autenticação ou semântica de health mudou.
- Nenhum trabalho da Fase 15 ou posterior foi implementado.
