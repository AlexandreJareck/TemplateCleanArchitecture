# Fase 16 — Atualização independente do PostgreSQL (checkpoint PostgreSQL 16)

Data de execução e verificação: 2026-10-12 (America/Sao_Paulo). Checkout autoritativo `C:\Users\alexa\source\repos\TemplateCleanArchNET`, branch `main` em `fb453e0` (merge da Fase 15), árvore limpa antes das edições. Nada foi commitado, enviado ou mergeado durante a execução.

## 1. Resumo executivo

- **PostgreSQL 18 não autorizado e não executado.** Nenhuma imagem 18 foi baixada, iniciada ou configurada; nenhum diretório de dados 18 foi escrito.
- O PostgreSQL 16 foi atualizado de **16.11** (pin da Fase 14) para **16.15**, o minor 16 mais recente suportado (fonte oficial, 2026-10-12): `postgres:16.15@sha256:ca0bd484cb98bf4b24eb1010e73fb3fcbd6714d240fbc1a10eea5b7dbecb641d`. Mesmo formato em disco; nenhum dump/restore necessário para o minor update.
- Backup e restore dos **dois** bancos (aplicação e Identity) + objetos globais foram ensaiados num ambiente isolado novo com **dados sintéticos** (migrações e seeds da própria WebApi + escritas sintéticas via API): estrutura, ACLs, collation/locale, migrações, sequências e conteúdo (contagem + md5 por tabela) **idênticos** entre origem 16.11, restore 16.15 e cópia com patch in-place 16.15.
- Script novo `scripts/postgres16-patch-rehearsal.ps1`: **68/68** checks (run `20261012151701` dentro do gate; também `20261012b` antes). Rollback cronometrado para o 16.11 preservado: **3,8 s** (2,7 s no run `20261012b`) em condições sintéticas.
- Overlay `docker-compose.postgres-rollback.yml` (16.11) e `compose-baseline.ps1 -PostgresRollback`: stack completo **75/75** no 16.11; baseline padrão **75/75** no 16.15.
- Gate sequencial completo verde (15:09–15:23). Contrato HTTP/OpenAPI idêntico ao `baseline-phase-6`. Nenhum pacote NuGet mudou. Nenhuma migração EF criada. Nenhum volume, backup ou dado apagado; dados do usuário não foram lidos. Fase 17 não iniciada.
- **Compatibilidade com dados reais não verificada**: só dados sintéticos foram usados.

## 2. Pré-requisitos e checkout verificados

