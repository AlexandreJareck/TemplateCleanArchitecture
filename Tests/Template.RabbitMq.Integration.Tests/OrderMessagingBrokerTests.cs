using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Shouldly;
using Template.Application.Features.Orders.EventHandlers;
using Template.Application.Interfaces.Messaging;
using Template.Application.Messaging;
using Template.Application.Messaging.Orders;
using Template.Infrastructure.Messaging.Consuming;
using Template.Infrastructure.Messaging.Serialization;
using Template.Infrastructure.Messaging.Topology;

namespace Template.RabbitMq.Integration.Tests;

[Collection(RabbitMqCollection.Name)]
public sealed class OrderMessagingBrokerTests(RabbitMqFixture broker) : IAsyncLifetime
{
    private readonly RecordingHandler handler = new();

    public Task InitializeAsync() => broker.ResetTopologyAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static OrderPlacedEvent NewOrder(string name = "Keyboard") => new(Guid.NewGuid(), name, 2, 49.90m);

    private MessagingHarness Harness(IReadOnlyDictionary<string, string?>? overrides = null, Action<IServiceCollection>? configure = null) =>
        MessagingHarness.Create(broker.Host, broker.Port, handler, overrides, configure);

    private Task WaitUntilEmptyAsync(string queue) =>
        Eventually.TrueAsync(async () => (await broker.InspectQueueAsync(queue)).MessageCount == 0, $"{queue} drained");

    [Fact]
    public async Task Topology_IsIdempotent_AndCompatibleWithThePrePhase9Declarations()
    {
        await using var connection = await broker.ConnectAsync();
        await using var channel = await connection.CreateChannelAsync();

        // Exactly what the 6.x code declared: ExchangeDeclare(name, type) and durable, non-exclusive queues without arguments.
        await channel.ExchangeDeclareAsync(OrderMessagingTopology.FanoutExchange, ExchangeType.Fanout);
        await channel.ExchangeDeclareAsync(OrderMessagingTopology.DirectExchange, ExchangeType.Direct);
        await channel.ExchangeDeclareAsync(OrderMessagingTopology.TopicExchange, ExchangeType.Topic);
        foreach (var subscription in OrderMessagingTopology.Subscriptions)
        {
            await channel.QueueDeclareAsync(subscription.Queue, durable: true, exclusive: false, autoDelete: false);
            await channel.QueueBindAsync(subscription.Queue, subscription.Exchange, subscription.BindingKey);
        }

        await OrderMessagingTopology.DeclareAsync(channel, CancellationToken.None);
        await OrderMessagingTopology.DeclareAsync(channel, CancellationToken.None);

        channel.IsOpen.ShouldBeTrue();
        foreach (var subscription in OrderMessagingTopology.Subscriptions)
        {
            await channel.QueueDeclarePassiveAsync(subscription.Queue);
            await channel.QueueDeclarePassiveAsync(subscription.DeadLetterQueue);
        }

        await channel.ExchangeDeclarePassiveAsync(OrderMessagingTopology.DeadLetterExchange);
    }

    [Fact]
    public async Task Fanout_ReachesBothFanoutQueues_Only()
    {
        await using var harness = Harness();
        await harness.StartConsumersAsync();
        var order = NewOrder();

        await harness.Publisher.PublishBroadcastAsync(order, CancellationToken.None);
        await handler.WhenHandledAsync(OrderMessagingTopology.FanoutA, order.OrderId);
        await handler.WhenHandledAsync(OrderMessagingTopology.FanoutB, order.OrderId);
        await harness.StopConsumersAsync();

        handler.Calls.Select(call => call.Context.Subscription).Order().ShouldBe(["fanout-a", "fanout-b"]);
        handler.Calls.ShouldAllBe(call => call.Event == order);
        foreach (var subscription in OrderMessagingTopology.Subscriptions)
            (await broker.InspectQueueAsync(subscription.Queue)).MessageCount.ShouldBe(0u);
    }

