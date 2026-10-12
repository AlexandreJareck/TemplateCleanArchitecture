using Shouldly;
using Template.Application.Messaging;
using Template.Application.Messaging.Orders;
using Template.Infrastructure.Messaging.Topology;
using Testcontainers.RabbitMq;

namespace Template.RabbitMq.Integration.Tests;

/// <summary>
/// Owns a dedicated broker on a fixed host port so it can be absent at startup, started later and restarted
/// without the adapter's endpoint changing.
/// </summary>
public sealed class BrokerAvailabilityTests : IAsyncLifetime
{
    private readonly int port = BrokerImage.FreeTcpPort();
    private readonly RecordingHandler handler = new();
    private RabbitMqContainer container = null!;

    public Task InitializeAsync()
    {
        container = BrokerImage.Builder().WithPortBinding(port, 5672).Build();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => container.DisposeAsync().AsTask();

    [Fact]
    public async Task Adapter_StartsWithoutBroker_ConnectsWhenItAppears_AndRecoversAfterARestart()
    {
        await using var harness = MessagingHarness.Create("127.0.0.1", port, handler);

        // 1. Broker unavailable: the host starts, publishing fails explicitly, consumers keep retrying.
        await harness.StartConsumersAsync(waitUntilConsuming: false);
        await Should.ThrowAsync<MessagePublishException>(() =>
            harness.Publisher.PublishBroadcastAsync(NewOrder(), CancellationToken.None));
        harness.Consumer.ExecuteTask!.IsCompleted.ShouldBeFalse();

        // 2. Broker appears: consumers connect on their own and messages flow.
        await container.StartAsync();
        await harness.Consumer.WaitUntilConsumingAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(60));
        var first = NewOrder();
        await PublishWithRetryAsync(harness, first);
        await handler.WhenHandledAsync(OrderMessagingTopology.FanoutA, first.OrderId);

        // 3. Broker restarts: automatic recovery restores the consumer connection; the publisher reconnects lazily.
        await container.StopAsync();
        await Should.ThrowAsync<MessagePublishException>(() =>
            harness.Publisher.PublishBroadcastAsync(NewOrder(), CancellationToken.None));
        await container.StartAsync();
        var second = NewOrder();
        await PublishWithRetryAsync(harness, second);
        await handler.WhenHandledAsync(OrderMessagingTopology.FanoutA, second.OrderId, TimeSpan.FromSeconds(60));
        await handler.WhenHandledAsync(OrderMessagingTopology.FanoutB, second.OrderId, TimeSpan.FromSeconds(60));

        // 4. Shutdown stays bounded.
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await harness.StopConsumersAsync();
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task Shutdown_WhileTheBrokerIsUnavailable_IsBoundedAndCancelsReconnection()
    {
        await using var harness = MessagingHarness.Create("127.0.0.1", port, handler);
        await harness.StartConsumersAsync(waitUntilConsuming: false);
        await Task.Delay(TimeSpan.FromSeconds(1)); // let several bounded reconnect attempts fail

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await harness.StopConsumersAsync();

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        harness.Consumer.ExecuteTask!.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Broker_RunsThePinnedVersion_WithKhepriAndEveryStableFeatureFlagEnabled()
    {
        await container.StartAsync();

        var version = await container.ExecAsync(["rabbitmq-diagnostics", "-q", "server_version"]);
        version.Stdout.Trim().ShouldBe(BrokerImage.ExpectedServerVersion);

        var flags = (await container.ExecAsync(["rabbitmqctl", "-q", "list_feature_flags", "name", "state", "stability"])).Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Skip(1)
            .Select(line => line.Split('\t'))
            .ToDictionary(columns => columns[0], columns => (State: columns[1], Stability: columns[2]));

        flags["khepri_db"].State.ShouldBe("enabled");
        flags.Where(flag => flag.Value.Stability is "required" or "stable" && flag.Value.State != "enabled")
            .Select(flag => flag.Key).ShouldBeEmpty();
    }

    [Fact]
    public async Task PersistentMessages_SurviveABrokerRestart_AndTheTopologyIsRedeclaredOnReconnect()
    {
        await container.StartAsync();
        var urgent = new OrderPlacedEvent(Guid.NewGuid(), "restart-urgent", 1, 1m);
        var broadcast = NewOrder();

        // 1. No consumers: the published messages stay ready in the durable queues.
        await using (var publisherOnly = MessagingHarness.Create("127.0.0.1", port, handler, new Dictionary<string, string?> { ["ConsumersEnabled"] = "false" }))
        {
            await publisherOnly.Publisher.PublishDirectAsync(urgent, "order.urgent", CancellationToken.None);
            await publisherOnly.Publisher.PublishBroadcastAsync(broadcast, CancellationToken.None);
        }

        // 2. Broker restart: persistent messages in durable queues survive, and so do their bindings to the durable
        //    dead-letter exchange. Whether the non-durable exchanges (kept for compatibility since Phase 9) survive is
        //    store-dependent, so it is not asserted; the application redeclares them idempotently on reconnect.
        await container.StopAsync();
        await container.StartAsync();
        await using (var connection = await new RabbitMQ.Client.ConnectionFactory { HostName = "127.0.0.1", Port = port, UserName = BrokerImage.UserName, Password = BrokerImage.Password }.CreateConnectionAsync())
        {
            await using var channel = await connection.CreateChannelAsync();
            (await channel.QueueDeclarePassiveAsync(OrderMessagingTopology.UrgentOrders.Queue)).MessageCount.ShouldBe(1u);
            (await channel.QueueDeclarePassiveAsync(OrderMessagingTopology.FanoutA.Queue)).MessageCount.ShouldBe(1u);
            (await channel.QueueDeclarePassiveAsync(OrderMessagingTopology.FanoutB.Queue)).MessageCount.ShouldBe(1u);
            (await channel.QueueDeclarePassiveAsync(OrderMessagingTopology.UrgentOrders.DeadLetterQueue)).MessageCount.ShouldBe(0u);
            await channel.ExchangeDeclarePassiveAsync(OrderMessagingTopology.DeadLetterExchange);
        }

        // 3. The application reconnects, redeclares its topology idempotently, and consumes the preserved messages
        //    (at-least-once: they are acknowledged only after the handler succeeded) plus a new one.
        await using var harness = MessagingHarness.Create("127.0.0.1", port, handler);
        await harness.StartConsumersAsync();
        var afterRestart = NewOrder();
        await PublishWithRetryAsync(harness, afterRestart);
        await handler.WhenHandledAsync(OrderMessagingTopology.UrgentOrders, urgent.OrderId, TimeSpan.FromSeconds(60));
        await handler.WhenHandledAsync(OrderMessagingTopology.FanoutA, broadcast.OrderId, TimeSpan.FromSeconds(60));
        await handler.WhenHandledAsync(OrderMessagingTopology.FanoutB, broadcast.OrderId, TimeSpan.FromSeconds(60));
        await handler.WhenHandledAsync(OrderMessagingTopology.FanoutA, afterRestart.OrderId, TimeSpan.FromSeconds(60));
        handler.Calls.Single(call => call.Event.OrderId == urgent.OrderId).Event.ShouldBe(urgent);
        await harness.StopConsumersAsync();
    }

    private static OrderPlacedEvent NewOrder() => new(Guid.NewGuid(), "availability", 1, 1m);

    /// <summary>The broker accepts connections slightly before it is fully ready; retry the publish within a bound.</summary>
    private static async Task PublishWithRetryAsync(MessagingHarness harness, OrderPlacedEvent order)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (true)
        {
            try
            {
                await harness.Publisher.PublishBroadcastAsync(order, CancellationToken.None);
                return;
            }
            catch (MessagePublishException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }
        }
    }
}
