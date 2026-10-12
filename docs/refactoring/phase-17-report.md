# Fase 17 — Atualização independente do servidor RabbitMQ

Data de execução e verificação: 2026-10-12 (America/Sao_Paulo). Checkout autoritativo `C:\Users\alexa\source\repos\TemplateCleanArchNET`, branch `main` em `999bc5c` (merge da Fase 16), árvore limpa antes das edições.

## 1. Resumo executivo

- Broker do Compose atualizado de **RabbitMQ 4.2.2** (pin da Fase 14, imagem sem sufixo) para **RabbitMQ 4.3.6 management**: `rabbitmq:4.3.6-management@sha256:8dd6e3570ddaa2ef82a6c3a8950e79c1f73893adfbdddbc374fc1f1ac8a0f5dd` (Erlang/OTP 27.3.4.18, Ubuntu 24.04.5, `linux/amd64`). A série 4.3 é a única com suporte comunitário em 2026-10-12.
- Caminho oficial seguido e ensaiado: **4.2.2 → 4.2.9** (último patch 4.2 público) **→ todas as feature flags estáveis habilitadas (inclui `khepri_db`) → 4.3.6 → flags estáveis novas da 4.3 habilitadas**. Cada salto rodou numa **cópia do volume parado**; a origem 4.2.2 só foi aberta pelo 4.2.2.
- Novo `scripts/rabbitmq4-upgrade-rehearsal.ps1`: **81/81** checks (run `20261012b`; também 81/81 dentro do gate, run `20261012160643`). Mensagens persistentes sentinela (9, com `message_id` conhecidos, incluindo uma já em DLQ) + 4 mensagens da própria aplicação sobreviveram aos dois saltos, a restart e a recriação do container, com propriedades e digest idênticos. Exportação/importação de definições com paridade semântica. Rollback cronometrado para a origem intocada: **7,9 s** (run `b`).
- Defeito de wiring do Compose comprovado e corrigido: sem `hostname` fixo, o nome do nó acompanha o id do container e **toda recriação do container (por exemplo, troca de imagem) sobe um nó novo e vazio**, abandonando filas/mensagens duráveis no volume. O serviço agora usa `hostname: rabbitmq` (nó `rabbit@rabbitmq`), e o alvo usa volumes novos `rabbitmq4-data`/`rabbitmq4-log`; `rabbitmq-lib`/`rabbitmq-log` ficam preservados para rollback (`docker-compose.rabbitmq-rollback.yml`).
- Suíte com broker real: **19/19** no alvo 4.3.6 e **19/19** na origem 4.2.2 (2 testes novos). Contrato `OrderPlacedEvent` v1, topologia da Fase 9 e contrato HTTP/OpenAPI inalterados. Nenhum pacote NuGet mudou.
- **Somente dados sintéticos.** Os volumes do usuário (`cleanarchitecture_rabbitmq-lib`/`-log`) não foram montados nem lidos; a compatibilidade com os dados reais **não foi verificada**. Nada foi apagado. Fase 18 não iniciada.

## 2. Pré-requisitos e checkout verificados

| Item | Evidência |
|---|---|
| Fase 9 | `Template.Infrastructure.Messaging` contém topologia, publisher com confirms, consumer hosted service, serializer, retry/DLQ, reconexão e deduplicação; Application só tem ports/contrato (`OrderPlacedEvent`, `IOrderEventPublisher`, `IOrderPlacedEventHandler`, `IProcessedMessageStore`); `RabbitMQ.Client` 7.2.2 (`Directory.Packages.props`); suíte com broker 17/17 na origem antes da mudança (Fase 16 gate) e 19/19 agora. |
| Fase 13 | Broker classificado como dependência **opcional** em `/health/ready` (probe TCP `message-broker`, `TcpEndpointHealthCheck`), logs sem payload/credencial, shutdown limitado; testes funcionais 191/191 e arquitetura 30/30. |
| Fase 14 | `phase-14-report.md` contém evidência real de imagens pinadas, volume limpo, health, restart, persistência e rollback. Compose antes da mudança batia com o relatório: `rabbitmq:4.2.2@sha256:18cc2597…`, `rabbitmq-diagnostics -q ping` 10s/10s/12/30s, config `rabbitmq-plugins` = `[rabbitmq_management].`, volumes `rabbitmq-lib`/`rabbitmq-log`, `RabbitMqSettings__*`. |
| Fases 15 e 16 | Relatórios presentes; Redis 8.10.2 em `redis8-data` e PostgreSQL 16.15 com overlays de rollback intocados. |
| Checkout | `main` em `999bc5c`, `git status` limpo. |

## 3. Inventário pré-mudança (sanitizado)

### Serviço `rabbitmq` (antes)