    [Fact]
    public async Task Direct_RoutesOnlyTheExactKey()
    {
        await using var harness = Harness();
        await harness.StartConsumersAsync();
        var unmatched = NewOrder("unmatched");
        var matched = NewOrder("matched");

        await harness.Publisher.PublishDirectAsync(unmatched, "order.normal", CancellationToken.None); // confirmed, then dropped
        await harness.Publisher.PublishDirectAsync(matched, "order.urgent", CancellationToken.None);
        await handler.WhenHandledAsync(OrderMessagingTopology.UrgentOrders, matched.OrderId);
        await harness.StopConsumersAsync();

        handler.Calls.Single().Event.ShouldBe(matched);
        handler.Calls.Single().Context.Subscription.ShouldBe(OrderMessagingTopology.UrgentOrders.Name);
    }

    [Fact]
    public async Task Topic_RoutesMatchingKeys_AndIgnoresNonMatchingKeys()
    {
        await using var harness = Harness();
        await harness.StartConsumersAsync();
        var sp = NewOrder("sp");
        var rj = NewOrder("rj");

        await harness.Publisher.PublishTopicAsync(NewOrder("usa"), "order.usa.ny", CancellationToken.None);
        await harness.Publisher.PublishTopicAsync(NewOrder("too-deep"), "order.brazil.sp.capital", CancellationToken.None);
        await harness.Publisher.PublishTopicAsync(sp, "order.brazil.sp", CancellationToken.None);
        await harness.Publisher.PublishTopicAsync(rj, "order.brazil.rj", CancellationToken.None);
        await handler.WhenHandledAsync(OrderMessagingTopology.BrazilOrders, rj.OrderId);
        await harness.StopConsumersAsync();

        handler.Calls.Select(call => call.Event).ShouldBe([sp, rj]);
        handler.Calls.ShouldAllBe(call => call.Context.Subscription == OrderMessagingTopology.BrazilOrders.Name);
    }

    [Fact]
    public async Task PublishedMessages_ArePersistent_WithTheLegacyBodyAndDocumentedProperties()
    {
        await using var harness = Harness(new Dictionary<string, string?> { ["ConsumersEnabled"] = "false" });
        var order = NewOrder();

        await harness.Publisher.PublishDirectAsync(order, "order.urgent", CancellationToken.None);
        var message = await broker.GetAsync(OrderMessagingTopology.UrgentOrders.Queue);

        message.ShouldNotBeNull();
        message.BasicProperties.Persistent.ShouldBeTrue();
        message.BasicProperties.ContentType.ShouldBe("application/json");
        message.BasicProperties.Type.ShouldBe("order.placed.v1");
        message.BasicProperties.MessageId.ShouldBe(order.OrderId.ToString("D"));
        message.Exchange.ShouldBe(OrderMessagingTopology.DirectExchange);
        message.RoutingKey.ShouldBe("order.urgent");
        Encoding.UTF8.GetString(message.Body.Span).ShouldBe(
            $$"""{"Id":"{{order.OrderId}}","ProductName":"Keyboard","Quantity":2,"Price":49.90}""");
    }

    [Fact]
    public async Task LegacyMessageWithoutProperties_IsStillConsumed()
    {
        await using var harness = Harness();
        await harness.StartConsumersAsync();
        var order = NewOrder("legacy");

        await broker.PublishRawAsync(
            OrderMessagingTopology.TopicExchange,
            "order.brazil.mg",
            OrderPlacedMessageSerializer.Serialize(order),
            new BasicProperties { Persistent = true });
        await handler.WhenHandledAsync(OrderMessagingTopology.BrazilOrders, order.OrderId);

        handler.Calls.Single().Context.MessageId.ShouldBe(order.OrderId.ToString("D"));
    }

    [Fact]
    public async Task Message_IsAcknowledgedOnlyAfterSuccessfulProcessing()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Behavior = async (_, _, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
        };
        await using var harness = Harness();
        await harness.StartConsumersAsync();
        var order = NewOrder();

