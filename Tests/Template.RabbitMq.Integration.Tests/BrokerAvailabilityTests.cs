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
