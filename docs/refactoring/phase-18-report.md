# Fase 18 — Atualização do Redis Insight como ferramenta descartável

Data de execução e verificação: 2026-10-12 (America/Sao_Paulo). Checkout autoritativo `C:\Users\alexa\source\repos\TemplateCleanArchNET`, branch `refactor/phase-18-redis-insight` criada a partir de `main` em `2725c8e` (merge da Fase 17), árvore limpa antes das edições.

## 1. Resumo executivo

- Redis Insight atualizado de **1.14.0** (`redislabs/redisinsight`, linha v1 aposentada em 2023-04-30) para **3.8.0** (`redis/redisinsight:3.8.0@sha256:b5e19ee240abef6edb435871b90ff8a210995422e8e018ab61c0339d318a1f84`, imagem oficial atual, último release GA, publicado em 2026-07-21).
- O alvo usa um **volume novo** `redis-insight-data` (`/data`); o volume legado `redis-insight-volume_data` (`/db`, formato do 1.x) continua declarado, **não montado**, e é selecionado pelo novo overlay `docker-compose.redis-insight-rollback.yml`.
- **Decisão de preservação: recriação limpa.** O volume do usuário tinha 0 conexões salvas na leitura somente leitura da Fase 15; o usuário respondeu que o estado legado pode ser recriado. A única conexão (`template-redis8` → `redis:6379`, db 0, sem TLS, usuário `default`) é recriada a cada start a partir de `RI_REDIS_*`; a senha vem de `REDIS_PASSWORD` em runtime (nada em código) e fica **cifrada** no volume com `REDIS_INSIGHT_ENCRYPTION_KEY`.
- O Insight é ferramenta descartável: nenhum serviço depende dele, a WebApi não tem configuração dele, readiness não o considera e a UI agora é publicada só em **loopback**.
- Novo ensaio `scripts/redis-insight-upgrade-rehearsal.ps1`: **67/67** (gate, run `20261012173231`). Baseline do stack completo **98/98** (inclui Insight parado/ausente) e rollback em stack completo para o 1.14.0 **95/95**. Smoke test real da UI no navegador embutido (dados sintéticos).
- Nenhum código de aplicação, projeto, pacote NuGet, rota, health ou OpenAPI mudou. Nenhum volume, configuração salva ou dado Redis foi apagado. Fase 19 não iniciada.

## 2. Pré-requisitos e checkout verificados

| Item | Evidência |
|---|---|
| Fase 15 | `phase-15-report.md` tem resultados reais (ensaio 60/60, rollback 73/73, suíte Redis real 23/23, gate verde). Compose atual: um serviço `redis` `redis:8.10.2@sha256:2c2dff79…` em `redis8-data`, `--requirepass ${REDIS_PASSWORD}`, RDB, `noeviction`, health `redis-cli ping`; sem `redis-search`/`redis-timeseries`/`redis-graph`; Insight `depends_on: [redis]`; overlay `docker-compose.redis-rollback.yml` presente. Revalidado nesta fase: suíte Redis 23/23 (reexecução), baseline com runtime 8.10.2 e Search/Time Series integrados, check `redis.no-removed-service-references`. |
| Fase 14 | Pins `versão@sha256`, baseline isolado, overlays de rollback por serviço — convenções seguidas. |
| Fases 16 e 17 | Relatórios presentes; PostgreSQL 16.15 e RabbitMQ 4.3.6 com overlays de rollback inalterados (baseline confirma `postgres.expected-image`, `rabbitmq.*`). |
| Checkout | `main` == `origin/main` (`2725c8e`), `git status` limpo; branch nova criada antes das edições. |

## 3. Inventário pré-mudança (sanitizado)

### Serviço `redis-insight` (antes)

