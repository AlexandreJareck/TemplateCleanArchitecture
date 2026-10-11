# Relatório da Fase 9 — 2026-10-10

Checkpoint de 2026-10-10 (America/Sao_Paulo). A Fase 9 colocou o RabbitMQ atrás de ports da Application, moveu todo o código do fornecedor para o novo projeto `Template.Infrastructure.Messaging` e migrou o `RabbitMQ.Client` de 6.8.1 para 7.2.2.

Não houve commit, push, PR, troca de branch, stash, reset nem clean. A única ação de Git foi um `git pull --ff-only origin main` autorizado pelo usuário: o `main` local estava três commits atrás do `origin/main`, que já contém o merge das Fases 0–8 (PR #1). **Nada das Fases 10+ foi implementado e o servidor RabbitMQ não foi atualizado.**

## Pré-condição: Fase 8

Verificada no estado real do `main` (`f293cb9`) antes de qualquer mudança:

- A Application não tem pacote nem tipo Redis. `ProductService` usa `IProductCache`.
- As exceções da Fase 8 tinham saído de `ArchitectureRulesTests.cs` e `check-boundaries.ps1`; restavam só as das Fases 9–10.
- `phase-8-report.md` existe com os resultados reais.
- Os testes da Fase 8 continuam verdes no gate abaixo (unitários, integração e funcionais).

## Resumo do design

- **Application** (sem dependência de fornecedor):
  - contrato de evento `OrderPlacedEvent`;
  - port de publicação `IOrderEventPublisher`;
  - port de processamento `IOrderPlacedEventHandler`;
  - port de idempotência `IProcessedMessageStore`;
  - caso de uso `PublishOrderCommand` (MediatR);
  - handler de negócio `OrderPlacedEventHandler`.
- **Infrastructure.Messaging**: concentra tudo que depende do RabbitMQ:
  - publisher assíncrono com publisher confirms;
  - hosted service consumidor;
  - política de entrega;
  - topologia;
  - serialização;
  - opções tipadas e validadas;
  - reconexão;
  - dead-letter;
  - store de deduplicação em memória.
- **WebApi**: só compõe (`AddMessagingInfrastructure`). O `MessageController` v1 envia o comando via `ISender` e mantém o contrato HTTP.
- **Domain**: inalterado. `Template.Domain.Order.Order` continua como corpo HTTP v1, para que o schema OpenAPI `Order` não mude, mas deixou de ser o contrato de integração.

## Inventário anterior (estado de `f293cb9`)

### Código e ciclo de vida

| Item | Antes da Fase 9 |
|---|---|
| Pacote | `RabbitMQ.Client` 6.8.1 e `Microsoft.Extensions.Hosting.Abstractions` referenciados por `Template.Application` |
| Publisher | `Application/Services/RabbitMq/RabbitMqService` (scoped), `IRabbitMqService` síncrono com `PublishOrderFanout(Order)`, `PublishOrderDirect(Order, routingKey)` e `PublishOrderTopic(Order, routingKey)` |
| Conexão do publisher | Uma conexão e um canal **novos por mensagem** (`using CreateConnection`/`CreateModel`), sem confirms. Declarava a exchange a cada publish. |
| Consumers | `Application/Consumers/OrderConsumer.cs`: `OrderConsumerTopic`, `OrderConsumerDirect`, `OrderConsumerFanoutA` e `OrderConsumerFanoutB`, todos derivados de `BackgroundService` |
| Ciclo de vida dos consumers | Conexão, canal, declarações e (no fanout) `BasicConsume` abertos **no construtor**. Com o broker fora, o construtor lançava exceção e derrubava o host. Fechamento síncrono em `Dispose`. Sem reconexão. |
| DI | `AddApplicationLayer(registerIntegrationHostedServices)` registrava o publisher e os 4 hosted services (desligados no ambiente Test desde a Fase 1) |
| Configuração | `Application/Settings/RabbitMqSettings` (`HostName`, `Port`, `UserName`, `Password`) com **credenciais default no código**; `Configure<RabbitMqSettings>` feito no `Program.cs` |
| HTTP | v1 `MessageController`: `POST create-order-fanout`, `create-order-direct?routingKey=` e `create-order-topic?routingKey=`, com corpo `Order`, `200 text/plain` e v1 deprecated, sem autenticação |
| Testes | Nenhum teste de mensageria |

### Topologia

| Exchange (tipo) | Durável | Fila | Binding | Consumer |
|---|---|---|---|---|
| `order_exchange_fanout` (fanout) | não (default 6.x) | `queue_fanout_a` | `""` | FanoutA |
| `order_exchange_fanout` (fanout) | não | `queue_fanout_b` | `""` | FanoutB |
| `order_exchange_direct` (direct) | não | `urgent_orders` | `order.urgent` | Direct |
| `order_exchange_topic` (topic) | não | `orders_brazil` | `order.brazil.*` | Topic |

As filas eram duráveis, não exclusivas, sem auto-delete e sem argumentos.

### Mensagens, ack e falhas

- **Payload:** JSON UTF-8 da entidade de domínio `Order` com os defaults do System.Text.Json (PascalCase: `Id`, `ProductName`, `Quantity`, `Price`).
- **Propriedades:** só `Persistent = true`. Não havia `content_type`, `type` nem `message_id`.
- **Ack:** manual (`autoAck: false`), com `BasicAck` logo após desserializar e logar.
- **Falhas:** sem tratamento.
  - Se a desserialização falhasse, a exceção escapava do callback, a mensagem ficava sem ack até o canal fechar e era reentregue sem limite.
  - Payload JSON `null` era logado e confirmado.
  - Não havia retry, DLQ, idempotência nem prefetch.

## Ports e contratos criados (Application)

| Tipo | Papel |
|---|---|
| `Messaging/Orders/OrderPlacedEvent` | Contrato de integração `order.placed` v1: `OrderId`, `ProductName`, `Quantity`, `Price`, todos obrigatórios |
| `Messaging/Orders/OrderDispatch` | `Broadcast`, `Direct` ou `Topic` (a semântica observável dos três endpoints) |
| `Interfaces/Messaging/IOrderEventPublisher` | `PublishBroadcastAsync`, `PublishDirectAsync(routingKey)` e `PublishTopicAsync(routingKey)`, todos com `CancellationToken` |
| `Messaging/MessagePublishException` | Falha de publicação: mensagem **não** aceita |
| `Interfaces/Messaging/IOrderPlacedEventHandler` + `Messaging/IncomingMessageContext` | Processamento de uma mensagem recebida. O contexto (`Subscription`, `MessageId`, `IsRedelivery`) é neutro em relação ao transporte. |
| `Interfaces/Messaging/IProcessedMessageStore` | Registro de mensagens já processadas por subscription |
| `Features/Orders/Commands/PublishOrderCommand` | Gera um `OrderId` novo e escolhe o port pelo `OrderDispatch`. O `Id` enviado pelo cliente continua sendo ignorado, como antes. |
| `Features/Orders/EventHandlers/OrderPlacedEventHandler` | Caso de uso atual (registrar o pedido em log), com deduplicação e marcação só após sucesso |

Os ports não expõem `IConnection`, `IChannel`, `BasicDeliverEventArgs`, `ReadOnlyMemory<byte>`, nomes de configuração nem serialização. Um teste de arquitetura verifica isso.

**Significado do retorno do publisher:** a `Task` concluída quer dizer que o broker confirmou ter aceitado a mensagem (publisher confirm). Não quer dizer que alguma fila casou nem que algum consumidor processou. Os outros resultados possíveis:

- `MessagePublishException`: mensageria desabilitada, broker inacessível, nack ou timeout.
- `OperationCanceledException`: o chamador cancelou, e o resultado fica indeterminado.

## Contrato `order.placed` v1

| Aspecto | Definição |
|---|---|
| Nome lógico e versão | `order.placed`, versão 1. No AMQP, `type = "order.placed.v1"`. |
| Campos obrigatórios | `Id` (GUID ≠ vazio), `ProductName` (não nulo), `Quantity` (int), `Price` (decimal) |
| Formato e encoding | JSON UTF-8, nomes PascalCase, **byte a byte igual** ao payload anterior (teste fixa isso) |
| Campos desconhecidos | Ignorados (compatibilidade com adições futuras) |
| Leitura compatível | Mensagens sem `content_type` ou sem `type` (publicadas pela versão 6.x) são lidas como v1 |
| `content_type` | `application/json`. Parâmetros como `; charset=utf-8` são aceitos. |
| Identificador da mensagem | `message_id` = `OrderId` (formato `D`). Mensagens legadas sem `message_id` usam o `Id` do corpo. |
| Correlação e timestamp | Não adicionados: não há fonte de correlação nem necessidade demonstrada |
| Routing key | Fanout `""`, direct e topic recebem a routing key da query string |
| Payload inválido | Malformado, campo obrigatório ausente, `Id` vazio, `ProductName` nulo, nomes camelCase: vai para a DLQ com motivo `invalid-payload`, sem retry |
| Payload incompatível | `content_type` diferente de JSON ou `type` diferente de `order.placed.v1`: vai para a DLQ com motivo `incompatible-contract`, sem retry |

Metadados novos e por que entraram:

- `message_id`: chave de idempotência sem precisar ler o corpo.
- `type`: detectar versão incompatível.
- `content_type`: detectar formato incompatível.

Os três são testados. Consumidores antigos (6.x) ignoram propriedades e leem o mesmo corpo, então um rollback continua lendo as mensagens novas.

## Adapters e hosted services (Infrastructure.Messaging)

| Tipo | Responsabilidade |
|---|---|
| `RabbitMqSettings` | Opções tipadas (seção `RabbitMqSettings`), validadas no startup quando `Enabled` |
| `Topology/OrderMessagingTopology` | Dono único dos nomes e das declarações idempotentes |
| `Serialization/OrderPlacedMessageSerializer` | Codificação e decodificação do v1, com as regras de compatibilidade |
| `Publishing/RabbitMqOrderEventPublisher` | Publisher singleton com uma conexão longa e um canal com confirms, serializado por `SemaphoreSlim` |
| `Publishing/DisabledOrderEventPublisher` | Sempre lança `MessagePublishException` e nunca reporta sucesso |
| `Consuming/RabbitMqOrderConsumerService` | `BackgroundService` com uma conexão auto-recuperável e um canal por subscription |
| `Consuming/OrderDeliveryProcessor` | Política de entrega sem tipos RabbitMQ (ack, requeue ou dead-letter) |
| `Consuming/DeliveryAttemptTracker` | Contagem limitada de tentativas por mensagem |
| `Deduplication/InMemoryProcessedMessageStore` | Implementação limitada de `IProcessedMessageStore` |
| `RabbitMqConnections` | Factory de conexão, classificação de falhas de broker e teardown limitado |
| `ServiceRegistration.AddMessagingInfrastructure(configuration, useBroker)` | Composição |

### APIs 7.x usadas

- `CreateConnectionAsync` e `CreateChannelAsync(new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true))`.
- `BasicPublishAsync` com `BasicProperties`. Ela aguarda o confirm e lança `PublishException` em caso de nack ou return.
- `AsyncEventingBasicConsumer.ReceivedAsync`, `BasicConsumeAsync`, `BasicQosAsync`, `BasicAckAsync`, `BasicNackAsync` e `BasicCancelAsync`.
- `ExchangeDeclareAsync`, `QueueDeclareAsync` e `QueueBindAsync`.
- Encerramento com `CloseAsync`, `AbortAsync` e `DisposeAsync`.

O corpo (`ReadOnlyMemory<byte>`) é copiado com `ToArray()` na primeira instrução do callback, antes de qualquer `await`, e só a cópia é decodificada e repassada.

## Configuração e DI

Configuração na seção `RabbitMqSettings`. Os nomes antigos foram preservados e as chaves novas são opcionais, com default:

| Chave | Default | Observação |
|---|---|---|
| `HostName`, `Port` | `rabbitmq`, `5672` | Iguais a antes |
| `UserName`, `Password` | **vazio** | Default sensível removido do código. Obrigatórios quando `Enabled`; os appsettings existentes já os fornecem. |
| `Enabled` | `true` | Feature flag geral. Com `false`, publicar lança exceção e nenhum consumer sobe. |
| `ConsumersEnabled` | `true` | Desliga só os consumers, por exemplo para um rollback com outro consumidor |
| `ConnectionTimeout` | 5 s | Conexão e handshake |
| `PublishTimeout` | 5 s | Limite por publish, incluindo conexão e confirm |
| `ReconnectDelay`, `MaxReconnectDelay` | 1 s, 30 s | Backoff exponencial limitado; também é o intervalo de recovery |
| `MaxDeliveryAttempts` | 3 | Limite de retry |
| `RetryDelay` | 1 s | Multiplicado pela tentativa antes do requeue |
| `PrefetchCount` | 1 | Mensagens sem ack por consumer |

Registros:

- `Program.cs` chama `AddApplicationLayer()` (o parâmetro `registerIntegrationHostedServices` foi removido, porque a Application não tem mais hosted services) e `AddMessagingInfrastructure(configuration, useBroker: !isTestEnvironment)`.
- Fora do Test, com `Enabled`, são registrados `RabbitMqOrderEventPublisher` (singleton) e o hosted service `RabbitMqOrderConsumerService`.
- No Test, ou com `Enabled=false`, entra o `DisabledOrderEventPublisher` e nenhum hosted service de mensageria.
- `IOrderPlacedEventHandler` é scoped, registrado pela Application; o consumer cria um escopo por mensagem.
- `IProcessedMessageStore` é singleton em memória.

## Topologia preservada

- Exchanges, tipos, filas, bindings, routing keys, filas duráveis e mensagens persistentes são exatamente os do inventário.
- As exchanges continuam **não duráveis**. Redeclará-las como duráveis gera `PRECONDITION_FAILED` em um broker que já as tem. Isso é risco residual para a Fase 17.
- As filas continuam **sem argumentos**. Acrescentar `x-dead-letter-exchange` exigiria apagar e recriar filas existentes.
- Um teste declara a topologia exatamente como a versão 6.x fazia e depois aplica a nova declaração duas vezes, sem erro.

**Adições (não destrutivas):**

- Exchange `order_dead_letter_exchange` (direct, durável).
- Quatro DLQs duráveis: `queue_fanout_a.dead_letter`, `queue_fanout_b.dead_letter`, `urgent_orders.dead_letter` e `orders_brazil.dead_letter`, cada uma ligada à DLX com routing key igual ao nome da fila de origem.
- Nada é apagado automaticamente.

**Mudança observável deliberada:** o publisher agora declara a topologia completa, inclusive filas e bindings, no início de cada conexão. Antes, uma publicação feita antes de os consumers subirem era descartada porque as filas ainda não existiam. Agora ela fica retida na fila durável. Com `ConsumersEnabled=false`, as mensagens se acumulam nas filas.

## Política de ack, retry, poison e idempotência

| Situação | Ação |
|---|---|
| Handler concluiu | `basic.ack` (somente depois do processamento) |
| Handler lançou exceção e a tentativa é menor que `MaxDeliveryAttempts` | Espera `RetryDelay × tentativa` (cancelável) e faz `basic.nack(requeue: true)`. O atraso evita o loop quente, já que filas clássicas reentregam na hora. |
| Tentativa atingiu `MaxDeliveryAttempts` | Dead-letter com motivo `max-delivery-attempts-exceeded` |
| Payload inválido ou contrato incompatível | Dead-letter imediato, sem chamar o handler |
| Shutdown durante o processamento | Sem ack e sem nack: o fechamento do canal devolve a mensagem à fila, sem contar tentativa |
| Falha ao confirmar o dead-letter | `nack(requeue: true)` após `RetryDelay`, sem perda |
| Falha ao enviar o ack | Log de aviso; o broker reentrega (at-least-once) |

**Dead-letter:**

- A cópia é publicada com `mandatory: true` e confirm na DLX, e só depois o original recebe ack. Assim não há perda silenciosa.
- A cópia mantém corpo, `message_id`, `type`, `content_type` e persistência.
- A cópia ganha os headers `x-dead-letter-reason`, `x-original-exchange`, `x-original-routing-key` e `x-original-queue`.

**Inspeção e republicação** (manual):

- Ler `*.dead_letter` pela management UI ou com `rabbitmqadmin get`.
- Republicar o corpo para a exchange e routing key dos headers `x-original-*`, ou mover com um shovel para a fila de origem pela exchange default. Antes disso, corrigir a causa.

**Idempotência:**

- `OrderPlacedEventHandler` consulta `IProcessedMessageStore` por (subscription, `message_id`). Mensagem duplicada é aceita sem repetir o efeito, e a marcação só acontece depois do sucesso.
- O store é em memória, limitado a 10.000 entradas com descarte das mais antigas, por processo.
- **Limitação:** o store não é compartilhado entre instâncias e se perde no restart. É aceitável enquanto o único efeito é uma linha de log; antes de qualquer efeito não idempotente é preciso um inbox durável.
- O contador de tentativas também é por processo: em cada vida de processo o retry tem limite, mas um restart zera a contagem.

**Garantia implementada:** **at-least-once**. Os testes com broker demonstram reentrega após falha e após shutdown, ack só após sucesso e entrega duplicada sem efeito duplicado. Não há garantia de exactly-once.

## Reconexão, startup, shutdown e indisponibilidade

- **Construtores:** nenhum abre conexão. O consumer conecta em `ExecuteAsync` (depois de `Task.Yield`), e o publisher só no primeiro publish.
- **Consumer sem broker no início:** tenta de novo com backoff de `ReconnectDelay` dobrando até `MaxReconnectDelay`. Cada tentativa tem `ConnectionTimeout`. As tentativas param no cancelamento do host. O host sobe normalmente e os endpoints não relacionados funcionam.
- **Queda depois de conectado:** o `AutomaticRecoveryEnabled`/`TopologyRecoveryEnabled` do client recupera a conexão, a topologia e os consumers. Um canal fechado pelo broker com a conexão ainda viva não é recuperado pelo client, então a sessão inteira é recriada.
- **Publisher:** usa conexão sem auto-recovery. Qualquer falha descarta a conexão e o próximo publish reconecta. Cada publish tem `PublishTimeout`, e com o broker fora a chamada falha com `MessagePublishException`, que a WebApi responde como 500 Problem Details sem detalhe interno, o mesmo status de antes.
- **Shutdown:** o cancelamento do host interrompe novas entregas, envia `basic.cancel`, espera até 5 s pelas entregas em andamento, fecha os canais e a conexão com timeout e faz abort e dispose. O publisher fecha o canal e a conexão no `DisposeAsync`. Com o broker indisponível, o stop leva menos de 1 s nos testes.
- **Defeitos de código × falhas operacionais:**
  - Falhas de broker (exceções do namespace do client, `IOException`, `SocketException`, `TimeoutException`) são tratadas e repetidas.
  - Cancelamento é tratado à parte.
  - Exceções de programação no consumer não são engolidas: o `BackgroundService` falha e o host aplica o comportamento padrão (`StopHost`).
  - No publisher, `ArgumentNullException` e erros semelhantes propagam.
- **Logs:** registram o id do pedido, a subscription, o `message_id`, o tamanho do corpo, o tipo da exceção e o motivo. Nunca registram payload, senha, connection string nem headers.

## Upgrade do RabbitMQ.Client

| Item | Valor |
|---|---|
| Versão anterior | 6.8.1 |
| Versão selecionada | **7.2.2**, a última estável, publicada em 2026-08-05 segundo o NuGet. Sem aviso de vulnerabilidade ou depreciação. TFMs `net8.0` e `netstandard2.0`, compatíveis com `net10.0`. |
| Data da consulta | 2026-10-10 |
| Pacotes relacionados | Não há família a alinhar: `RabbitMQ.Client` é o único pacote do fornecedor. A Application trocou `Microsoft.Extensions.Hosting.Abstractions` por `Microsoft.Extensions.Logging.Abstractions` e `Microsoft.Extensions.Options` 10.0.12 (família já fixada). Para testes entrou `Testcontainers.RabbitMq` 4.16.0, igual ao `Testcontainers.PostgreSql` já fixado. |

### Fontes oficiais

- [NuGet `RabbitMQ.Client` 7.2.2](https://www.nuget.org/packages/RabbitMQ.Client/7.2.2) e o [índice de versões](https://api.nuget.org/v3-flatcontainer/rabbitmq.client/index.json).
- [Releases do repositório oficial](https://github.com/rabbitmq/rabbitmq-dotnet-client/releases): as notas da 7.2.x não registram breaking changes.
- [Guia de migração v7](https://github.com/rabbitmq/rabbitmq-dotnet-client/blob/main/v7-MIGRATION.md).
- [Guia da API .NET](https://www.rabbitmq.com/client-libraries/dotnet-api-guide) e a [página do client .NET](https://www.rabbitmq.com/client-libraries/dotnet): a 7.x consta como "supported" e não há matriz explícita de versões do broker.
- [Tutorial de publisher confirms](https://www.rabbitmq.com/tutorials/tutorial-seven-dotnet).
- [NuGet `Testcontainers.RabbitMq`](https://api.nuget.org/v3-flatcontainer/testcontainers.rabbitmq/index.json) e a [tag `rabbitmq:4.2.2` no Docker Hub](https://hub.docker.com/_/rabbitmq) (ativa, digest `sha256:3b98915b…660f`).
- A API exata foi conferida no XML de documentação do pacote 7.2.2 restaurado.

### Breaking changes tratados

- Toda a API passou a ser assíncrona com sufixo `Async`, com `CancellationToken` repassado.
- `IModel` virou `IChannel`.
- `CreateBasicProperties` saiu de cena em favor de `new BasicProperties`.
- `EventingBasicConsumer.Received` virou `AsyncEventingBasicConsumer.ReceivedAsync`.
- O corpo agora é `ReadOnlyMemory<byte>`, válido só durante o callback, e é copiado antes do primeiro `await`.
- Publisher confirms são configurados por `CreateChannelOptions`, e `BasicPublishAsync` aguarda o confirm, lançando `PublishException` em nack ou return.
- Timeouts de continuação agora aparecem como `OperationCanceledException`. O publisher distingue cancelamento do chamador de timeout interno pelo próprio token.
- `CreateConnectionAsync` embrulha timeouts em `BrokerUnreachableException`.

### Rollback do pacote

Restaurar o `PackageVersion` do RabbitMQ.Client para 6.8.1 exige reverter também o adapter, porque as APIs são incompatíveis. Fazer em conjunto:

1. Reverter o projeto `Template.Infrastructure.Messaging`, as mudanças na Application, a referência e o registro no WebApi, o `MessageController` e os testes.
2. Restaurar `Directory.Packages.props`.
3. Rodar restore e o gate.

Os contratos de mensagem e a topologia são compatíveis nos dois sentidos, então não há migração de dados. Para desligar sem reverter código, use `RabbitMqSettings:Enabled=false` (ou `ConsumersEnabled=false`).

**Servidor:** o broker de desenvolvimento (`rabbitmq:latest` no compose, que localmente resolve para 4.2.2) **não foi alterado**: imagem, versão major, feature flags e volumes continuam iguais. Os testes usam containers descartáveis `rabbitmq:4.2.2`, a mesma versão, sem volumes do usuário.

## Testes adicionados

- **Unitários (+14):**
  - `PublishOrderCommandHandlerTests`: port correto para fanout, direct e topic; repasse de routing key e token; mapeamento do contrato; id novo por publish; propagação de `MessagePublishException` e de cancelamento; routing key obrigatória.
  - `OrderPlacedEventHandlerTests`: ordem verificar → marcar com token; duplicata; deduplicação por subscription; falha do store não marca; cancelamento.
  - `ServiceRegistrationTests`: handler scoped e nenhum hosted service ou publisher na Application.
- **Arquitetura (+4 regras, exceções da Fase 9 removidas):**
  - Core sem plumbing de mensageria nem hosted services, e Domain sem mensageria.
  - `RabbitMQ.Client`, `using RabbitMQ`, `BackgroundService`/`AddHostedService` e `RabbitMqSettings` só no projeto Messaging.
  - Messaging referencia só a Application e implementa os ports, que não expõem tipos do client.
  - WebApi sem pacote nem plumbing RabbitMQ, só `AddMessagingInfrastructure`.
  - Mutações controladas novas para cada marcador.
  - A lista de pacotes e fontes de exceção da Application agora contém **apenas** o Elasticsearch (Fase 10).
- **Integração sem broker (+51):**
  - Serializer: compatibilidade byte a byte com o payload antigo, round-trip, headers legados, campos desconhecidos, 14 payloads inválidos e 4 incompatíveis.
  - `OrderDeliveryProcessor`: ack, fallback de `message_id`, inválido, incompatível, retry limitado → DLQ, reset de tentativas, shutdown sem contar tentativa, duplicata com o handler real, limites do tracker e do store.
  - Adapter com broker inacessível (loopback porta 1): publish falha dentro do timeout sem vazar credencial; cancelamento; routing key nula; desabilitado; composição de Test; consumer que não bloqueia o startup e para rápido; `ConsumersEnabled=false`; validação de opções; defaults sem credencial.
- **Funcionais (+10):**
  - Fanout, direct e topic preservam status, texto e `text/plain` e chamam o port certo com a routing key.
  - O id do cliente é substituído.
  - Routing key ausente continua respondendo 400.
  - Falha do port vira 500 Problem Details sem detalhe interno.
  - O cancelamento do cliente chega ao port.
  - Nenhum hosted service de mensageria roda no host de teste.
  - Sem broker, o publish falha e os endpoints de produto e Swagger continuam 200.
- **Broker real, opt-in (`Tests/Template.RabbitMq.Integration.Tests`, 17):**
  - Topologia idempotente e compatível com a da 6.x.
  - Fanout chega às duas filas; direct só com a chave exata; topic com chaves que casam e que não casam.
  - Propriedades e corpo persistidos.
  - Mensagem legada sem propriedades é consumida.
  - Ack só após sucesso, e requeue no shutdown com processamento em andamento.
  - Reentrega após falha transitória.
  - Limite de retry, DLQ com headers e ausência de requeue infinito.
  - Payload inválido e contrato incompatível vão para a DLQ.
  - Duplicata processada uma vez.
  - Rajada de 50 mensagens com corpo copiado sem corrupção.
  - Cancelamento do publish.
  - Liberação de consumers e conexões no shutdown (`rabbitmqctl list_connections`).
  - Startup sem broker, conexão quando o broker aparece, recuperação após restart do broker em porta fixa, e shutdown limitado com o broker fora.

Os testes com broker usam sinais e polling com timeout e diagnóstico (log capturado do adapter), sem esperas arbitrárias para obter os resultados. Há duas exceções deliberadas: uma janela de 1 s para provar a ausência de requeue infinito, e esperas para observar tentativas de reconexão.

### Defeitos encontrados pelos testes com broker e corrigidos

1. O sinal de prontidão do consumer era recriado no início da sessão, e quem esperava antes ficava preso. Agora o sinal só é renovado quando uma sessão termina.
2. No shutdown, o `nack(requeue)` de uma entrega cancelada devolvia a mensagem ao mesmo consumer antes do `basic.cancel` chegar, criando um loop curto de reentregas. Agora a entrega fica sem ack e o fechamento do canal a devolve à fila.

## Comandos e resultados

O gate final rodou em sequência, de 17:25:58 a 17:27:30:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-phase-validation.ps1
```

Ele usa o SDK fixado `C:\Users\alexa\.dotnet\dotnet.exe` (10.0.401), e todos os passos terminaram com exit=0. O script ganhou o passo `06b-test-rabbitmq`.

| Passo | Resultado |
|---|---|
| `restore TemplateCleanArchitecture.App.slnf` | OK |
| `build Template.WebApi` | OK, 0 avisos, 0 erros |
| Arquitetura | **16/16** (12 + 4 regras novas) |
| Unitários | **57/57** (43 + 14) |
| Integração EF InMemory + adapters | **78/78** (27 + 51) |
| Integração PostgreSQL 16.15 | **5/5** |
| **RabbitMQ real (`rabbitmq:4.2.2`, opt-in)** | **17/17** (cerca de 16 s; também 17/17 em três execuções isoladas anteriores) |
| Funcionais | **41/41** (31 + 10) |
| `list package --vulnerable --include-transitive` | Nenhum pacote vulnerável nos 14 projetos, incluindo `Template.Infrastructure.Messaging` e `Template.RabbitMq.Integration.Tests` |
| `docker compose config -q` | OK (compose não alterado) |
| Captura do OpenAPI | OK |
| `graphify update . --force` | OK: 2.255 nós, 4.647 arestas, 144 comunidades. Avisos: `tree_sitter_sql` ausente e rótulos de comunidade desatualizados (`graphify label`). A contagem inclui documentos novos não rastreados em `docs/refactoring`. |
| `git diff --check` | exit 0. Só avisos informativos de normalização LF/CRLF, sem erro de espaço em branco. |

Também passou o `.agents/skills/architecture-boundary-check/scripts/check-boundaries.ps1`: "Architecture boundary scan passed."

A primeira execução do gate (17:23) falhou somente no restore, porque o `.slnf` já listava `Template.RabbitMq.Integration.Tests` antes de ele ser adicionado ao `.sln`. Isso foi corrigido com `dotnet sln add`, e o gate inteiro rodou de novo, com o resultado acima.

### Smoke da WebApi real (fora do ambiente Test)

Script temporário (fora do repositório). A WebApi rodou com `dotnet run --no-build` em `Development`, com `UseInMemoryDatabase=true`, credenciais efêmeras via variáveis de ambiente, um RabbitMQ `rabbitmq:4.2.2` descartável e um Elasticsearch 8.17.4 descartável. O Elasticsearch foi necessário porque o sink Serilog/Elastic derruba o startup fora do Test quando o Elastic não está disponível. Esse bloqueio é anterior a esta fase e pertence à Fase 10.

| Cenário | Resultado |
|---|---|
| A. Broker disponível | Fanout, direct e topic → 200 com os textos exatos. As quatro filas com 1 consumer e 0 mensagens (consumidas e confirmadas); DLQs vazias; conexões `template-webapi-publisher` e `template-webapi-consumers` presentes; quatro eventos "Order … received" no log. |
| B. Broker parado com o app rodando | Publish → 500 Problem Details; `/swagger/v1/swagger.json` → 200; o consumer entra em recovery |
| C. App iniciado sem broker | Startup OK; Swagger → 200; publish → 500 |

### Comparação do OpenAPI com `docs/refactoring/baseline-phase-6/`

- `openapi-v1.json` e `openapi-v2.json`: **idênticos ao blob versionado**. A única diferença de bytes contra o arquivo do checkout é a quebra de linha (o Git aplica CRLF no checkout e a captura grava LF). Normalizando as quebras, os bytes são iguais, e a comparação JSON estrutural tem zero diferenças. O baseline não foi alterado.
- `health.json`: difere só em `totalDuration`. `product-v1-get.json` e `product-v2-get.json`: diferem só em `createdDateTime` (horário do seed). São os mesmos campos voláteis da Fase 8.
- Contrato HTTP: rotas, métodos, parâmetros, corpo `Order`, status, texto (inclusive o espaço inicial de `" TOPIC - ..."`), `text/plain`, deprecação v1 e ausência de autenticação foram preservados e são cobertos pelos testes funcionais.

## Riscos residuais

- **Exchanges não duráveis:** somem num restart do broker até serem redeclaradas. O publisher e o recovery as redeclaram, mas uma publicação de terceiros nesse intervalo falha. Corrigir exige recriar as exchanges (Fase 17).
- **Escopo do contador e do store:** são por processo. Com várias instâncias ou após restart, duplicatas e contagens não são compartilhadas. Enquanto o efeito é log, isso é aceitável; precisa de inbox durável antes de efeitos de negócio.
- **Bloqueio durante o retry:** o requeue com atraso segura a mensagem no consumer durante `RetryDelay × tentativa` (prefetch 1). Uma mensagem problemática atrasa as seguintes da mesma fila por alguns segundos, de forma limitada.
- **Rotas sem fila:** publicações que não casam com nenhum binding continuam confirmadas e descartadas (sem `mandatory`), para preservar o 200 dos endpoints v1.
- **Variáveis do compose:** o `docker-compose.yml` passa `RabbitMQ__Host`, `RabbitMQ__Port`, `RabbitMQ__Username` e `RabbitMQ__Password`, que **não** casam com a seção `RabbitMqSettings` (`HostName`, `Port`, `UserName`, `Password`). Hoje (antes e depois desta fase) o container usa os valores do appsettings. Para não alterar a origem das credenciais sem validar o compose completo, isso não foi mudado. A correção sugerida é mapear `RabbitMqSettings__HostName` e as demais chaves.
- **Credenciais nos appsettings:** saíram do código, mas os `appsettings*.json` versionados ainda trazem valores. Mover para user-secrets ou variáveis de ambiente é decisão operacional fora desta fase (Fase 12/13).
- **Sem health check do RabbitMQ:** não fazia parte do design adotado. A Fase 13 decide readiness.
- **Ambiente completo:** o `docker compose up` completo e um `dotnet run` contra o broker do compose não foram executados.

## Gates não executados

- **`docker compose up` completo** com a imagem `rabbitmq:latest` e o volume do compose: não executado, para não tocar no broker nem nos volumes do usuário. A compatibilidade com essa versão (4.2.2) foi coberta por containers descartáveis da mesma versão.
- **`dotnet run` contra os serviços reais do compose** (PostgreSQL, Redis, RabbitMQ, Elastic): não executado. O smoke usou banco em memória e fixtures descartáveis.
- **VSIX**: fora do gate, como já registrado (sem mudança no VSIX).
- **Teste com Redis real**: continua pendente desde a Fase 8 (fora do escopo).

## Confirmações

- O contrato HTTP e o OpenAPI v1/v2 foram preservados (ver comparação acima).
- As exceções de arquitetura da Fase 9 foram removidas; a única restante é a da Fase 10 (Elasticsearch: o pacote e `ElasticService.cs`).
- O servidor RabbitMQ não foi atualizado: imagem, versão, feature flags e volumes do compose estão intactos.
- Nenhum trabalho das Fases 10+ foi iniciado.