| Atributo | Valor |
|---|---|
| Imagem | `rabbitmq:4.2.2@sha256:18cc2597e9b07bc29f5f991ca2836555245b4f94563773c19b662944f4b6d870` (variante sem management; o plugin era habilitado pelo config). Observação: a tag `4.2.2` hoje aponta para outro digest (`3b98915b…`, rebuild de 2026-01-20), usado pelos testes antigos sem digest. |
| Runtime | RabbitMQ 4.2.2, Erlang/OTP 27.3.4.6 (ERTS 15.2.7.4), Ubuntu 24.04.3, OpenSSL 3.5.4, `x86_64` |
| `enabled_plugins` | Config do Compose `rabbitmq-plugins` montado em `/etc/rabbitmq/enabled_plugins`: `[rabbitmq_management].` (o default da imagem, `[rabbitmq_prometheus]`, fica sobrescrito). Plugins efetivos: `rabbitmq_management`, `rabbitmq_management_agent`, `rabbitmq_web_dispatch`. |
| Hostname / nó | **não fixado** → `rabbit@<id do container>` |
| Ambiente (nomes) | `RABBITMQ_DEFAULT_USER`, `RABBITMQ_DEFAULT_PASS` |
| Volumes | `rabbitmq-lib:/var/lib/rabbitmq/`, `rabbitmq-log:/var/log/rabbitmq` |
| Health / restart / rede | `rabbitmq-diagnostics -q ping`; `restart: always`; `nw`; portas `${RABBITMQ_PORT}:5672` e `${RABBITMQ_MANAGEMENT_PORT}:15672` só no override |
| Dependentes | `webapi` (`condition: service_healthy`) |
| Limites de recurso | nenhum |

Nó (ambiente de origem do ensaio): cluster de um nó (`rabbit@p17-rabbit`, disk node), sem alarmes, sem partições; listeners 5672 (AMQP 0-9-1/1.0), 15672 (HTTP API), 25672 (clustering/CLI); watermark de memória 0,6 da memória disponível; limite de disco livre 50 MB; diretório de dados `/var/lib/rabbitmq/mnesia/rabbit@<host>`; parada limpa em 1,5 s.

No ambiente local existem só os volumes normais `cleanarchitecture_rabbitmq-lib`/`-log` (nenhum container normal do RabbitMQ). Eles **não foram montados nem lidos**; seu `CreatedAt` foi comparado antes/depois de cada ensaio (inalterado).

### Feature flags da origem (nó novo 4.2.2)

Todas `enabled`: obrigatórias (`classic_mirrored_queue_version`, `classic_queue_type_delivery_support`, `direct_exchange_routing_v2`, `drop_unroutable_metric`, `empty_basic_get_metric`, `feature_flags_v2`, `implicit_default_bindings`, `listener_records_in_ets`, `maintenance_mode_status`, `message_containers`, `quorum_queue`, `restart_streams`, `stream_filtering`, `stream_queue`, `stream_sac_coordinator_unblock_group`, `stream_single_active_consumer`, `stream_update_config_command`, `tracking_records_in_ets`, `user_limits`, `virtual_host_metadata`) e estáveis (`detailed_queues_endpoint`, `khepri_db`, `message_containers_deaths_v2`, `quorum_queue_non_voters`, `rabbit_exchange_type_local_random`, `rabbitmq_4.0.0`, `rabbitmq_4.1.0`, `rabbitmq_4.2.0`). O metadata store da origem já é **Khepri**. Recursos depreciados em uso: nenhum (`list_deprecated_features --used` vazio).

### Topologia, vhosts, usuários, políticas (idêntica na origem, no salto, no alvo, após restart e na importação)

| Exchange | Tipo | Durável | auto_delete | internal | Argumentos |
|---|---|---|---|---|---|
| `order_exchange_fanout` | fanout | **não** | não | não | — |
| `order_exchange_direct` | direct | **não** | não | não | — |
| `order_exchange_topic` | topic | **não** | não | não | — |
| `order_dead_letter_exchange` | direct | sim | não | não | — |

| Fila | Tipo | Durável | Exclusiva | auto_delete | Argumentos | Binding |
|---|---|---|---|---|---|---|
| `queue_fanout_a`, `queue_fanout_b` | classic | sim | não | não | `x-queue-type=classic` (default do servidor) | fanout, chave `""` |
| `urgent_orders` | classic | sim | não | não | idem | direct `order.urgent` |
| `orders_brazil` | classic | sim | não | não | idem | topic `order.brazil.*` |
| `<fila>.dead_letter` (4) | classic | sim | não | não | idem | DLX, chave = nome da fila de origem |

Vhost `/` apenas; um usuário (o bootstrap, tag `administrator`, permissões `.* .* .*`); topic permissions, policies, operator policies, limites e parâmetros: **nenhum**. Nenhum `x-queue-mode`/`x-queue-version` (sem CQv1). Sem quorum queues nem streams.

### Aplicação (inalterada)