| Atributo | Valor |
|---|---|
| Imagem | `redislabs/redisinsight:1.14.0@sha256:b03ab1426d0d861b0370e05ee670a8819c8ab36b987ca59e4a3e92e0038d089c` (namespace legado `redislabs`, oficial da Redis) |
| Runtime | RedisInsight 1.14.0, Python 3.11 (Django), `linux/amd64`, entrypoint `bash ./docker-entry.sh` + `python entry.pyc` |
| Usuário | `redisinsight` |
| Porta | 8001 no container; `${REDIS_INSIGHT_PORT}:8001` no override (todas as interfaces) |
| Volume | `redis-insight-volume_data:/db` (SQLite + log) |
| Ambiente | nenhum |
| Health | nenhum health check (endpoint `/healthcheck/` existe e responde `OK`) |
| Dependências | `depends_on: [redis]` (Fase 15) |
| Restart / rede | `restart: always`, `nw`, `container_name: redis-insight` |
| Dependentes | nenhum |
| Referências | `docker-compose*.yml`, `.env.example` (`REDIS_INSIGHT_PORT`), `scripts/compose-baseline.ps1` (major 1.x, probe Python de AUTH/PING, `service.redis-insight-running`), docs históricos. Nenhuma referência em `Src`/`Tests`. |

### Ambiente local do usuário

- Volume `cleanarchitecture_redis-insight-volume_data`: driver local, criado em 2026-01-07T02:01:31Z, 483,3 kB, nenhum container o usa, não compartilhado. **Não foi montado nesta fase** (a tentativa de cópia somente leitura foi negada pela política de permissões e o usuário não exigiu preservação). Evidência de conteúdo: leitura somente leitura da Fase 15 (2026-10-11, SQLite `immutable=1`): tabela `instances` com **0** linhas.
- Nenhum container normal do Insight; imagem 1.14.0 presente localmente por digest (repuxada por digest durante esta fase após a limpeza de imagens feita pelo usuário).
- Backups/exports do Insight: nenhum encontrado.

### Inventário de configurações salvas e classificação

| Estado | Classificação |
|---|---|
| Conexões salvas do usuário | nenhuma (0) — nada a migrar |
| Conexão necessária (Redis 8 da Fase 15) | **necessária para recriar** → recriada por `RI_REDIS_HOST/PORT/ALIAS` (metadados não secretos em Compose) + `RI_REDIS_PASSWORD=${REDIS_PASSWORD}` (segredo externo, `.env`) |
| Conexões para `redis-search`/`redis-timeseries`/`redis-graph` | **obsoletas** (serviços removidos na Fase 15) — não recriadas |
| Preferências de UI/telemetria do 1.x, queries, favoritos | opcionais/descartáveis (não há formato de migração para a 3.x) |
| Credenciais/certificados/SSH | nenhum no volume do usuário (0 conexões); no 1.x a API devolve a senha salva **em texto claro** (achado de segurança comprovado no ensaio) |

### Pergunta ao usuário

Foram apresentadas as opções (reaproveitar a evidência da Fase 15 vs. inventariar uma cópia) e o pedido de aceitar o EULA só no ensaio. Resposta: o usuário fez limpeza de imagens e pediu para usar o que existir e criar novo se necessário — interpretado como estado legado descartável/recriável e autorização para o ensaio. O volume legado permanece intacto para rollback.

## 4. Versões, imagens e fontes (verificado em 2026-10-12)

| Papel | Imagem | Versão | Porta | Dados | Usuário | Arquitetura |
|---|---|---|---|---|---|---|
| Origem / rollback | `redislabs/redisinsight:1.14.0@sha256:b03ab1426d0d861b0370e05ee670a8819c8ab36b987ca59e4a3e92e0038d089c` | 1.14.0 | 8001 (5540 com `RIPORT`) | `/db` | `redisinsight` | amd64 |
| **Alvo** | `redis/redisinsight:3.8.0@sha256:b5e19ee240abef6edb435871b90ff8a210995422e8e018ab61c0339d318a1f84` (índice; amd64 `sha256:0131987f…`, arm64 `sha256:c5727416…`) | **3.8.0** (Node 22.22.0, Alpine, build `815499a8`) | **5540** | **`/data`** | `node` (uid 1000) | amd64, arm64 |

