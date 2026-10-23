using System.Diagnostics;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Shouldly;
using Template.Application.Interfaces.Messaging;
using Template.Application.Messaging;
using Template.Application.Messaging.Orders;
using Template.Application.Messaging.Procurement;
using Template.Domain.Requisitions;
using Template.Infrastructure.Messaging;
using Template.Infrastructure.Messaging.Consuming;
using Template.Infrastructure.Messaging.Publishing;
using Template.Infrastructure.Messaging.Topology;

namespace Template.Integration.Tests.Messaging;

public sealed class ProcurementMessagingAdapterTests
{
    private static readonly DateTime Occurred = new(2027, 3, 1, 13, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Registration_WithoutBroker_AddsNoPublisherOrConsumer()
    {
        await using var provider = BuildProvider(new Handler(), useBroker: false);

        provider.GetService<IProcurementEventPublisher>().ShouldBeNull();
        provider.GetServices<IHostedService>().ShouldBeEmpty();
    }

    [Fact]
    public void BrokerEndpoint_ReflectsEnabledDisabledAndDefaultConfiguration()
    {
        var enabled = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMqSettings:Enabled"] = "true",
            ["RabbitMqSettings:HostName"] = "broker.internal",
            ["RabbitMqSettings:Port"] = "5673"
        }).Build();
        var disabled = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMqSettings:Enabled"] = "false"
        }).Build();

        var endpoint = ServiceRegistration.GetMessageBrokerEndpoint(enabled)!;
        endpoint.Host.ShouldBe("broker.internal");
        endpoint.Port.ShouldBe(5673);
        ServiceRegistration.GetMessageBrokerEndpoint(disabled).ShouldBeNull();
        ServiceRegistration.GetMessageBrokerEndpoint(new ConfigurationBuilder().Build())!.Port.ShouldBe(5672);
    }

    [Fact]
    public async Task DisabledConsumer_StopsImmediately_AndPublisherRejectsTheEvent()
    {
        await using var provider = BuildProvider(new Handler(), enabled: false);
        var consumer = provider.GetServices<IHostedService>().Single();
        await consumer.StartAsync(CancellationToken.None);
        await ((BackgroundService)consumer).ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));

        await Should.ThrowAsync<MessagePublishException>(() =>
            provider.GetRequiredService<IProcurementEventPublisher>().PublishAsync(Envelope(), CancellationToken.None));
    }

    [Fact]
    public async Task ConsumersDisabled_StopsImmediately_WhilePublisherRemainsConfigured()
    {
        await using var provider = BuildProvider(new Handler(), consumersEnabled: false);
        var consumer = provider.GetServices<IHostedService>().Single();

        await consumer.StartAsync(CancellationToken.None);
        await ((BackgroundService)consumer).ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));

        provider.GetRequiredService<IProcurementEventPublisher>().ShouldBeOfType<RabbitMqProcurementEventPublisher>();
    }

    [Fact]
    public async Task Consumer_UnreachableBroker_RetriesWithoutBlockingStartup_ThenStopsPromptly()
    {
        await using var provider = BuildProvider(new Handler());
        var consumer = provider.GetServices<IHostedService>().Single();
        var service = (BackgroundService)consumer;
        var stopwatch = Stopwatch.StartNew();

        await consumer.StartAsync(CancellationToken.None);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));
        await Task.Delay(TimeSpan.FromMilliseconds(350));
        service.ExecuteTask!.IsCompleted.ShouldBeFalse();

        stopwatch.Restart();
        await consumer.StopAsync(CancellationToken.None);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        service.ExecuteTask.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Publisher_UnreachableBroker_FailsWithinTheConfiguredBound_AndDisposesIdempotently()
    {
        await using var provider = BuildProvider(new Handler());
        var publisher = provider.GetRequiredService<RabbitMqProcurementEventPublisher>();
        var stopwatch = Stopwatch.StartNew();

        await Should.ThrowAsync<MessagePublishException>(() => publisher.PublishAsync(Envelope(), CancellationToken.None));
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
        await publisher.DisposeAsync();
        await publisher.DisposeAsync();
        await Should.ThrowAsync<ObjectDisposedException>(() => publisher.PublishAsync(Envelope(), CancellationToken.None));
    }

    [Fact]
    public async Task Publisher_OpenChannel_PublishesTwiceWithTheCachedConfirmedChannel()
    {
        await using var provider = BuildProvider(new Handler());
        var publisher = provider.GetRequiredService<RabbitMqProcurementEventPublisher>();
        var channel = DispatchProxy.Create<IChannel, ChannelProxy>();
        typeof(RabbitMqProcurementEventPublisher).GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(publisher, channel);

        await publisher.PublishAsync(Envelope(), CancellationToken.None);
        await publisher.PublishAsync(Envelope(), CancellationToken.None);

        var proxy = (ChannelProxy)(object)channel;
        proxy.Calls.Count(name => name == nameof(IChannel.BasicPublishAsync)).ShouldBe(2);
    }

    [Fact]
    public async Task ProcurementTopology_DeclaresEveryDurableEntityAndBinding()
    {
        var channel = DispatchProxy.Create<IChannel, ChannelProxy>();

        await ProcurementMessagingTopology.DeclareAsync(channel, CancellationToken.None);

        var calls = ((ChannelProxy)(object)channel).Calls;
        calls.Count(name => name == nameof(IChannel.ExchangeDeclareAsync)).ShouldBe(2);
        calls.Count(name => name == nameof(IChannel.QueueDeclareAsync)).ShouldBe(2);
        calls.Count(name => name == nameof(IChannel.QueueBindAsync)).ShouldBe(2);
    }

    [Fact]
    public async Task LegacyPublisher_OpenChannelAndConnection_ExerciseConfirmedPublishAndGracefulDispose()
    {
        var settings = Settings();
        settings.UserName = "phase28-test";
        settings.Password = "phase28-test-only";
        var publisher = new RabbitMqOrderEventPublisher(
            Options.Create(settings), NullLogger<RabbitMqOrderEventPublisher>.Instance);
        var channel = DispatchProxy.Create<IChannel, ChannelProxy>();
        var connection = DispatchProxy.Create<IConnection, ConnectionProxy>();
        typeof(RabbitMqOrderEventPublisher).GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(publisher, channel);
        typeof(RabbitMqOrderEventPublisher).GetField("connection", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(publisher, connection);

        await publisher.PublishBroadcastAsync(new OrderPlacedEvent(Guid.NewGuid(), "Phase 28", 1, 1m), CancellationToken.None);
        await publisher.DisposeAsync();
        await publisher.DisposeAsync();

        ((ChannelProxy)(object)channel).Calls.ShouldContain(nameof(IChannel.BasicPublishAsync));
        ((ConnectionProxy)(object)connection).Calls.ShouldContain(nameof(IConnection.CloseAsync));
    }

    [Fact]
    public async Task LegacyPublisher_ChannelIoFailure_IsTranslatedAndResetsTheTransport()
    {
        var settings = Settings();
        settings.UserName = "phase28-test";
        settings.Password = "phase28-test-only";
        var publisher = new RabbitMqOrderEventPublisher(
            Options.Create(settings), NullLogger<RabbitMqOrderEventPublisher>.Instance);
        var channel = DispatchProxy.Create<IChannel, ChannelProxy>();
        ((ChannelProxy)(object)channel).FailureByMethod[nameof(IChannel.BasicPublishAsync)] = new IOException("synthetic");
        var connection = DispatchProxy.Create<IConnection, ConnectionProxy>();
        typeof(RabbitMqOrderEventPublisher).GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(publisher, channel);
        typeof(RabbitMqOrderEventPublisher).GetField("connection", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(publisher, connection);

        await Should.ThrowAsync<MessagePublishException>(() => publisher.PublishBroadcastAsync(
            new OrderPlacedEvent(Guid.NewGuid(), "Phase 28", 1, 1m), CancellationToken.None));
        await publisher.DisposeAsync();

        ((ConnectionProxy)(object)connection).Calls.ShouldContain(nameof(IConnection.CloseAsync));
    }

    [Theory]
    [InlineData(InboxApplyResult.Applied)]
    [InlineData(InboxApplyResult.Duplicate)]
    public async Task Processor_AcknowledgesAValidAppliedOrDuplicateEvent(InboxApplyResult result)
    {
        var handler = new Handler { Result = result };
        await using var provider = ProcessorProvider(handler);
        var channel = new Channel();

        await Processor(provider).ProcessAsync(Delivery(), channel, Settings(), CancellationToken.None);

        handler.Calls.ShouldBe(1);
        channel.Acknowledged.ShouldBe([42ul]);
        channel.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task Processor_InvalidContract_IsCopiedToTheDeadLetterQueueBeforeAck()
    {
        await using var provider = ProcessorProvider(new Handler());
        var channel = new Channel();
        var envelope = Envelope();
        var properties = Properties(envelope);
        properties.ContentType = "text/plain";
        properties.Headers = null;
        var invalid = Delivery(envelope, properties);

        await Processor(provider).ProcessAsync(invalid, channel, Settings(), CancellationToken.None);

        channel.Published.Single().Exchange.ShouldBe(ProcurementMessagingTopology.DeadLetterExchange);
        channel.Published.Single().Properties.Headers![ProcurementDeliveryProcessor.DeadLetterReasonHeader]
            .ShouldBe("invalid-or-incompatible-contract");
        channel.Acknowledged.ShouldBe([42ul]);
    }

    [Fact]
    public async Task Processor_MessageIdMismatch_IsClassifiedAsAnInvalidContract()
    {
        await using var provider = ProcessorProvider(new Handler());
        var channel = new Channel();
        var envelope = Envelope();
        var properties = Properties(envelope);
        properties.MessageId = Guid.NewGuid().ToString("D");

        await Processor(provider).ProcessAsync(Delivery(envelope, properties), channel, Settings(), CancellationToken.None);

        channel.Published.Single().Properties.Headers![ProcurementDeliveryProcessor.DeadLetterReasonHeader]
            .ShouldBe("invalid-or-incompatible-contract");
    }

    [Fact]
    public async Task Processor_TransientFailure_RepublishesWithDurableAttemptBeforeAck()
    {
        await using var provider = ProcessorProvider(new Handler { Failure = new InvalidOperationException("synthetic") });
        var channel = new Channel();

        await Processor(provider).ProcessAsync(Delivery(), channel, Settings(), CancellationToken.None);

        var published = channel.Published.Single();
        published.Exchange.ShouldBe(ProcurementMessagingTopology.Exchange);
        published.Properties.Persistent.ShouldBeTrue();
        published.Properties.Headers![ProcurementDeliveryProcessor.AttemptHeader].ShouldBe(1);
        channel.Acknowledged.ShouldBe([42ul]);
    }

    [Theory]
    [InlineData((byte)2)]
    [InlineData((short)2)]
    [InlineData(2)]
    [InlineData(2L)]
    public async Task Processor_FinalAttempt_IsDeadLettered_ForSupportedHeaderNumberTypes(object attempt)
    {
        await using var provider = ProcessorProvider(new Handler { Failure = new InvalidOperationException("synthetic") });
        var channel = new Channel();
        var envelope = Envelope();
        var properties = Properties(envelope);
        properties.Headers = new Dictionary<string, object?> { [ProcurementDeliveryProcessor.AttemptHeader] = attempt };

        await Processor(provider).ProcessAsync(Delivery(envelope, properties), channel, Settings(), CancellationToken.None);

        channel.Published.Single().Exchange.ShouldBe(ProcurementMessagingTopology.DeadLetterExchange);
        channel.Published.Single().Properties.Headers![ProcurementDeliveryProcessor.DeadLetterReasonHeader]
            .ShouldBe("max-delivery-attempts-exceeded");
    }

    public static TheoryData<object, int> RetryHeaderCases => new()
    {
        { Encoding.UTF8.GetBytes("1"), 2 },
        { Encoding.UTF8.GetBytes("not-a-number"), 1 },
        { (long)int.MaxValue + 1, 1 },
        { "unsupported", 1 }
    };

    [Theory]
    [MemberData(nameof(RetryHeaderCases))]
    public async Task Processor_UnsupportedOrTextAttemptHeaders_AreHandledSafely(object value, int expectedAttempt)
    {
        await using var provider = ProcessorProvider(new Handler { Failure = new InvalidOperationException("synthetic") });
        var channel = new Channel();
        var envelope = Envelope();
        var properties = Properties(envelope);
        properties.Headers = new Dictionary<string, object?> { [ProcurementDeliveryProcessor.AttemptHeader] = value };

        await Processor(provider).ProcessAsync(Delivery(envelope, properties), channel, Settings(), CancellationToken.None);

        channel.Published.Single().Properties.Headers![ProcurementDeliveryProcessor.AttemptHeader].ShouldBe(expectedAttempt);
    }

    [Fact]
    public async Task Processor_NullHeaders_StartAtTheFirstRetry()
    {
        await using var provider = ProcessorProvider(new Handler { Failure = new InvalidOperationException("synthetic") });
        var channel = new Channel();
        var envelope = Envelope();
        var properties = Properties(envelope);
        properties.Headers = null;

        await Processor(provider).ProcessAsync(Delivery(envelope, properties), channel, Settings(), CancellationToken.None);

        channel.Published.Single().Properties.Headers![ProcurementDeliveryProcessor.AttemptHeader].ShouldBe(1);
    }

    [Fact]
    public async Task Processor_HandlerCancellationDuringShutdown_DoesNotSettleTheDelivery()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new Handler
        {
            Behavior = token =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(token);
            }
        };
        await using var provider = ProcessorProvider(handler);
        var channel = new Channel();

        await Processor(provider).ProcessAsync(Delivery(), channel, Settings(), cancellation.Token);

        channel.Acknowledged.ShouldBeEmpty();
        channel.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task Processor_SettleCancellation_LeavesTheDeliveryUnacknowledged()
    {
        await using var provider = ProcessorProvider(new Handler { Failure = new InvalidOperationException("synthetic") });
        var channel = new Channel { PublishFailure = new OperationCanceledException() };

        await Processor(provider).ProcessAsync(Delivery(), channel, Settings(), CancellationToken.None);

        channel.Acknowledged.ShouldBeEmpty();
    }

    [Fact]
    public async Task Processor_AlreadyStopping_DoesNotAcquireOrSettleTheDelivery()
    {
        var handler = new Handler();
        await using var provider = ProcessorProvider(handler);
        var channel = new Channel();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Processor(provider).ProcessAsync(Delivery(), channel, Settings(), cancellation.Token);

        handler.Calls.ShouldBe(0);
        channel.Acknowledged.ShouldBeEmpty();
    }

    private static ServiceProvider BuildProvider(
        Handler handler,
        bool useBroker = true,
        bool enabled = true,
        bool consumersEnabled = true)
    {
        var values = new Dictionary<string, string?>
        {
            ["RabbitMqSettings:Enabled"] = enabled.ToString(),
            ["RabbitMqSettings:ConsumersEnabled"] = consumersEnabled.ToString(),
            ["RabbitMqSettings:HostName"] = "127.0.0.1",
            ["RabbitMqSettings:Port"] = "1",
            ["RabbitMqSettings:UserName"] = "phase28-test",
            ["RabbitMqSettings:Password"] = "phase28-test-only",
            ["RabbitMqSettings:ConnectionTimeout"] = "00:00:01",
            ["RabbitMqSettings:PublishTimeout"] = "00:00:02",
            ["RabbitMqSettings:ReconnectDelay"] = "00:00:00.050",
            ["RabbitMqSettings:MaxReconnectDelay"] = "00:00:00.100"
        };
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IProcurementIntegrationEventHandler>(handler);
        services.AddProcurementMessagingInfrastructure(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(), useBroker);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static ServiceProvider ProcessorProvider(Handler handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProcurementIntegrationEventHandler>(handler);
        return services.BuildServiceProvider();
    }

    private static ProcurementDeliveryProcessor Processor(ServiceProvider provider) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ProcurementDeliveryProcessor>.Instance);

    private static RabbitMqSettings Settings() => new()
    {
        MaxDeliveryAttempts = 3,
        RetryDelay = TimeSpan.Zero,
        PublishTimeout = TimeSpan.FromSeconds(1)
    };

    private static IntegrationEventEnvelope Envelope() => ProcurementIntegrationEventContract.FromDomainEvent(
        new RequisitionExpired(Occurred, Guid.NewGuid(), 1), "phase28-integration");

    private static BasicProperties Properties(IntegrationEventEnvelope envelope) => new()
    {
        Persistent = true,
        ContentType = "application/json",
        Type = ProcurementMessagingTopology.ContractType,
        MessageId = envelope.EventId.ToString("D"),
        CorrelationId = envelope.CorrelationId,
        Headers = new Dictionary<string, object?>()
    };

    private static ProcurementDelivery Delivery(
        IntegrationEventEnvelope? envelope = null,
        BasicProperties? properties = null)
    {
        envelope ??= Envelope();
        return new ProcurementDelivery(
            ProcurementIntegrationEventContract.Serialize(envelope), properties ?? Properties(envelope),
            ProcurementMessagingTopology.Exchange, envelope.EventType, 42);
    }

    private sealed class Handler : IProcurementIntegrationEventHandler
    {
        public InboxApplyResult Result { get; init; } = InboxApplyResult.Applied;
        public Exception? Failure { get; init; }
        public Func<CancellationToken, Task<InboxApplyResult>>? Behavior { get; init; }
        public int Calls { get; private set; }

        public Task<InboxApplyResult> HandleAsync(
            string consumerName, IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
        {
            Calls++;
            if (Behavior is not null)
                return Behavior(cancellationToken);
            if (Failure is not null)
                throw Failure;
            return Task.FromResult(Result);
        }
    }

    private sealed class Channel : IProcurementDeliveryChannel
    {
        public List<ulong> Acknowledged { get; } = [];
        public List<Published> Published { get; } = [];
        public Exception? PublishFailure { get; init; }

        public ValueTask AckAsync(ulong deliveryTag, CancellationToken cancellationToken)
        {
            Acknowledged.Add(deliveryTag);
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishAsync(
            string exchange, string routingKey, BasicProperties properties, ReadOnlyMemory<byte> body,
            CancellationToken cancellationToken)
        {
            if (PublishFailure is not null)
                return ValueTask.FromException(PublishFailure);
            Published.Add(new Published(exchange, routingKey, properties, body.ToArray()));
            return ValueTask.CompletedTask;
        }
    }

    private sealed record Published(string Exchange, string RoutingKey, BasicProperties Properties, byte[] Body);

    private class ChannelProxy : DispatchProxy
    {
        public List<string> Calls { get; } = [];
        public Dictionary<string, Exception> FailureByMethod { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var method = targetMethod ?? throw new InvalidOperationException();
            Calls.Add(method.Name);
            if (FailureByMethod.TryGetValue(method.Name, out var failure))
            {
                if (method.ReturnType == typeof(ValueTask))
                    return ValueTask.FromException(failure);
                throw failure;
            }
            if (method.Name == "get_IsOpen")
                return true;
            if (method.ReturnType == typeof(ValueTask))
                return ValueTask.CompletedTask;
            if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
                return Activator.CreateInstance(method.ReturnType);
            if (method.ReturnType == typeof(Task))
                return Task.CompletedTask;
            if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = method.ReturnType.GetGenericArguments()[0];
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(resultType)
                    .Invoke(null, [resultType.IsValueType ? Activator.CreateInstance(resultType) : null]);
            }
            return method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
        }
    }

    private class ConnectionProxy : DispatchProxy
    {
        public List<string> Calls { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var method = targetMethod ?? throw new InvalidOperationException();
            Calls.Add(method.Name);
            if (method.Name == "get_IsOpen")
                return true;
            if (method.ReturnType == typeof(ValueTask))
                return ValueTask.CompletedTask;
            if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
                return Activator.CreateInstance(method.ReturnType);
            return method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
        }
    }
}