        await harness.Publisher.PublishDirectAsync(order, "order.urgent", CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var whileProcessing = await broker.InspectQueueAsync(OrderMessagingTopology.UrgentOrders.Queue);

        // Graceful shutdown while the handler is still running: the delivery must go back to the queue.
        await harness.StopConsumersAsync();
        var afterShutdown = await broker.InspectQueueAsync(OrderMessagingTopology.UrgentOrders.Queue);

        whileProcessing.MessageCount.ShouldBe(0u); // delivered, unacknowledged
        afterShutdown.MessageCount.ShouldBe(1u); // not acknowledged, requeued
        afterShutdown.ConsumerCount.ShouldBe(0u);

        release.TrySetResult();
        handler.Behavior = null;
        await using var second = Harness();
        await second.StartConsumersAsync();
        await Eventually.TrueAsync(() => Task.FromResult(handler.Calls.Count == 2), "redelivered message processed");
        await second.StopConsumersAsync(); // waits for the in-flight acknowledgement

        var final = await broker.InspectQueueAsync(OrderMessagingTopology.UrgentOrders.Queue);
        final.MessageCount.ShouldBe(0u); // acknowledged after success, nothing redelivered
        handler.Calls.Count(call => call.Event == order).ShouldBe(2);
        handler.Calls.Last().Context.IsRedelivery.ShouldBeTrue();
    }

    [Fact]
    public async Task TransientFailure_IsRedelivered_AndThenAcknowledged()
    {
        var failures = 1;
        handler.Behavior = (_, _, _) => Interlocked.Decrement(ref failures) >= 0
            ? throw new InvalidOperationException("transient")
            : Task.CompletedTask;
        await using var harness = Harness();
        await harness.StartConsumersAsync();
        var order = NewOrder();

        await harness.Publisher.PublishDirectAsync(order, "order.urgent", CancellationToken.None);
        await Eventually.TrueAsync(() => Task.FromResult(handler.Calls.Count == 2), "second delivery processed");
        await harness.StopConsumersAsync();

        handler.Calls.Select(call => call.Context.IsRedelivery).ShouldBe([false, true]);
        (await broker.InspectQueueAsync(OrderMessagingTopology.UrgentOrders.Queue)).MessageCount.ShouldBe(0u);
        (await broker.InspectQueueAsync(OrderMessagingTopology.UrgentOrders.DeadLetterQueue)).MessageCount.ShouldBe(0u);
    }

    [Fact]
    public async Task PersistentFailure_StopsAtTheRetryLimit_AndIsDeadLettered_WithoutInfiniteRequeue()
    {
        handler.Behavior = (_, _, _) => throw new InvalidOperationException("always fails");
        await using var harness = Harness();
        await harness.StartConsumersAsync();
        var order = NewOrder();

        await harness.Publisher.PublishDirectAsync(order, "order.urgent", CancellationToken.None);
        await Eventually.TrueAsync(
            async () => (await broker.InspectQueueAsync(OrderMessagingTopology.UrgentOrders.DeadLetterQueue)).MessageCount == 1,
            "message dead-lettered");
        await Task.Delay(TimeSpan.FromSeconds(1)); // a requeue loop would add calls during this window
        await harness.StopConsumersAsync();

        handler.Calls.Count.ShouldBe(3);
        (await broker.InspectQueueAsync(OrderMessagingTopology.UrgentOrders.Queue)).MessageCount.ShouldBe(0u);
        var deadLetter = await broker.GetAsync(OrderMessagingTopology.UrgentOrders.DeadLetterQueue);
        deadLetter.ShouldNotBeNull();
        deadLetter.BasicProperties.Persistent.ShouldBeTrue();
        deadLetter.BasicProperties.MessageId.ShouldBe(order.OrderId.ToString("D"));
        Header(deadLetter, RabbitMqOrderConsumerService.DeadLetterReasonHeader).ShouldBe(OrderDeliveryProcessor.RetriesExhaustedReason);
        Header(deadLetter, RabbitMqOrderConsumerService.OriginalQueueHeader).ShouldBe("urgent_orders");
        Header(deadLetter, RabbitMqOrderConsumerService.OriginalExchangeHeader).ShouldBe(OrderMessagingTopology.DirectExchange);
        Header(deadLetter, RabbitMqOrderConsumerService.OriginalRoutingKeyHeader).ShouldBe("order.urgent");
        deadLetter.Body.ToArray().ShouldBe(OrderPlacedMessageSerializer.Serialize(order));
    }