Seleção: 3.8.0 é o release GA mais recente (notas oficiais de julho/2026; tags `3.8`, `3.8.0` e `latest` apontam para o mesmo digest; nenhum 3.8.x/3.10 publicado). Sem prerelease, sem tag flutuante. GitHub Security Advisories do repositório RedisInsight: nenhum aviso publicado. Suporte a Redis 8: a 3.8.0 traz suporte a tipos do Redis 8.8 e renomeia "Redis Query Engine" para "Redis Search"; comprovado empiricamente contra 8.10.2 (módulos `search`, `timeseries`, `ReJSON`, `bf`, `vectorset` reconhecidos).

Fontes oficiais:

- Notas de release: https://redis.io/docs/latest/develop/tools/insight/release-notes/ e https://redis.io/docs/latest/develop/tools/insight/release-notes/v.3.8.0/
- Docker: https://redis.io/docs/latest/operate/redisinsight/install/install-on-docker/ (porta 5540, volume em `/data`, uid 1000, health `GET /api/health/`)
- Configuração: https://redis.io/docs/latest/operate/redisinsight/configuration/ (`RI_REDIS_*`, `RI_ENCRYPTION_KEY`, `RI_ACCEPT_TERMS_AND_CONDITIONS`, `RI_DATABASE_MANAGEMENT`, `RI_PRE_SETUP_DATABASES_PATH`)
- Imagem e tags: https://hub.docker.com/r/redis/redisinsight (`docker buildx imagetools inspect redis/redisinsight:3.8.0`)
- Linha 1.x: https://redis.io/docs/latest/develop/tools/insight/release-notes/v1.14.0/ ("version 1.X was retired on April 30, 2023"; 1.14 adiciona exportação de conexões para migrar à v2); importação em lote: https://redis.io/docs/latest/develop/tools/insight/release-notes/v.2.16.0/
- Avisos de segurança: https://github.com/redis/RedisInsight/security/advisories (nenhum publicado)

Compatibilidade de dados e decisão:

- 1.x (Python/Django, SQLite em `/db`) e 3.x (Node/NestJS, SQLite em `/data`) têm esquemas diferentes; não há migração oficial de volume. O único caminho oficial é **exportar conexões no 1.14 e importar** na 2.x/3.x. A exportação do 1.14 chamada diretamente pela API (`POST /api/export-connections/`) respondeu 500 num volume sintético; não foi necessária porque não havia conexões reais. Volume legado **nunca** montado no alvo.
- Mudanças relevantes: porta 8001 → 5540; `/db` → `/data`; usuário → uid 1000; a 3.x só aplica conexões pré-configuradas e libera a navegação **depois do aceite do EULA** (padrão `RI_ACCEPT_TERMS_AND_CONDITIONS` não definido = não aceito); sem chave, senhas ficam sem cifra no volume (estratégias disponíveis `PLAIN`/`KEY`; keytar indisponível no container, erro de log esperado `libsecret-1.so.0`).

## 5. Mudanças de Compose e configuração

| Arquivo | Mudança |
|---|---|
| `docker-compose.yml` | `redis-insight` → imagem 3.8.0 por digest; `environment` com `RI_REDIS_HOST: redis`, `RI_REDIS_PORT: "6379"`, `RI_REDIS_ALIAS: template-redis8`, `RI_REDIS_PASSWORD: ${REDIS_PASSWORD}`, `RI_ENCRYPTION_KEY: ${REDIS_INSIGHT_ENCRYPTION_KEY:-}`; volume `redis-insight-data:/data`; health check `wget -q -O /dev/null http://127.0.0.1:5540/api/health/` (10s/5s/12/20s); `depends_on: [redis]` mantido; volume `redis-insight-data` declarado e `redis-insight-volume_data` mantido com comentário de rollback. |
| `docker-compose.override.yml` | `127.0.0.1:${REDIS_INSIGHT_PORT}:5540` (antes `${REDIS_INSIGHT_PORT}:8001` em todas as interfaces). |
| `docker-compose.redis-insight-rollback.yml` (novo) | volta para 1.14.0 por digest, `environment: !override {RIPORT: "5540"}` (remove `RI_*` e mantém o mapeamento de porta do override), `volumes: !override [redis-insight-volume_data:/db]`, health check Python em `/healthcheck/`. |
| `.env.example` | nova chave `REDIS_INSIGHT_ENCRYPTION_KEY=CHANGE_ME` (placeholder). |