- `RabbitMqSettings`: `HostName`, `Port`, `UserName`, `Password`, `Enabled`, `ConsumersEnabled`, `ConnectionTimeout`, `PublishTimeout`, `ReconnectDelay`, `MaxReconnectDelay`, `MaxDeliveryAttempts`, `RetryDelay`, `PrefetchCount`; Compose mapeia `RabbitMqSettings__HostName=rabbitmq`, `__Port=5672`, `__UserName=${RABBITMQ_DEFAULT_USER}`, `__Password=${RABBITMQ_DEFAULT_PASS}`. Sem vhost configurável (usa `/`), sem TLS.
- `OrderMessagingTopology` declara tudo de forma idempotente no início de cada conexão (publisher e consumer).
- `OrderPlacedEvent` v1: JSON UTF-8 PascalCase (`Id`, `ProductName`, `Quantity`, `Price`), `content_type=application/json`, `type=order.placed.v1`, `message_id=OrderId`, `delivery_mode=2`.
- Publish = confirm do broker; consumo com ack manual após sucesso, `nack(requeue)` com atraso até `MaxDeliveryAttempts`, DLQ com `x-dead-letter-reason` e headers `x-original-*`, `BasicQos(prefetch, global: false)`, deduplicação em memória, reconexão limitada, shutdown com `basic.cancel`. **At-least-once.**
- `RabbitMQ.Client` 7.2.2; testes com `Testcontainers.RabbitMq` 4.16.0.
- Publishers/consumers externos no escopo informado: nenhum.

## 4. Versões e compatibilidade (verificado em 2026-10-12)

| Papel | Imagem | RabbitMQ | Erlang/OTP | Base |
|---|---|---|---|---|
| Origem (Fase 14, rollback) | `rabbitmq:4.2.2@sha256:18cc2597e9b07bc29f5f991ca2836555245b4f94563773c19b662944f4b6d870` | 4.2.2 | 27.3.4.6 | Ubuntu 24.04.3 |
| Salto intermediário (só no ensaio) | `rabbitmq:4.2.9-management@sha256:9e9fd21d60f060acc9ecbd847cd1dcb87e6550bdb0297c27e79a40bfe1766ce4` | 4.2.9 | 27.3.4.18 | Ubuntu 24.04.5 |
| **Alvo** | `rabbitmq:4.3.6-management@sha256:8dd6e3570ddaa2ef82a6c3a8950e79c1f73893adfbdddbc374fc1f1ac8a0f5dd` (manifest amd64 `sha256:1d306c8f…`) | **4.3.6** | **27.3.4.18** | Ubuntu 24.04.5, OpenSSL 3.5.9 |

Fontes oficiais consultadas:

- Suporte por série: https://www.rabbitmq.com/release-information — 4.3 (4.3.6, 2026-09-16) com suporte comunitário até 2027-01-31; 4.2 com suporte comunitário encerrado em 2026-07-31 (4.2.10/4.2.11 só comerciais). Releases públicos no GitHub: https://github.com/rabbitmq/rabbitmq-server/releases (último 4.2 público = **v4.2.9**, 2026-07-20; **v4.3.6**, 2026-09-14).
- Guia de upgrade: https://www.rabbitmq.com/docs/upgrade — caminhos 4.2.x → 4.3.x; "todas as feature flags estáveis devem ser habilitadas antes do upgrade"; downgrade não suportado; blue-green recomendado quando é preciso voltar.
- Notas 4.3.0: https://github.com/rabbitmq/rabbitmq-server/releases/tag/v4.3.0 — exige o **último patch 4.2.x disponível** antes de 4.3.0; todas as flags até 4.2.0 (inclui `khepri_db`) obrigatórias; Mnesia removido; CQv1 removido (`x-queue-version=1`/`x-queue-mode` recusados); filas não duráveis e não exclusivas recusadas por padrão; recursos depreciados desligados por padrão.
- Notas 4.2.9 e 4.3.6: https://github.com/rabbitmq/rabbitmq-server/releases/tag/v4.2.9 , https://github.com/rabbitmq/rabbitmq-server/releases/tag/v4.3.6 (mínimo Erlang 27.0).
- Matriz Erlang: https://www.rabbitmq.com/docs/which-erlang — 4.3.6: OTP 27.0–28.x; 4.2.9–4.2.11: 27.0–27.x (OTP 28 só para clusters novos). As três imagens usam OTP 27, dentro das faixas.
- Feature flags: https://www.rabbitmq.com/docs/feature-flags — flags não podem ser desabilitadas; `enable_feature_flag all` só habilita estáveis; nó com flag obrigatória desabilitada se recusa a iniciar; depois de habilitar, não há volta para a versão anterior.
- Backup/definições: https://www.rabbitmq.com/docs/backup , https://www.rabbitmq.com/docs/definitions — definições não contêm mensagens; backup de mensagens exige o nó parado; restore de disco só com o **mesmo nome de nó**.
- Imagem oficial: https://hub.docker.com/_/rabbitmq (tags `4.3.6-management`, `4.2.9-management`; `docker buildx imagetools inspect`).
- Cliente .NET: https://www.rabbitmq.com/client-libraries/dotnet (7.x suportado; sem matriz de versão do broker) — compatibilidade comprovada empiricamente (19/19 no 4.3.6).
- Avisos de segurança: https://github.com/rabbitmq/rabbitmq-server/security/advisories — p.ex. GHSA-h964-v5mf-22cq (alto, wildcards consecutivos em topic geram trabalho combinatório; corrigido em 4.3.5; o projeto usa exchange topic), GHSA-cfqc-c682-93mm (alto, Web STOMP; corrigido em 4.2.10/4.3.x; plugin não habilitado), avisos de AMQP 1.0/MQTT/Stream corrigidos até 4.3.6. O 4.2.9 público não recebe as correções de 4.2.10+, o que reforça 4.3.6.

