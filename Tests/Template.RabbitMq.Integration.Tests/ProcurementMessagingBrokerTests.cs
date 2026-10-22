using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Shouldly;
using Template.Application.Interfaces.Messaging;
using Template.Application.Messaging.Procurement;
using Template.Domain.Requisitions;
using Template.Infrastructure.Messaging;
using Template.Infrastructure.Messaging.Consuming;
using Template.Infrastructure.Messaging.Topology;

namespace Template.RabbitMq.Integration.Tests;

[Collection(RabbitMqCollection.Name)]
public sealed class ProcurementMessagingBrokerTests(RabbitMqFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetTopologyAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ConfirmedPublish_IsPersistentRoutedAndByteCompatible()
    {
        await using var harness = CreateHarness(new Handler());
        var envelope = Envelope();

        await harness.Publisher.PublishAsync(envelope, CancellationToken.None);

        var delivery = await fixture.GetAsync(ProcurementMessagingTopology.ProjectionQueue);
        delivery.ShouldNotBeNull();
        delivery.BasicProperties.Persistent.ShouldBeTrue();
        delivery.BasicProperties.Type.ShouldBe(ProcurementMessagingTopology.ContractType);
        delivery.BasicProperties.MessageId.ShouldBe(envelope.EventId.ToString("D"));
        ProcurementIntegrationEventContract.Deserialize(delivery.Body.Span).EventId.ShouldBe(envelope.EventId);
    }

    [Fact]
    public async Task DuplicateDelivery_InvokesTheInboxUseCaseButAppliesTheProtectedEffectOnce()
    {
        var handler = new Handler();
        await using var harness = CreateHarness(handler);
        await harness.StartAsync();
        var envelope = Envelope();

        await harness.Publisher.PublishAsync(envelope, CancellationToken.None);
        await harness.Publisher.PublishAsync(envelope, CancellationToken.None);
        await Eventually.TrueAsync(() => Task.FromResult(handler.Calls >= 2), "both duplicate deliveries handled");

        handler.Effects.ShouldBe(1);
        (await fixture.InspectQueueAsync(ProcurementMessagingTopology.ProjectionQueue)).MessageCount.ShouldBe(0u);
    }

    [Fact]
    public async Task TransientFailures_AreRetriedWithDurableAttemptHeader_ThenAcknowledged()
    {
        var handler = new Handler { FailuresRemaining = 2 };
        await using var harness = CreateHarness(handler);
        await harness.StartAsync();
        await harness.Publisher.PublishAsync(Envelope(), CancellationToken.None);

        await Eventually.TrueAsync(() => Task.FromResult(handler.Calls >= 3 && handler.Effects == 1), "third attempt succeeds");
        (await fixture.InspectQueueAsync(ProcurementMessagingTopology.ProjectionDeadLetterQueue)).MessageCount.ShouldBe(0u);
    }

    [Fact]
    public async Task PoisonMessage_IsDeadLetteredAfterTheBoundedAttempts()
    {
        var handler = new Handler { FailuresRemaining = int.MaxValue };
        await using var harness = CreateHarness(handler);
        await harness.StartAsync();
        await harness.Publisher.PublishAsync(Envelope(), CancellationToken.None);

        await Eventually.TrueAsync(async () =>
            (await fixture.InspectQueueAsync(ProcurementMessagingTopology.ProjectionDeadLetterQueue)).MessageCount == 1,
            "poison event reaches the procurement DLQ");
        var dead = await fixture.GetAsync(ProcurementMessagingTopology.ProjectionDeadLetterQueue);
        dead.ShouldNotBeNull();
        Encoding.UTF8.GetString((byte[])dead.BasicProperties.Headers![RabbitMqProcurementConsumerService.DeadLetterReasonHeader]!)
            .ShouldBe("max-delivery-attempts-exceeded");
    }

    [Fact]
    public async Task GracefulStop_LeavesAnInterruptedDeliveryUnacknowledgedForRecovery()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler
        {
            Behavior = async cancellationToken =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        };
        await using var harness = CreateHarness(handler);
        await harness.StartAsync();
        await harness.Publisher.PublishAsync(Envelope(), CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        await harness.StopAsync();

        await Eventually.TrueAsync(async () =>
            (await fixture.InspectQueueAsync(ProcurementMessagingTopology.ProjectionQueue)).MessageCount == 1,
            "the interrupted delivery is returned to the durable queue");
        handler.Effects.ShouldBe(0);
    }

    [Fact]
    public async Task TwoConsumers_ContendForOneQueue_WithoutRepeatingProtectedEffects()
    {
        var handler = new Handler();
        await using var firstWorker = CreateHarness(handler);
        await using var secondWorker = CreateHarness(handler);
        await firstWorker.StartAsync();
        await secondWorker.StartAsync();

        foreach (var envelope in Enumerable.Range(0, 12).Select(_ => Envelope()))
            await firstWorker.Publisher.PublishAsync(envelope, CancellationToken.None);

        await Eventually.TrueAsync(() => Task.FromResult(handler.Effects == 12), "both workers drain the shared queue");
        handler.Calls.ShouldBe(12);
        (await fixture.InspectQueueAsync(ProcurementMessagingTopology.ProjectionQueue)).MessageCount.ShouldBe(0u);
    }

    private ProcurementHarness CreateHarness(Handler handler)
    {
        var values = new Dictionary<string, string?>
        {
            ["RabbitMqSettings:HostName"] = fixture.Host,
            ["RabbitMqSettings:Port"] = fixture.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RabbitMqSettings:UserName"] = BrokerImage.UserName,
            ["RabbitMqSettings:Password"] = BrokerImage.Password,
            ["RabbitMqSettings:ConnectionTimeout"] = "00:00:03",
            ["RabbitMqSettings:PublishTimeout"] = "00:00:05",
            ["RabbitMqSettings:ReconnectDelay"] = "00:00:00.100",
            ["RabbitMqSettings:MaxReconnectDelay"] = "00:00:01",
            ["RabbitMqSettings:MaxDeliveryAttempts"] = "3",
            ["RabbitMqSettings:RetryDelay"] = "00:00:00.025",
            ["RabbitMqSettings:PrefetchCount"] = "1"
        };
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(CapturingLogger<>));
        services.AddSingleton<IProcurementIntegrationEventHandler>(handler);
        services.AddProcurementMessagingInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), true);
        return new ProcurementHarness(services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
    }

    private static IntegrationEventEnvelope Envelope() => ProcurementIntegrationEventContract.FromDomainEvent(
        new RequisitionExpired(new DateTime(2027, 3, 1, 13, 0, 0, DateTimeKind.Utc), Guid.NewGuid(), 1), "rabbit-phase28");

    private sealed class Handler : IProcurementIntegrationEventHandler
    {
        private readonly ConcurrentDictionary<Guid, byte> processed = new();
        private int calls;
        private int effects;
        private int failuresRemaining;
        public int FailuresRemaining { get => Volatile.Read(ref failuresRemaining); set => Volatile.Write(ref failuresRemaining, value); }
        public int Calls => Volatile.Read(ref calls);
        public int Effects => Volatile.Read(ref effects);
        public Func<CancellationToken, Task>? Behavior { get; init; }

        public async Task<InboxApplyResult> HandleAsync(string consumerName, IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            if (Interlocked.Decrement(ref failuresRemaining) >= 0)
                throw new InvalidOperationException("synthetic-handler-failure");
            if (Behavior is not null)
                await Behavior(cancellationToken);
            if (!processed.TryAdd(envelope.EventId, 0))
                return InboxApplyResult.Duplicate;
            Interlocked.Increment(ref effects);
            return InboxApplyResult.Applied;
        }
    }

    private sealed class ProcurementHarness(ServiceProvider provider) : IAsyncDisposable
    {
        public IProcurementEventPublisher Publisher => provider.GetRequiredService<IProcurementEventPublisher>();
        public Task StartAsync() => provider.GetServices<IHostedService>().Single(service => service is RabbitMqProcurementConsumerService)
            .StartAsync(CancellationToken.None);
        public Task StopAsync() => provider.GetServices<IHostedService>().Single(service => service is RabbitMqProcurementConsumerService)
            .StopAsync(CancellationToken.None);
        public async ValueTask DisposeAsync()
        {
            foreach (var service in provider.GetServices<IHostedService>())
                await service.StopAsync(CancellationToken.None);
            await provider.DisposeAsync();
        }
    }
}