Decisões: nenhum aceite automático de EULA nem `RI_ACCEPT_TERMS_AND_CONDITIONS` em Compose (o usuário aceita na UI; a conexão aparece em seguida); a chave de cifra é opcional (`:-`) para que um `.env` antigo continue subindo o stack — sem ela a senha fica sem cifra no volume de tooling (documentado). Gerenciamento de conexões continua habilitado. Nenhum serviço depende do Insight; serviços/portas `redis-graph`/`redis-search`/`redis-timeseries` não voltaram (`redis.no-removed-service-references` e `target.no-removed-redis-services` PASS). Servidor Redis, volume, ACL, persistência e conexão da aplicação **inalterados**.

## 6. Ensaio isolado (`scripts/redis-insight-upgrade-rehearsal.ps1`, gate `06n`)

Projeto `p18-<RunId>` = `docker-compose.yml` + `docker-compose.baseline.yml` + overlay gerado (git-ignorado) que publica o alvo em `127.0.0.1:15540` e adiciona o serviço `redis-insight-legacy` (imagem, `RIPORT` e volume lidos do overlay de rollback commitado, em `127.0.0.1:18001`) — alvo e legado nunca substituem o container um do outro. Só `redis`, `redis-insight` e `redis-insight-legacy` sobem. Credenciais sintéticas aleatórias num env não versionado.

| Identificador (run do gate `20261012173231`) | Valor |
|---|---|
| Containers | `p18-20261012173231-redis-1`, `-redis-insight-1` (alvo), `-redis-insight-legacy-1` |
| Volume alvo | `p18-20261012173231_redis-insight-data` (criado no run, só ele montado em `/data`) |
| Volume legado | `p18-20261012173231_redis-insight-volume_data` (`/db`) |
| Volume Redis | `p18-20261012173231_redis8-data` |
| Rede | `p18-20261012173231_nw` |

Dados sintéticos: 5 entradas de cache no layout do cliente (`template:product:v1:<id>`, hash `absexp/sldexp/data`, TTL 12 h), índice `idx:p18:product` (TEXT/NUMERIC/TAG) com 3 hashes, séries `p18:ts:raw` (labels, retenção, `DUPLICATE_POLICY LAST`) e `p18:ts:avg` com regra `avg 60000`.

Resultados (67/67; runs `a` e `b` falharam só por defeitos do script em PowerShell 5.1 — arrays de `ConvertFrom-Json`, `.Count` de `PSCustomObject` e corpo de erro em `ErrorDetails`; run `c` e o run do gate verdes):