    [Fact]
    public async Task InvalidPayload_IsDeadLettered_WithoutReachingTheHandler()
    {
        await using var harness = Harness();
        await harness.StartConsumersAsync();

        await broker.PublishRawAsync(OrderMessagingTopology.DirectExchange, "order.urgent", "not json"u8.ToArray(), new BasicProperties { Persistent = true });
        await Eventually.TrueAsync(
            async () => (await broker.InspectQueueAsync(OrderMessagingTopology.UrgentOrders.DeadLetterQueue)).MessageCount == 1,
            "invalid payload dead-lettered");

        handler.Calls.ShouldBeEmpty();
        var deadLetter = await broker.GetAsync(OrderMessagingTopology.UrgentOrders.DeadLetterQueue);
        Header(deadLetter!, RabbitMqOrderConsumerService.DeadLetterReasonHeader).ShouldBe(OrderDeliveryProcessor.InvalidPayloadReason);
        Encoding.UTF8.GetString(deadLetter!.Body.Span).ShouldBe("not json");
    }

    [Fact]
    public async Task IncompatibleContract_IsDeadLettered_WithoutReachingTheHandler()
    {
        await using var harness = Harness();
        await harness.StartConsumersAsync();

        await broker.PublishRawAsync(
            OrderMessagingTopology.TopicExchange,
            "order.brazil.sp",
            OrderPlacedMessageSerializer.Serialize(NewOrder()),
            new BasicProperties { Persistent = true, ContentType = "application/json", Type = "order.placed.v2" });
        await Eventually.TrueAsync(
            async () => (await broker.InspectQueueAsync(OrderMessagingTopology.BrazilOrders.DeadLetterQueue)).MessageCount == 1,
            "incompatible contract dead-lettered");

        handler.Calls.ShouldBeEmpty();
        var deadLetter = await broker.GetAsync(OrderMessagingTopology.BrazilOrders.DeadLetterQueue);
        Header(deadLetter!, RabbitMqOrderConsumerService.DeadLetterReasonHeader).ShouldBe(OrderDeliveryProcessor.IncompatibleContractReason);
        deadLetter!.BasicProperties.Type.ShouldBe("order.placed.v2");
    }

    [Fact]
    public async Task DuplicateMessage_IsAcknowledged_AndProcessedOnce_ByTheApplicationHandler()
    {
        var store = new CountingProcessedMessageStore();
        var applicationHandler = new OrderPlacedEventHandler(store, NullLogger<OrderPlacedEventHandler>.Instance);
        await using var harness = MessagingHarness.Create(broker.Host, broker.Port, applicationHandler);
        await harness.StartConsumersAsync();
        var order = NewOrder();
        var body = OrderPlacedMessageSerializer.Serialize(order);
        var properties = new BasicProperties { Persistent = true, MessageId = order.OrderId.ToString("D"), Type = OrderPlacedMessageSerializer.MessageType };

        await broker.PublishRawAsync(OrderMessagingTopology.DirectExchange, "order.urgent", body, properties);
        await broker.PublishRawAsync(OrderMessagingTopology.DirectExchange, "order.urgent", body, properties);
        await Eventually.TrueAsync(() => Task.FromResult(store.Checks == 2), "both copies delivered");
        await WaitUntilEmptyAsync(OrderMessagingTopology.UrgentOrders.Queue);
        await harness.StopConsumersAsync();

        store.Marks.ShouldBe(1);
        (await broker.InspectQueueAsync(OrderMessagingTopology.UrgentOrders.Queue)).MessageCount.ShouldBe(0u);
    }