**Por que 4.3.6 e não só o patch 4.2.9:** 4.2 está fora do suporte comunitário e o último patch público (4.2.9) não tem as correções de segurança de agosto/setembro de 2026; 4.3.6 é estável, suportado, roda o mesmo Erlang 27 e o cliente 7.2.2 passa a suíte inteira. Nenhum pacote NuGet precisou mudar.

**Sequência suportada (adotada):** 4.2.2 → **4.2.9** → `rabbitmqctl enable_feature_flag all` → **4.3.6** → `rabbitmqctl enable_feature_flag all` (estáveis novas da 4.3). Pular o 4.2.9 não é suportado pela nota da 4.3.0.

## 5. Feature flags

| Momento | Estado |
|---|---|
| Origem 4.2.2 (nó novo) | todas as obrigatórias e estáveis já `enabled`, inclusive `khepri_db` |
| 4.2.9 antes/depois de `enable_feature_flag all` | idêntico (no-op); nenhuma flag estável desabilitada |
| 4.3.6 logo após o upgrade | as flags 4.2 viraram `required`; **novas estáveis desabilitadas**: `rabbitmq_4.3.0`, `tie_binding_to_dest_with_keep_while_cond`, `topic_binding_projection_v4`, `topic_binding_projection_v5`, `track_qq_members_uids` |
| 4.3.6 final | habilitadas conforme a orientação oficial ("após atualizar todos os nós, habilitar as flags estáveis da nova versão"); nenhuma flag estável/obrigatória desabilitada |

Decisões e implicações:

- Nenhum plugin adiciona flags (só management).
- Habilitar flags é **irreversível**: depois disso o volume não pode voltar para 4.2.x. Por isso as habilitações aconteceram só nas cópias (`p17-hop-*`, `p17-tgt-*`), e o rollback é reconectar a aplicação ao ambiente 4.2.2 preservado.
- Um nó 4.3.6 novo (Compose com volume limpo) já nasce com todas as flags estáveis habilitadas; o baseline confirma (`rabbitmq.stable-feature-flags-enabled`).
- Para os dados reais (não lidos), um volume criado por versões antigas (por exemplo 3.13 com Mnesia, quando `latest` resolvia para outra série) pode ter flags desabilitadas ou exigir saltos adicionais (3.13 → 4.2 → 4.3). Isso precisa ser verificado na cópia antes de qualquer migração autorizada.

## 6. Plugins e imagem management

- Plugins explícitos e reproduzíveis: o config `rabbitmq-plugins` (`[rabbitmq_management].`) continua sobrescrevendo o default da imagem (`[rabbitmq_management,rabbitmq_prometheus].`). Efetivos no alvo: `rabbitmq_management`, `rabbitmq_management_agent`, `rabbitmq_web_dispatch` — iguais à origem; **Prometheus não foi habilitado**, nenhum plugin temporário de transferência (shovel/federation) foi usado.
- API de management no alvo: `/api/overview` 200 (management 4.3.6), `/api/whoami`, `get`/`publish` usados pelo ensaio. Persistência dos plugins após restart verificada (baseline e ensaio).
- Aviso no log do 4.3.6: o recurso depreciado `management_metrics_collection` continua permitido por padrão e é usado pelo plugin de management (só aviso; risco residual para uma versão futura).
- A imagem `-management` traz o mesmo servidor/Erlang da variante sem sufixo e acrescenta o `rabbitmqadmin`; o conjunto de plugins é controlado pelo config.

## 7. Definições (exportação, importação, paridade)

- Exportação com `rabbitmqctl export_definitions` dentro do container (origem 4.2.2 e alvo 4.3.6), copiada com `docker cp` para `artifacts/validation/rabbitmq-rehearsal/<RunId>/secure/` (git-ignored, ACL restrita ao usuário atual com `icacls /inheritance:r`); o arquivo temporário no container é removido logo após a cópia.
- Os exports contêm o hash da senha sintética. Só cópias sanitizadas (usuário mascarado, apenas `has_hash`/algoritmo, entidades ordenadas, sem campos de versão e sem exchanges `amq.rabbitmq.*` do próprio broker) são comparadas e ficam fora da pasta segura. Check `secrets.hashes-only-in-secure-folder` e `secrets.no-password-in-evidence` PASS.
- Completude: vhosts, usuários e permissões, topic permissions, parâmetros, políticas, filas, exchanges e bindings (exportados vazios onde não existem).
- **Paridade origem 4.2.2 × alvo 4.3.6:** idêntica.
- **Importação** do export da origem num 4.3.6 novo (`p17-imp-<RunId>`, nó próprio): sucesso; topologia (exchanges, filas, argumentos, bindings, vhost, usuário, permissões, políticas) idêntica à origem; o usuário importado autentica com a senha original (hash preservado); **0 mensagens** em todas as filas — prova de que definições não carregam mensagens.
- Diferença esperada e registrada: o 4.3.6 cria as exchanges internas do broker `amq.rabbitmq.log` e `amq.rabbitmq.trace` (topic, duráveis, internal); não pertencem à aplicação e foram excluídas da comparação.
- Nenhuma definição foi importada em broker do usuário.