| Grupo | Checks |
|---|---|
| Estático | imagem do Compose = pin; tag `3.8.0` no registry = digest pinado; manifest `linux/amd64`; volume novo apenas; `depends_on` só `redis`; nenhum serviço depende do Insight; sem serviços Redis removidos; overlay de rollback = 1.14.0 em `redis-insight-volume_data` com `RIPORT` |
| Legado | 1.14 saudável, conexão sintética salva (201) e inventariada por nome/host:porta; **a API 1.x expõe a senha salva** (registrado só como booleano); volume legado com digest de conteúdo após parar |
| Startup | healthy em **6 s** (limite 180 s); container com o image ID do pin; só `…_redis-insight-data:/data` montado; volume criado no run; usuário `node` |
| UI/API | `/api/health/` `{"status":"up"}`; `/api/info` `appVersion 3.8.0`, `DOCKER_ON_PREMISE`, estratégias `PLAIN,KEY`; `index.html` com `#root`; 4 assets JS 200 |
| EULA | antes do aceite: `agreements: null` e 0 conexões; aceite só no projeto isolado (analytics/notificações desligados, cifra ligada) + tema `DARK` |
| Conexão | exatamente 1: `template-redis8@redis:6379/db0`, sem TLS, `password: true` (valor nunca retornado); nenhuma conexão legada/grafo; `connect` 200; `STANDALONE`, 8.10.2; módulos `bf,ReJSON,search,timeseries,vectorset` (sem graph) |
| Inspeção (só metadados) | scan limitado `template:product:v1:*` (count 100) → 5 hashes com TTL > 0; `get-info` TTL/3 campos; índice listado, `num_docs` 3, 3 atributos; 2 chaves `TSDB-TYPE`, TTL −1, 2 amostras; overview 10 chaves; **snapshot (DBSIZE, `FT._LIST`, `DUMP` de todas as chaves) idêntico** antes/depois |
| Auth | senha errada → 424 `RedisConnectionUnauthorizedException` ("Failed to authenticate…"), valor não ecoado nem salvo |
| Restart | healthy; aceite, analytics=false e tema persistidos; mesma conexão (mesmo id) e reconecta |
| Segredos | senha e valor errado ausentes de todos os arquivos de `/data` (SQLite cifrado com a chave), dos logs do container e da pasta de evidências |
| Ausência | alvo parado, Redis running, `PING` PONG, snapshot idêntico; volume legado com o mesmo digest (alvo não escreveu nele) |
| Rollback | legado 1.14 saudável em **≈2 s** no volume intocado, UI 200, conexão sintética preservada, monta só `…_redis-insight-volume_data:/db`, dados Redis idênticos |
| Encerramento | `stop` de tudo; volumes retidos; volumes normais (`cleanarchitecture_*`) inalterados (nome + `CreatedAt`) |

### Smoke test de UI no navegador embutido

Sobre o alvo retido do run `20261012c` (dados sintéticos, sem credenciais na tela): a lista de bancos mostrou `template-redis8` (`redis:6379`); a aba **Browse** carregou com 10 chaves agrupadas em `p18` e `template` (scan 10/10); a aba **Search** listou `idx:p18:product` (prefixo `p18:search:product:`, TEXT/NUMERIC/TAG, 3 docs). Requisições de assets/API 200; único erro de console: `GET /api/databases/0/settings` 404 (sem configurações por banco salvas; benigno). Captura de tela não registrada (a ferramenta expirou ao renderizar); evidência por texto da página e rede. Containers parados em seguida.

## 7. Independência da aplicação e baseline do stack

`scripts/compose-baseline.ps1` (gate `06e`), projeto `p14-baseline-20261012172836`: **98/98**. Checks novos/ajustados: major `redis/redisinsight` 3.x; imagem/volume do alvo; chaves `RI_*` esperadas; `redis-insight.no-dependents`; `webapi-has-no-insight-setting`; `no-committed-credentials` (só interpolações `${…}`); probe AUTH/PING agora em Node (a imagem 3.x não tem Python) → `['+OK', '+PONG']`; `/api/health/` up; `/api/info` 3.8.0; **EULA não aceito automaticamente**; `service.redis-insight-healthy`; **Insight parado** → `/health/ready` 200 sem menção ao Insight, cache read-through 200, `redis-cli PING` PONG, depois volta healthy; **stack inteiro sobe sem o Insight** (todos os serviços exceto ele, `--wait`) e WebApi pronta; depois o Insight entra e tudo fica healthy.

Rollback em stack completo (`compose-baseline.ps1 -RedisInsightRollback`, gate `06o`), projeto `p18-rollback-20261012173324`: **95/95** (imagem 1.14.0, volume `redis-insight-volume_data:/db`, `/healthcheck/` OK, probe Python autentica no Redis 8, mesmos checks de independência).

## 8. Rollback

Procedimento (não toca dados Redis nem o volume novo):

```powershell
docker compose -f docker-compose.yml -f docker-compose.override.yml -f docker-compose.redis-insight-rollback.yml up -d redis-insight
```