| Item | Evidência |
|---|---|
| Fase 7 | `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 explícito; migrações `20250326224009_InitialPostgresMigration` e `20261010151729_ProductPriceDecimal` (aplicação) e `20250326223511_InitialPostgresMigration` (Identity); paginação determinística `OrderBy(Created).ThenBy(Id)` em `ProductRepository`; `numeric(18,2)` para `Price`; suíte `Template.PostgreSql.Integration.Tests` (preflight decimal, transação, cancelamento) 5/5 no gate. |
| Fase 14 | `phase-14-report.md` com evidência real de volume limpo, dois bancos, migrações/seeds duas vezes, restart não destrutivo, persistência e rollback; Compose atual batia com o relatório (`postgres:16.11@sha256:056b54f0…`, `init-db.sh`, healthcheck TCP dos dois bancos, WebApi `service_healthy`). |
| Fase 15 | `phase-15-report.md` presente; Redis 8.10.2 em `redis8-data` e overlay `docker-compose.redis-rollback.yml` intocados; suíte Redis 23/23 no gate. |
| Fases 0–13 | Relatórios presentes; arquitetura 30/30, funcional 191/191 no gate. |
| Checkout | `main` em `fb453e0`, `git status` limpo antes das edições. |

## 3. Autorização do PostgreSQL 18

**Não autorizado e não executado.** Para uma eventual autorização futura o usuário precisa receber: origem 16.15 → alvo 18.x (minor atual 18.6 em 2026-10-12, a reverificar); métodos oficiais `pg_upgrade` (cópia, nunca `--link` sobre a única origem) ou `pg_dump`/`pg_restore` lógico (preferível aqui: bancos de ~8 MB); cópias/ambientes isolados novos; janela de write-freeze/cutover; só `plpgsql` instalado (sem bloqueio de extensão); collation libc `en_US.utf8` (o 18 em Debian trixie usa a mesma glibc 2.41, mas exige verificação); ponto de rollback = servidor/volume 16.15 intocado ou restore 16 verificado. Nada disso foi executado.

## 4. Inventário pré-mudança (sanitizado)

### Serviço `db` no Compose

| Atributo | Valor |
|---|---|
| Imagem | `postgres:16.11@sha256:056b54f00419b49289227ab12d09df508543883f407fe9935a2cec430ef8aa8d` (índice multi-arch; runtime linux/amd64, Debian 13.2, glibc 2.41-12) |
| Container | `my_postgres_db` (o overlay de baseline remove o nome fixo) |
| Ambiente (nomes) | `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB_NAME`, `POSTGRES_IDENTITY_DB_NAME` |
| Volume | `postgres-data:/var/lib/postgresql/data`; `./init-db.sh` em `/docker-entrypoint-initdb.d` (ro) |
| Healthcheck | `psql -h 127.0.0.1` `SELECT 1` nos dois bancos, 5 s/5 s/12 tentativas/start 30 s, sem senha (trust de loopback) |
| Rede / restart / portas | `nw` / `always` / `${POSTGRES_PORT}:5432` só no override |
| Dependentes | `webapi` (`condition: service_healthy`) |

`init-db.sh` cria, de forma idempotente (`CREATE DATABASE … WHERE NOT EXISTS … \gexec`), os bancos nomeados por `POSTGRES_DB_NAME` e `POSTGRES_IDENTITY_DB_NAME`, ambos de propriedade do usuário bootstrap. Conexões: `ConnectionStrings:DefaultConnection` e `ConnectionStrings:IdentityConnection` (`Host=db;Port=5432;Username=…;Password=…;Database=…;SearchPath=public`, valores não registrados). A WebApi aplica migrações e seeds na inicialização (`ApplicationDbContext` → `public`, `IdentityContext` → schema `Identity`). EF Core 10.0.12, Npgsql EF 10.0.3.

### Bancos (ambiente de origem 16.11 do ensaio, dados sintéticos)

| Atributo | Aplicação | Identity |
|---|---|---|
| Encoding / collate / ctype | UTF8 / `en_US.utf8` / `en_US.utf8` | idem |
| Provider / `datcollversion` / versão real | libc (`c`) / 2.41 / 2.41 | idem |
| TimeZone / DateStyle | `Etc/UTC` / `ISO, MDY` | idem |
| Extensões | `plpgsql 1.0` | `plpgsql 1.0` |
| Schemas | `public` (dono `pg_database_owner`) | `Identity` (dono = usuário bootstrap), `public` |
| Tabelas (linhas) | `Products` (6), `__EFMigrationsHistory` (2) | `Role` (1), `User` (1), `UserRoles` (0), `RoleClaims`/`UserClaims`/`UserLogins`/`UserTokens` (0), `__EFMigrationsHistory` (1) |
| Índices / constraints (ambos) | 17 índices / 16 constraints no total | |
| Sequências | — | `RoleClaims_Id_seq`, `UserClaims_Id_seq` (não usadas) |
| Collations de coluna | só `default` | só `default` |
| Triggers/funções/tipos/colunas geradas/large objects de usuário | 0 | 0 |
| Roles / default ACL | 1 role não-sistema (bootstrap, superuser), 0 default ACL; todas as tabelas pertencem ao usuário bootstrap, sem grants a terceiros | idem |
| Tamanho | 7,6 MB | 7,9 MB |

Observação herdada (não alterada): o seed cria 1 usuário e 1 role, mas nenhuma linha em `UserRoles`.

### Ambiente local in-scope

- Volume normal `cleanarchitecture_postgres-data` (dados do usuário): **não montado, não lido**; `CreatedAt` comparado antes/depois de cada ensaio (inalterado).
- Volumes isolados preexistentes `p14-baseline-*_postgres-data` e `p15-rollback-*_postgres-data` (ensaios anteriores) intocados.
- Containers de outro projeto (`nestjs-project-db-1`, `postgres:17`) não tocados.
- Não havia backup, procedimento de retenção ou documentação de restore PostgreSQL anteriores no repositório.

## 5. Pesquisa de versões e compatibilidade (verificada em 2026-10-12)

| Item | Atual | Selecionado | Fonte | Evidência / mudanças | Ação de migração | Rollback |
|---|---|---|---|---|---|---|
| Servidor PostgreSQL 16 | 16.11 | **16.15** (2026-08-13) | https://www.postgresql.org/support/versioning/ | 16 suportado até 2028-11-09; minors compartilham o formato em disco e são compatíveis entre si | trocar o binário (imagem) e reiniciar | imagem 16.11 (overlay) no mesmo volume |
| Notas 16.12 | — | — | https://www.postgresql.org/docs/release/16.12/ | CVE-2026-2003…2006; validação multibyte mais estrita (pode expor texto inválido já gravado); erros de NOTIFY viram FATAL | nenhuma | — |
| Notas 16.13 | — | — | https://www.postgresql.org/docs/release/16.13/ | correção de regressão do CVE-2026-2006 (`substring` em TOAST) | nenhuma | — |
| Notas 16.14 | — | — | https://www.postgresql.org/docs/release/16.14/ | CVE-2026-6472…6479, 6637, 6638 (autenticação timing-safe, `pg_basebackup`/`pg_rewind`, `PQfn`) | nenhuma | — |
| Notas 16.15 | — | — | https://www.postgresql.org/docs/release/16.15/ | ~28 CVEs (incl. `pg_dump`, GSSAPI/`pg_hba`, `pgcrypto`); REINDEX só para `btree_gist` com NaN e `ltree` grande; `output_plugin_libraries` novo; psql pula dados inline após `COPY` falho | nenhuma (sem `btree_gist`, `ltree`, `pgcrypto`, decodificação lógica ou `COPY` scriptado) | — |
| Imagem oficial | `postgres:16.11@sha256:056b54f0…` | `postgres:16.15@sha256:ca0bd484cb98bf4b24eb1010e73fb3fcbd6714d240fbc1a10eea5b7dbecb641d` | Docker Hub `library/postgres` (tag `16.15` = `16` = `16.15-trixie`, mesmo índice; `docker buildx imagetools inspect`) | ambos Debian trixie, glibc 2.41 (`2.41-12` → `2.41-12+deb13u4`): `datcollversion` 2.41 inalterado, sem aviso de collation | — | — |
| Npgsql / EF Core | 10.0.3 / 10.0.12 | **inalterados** | https://www.npgsql.org/doc/compatibility.html, https://www.npgsql.org/efcore/release-notes/10.0.html | Npgsql suporta todas as versões PostgreSQL em suporte; nada no 10.0 exige mudança para 16.x | nenhuma | — |
| Testcontainers (fixture) | `postgres:16.15` (tag mutável) | `postgres:16.15@sha256:ca0bd484…` | idem imagem | elimina a última tag PostgreSQL mutável | — | reverter a string |

Dump/restore: `pg_dump`/`pg_dumpall`/`pg_restore` 16.15 (https://www.postgresql.org/docs/16/backup-dump.html, https://www.postgresql.org/docs/16/app-pgrestore.html); ferramentas do minor mais novo contra servidor do mesmo major são suportadas e incluem a correção CVE-2026-19385 do `pg_dump`.

Incidente registrado: durante a pesquisa, um comando com sintaxe errada (`docker run --rm --entrypoint "" …`) fez o Docker baixar `postgres:latest` (resolvido a `sha256:74935e72…`). Foi só um pull de imagem; o container falhou antes de iniciar, nenhum dado ou volume foi criado ou tocado. A imagem foi mantida (remoção depende de aprovação) e não é referenciada pelo repositório.

## 6. Ensaio isolado de backup/restore, patch e rollback

`scripts/postgres16-patch-rehearsal.ps1 [-RunId <id>]`. Recursos novos por run (nenhum publicado no host, exceto a WebApi em `127.0.0.1:18160`):

| Papel | Container | Volume | Imagem |
|---|---|---|---|
| Origem / baseline de rollback | `p16-src-<RunId>` | `p16-src-<RunId>-data` (+ `init-db.sh`) | 16.11 |
| Restore | `p16-rst-<RunId>` | `p16-rst-<RunId>-data` | 16.15 |
| Patch in-place (cópia) | `p16-pat-<RunId>` | `p16-pat-<RunId>-data` (cópia de arquivos da origem **parada**) | 16.15 |
| WebApi | `p16-api-{src,rst,pat,rb}-<RunId>` | — | `p16-webapi:<RunId>` (build do Dockerfile do repo) |
| Rede | `p16-net-<RunId>` | | |

A WebApi roda em `Production` com cache, broker, busca, sink Elastic e health check Elastic desabilitados por variável (`CacheSettings__Enabled`, `RabbitMqSettings__Enabled`, `PersonSearchSettings__Enabled`, `ElasticLoggingSettings__Enabled`, `ElasticsearchHealthCheckSettings__Enabled`), de modo que `/health/ready` depende só dos dois bancos. Credenciais sintéticas aleatórias passam só pelo ambiente do processo (`docker run -e NOME`), nunca na linha de comando ou em evidência; o nome do usuário bootstrap é mascarado como `<user>`. Evidência em `artifacts/validation/pg16-rehearsal/<RunId>` (git-ignored).

Etapas:

1. **Origem 16.11** com `init-db.sh`; WebApi aplica migrações e seeds; escritas sintéticas via API v2 (login, create 1234.56, update 9876543.21, create+delete). WebApi parada = **write freeze**, ponto de recuperação único e consistente para os dois bancos.
2. **Backup** com ferramentas 16.15 pela rede privada: `pg_dumpall --globals-only --no-role-passwords`, `pg_dump -Fc` de cada banco; SHA-256; `pg_restore --list` legível; globals sem senha e só com o role bootstrap.
3. **Restore** num 16.15 novo: globals (único erro aceito: o role bootstrap já existe), `pg_restore --create --exit-on-error` para cada banco, `ANALYZE`. Repetição do restore em bancos novos `*_restore_check` (mantidos) com o mesmo fingerprint.
4. **Aplicação no 16.15**: readiness, nenhuma migração aplicada, seeds sem mudança de fingerprint, `/health*` 200 e sanitizado, login v2 200, senha errada 400, POST sem token 401, produto restaurado com decimal exato, v1 por id, paginação, CRUD com `12345678901234.99` (limite de `numeric(18,2)`), segunda inicialização idempotente, restart do PostgreSQL (readiness 503 durante, recupera sem reiniciar a WebApi, dados persistidos, recuperação de shutdown limpo), parada graciosa da WebApi.
5. **Patch in-place** (caminho real do minor update): origem parada, cópia `cp -a` do volume para um volume novo, 16.15 sobre a cópia: `Skipping initialization`, sem `WARNING`/`FATAL`/`PANIC`/mismatch de collation, snapshot idêntico, WebApi pronta, login 200, nenhuma migração aplicada. O volume de origem só foi aberto pelo 16.11 (verificado).
6. **Rollback cronometrado**: para a WebApi (write freeze), garante a origem 16.11 pronta, sobe a WebApi apontando para ela e para o relógio quando `/health/ready` = 200. Origem idêntica ao snapshot do backup, login e produto OK.
7. Para tudo com `docker stop`; volumes e backups retidos; volumes normais inalterados; nenhuma senha na evidência.

### Resultados (run `20261012151701`, dentro do gate)

| Item | Resultado |
|---|---|
| Checks | **68/68** |
| Backup (3 artefatos) | 2,2 s |
| Restore dos dois bancos | 2,1 s |
| Patch: 16.15 pronto sobre a cópia 16.11 | 1,1 s |
| Rollback até readiness no 16.11 | **3,8 s** |

Manifesto de backup do run `20261012b` (exemplo; cada run tem o seu em `02-backup-manifest.txt`):

| Artefato | Bytes | SHA-256 |
|---|---|---|
| `application.dump` | 3 478 | `56DC36A3…42B015AC` |
| `identity.dump` | 15 564 | `A9C4AF5E…9CA942` |
| `globals.sql` | 534 | `771A32FF…67AA6627` |

TOC: 6 entradas (aplicação) e 42 (Identity), incluindo `TABLE DATA`. Ponto de recuperação em UTC registrado no manifesto.

### Comparações

- **Estrutura e dados** (encoding, locale, provider e versão de collation, settings, extensões, schemas e ACLs, donos, colunas com tipo/precisão/escala/default/collation, índices, constraints, grants, default ACL, roles, migrações, sequências, contagem + md5 por tabela): origem 16.11 = restore 16.15 = patch 16.15, comparação byte a byte.
- **Consultas representativas** (contagem + md5 do resultado): produto por id, página ordenada `Created, Id`, filtro por nome, escala do `Price`, ordenação por collation com acentos/maiúsculas/underscore, usuário por `NormalizedUserName`, roles do usuário, roles ordenados — idênticas.
- **Planos** (`EXPLAIN (COSTS OFF)` com estatísticas nos dois lados, e com `enable_seqscan = off` para provar uso dos índices `PK_Products`, `UserNameIndex`, `PK_UserRoles`, `PK_Role`): formas idênticas. `EXPLAIN (ANALYZE, BUFFERS)` salvo só para leitura; tempos de sub-milissegundo em tabelas minúsculas **não** provam equivalência de desempenho. No primeiro run (`20261012a`) a origem não tinha `ANALYZE` e escolhia seq scans; a diferença era de estatísticas, não do servidor, e o script passou a analisar os dois lados.

### Migrações e seeds

- Esperado = aplicado: aplicação 2 (`…InitialPostgresMigration`, `…ProductPriceDecimal`), Identity 1, histórico em cada banco correto, sem cruzamento (`compose-baseline` `migrations.no-cross-wiring`).
- Aplicação a partir do vazio: origem do ensaio e baseline Compose (volumes limpos, 16.15 e 16.11).
- Idempotência: segunda inicialização sem `Applying migration` e fingerprint/snapshot iguais (ensaio e baseline).
- Sem mudança de modelo pendente: suíte funcional 191/191 inclui o teste de nenhuma mudança pendente do Identity; a suíte PostgreSQL migra do vazio até a última migração. Nenhum delta relacional → **nenhuma migração criada**.

## 7. Compose

- `docker-compose.yml`: `db` → `postgres:16.15@sha256:ca0bd484…`; resto do serviço inalterado.
- `docker-compose.postgres-rollback.yml` (novo): troca só a imagem do `db` para o 16.11 da Fase 14. Minors são compatíveis em disco, então pode reabrir o mesmo `postgres-data`. Proíbe uso com dados escritos por 17/18.
- `scripts/compose-baseline.ps1`: `-PostgresRollback` (projeto `p16-rollback-<RunId>` com o overlay), checks `postgres.expected-image` e `postgres.runtime-version`.
- `scripts/run-phase-validation.ps1`: `-PostgresRehearsal` executa `06i-postgres16-patch-rehearsal` e `06j-compose-postgres-rollback`.

Resultados: `06e` baseline padrão `p14-baseline-20261012151433` **75/75** (runtime 16.15); `06j` rollback `p16-rollback-20261012151852` **75/75** (runtime 16.11). Ambos incluem volume limpo, `init-db.sh`, dois bancos, migrações/seeds duas vezes, health, Identity, RabbitMQ, Elasticsearch, Redis, restart do stack, persistência, parada graciosa e OpenAPI.

## 8. Procedimento de rollback (minor 16)

1. Parar a WebApi (write freeze).
2. Ambiente normal: `docker compose -f docker-compose.yml [-f docker-compose.override.yml] -f docker-compose.postgres-rollback.yml up -d db webapi` (16.11 no mesmo volume; formato em disco do major 16 inalterado). Alternativa: restaurar o backup lógico mais recente num servidor 16 novo (`pg_dumpall --globals-only` + `pg_restore --create` por banco, como no script).
3. Aguardar `/health/ready` = 200 e verificar login e um produto.

Tempo medido no ensaio: 2,7–3,8 s até readiness, em bancos de ~8 MB sintéticos, Docker Desktop local, imagens já presentes. Não é garantia de RTO em produção. Nunca iniciar um 16.x sobre dados escritos por 17/18.

## 9. Validação executada (gate `run-phase-validation.ps1 -ComposeBaseline -PostgresRehearsal -SkipGraphify`, 2026-10-12 15:09:57–15:23:18)

| Etapa | Resultado real |
|---|---|
| 01 restore | exit 0 |
| 02 build WebApi | 0 avisos, 0 erros |
| 03 arquitetura | 30/30 |
| 04 unidade | 107/107 |
| 05 integração (InMemory/adapters) | 141/141 |
| 06 PostgreSQL (Testcontainers 16.15 por digest) | 5/5 |
| 06b RabbitMQ 4.2.2 | 17/17 |
| 06c Elasticsearch 9.5.5 | 13/13 |
| 06f Redis 8.10.2 | 23/23 |
| 06e Compose baseline (16.15) | 75/75 |
| 06i ensaio PostgreSQL 16 | 68/68 |
| 06j Compose rollback (16.11) | 75/75 |
| 07 funcional | 191/191 |
| 08 vulnerabilidades (diretas e transitivas) | nenhum pacote vulnerável (17 projetos) |
| 09 `docker compose config -q` | exit 0 |
| 10 OpenAPI | v1 e v2 idênticos ao `docs/refactoring/baseline-phase-6` (comparação exata normalizando CRLF/LF: 24 372 e 21 632 caracteres) |
| 12 `git diff --check` | exit 0 |

Ensaios fora do gate: `20261012a` 63/68 (falhas do próprio script: contagem de migrações, filtro de erro dos globals, comparação de decimal e de nome de imagem, planos sem `ANALYZE` na origem — corrigidos), `20261012b` 68/68.

Graphify não regenerado: nenhuma mudança estrutural de código (só uma string de imagem em teste, scripts e YAML); `graphify-out` não é versionado desde `dea0c69`.

## 10. Auditoria de pacotes, imagens e segurança

- Nenhuma tag PostgreSQL mutável: Compose, overlay e fixture usam `versão@sha256`; baseline confirma todas as imagens pinadas.
- Pacotes NuGet e demais imagens (Redis, RabbitMQ, Elastic/Kibana, Redis Insight, .NET) **inalterados**.
- `.env` continua ignorado/não versionado; `.env.example` inalterado (só placeholders).
- Evidências e backups verificados sem a senha sintética; globals sem hash de senha; nenhum hash, stamp, token ou dado pessoal registrado (resultados só como contagem + md5).

## 11. Recursos retidos (nada removido)

- Volumes: `p16-{src,rst,pat}-20261012a-data`, `p16-{src,rst,pat}-20261012b-data`, `p16-{src,rst,pat}-20261012151701-data`; volumes Compose dos projetos `p14-baseline-20261012151433` e `p16-rollback-20261012151852`.
- Containers parados: `p16-{src,rst,pat}-<RunId>` e `p16-api-{src,rst,pat,rb}-<RunId>` dos três runs, e os dos dois projetos Compose.
- Redes `p16-net-<RunId>`; imagens `p16-webapi:<RunId>`, `postgres:16.11@…`, `postgres:16.15@…`, e a `postgres:latest` baixada acidentalmente.
- Backups: `artifacts/validation/pg16-rehearsal/<RunId>/backup` (git-ignored).
- Limpeza de qualquer um desses recursos depende de aprovação explícita.

## 12. Riscos residuais e limitações

- Só dados sintéticos; a compatibilidade com os dados reais em `cleanarchitecture_postgres-data` não foi verificada (não foram lidos). A troca de imagem no ambiente normal é um minor update e não altera o formato, mas deve ser feita com backup prévio.
- Tempos e planos medidos em tabelas minúsculas; não há prova de desempenho.
- O caminho normal `docker compose up` com override (HTTPS, user secrets) continua não verificado (herdado da Fase 14).
- Seed sem vínculo usuário-role (`UserRoles` vazio) é comportamento preexistente, fora do escopo.

## 13. Gates não executados

- PostgreSQL 18 (migração, testes, cutover): **não autorizado e não executado**.
- Ensaio Elastic 8→9 (`06d`) e ensaio Redis (`06g`/`06h`): não fazem parte desta fase e não foram reexecutados; as suítes reais de Redis/RabbitMQ/Elasticsearch rodaram.
- Graphify (`11`): não exigido (ver §9).

## 14. Confirmações

- Nenhum servidor mais antigo foi apontado para dados de major mais novo; nenhum dado 18 existe.
- Nenhum volume, backup, banco ou dado material foi apagado; nenhum prune.
- Nenhuma migração EF criada ou reescrita; nenhum arquivo gerado editado.
- Nenhum trabalho da Fase 17+ foi implementado.