## 8. Preservação de mensagens e dados

Estratégia: **upgrade in-place sequencial em cópias verificadas** do diretório de dados (método oficial para nó único), com o nó parado antes de cada cópia (backup de mensagens exige o nó parado) e o **mesmo nome de nó** (`rabbit@p17-rabbit`), já que o restore de disco só é suportado com o mesmo nome. Nenhum blue-green com shovel/federation foi necessário.

Dados: **sintéticos**. Mensagens da própria aplicação (publicadas pelos endpoints v1 com `ConsumersEnabled=false`) e sentinelas publicadas pela API de management com `message_id` conhecido, `delivery_mode=2`, `content_type` e `type` v1:

| Fila | Mensagens no ponto de backup |
|---|---|
| `queue_fanout_a`, `queue_fanout_b`, `urgent_orders`, `orders_brazil` | 3 cada (2 sentinelas + 1 da aplicação) |
| `urgent_orders.dead_letter` | 1 sentinela já dead-lettered |
| demais DLQs | 0 |

Write freeze: a WebApi da origem é parada antes do inventário; nenhuma entrega fica sem ack (sem consumidores). Inventário = `fila|message_id|delivery_mode|content_type|type|sha256(corpo)` via `get` com `ack_requeue_true` (nenhuma mensagem removida; nenhum corpo gravado).

| Etapa | Resultado |
|---|---|
| Origem 4.2.2 | 13 mensagens, todas persistentes com propriedades v1 |
| Salto 4.2.9 (cópia) | inventário idêntico; topologia idêntica |
| Alvo 4.3.6 (cópia do salto) | inventário idêntico (13) |
| Restart do alvo | inventário e topologia idênticos |
| Container recriado (mesmo volume, mesmo hostname) | inventário idêntico |
| Container sem hostname fixo (cópia do alvo) | **nó novo e vazio** (nenhuma fila da aplicação) — prova do defeito corrigido no Compose |
| Aplicação no alvo | as 12 mensagens das filas de trabalho consumidas e confirmadas; sentinela da DLQ retida |

Mensagens em voo/sem ack: no ensaio não há (write freeze + sem consumidores). Num cutover real, parar publishers e consumers antes de parar o broker; entregas sem ack voltam para a fila quando a conexão fecha (at-least-once, duplicatas possíveis e toleradas pela deduplicação).

## 9. Ensaio isolado — `scripts/rabbitmq4-upgrade-rehearsal.ps1`

Recursos novos por run (nada publicado no host além de portas de management e da WebApi em loopback): rede `p17-net-<RunId>`; brokers `p17-src`, `p17-hop`, `p17-tgt`, `p17-tgt2`, `p17-noh`, `p17-imp` (`-<RunId>`) com volumes `<nome>-data`; WebApis `p17-api-{src,tgt,rb}-<RunId>` da imagem `p17-webapi:<RunId>` (build do Dockerfile do repositório; banco em memória; cache, busca e Elastic desabilitados; adapter de mensageria real). Credenciais sintéticas aleatórias só no ambiente do processo (`docker run -e NOME`), nunca na linha de comando nem na evidência; usuário mascarado como `<user>`. O CLI só é chamado depois de `Server startup complete` (antes disso ele cria um cookie Erlang errado e derruba o nó — comportamento observado em sondagem, por isso a espera).

Etapas: origem → topologia pela aplicação + sentinelas → inventário (versões, flags, plugins, recursos depreciados, status, cluster, alarmes, listeners, ambiente, definições) → parada limpa → cópia → 4.2.9 → `enable_feature_flag all` → parada → cópia → 4.3.6 (health: `check_running`, `check_local_alarms`, `check_port_connectivity`, `check_virtual_hosts`; log sem `[error]`) → flags novas → plugins → API management → topologia/mensagens/definições → restart → recriação → sem hostname → importação → aplicação no alvo → restart do broker com a aplicação rodando → shutdown → rollback cronometrado → retenção e varredura de segredos.

| Run | Resultado | Observação |
|---|---|---|
| `20261012a` | 72/81 | Defeitos do próprio script: o Windows PowerShell devolve o array JSON do `get` como um objeto (inventário só com 1 mensagem por fila), `Trim()` removia a coluna vazia inicial do binding da exchange default, as exchanges internas novas do 4.3 entravam na comparação, e o regex de hash casava com o nome do algoritmo `rabbit_password_hashing_sha256`. Corrigidos. |
| `20261012b` | **81/81** | |
| `20261012160643` (gate) | **81/81** | rollback 7,4 s |

Tempos do run `20261012b` (Docker Desktop local, imagens já presentes, dados minúsculos): parada limpa da origem 1,5 s; 4.3.6 pronto sobre a cópia 4,8 s; publish da aplicação recuperado 6,0 s após `docker restart` do broker; shutdown da WebApi 0,4 s; **rollback até readiness + publish OK: 7,9 s**.

