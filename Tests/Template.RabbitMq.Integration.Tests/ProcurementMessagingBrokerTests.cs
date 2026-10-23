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
using Template.Domain.Sourcing;
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

    [Fact]
    public async Task T_SRC_022_RfqPublished_ReachesTheNotificationConsumer_AndTheProjection_EachOnce()
    {
        var handler = new Handler();
        await using var harness = CreateHarness(handler);
        await harness.StartAsync();
        var envelope = RfqPublishedEnvelope();

        await harness.Publisher.PublishAsync(envelope, CancellationToken.None);
        await harness.Publisher.PublishAsync(envelope, CancellationToken.None); // duplicate publish (crash after confirm)

        await Eventually.TrueAsync(() => Task.FromResult(handler.Calls >= 4), "both consumers handled both deliveries");
        handler.EffectsFor(ProcurementMessagingTopology.NotificationConsumer).ShouldBe(1);
        handler.EffectsFor(ProcurementMessagingTopology.ProjectionConsumer).ShouldBe(1);
        (await fixture.InspectQueueAsync(ProcurementMessagingTopology.NotificationQueue)).MessageCount.ShouldBe(0u);
    }

    [Fact]
    public async Task T_SRC_022_NotificationFailures_RetryOnlyTheNotificationQueue_ThenDeadLetterThere()
    {
        var handler = new Handler { FailingConsumer = ProcurementMessagingTopology.NotificationConsumer, FailuresRemaining = int.MaxValue };
        await using var harness = CreateHarness(handler);
        await harness.StartAsync();

        await harness.Publisher.PublishAsync(RfqPublishedEnvelope(), CancellationToken.None);

        await Eventually.TrueAsync(async () =>
            (await fixture.InspectQueueAsync(ProcurementMessagingTopology.NotificationDeadLetterQueue)).MessageCount == 1,
            "the notification poison event reaches its own DLQ");
        handler.EffectsFor(ProcurementMessagingTopology.ProjectionConsumer).ShouldBe(1); // the retries never fanned out again
        handler.CallsFor(ProcurementMessagingTopology.ProjectionConsumer).ShouldBe(1);
        (await fixture.InspectQueueAsync(ProcurementMessagingTopology.ProjectionDeadLetterQueue)).MessageCount.ShouldBe(0u);
    }

    [Fact]
    public async Task T_RPT_005_p_ReportReady_NotifiesTheRequester_WhileRequestedReachesOnlyTheProjection()
    {
        var handler = new Handler();
        await using var harness = CreateHarness(handler);
        await harness.StartAsync();
        var occurred = new DateTime(2027, 3, 1, 13, 0, 0, DateTimeKind.Utc);
        var jobId = Guid.NewGuid();
        var requester = Guid.NewGuid();
        var requested = ProcurementIntegrationEventContract.FromDomainEvent(
            new Template.Domain.Reporting.ReportRequested(occurred, jobId, Template.Domain.Reporting.ReportType.RequisitionPdf, requester), "rabbit-phase30");
        var ready = ProcurementIntegrationEventContract.FromDomainEvent(
            new Template.Domain.Reporting.ReportReady(occurred.AddSeconds(5), jobId, Template.Domain.Reporting.ReportType.RequisitionPdf, requester, Guid.NewGuid()),
            "rabbit-phase30");

        await harness.Publisher.PublishAsync(requested, CancellationToken.None);
        await harness.Publisher.PublishAsync(ready, CancellationToken.None);
        await harness.Publisher.PublishAsync(ready, CancellationToken.None); // duplicate after a confirmed publish

        await Eventually.TrueAsync(() => Task.FromResult(handler.Calls >= 5), "projection twice + once, notification twice");
        handler.CallsFor(ProcurementMessagingTopology.NotificationConsumer).ShouldBe(2); // only report.ready, delivered twice
        handler.EffectsFor(ProcurementMessagingTopology.NotificationConsumer).ShouldBe(1);
        handler.EffectsFor(ProcurementMessagingTopology.ProjectionConsumer).ShouldBe(2); // requested and ready
        (await fixture.InspectQueueAsync(ProcurementMessagingTopology.NotificationQueue)).MessageCount.ShouldBe(0u);
    }

    private static IntegrationEventEnvelope RfqPublishedEnvelope() => ProcurementIntegrationEventContract.FromDomainEvent(
        new RfqPublished(new DateTime(2027, 3, 1, 13, 0, 0, DateTimeKind.Utc), Guid.NewGuid(), "RFQ-2027-000001", Guid.NewGuid(),
            new DateTime(2027, 3, 4, 13, 0, 0, DateTimeKind.Utc), [Guid.NewGuid(), Guid.NewGuid()], SourcingMethod.Competitive), "rabbit-phase29");

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
        private readonly ConcurrentDictionary<(string Consumer, Guid EventId), byte> processed = new();
        private readonly ConcurrentDictionary<string, int> callsByConsumer = new();
        private int calls;
        private int effects;
        private int failuresRemaining;
        public int FailuresRemaining { get => Volatile.Read(ref failuresRemaining); set => Volatile.Write(ref failuresRemaining, value); }
        public int Calls => Volatile.Read(ref calls);
        public int Effects => Volatile.Read(ref effects);
        public Func<CancellationToken, Task>? Behavior { get; init; }

        /// <summary>When set, only this consumer fails (Phase 29 per-queue retry proof).</summary>
        public string? FailingConsumer { get; init; }

        public int EffectsFor(string consumer) => processed.Keys.Count(key => key.Consumer == consumer);

        public int CallsFor(string consumer) => callsByConsumer.GetValueOrDefault(consumer);

        public async Task<InboxApplyResult> HandleAsync(string consumerName, IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            callsByConsumer.AddOrUpdate(consumerName, 1, (_, value) => value + 1);
            if ((FailingConsumer is null || FailingConsumer == consumerName) && Interlocked.Decrement(ref failuresRemaining) >= 0)
                throw new InvalidOperationException("synthetic-handler-failure");
            if (Behavior is not null)
                await Behavior(cancellationToken);
            if (!processed.TryAdd((consumerName, envelope.EventId), 0))
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