1. O serviço `redis-insight` é recriado com 1.14.0 no volume `redis-insight-volume_data` (`/db`), escutando 5540 (`RIPORT`), mesmo mapeamento loopback.
2. Conexões salvas no 1.x continuam no volume legado (no ambiente do usuário: nenhuma); a conexão ao Redis 8 é recriada pela UI se necessário.
3. Para voltar ao alvo: o mesmo comando sem o overlay. Nunca montar `redis-insight-data` no 1.14 nem `redis-insight-volume_data` na 3.x.
4. Desfazer a mudança de código: `git revert` do commit desta fase.

Demonstrado no ensaio (seção 6) e no stack completo (seção 7). Redis e WebApi não dependem do serviço, então a troca não os afeta.

## 9. Gate de validação

Comando: `powershell -ExecutionPolicy Bypass -File .\scripts\run-phase-validation.ps1 -ComposeBaseline -RedisInsightRehearsal` (SDK 10.0.401), 2026-10-12 17:22–17:38, sequencial.

| Passo | Resultado |
|---|---|
| 01 restore | exit 0 |
| 02 build WebApi | exit 0, 0 avisos, 0 erros |
| 03 arquitetura | 30/30 |
| 04 unit | 107/107 |
| 05 integration | 141/141 |
| 06 PostgreSQL | 5/5 |
| 06b RabbitMQ | 19/19 |
| 06c Elasticsearch | 13/13 |
| 06f Redis 8 real | **13/23 no gate (falha ambiental)**; reexecução isolada logo após o gate: **23/23** |
| 06e Compose baseline | 98/98 (`p14-baseline-20261012172836`) |
| 06n ensaio do Insight | 67/67 (`p18-20261012173231`) |
| 06o rollback do Insight (stack completo) | 95/95 (`p18-rollback-20261012173324`) |
| 07 functional | 191/191 |
| 08 vulnerabilidades (diretas + transitivas) | nenhum pacote vulnerável (17 projetos) |
| 09 `docker compose config -q` | exit 0 |
| 10 OpenAPI | v1/v2 idênticos a `docs/refactoring/baseline-phase-6` normalizando CRLF (bytes diferem só no fim de linha, como nas fases anteriores) |
| 11 Graphify | 3 978 nós, 8 502 arestas, 230 comunidades; `graphify-out/` é git-ignorado, então nenhum arquivo versionado mudou (regeneração feita pelo gate; nenhuma mudança estrutural de código exigia) |
| 12 `git diff --check` | exit 0 |

Falha do `06f` no gate: as 13 falhas foram todas da classe `ProductCacheRedisTests`, com `InvalidOperationException: Could not find resource 'DockerContainer'` depois que o container *resource reaper* (Ryuk) do Testcontainers não ficou pronto na espera por log (`UntilMessageIsLogged`) — as outras 10 da mesma suíte passaram. Nenhum arquivo de teste/projeto mudou nesta fase; a reexecução isolada (`artifacts/validation/06f-test-redis-rerun.log`) passou 23/23. Registrado como instabilidade ambiental, não como gate verde do run.

Descoberta de testes: contagens idênticas à Fase 17 (30, 107, 141, 5, 19, 13, 23, 191; total 529). Nenhum `.csproj`, `Directory.Packages.props`, `Src/` ou `Tests/` alterado (`git diff --stat` vazio nesses caminhos); nenhum pacote NuGet adicionado.

## 10. Testes e scripts adicionados/atualizados

- **Novo** `scripts/redis-insight-upgrade-rehearsal.ps1` (67 checks; seção 6).
- `scripts/compose-baseline.ps1`: topologia/independência do Insight, probe em Node, modo `-RedisInsightRollback` (projeto `p18-rollback-<RunId>`).
- `scripts/run-phase-validation.ps1`: `-RedisInsightRehearsal` (`06n` ensaio, `06o` rollback em stack completo).
- Nenhum teste .NET mudou (a fase não toca código de aplicação).