## 10. Contrato v1, topologia e comportamento de entrega no alvo

Suíte `Tests/Template.RabbitMq.Integration.Tests` (Testcontainers, broker descartável sem volumes do usuário):

| Imagem | Resultado |
|---|---|
| Alvo `rabbitmq:4.3.6-management@sha256:8dd6e357…` (default) | **19/19** |
| Origem `rabbitmq:4.2.2@sha256:18cc2597…` (`TEMPLATE_RABBITMQ_TEST_IMAGE`) | **19/19** |

Cobertura: topologia idempotente e compatível com as declarações 6.x; fanout para as duas filas; direct com chave exata e não correspondente; topic que casa e que não casa; propriedades persistentes e corpo byte a byte do v1; mensagem legada sem propriedades; ack só após sucesso e requeue no shutdown; reentrega após falha transitória; limite de retry + DLQ com headers sem requeue infinito; payload inválido e contrato incompatível para a DLQ; duplicata processada uma vez; rajada de 50 mensagens com corpo `ReadOnlyMemory<byte>` copiado; cancelamento de publish; liberação de consumers/conexões; startup sem broker, conexão quando ele aparece, recuperação após restart, shutdown limitado com broker fora.

Testes novos (lacunas criadas pela troca de servidor):

- `Broker_RunsThePinnedVersion_WithKhepriAndEveryStableFeatureFlagEnabled`: versão do servidor = tag da imagem; `khepri_db` e todas as flags estáveis/obrigatórias habilitadas.
- `PersistentMessages_SurviveABrokerRestart_AndTheTopologyIsRedeclaredOnReconnect`: mensagens persistentes em filas duráveis sobrevivem ao restart; a aplicação reconecta, redeclara a topologia e consome as mensagens preservadas e uma nova.

Achado: no 4.3.6 as exchanges **não duráveis** da Fase 9 **sobreviveram** ao restart do broker (primeira versão do teste novo, que afirmava o contrário e falhou; o comportamento no 4.2.2 não foi medido isoladamente) — o risco residual da Fase 9 ("somem no restart") não se manifestou. O teste não afirma nenhum dos dois comportamentos, porque depende do metadata store; a aplicação continua redeclarando a topologia a cada conexão.

Ensaio com a WebApi real no alvo: `/health/ready` 200 com `message-broker` Healthy; mensagens preservadas consumidas e confirmadas; fanout/direct/topic 200 e consumidos; routing key sem binding 200 e descartada (comportamento v1); payload inválido → `urgent_orders.dead_letter` com `x-dead-letter-reason=invalid-payload` e `delivery_mode=2`; conexões `template-webapi-publisher` e `template-webapi-consumers`; restart do broker com a aplicação rodando → publish recuperado e consumers recuperados automaticamente, topologia igual; `docker stop` da WebApi → exit 0, `Application stopping`, nenhuma conexão restante; log da aplicação sem a senha.

Contrato `OrderPlacedEvent` v1, nomes/tipos/durabilidade de exchanges e filas, bindings, DLQs, headers de retry, confirms, persistência, momento do ack, semântica at-least-once e deduplicação: **inalterados** (nenhum arquivo de `Src/` mudou). Exactly-once **não** é garantido nem alegado.

## 11. Compose, health e configuração

| Arquivo | Mudança |
|---|---|
| `docker-compose.yml` | `rabbitmq`: imagem `rabbitmq:4.3.6-management@sha256:8dd6e357…`; `hostname: rabbitmq` (nó `rabbit@rabbitmq`); volumes `rabbitmq4-data:/var/lib/rabbitmq/` e `rabbitmq4-log:/var/log/rabbitmq` (novos); `rabbitmq-lib`/`rabbitmq-log` continuam declarados para rollback. Health check, config de plugins, ambiente, rede, restart e `depends_on` da WebApi inalterados. |
| `docker-compose.rabbitmq-rollback.yml` (novo) | Imagem 4.2.2 da Fase 14, hostname original (`!reset null`), volumes `rabbitmq-lib`/`rabbitmq-log`. Proíbe uso com `rabbitmq4-data`. |
| `scripts/rabbitmq4-upgrade-rehearsal.ps1` (novo) | Ensaio da seção 9. |
| `scripts/compose-baseline.ps1` | `-RabbitMqRollback` (projeto `p17-rollback-<RunId>`); checks estáticos (imagem, volume, hostname) e de runtime (versão, plugins, flags, nome do nó). |
| `scripts/run-phase-validation.ps1` | `-RabbitMqRehearsal`: `06k` ensaio, `06l` suíte com broker contra a origem 4.2.2, `06m` baseline com o overlay de rollback. |
| `Tests/Template.RabbitMq.Integration.Tests` | Imagem alvo pinada por digest, `TEMPLATE_RABBITMQ_TEST_IMAGE`, 2 testes novos. |