    [Fact]
    public async Task BurstOfMessages_IsDecodedFromCopiedBodies_WithoutCorruption()
    {
        await using var harness = Harness(new Dictionary<string, string?> { ["PrefetchCount"] = "20" });
        await harness.StartConsumersAsync();
        var orders = Enumerable.Range(0, 50).Select(index => NewOrder($"burst-{index}")).ToArray();

        foreach (var order in orders)
            await harness.Publisher.PublishTopicAsync(order, "order.brazil.sp", CancellationToken.None);
        await Eventually.TrueAsync(() => Task.FromResult(handler.Calls.Count == orders.Length), "all burst messages handled");

        handler.Calls.Select(call => call.Event).ShouldBe(orders, ignoreOrder: true);
    }

    [Fact]
    public async Task Publish_WithCancelledToken_IsCancelled_AndNothingIsSent()
    {
        await using var harness = Harness(new Dictionary<string, string?> { ["ConsumersEnabled"] = "false" });
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            harness.Publisher.PublishDirectAsync(NewOrder(), "order.urgent", cancellation.Token));

        await harness.Publisher.PublishDirectAsync(NewOrder("after"), "order.urgent", CancellationToken.None);
        (await broker.InspectQueueAsync(OrderMessagingTopology.UrgentOrders.Queue)).MessageCount.ShouldBe(1u);
    }

    [Fact]
    public async Task Shutdown_ReleasesConsumersAndConnections()
    {
        var harness = Harness();
        await harness.StartConsumersAsync();
        await harness.Publisher.PublishBroadcastAsync(NewOrder(), CancellationToken.None);
        await handler.WhenAnyHandledAsync(OrderMessagingTopology.FanoutA);

        foreach (var subscription in OrderMessagingTopology.Subscriptions)
            (await broker.InspectQueueAsync(subscription.Queue)).ConsumerCount.ShouldBe(1u);
        (await ListConnectionNamesAsync()).ShouldContain(name => name.Contains("template-webapi-consumers"));

        var consumer = harness.Consumer;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await harness.DisposeAsync();
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));

        consumer.ExecuteTask!.IsCompletedSuccessfully.ShouldBeTrue();
        foreach (var subscription in OrderMessagingTopology.Subscriptions)
            (await broker.InspectQueueAsync(subscription.Queue)).ConsumerCount.ShouldBe(0u);
        await Eventually.TrueAsync(
            async () => !(await ListConnectionNamesAsync()).Any(name => name.Contains("template-webapi")),
            "adapter connections closed");
    }

    private async Task<string[]> ListConnectionNamesAsync()
    {
        var result = await broker.Container.ExecAsync(["rabbitmqctl", "list_connections", "client_properties", "--silent"]);
        return result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string? Header(BasicGetResult message, string name) =>
        message.BasicProperties.Headers is { } headers && headers.TryGetValue(name, out var value) && value is byte[] bytes
            ? Encoding.UTF8.GetString(bytes)
            : null;

    private sealed class CountingProcessedMessageStore : IProcessedMessageStore
    {
        private readonly HashSet<string> processed = [];
        private int checks;
        private int marks;

        public int Checks => Volatile.Read(ref checks);

        public int Marks => Volatile.Read(ref marks);

        public Task<bool> WasProcessedAsync(string subscription, string messageId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref checks);
            lock (processed)
                return Task.FromResult(processed.Contains($"{subscription}|{messageId}"));
        }

        public Task MarkProcessedAsync(string subscription, string messageId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref marks);
            lock (processed)
                processed.Add($"{subscription}|{messageId}");
            return Task.CompletedTask;
        }
    }
}