## 11. Segurança

- Nenhuma senha, chave, token, connection string ou export com segredo foi commitado ou impresso; `RI_REDIS_PASSWORD`/`RI_ENCRYPTION_KEY` só como `${VARIÁVEL}` (check automático). Evidências sem segredos (check automático); envs sintéticos ficam em `artifacts/validation/**` (git-ignorado).
- `docker compose config` renderiza a senha do Redis no ambiente do Insight, como já acontecia com a connection string da WebApi — mesmo modelo de segredo via `.env`.
- UI administrativa sem autenticação: publicada agora só em `127.0.0.1` (antes em todas as interfaces). Telemetria não habilitada; EULA nunca aceito automaticamente fora do ensaio.
- A linha 1.x devolve senhas salvas em texto claro pela API — motivo adicional para não usar o rollback além do necessário.

## 12. Recursos retidos (exclusão só com autorização explícita)

- Do usuário, intocados: `cleanarchitecture_redis-insight-volume_data` (`CreatedAt` 2026-01-07T02:01:31Z inalterado) e todos os demais volumes `cleanarchitecture_*`.
- Ensaio: projetos `p18-20261012{a,b,c,173231}` (containers `-redis-1`, `-redis-insight-1`, `-redis-insight-legacy-1`; volumes `_redis8-data`, `_redis-insight-data`, `_redis-insight-volume_data`; redes `_nw`); evidências em `artifacts/validation/redis-insight-rehearsal/<RunId>/`.
- Experimento inicial (descartável, sintético): containers `p18-probe-redis`, `p18-probe-ri`, `p18-probe-legacy`, `p18-probe-legacy-port`; volumes `p18-probe-ri-data`, `p18-probe-legacy-db` (+ volume anônimo do 1.14); rede `p18-probe-nw`.
- Baseline `p14-baseline-20261012172836` e rollback `p18-rollback-20261012173324` (containers parados, volumes, redes, imagem `<projeto>-webapi`).
- Imagens: `redis/redisinsight:3.8.0` (alvo) e `redislabs/redisinsight:1.14.0` (rollback) por digest.
- Todos os containers terminaram parados. A limpeza do volume legado do usuário e dos recursos de ensaio exige autorização separada; depois de apagar o volume legado, o rollback com conexões 1.x deixa de ser possível (hoje não há nenhuma).

## 13. Riscos residuais e limitações

- O conteúdo atual do volume legado do usuário não foi relido nesta fase (permissão negada para montá-lo); vale a evidência de 0 conexões de 2026-10-11 e o fato de não haver container que o use desde então.
- A exportação 1.14 → importação 3.x não foi demonstrada (endpoint 500 via API); irrelevante com 0 conexões.
- Sem `REDIS_INSIGHT_ENCRYPTION_KEY` no `.env` do usuário, a senha fica sem cifra no volume de tooling; trocar ou remover a chave depois torna a senha salva ilegível (a conexão pré-configurada é recriada a partir do ambiente no próximo start).
- O Insight usa o usuário `default` do Redis (privilégio total), igual à aplicação; a regra ACL mínima validada na Fase 15 não foi adotada (fora de escopo).
- `docker compose up` do ambiente normal com override/HTTPS não foi executado (igual às fases anteriores).
- Instabilidade do resource reaper do Testcontainers observada uma vez na suíte Redis.

## 14. Gates não executados ou bloqueados

- Inventário do conteúdo atual do volume legado do usuário (montagem negada; não exigido pelo usuário).
- Ambiente normal (`cleanarchitecture`) não iniciado nem alterado.
- VSIX (não-gate conhecido).

## 15. Confirmações

- Nenhum volume, configuração salva, conexão, dado Redis (chave, índice, série, TTL, ACL) ou estado material de tooling foi apagado ou sobrescrito; o volume legado nunca foi montado no alvo.
- Código de aplicação, pacotes, contratos HTTP, health/readiness e OpenAPI inalterados.
- Nenhum trabalho da Fase 19 ou posterior foi implementado.