`RabbitMqSettings` e o mapeamento por nome no Compose não mudaram; credenciais continuam vindo de `RABBITMQ_DEFAULT_USER`/`RABBITMQ_DEFAULT_PASS`; sem fallback com credencial; TLS inalterado (não usado); portas de management continuam só no override. Health continua `rabbitmq-diagnostics -q ping` (limitado, não mutante); a readiness da WebApi continua tratando o broker como opcional (Fase 13). `.env.example` não precisou de chave nova.

Baseline isolado do Compose (`compose-baseline.ps1`): run `20261012p17a` **82/82** antes do gate e 82/82 (`p14-baseline-20261012160406`) no gate; rollback `-RabbitMqRollback` 81/81 (`p17-rollback-20261012161045`, runtime 4.2.2 em `rabbitmq-lib`). Inclui volume limpo, health, publish/consumo via WebApi, versão 4.3.6, plugins, flags, nó `rabbit@rabbitmq`, restart completo com filas duráveis persistidas, parada graciosa, OpenAPI.

## 12. Cutover e rollback

Antes do cutover (registrado):

| Item | Valor |
|---|---|
| Imagens | origem `rabbitmq:4.2.2@sha256:18cc2597…` → alvo `rabbitmq:4.3.6-management@sha256:8dd6e357…` (salto `4.2.9-management@sha256:9e9fd21d…` só para migrar dados existentes) |
| Versões | 4.2.2/OTP 27.3.4.6 → 4.3.6/OTP 27.3.4.18 |
| Nó / container / volumes | origem `rabbit@<id do container>`, `rabbitmq`, `rabbitmq-lib`/`rabbitmq-log` → alvo `rabbit@rabbitmq`, `rabbitmq`, `rabbitmq4-data`/`rabbitmq4-log` |
| Flags/plugins da origem | seção 3 (todas habilitadas; management) |
| Ponto de recuperação | volume `rabbitmq-lib` intocado + export de definições |
| Preservação | ambiente local: o alvo começa vazio em `rabbitmq4-data` (como Redis e Elasticsearch nas Fases 11 e 15); migrar mensagens reais exige autorização e a sequência ensaiada numa cópia |
| Pausa | parar a WebApi (publishers e consumers) durante a troca |
| Em voo | sem ack → voltam para a fila ao fechar a conexão |
| Go/no-go | broker healthy, versão 4.3.6, flags estáveis habilitadas, plugins = management, topologia redeclarada, `/health/ready` com broker Healthy, publish v1 200 e consumido |
| Troca | `docker compose up -d rabbitmq webapi` com o `docker-compose.yml` atualizado (uma mudança coerente; não há dois conjuntos de consumidores) |

**Rollback** (não é downgrade de dados):

1. Parar a WebApi (write freeze).
2. `docker compose -f docker-compose.yml [-f docker-compose.override.yml] -f docker-compose.rabbitmq-rollback.yml up -d rabbitmq webapi` — volta a imagem 4.2.2 nos volumes `rabbitmq-lib`/`rabbitmq-log`; `rabbitmq4-data` fica intocado.
3. Aguardar o broker healthy e `/health/ready`; publicar uma mensagem de teste.

Limitação herdada: como o Compose antigo não fixava hostname, o estado anterior em `rabbitmq-lib` pertence ao nome de nó do container que o criou; um container recriado com o hostname original aleatório não o recarrega. Para recuperar aquele estado, o hostname precisa ser o do nó original (descobrível listando `mnesia/` numa **cópia** do volume, o que exige autorização para ler dados do usuário). Mensagens publicadas no alvo depois do cutover não estão na origem: drenar ou republicar antes de voltar, se importarem.

Medido no ensaio: **7,9 s** (run `b`) e 7,4 s (gate), de "iniciar a origem parada" até readiness + publish 200, com dados minúsculos, Docker Desktop local e imagens presentes. Não é garantia de RTO.

## 13. Validação executada

Executado: `powershell -ExecutionPolicy Bypass -File .\scripts\run-phase-validation.ps1 -ComposeBaseline -RabbitMqRehearsal` (2026-10-12 15:59:56–16:15:12, SDK pinado `C:\Users\alexa\.dotnet\dotnet.exe` 10.0.401, passos sequenciais). Todos os passos com exit 0.

| Etapa | Resultado real |
|---|---|
| 01 restore | exit 0 |
| 02 build WebApi | 0 avisos, 0 erros |
| 03 arquitetura | 30/30 |
| 04 unidade | 107/107 |
| 05 integração (InMemory/adapters sem broker) | 141/141 |
| 06 PostgreSQL 16.15 | 5/5 |
| 06b RabbitMQ — alvo 4.3.6-management | **19/19** |
| 06c Elasticsearch 9.5.5 | 13/13 |
| 06f Redis 8.10.2 | 23/23 |
| 06e Compose baseline (alvo 4.3.6), `p14-baseline-20261012160406` | **82/82** |
| 06k ensaio RabbitMQ 4.2.2 → 4.2.9 → 4.3.6, run `20261012160643` | **81/81** (rollback 7,4 s) |
| 06l RabbitMQ — origem 4.2.2 (rollback) | **19/19** |
| 06m Compose com overlay de rollback 4.2.2, `p17-rollback-20261012161045` | **81/81** |
| 07 funcional | 191/191 |
| 08 vulnerabilidades (diretas e transitivas) | nenhum pacote vulnerável (17 projetos) |
| 09 `docker compose config -q` | exit 0 |
| 10 OpenAPI | v1 e v2 idênticos ao `docs/refactoring/baseline-phase-6` (comparação exata normalizando CRLF/LF: 24 372 e 21 632 caracteres) |
| 11 Graphify | `graphify update . --force`: 3 966 nós, 8 485 arestas, 224 comunidades (`graphify-out` não é versionado) |
| 12 `git diff --check` | exit 0 (só avisos de normalização LF/CRLF) |

Fora do gate (antes dele): suíte RabbitMQ 19/19 no alvo e 19/19 na origem; ensaio `20261012a` 72/81 (defeitos do script, corrigidos) e `20261012b` 81/81; baseline `20261012p17a` 82/82.

## 14. Auditoria de pacotes, imagens e segurança

- Runtime verificado: 4.3.6 / Erlang 27.3.4.18 / `linux/amd64`; digest do índice `8dd6e357…`.
- Nenhuma tag RabbitMQ mutável: Compose, overlay, testes e script usam `versão@sha256` (o baseline confirma todas as imagens pinadas).
- Plugins: só management (+ dependências); flags: todas as estáveis habilitadas.
- Pacotes NuGet **inalterados** (`RabbitMQ.Client` 7.2.2, `Testcontainers.RabbitMq` 4.16.0); auditoria de vulneráveis diretos e transitivos no gate.
- Outras imagens (PostgreSQL, Redis, Elasticsearch/Kibana, Redis Insight, .NET) **inalteradas**.
- `.env` continua ignorado/não versionado; `.env.example` inalterado (placeholders).
- Evidências: sem senha (varredura automática), hashes só na pasta segura, nenhum cookie Erlang copiado para fora dos volumes, nenhum payload gravado (só digest).

## 15. Recursos retidos (nada removido)

- Runs `20261012a`, `20261012b` e `20261012160643`: containers parados `p17-{src,hop,tgt,tgt2,noh,imp}-<RunId>` e `p17-api-{src,tgt,rb}-<RunId>`; volumes `p17-{src,hop,tgt,noh,imp}-<RunId>-data`; redes `p17-net-<RunId>`; imagens `p17-webapi:<RunId>`.
- Contêineres de diagnóstico `p17-diag-1` (4.3.6) e `p17-diag-2` (4.2.2), parados, usados para inventariar flags de nós novos (volumes anônimos associados).
- Projetos Compose `p14-baseline-20261012p17a`, `p14-baseline-20261012160406` e `p17-rollback-20261012161045` (containers parados, redes e volumes do projeto).
- Imagens `rabbitmq:4.2.9-management@…` e `rabbitmq:4.3.6-management@…` baixadas; `rabbitmq:4.2.2@…` mantida.
- Exports de definições em `artifacts/validation/rabbitmq-rehearsal/<RunId>/secure` (git-ignored, ACL restrita).
- Volumes do usuário `cleanarchitecture_rabbitmq-lib`/`-log` intocados.
- Limpeza de qualquer recurso depende de aprovação explícita.

## 16. Riscos residuais e limitações

- **Só dados sintéticos**: a migração dos dados reais em `cleanarchitecture_rabbitmq-lib` não foi verificada (versão de origem, flags e nome de nó desconhecidos). Se o volume tiver sido criado por 3.13/Mnesia, são necessários saltos adicionais (3.13 → 4.2.9 → 4.3.6) e possivelmente blue-green com definições + shovel.
- O nó novo do Compose começa vazio em `rabbitmq4-data`; mensagens que estivessem no volume antigo não aparecem no alvo sem migração.
- `management_metrics_collection` está depreciado (permitido por padrão no 4.3.6).
- Exchanges continuam não duráveis (compatibilidade da Fase 9); no 4.3.6 elas sobreviveram ao restart, mas isso não é contrato.
- Deduplicação e contagem de tentativas continuam por processo (herdado da Fase 9).
- Tempos medidos com dados minúsculos; não são RTO de produção.
- O `docker compose up` normal com override (HTTPS, user secrets) continua não verificado (herdado da Fase 14).

## 17. Gates não executados

- Migração dos dados reais do RabbitMQ: **não autorizada, não executada**.
- Ensaios Elastic (`06d`), Redis (`06g`/`06h`) e PostgreSQL (`06i`/`06j`): de outras fases, não reexecutados; as suítes reais de Redis, Elasticsearch e PostgreSQL rodaram no gate.
- Validação do VSIX (não-gate conhecido).

## 18. Confirmações

- Nenhuma fila, definição, mensagem persistida, volume, backup ou estado de feature flag do usuário foi apagado, purgado, lido ou alterado; nenhum broker mais antigo abriu dados escritos por um mais novo (check `rollback.source-volume-only-opened-by-source`).
- Contrato HTTP/OpenAPI, contrato de mensagem v1 e topologia inalterados.
- Nenhum trabalho da Fase 18 ou posterior foi implementado.
