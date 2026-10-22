using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Template.Application.Features.Orders.EventHandlers;
using Template.Application.Interfaces.Messaging;
using Template.Application.Logging;
using Template.Application.Messaging;
using Template.Application.Messaging.Orders;
using Template.Infrastructure.Messaging;
using Template.Infrastructure.Messaging.Consuming;
using Template.Infrastructure.Messaging.Deduplication;
using Template.Infrastructure.Messaging.Serialization;
using Template.Infrastructure.Messaging.Topology;

namespace Template.Integration.Tests.Messaging;

public sealed class OrderDeliveryProcessorTests
{
    private static readonly OrderPlacedEvent Order = new(Guid.Parse("a3b8d1b6-0b3b-4b1a-9c1a-1a2b3c4d5e6f"), "Keyboard", 1, 10m);
    private static readonly OrderSubscription Subscription = OrderMessagingTopology.UrgentOrders;

    private static ReceivedDelivery Delivery(string? messageId = "message-1", bool redelivered = false, byte[]? body = null, string? contentType = OrderPlacedMessageSerializer.ContentType, string? type = OrderPlacedMessageSerializer.MessageType) =>
        new(body ?? OrderPlacedMessageSerializer.Serialize(Order), contentType, type, messageId, redelivered);

    private static OrderDeliveryProcessor CreateProcessor(IOrderPlacedEventHandler handler, int maxAttempts = 3, ILogger<OrderDeliveryProcessor>? logger = null)
    {
        var provider = new ServiceCollection().AddScoped(_ => handler).BuildServiceProvider();
        var settings = new RabbitMqSettings { MaxDeliveryAttempts = maxAttempts, RetryDelay = TimeSpan.Zero };
        return new OrderDeliveryProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new DeliveryAttemptTracker(),
            Options.Create(settings),
            logger ?? NullLogger<OrderDeliveryProcessor>.Instance);
    }

    [Fact]
    public async Task Success_IsAcknowledged_WithTheDecodedEventAndContext()
    {
        var handler = new ScriptedHandler();
        var processor = CreateProcessor(handler);

        var decision = await processor.ProcessAsync(Subscription, Delivery(redelivered: true), CancellationToken.None);

        decision.ShouldBe(DeliveryDecision.Ack);
        handler.Calls.Single().ShouldBe((Order, new IncomingMessageContext("direct-urgent-orders", "message-1", true)));
    }

    [Fact]
    public async Task Processing_RunsInsideACorrelationScope_WithTheValidatedMessageId()
    {
        var logger = new ScopeCapturingLogger();
        var handler = new ScriptedHandler(failures: 1);

        await CreateProcessor(handler, logger: logger).ProcessAsync(Subscription, Delivery(messageId: "message-1"), CancellationToken.None);

        var retry = logger.Entries.Single(entry => entry.EventId == LogEvents.MessageRetryScheduled);
        retry.Scope["CorrelationId"].ShouldBe("message-1");
        retry.Scope["Subscription"].ShouldBe(Subscription.Name);
    }

    [Theory]
    [InlineData("bad id with spaces")]
    [InlineData("Bearer eyJhbGciOiJIUzI1NiJ9.payload.signature")]
    [InlineData(null)]
    public async Task UnusableInboundMessageId_IsReplacedByTheOrderId_AndNeverLogged(string? inbound)
    {
        var logger = new ScopeCapturingLogger();
        var handler = new ScriptedHandler(failures: 1);
        var oversized = inbound ?? new string('x', CorrelationId.MaxLength + 1);

        await CreateProcessor(handler, logger: logger).ProcessAsync(Subscription, Delivery(messageId: oversized), CancellationToken.None);

        handler.Calls.Single().Context.MessageId.ShouldBe(Order.OrderId.ToString("D"));
        var retry = logger.Entries.Single(entry => entry.EventId == LogEvents.MessageRetryScheduled);
        retry.Scope["CorrelationId"].ShouldBe(Order.OrderId.ToString("D"));
        logger.Entries.ShouldAllBe(entry => !entry.Text.Contains(oversized));
    }

    [Fact]
    public async Task RejectedMessageWithAnUnusableId_GetsANewSafeCorrelationId()
    {
        var logger = new ScopeCapturingLogger();

        var decision = await CreateProcessor(new ScriptedHandler(), logger: logger)
            .ProcessAsync(Subscription, Delivery(messageId: "bad id\nwith newline", body: Encoding.UTF8.GetBytes("not json")), CancellationToken.None);

        decision.Action.ShouldBe(DeliveryAction.DeadLetter);
        var rejected = logger.Entries.Single(entry => entry.EventId == LogEvents.MessageRejected);
        var correlationId = rejected.Scope["CorrelationId"]?.ToString();
        CorrelationId.IsValid(correlationId).ShouldBeTrue();
        rejected.Text.ShouldNotContain("bad id");
    }

    [Fact]
    public async Task LegacyMessageWithoutMessageId_UsesTheOrderIdForDeduplication()
    {
        var handler = new ScriptedHandler();

        await CreateProcessor(handler).ProcessAsync(Subscription, Delivery(messageId: null, contentType: null, type: null), CancellationToken.None);

        handler.Calls.Single().Context.MessageId.ShouldBe(Order.OrderId.ToString("D"));
    }

    [Fact]
    public async Task InvalidPayload_IsDeadLettered_WithoutCallingTheHandler()
    {
        var handler = new ScriptedHandler();

        var decision = await CreateProcessor(handler).ProcessAsync(Subscription, Delivery(body: Encoding.UTF8.GetBytes("{not json")), CancellationToken.None);

        decision.ShouldBe(DeliveryDecision.DeadLetter(OrderDeliveryProcessor.InvalidPayloadReason));
        handler.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task IncompatibleContract_IsDeadLettered_WithoutCallingTheHandler()
    {
        var handler = new ScriptedHandler();

        var decision = await CreateProcessor(handler).ProcessAsync(Subscription, Delivery(type: "order.placed.v2"), CancellationToken.None);

        decision.ShouldBe(DeliveryDecision.DeadLetter(OrderDeliveryProcessor.IncompatibleContractReason));
        handler.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task TransientFailure_IsRequeued_UntilTheRetryLimit_ThenDeadLettered()
    {
        var handler = new ScriptedHandler(failures: int.MaxValue);
        var processor = CreateProcessor(handler, maxAttempts: 3);

        var decisions = new List<DeliveryDecision>();
        for (var attempt = 0; attempt < 3; attempt++)
            decisions.Add(await processor.ProcessAsync(Subscription, Delivery(redelivered: attempt > 0), CancellationToken.None));

        decisions.ShouldBe([
            DeliveryDecision.Requeue,
            DeliveryDecision.Requeue,
            DeliveryDecision.DeadLetter(OrderDeliveryProcessor.RetriesExhaustedReason)
        ]);
        handler.Calls.Count.ShouldBe(3);
    }

    [Fact]
    public async Task TransientFailure_ThenSuccess_IsAcknowledged_AndTheAttemptCountResets()
    {
        var handler = new ScriptedHandler(failures: 1);
        var processor = CreateProcessor(handler, maxAttempts: 2);

        (await processor.ProcessAsync(Subscription, Delivery(), CancellationToken.None)).ShouldBe(DeliveryDecision.Requeue);
        (await processor.ProcessAsync(Subscription, Delivery(redelivered: true), CancellationToken.None)).ShouldBe(DeliveryDecision.Ack);

        handler.FailNextCalls(1);
        (await processor.ProcessAsync(Subscription, Delivery(), CancellationToken.None)).ShouldBe(DeliveryDecision.Requeue);
    }

    [Fact]
    public async Task Shutdown_DuringProcessing_RequeuesWithoutCountingAnAttempt()
    {
        using var stopping = new CancellationTokenSource();
        var handler = new ScriptedHandler(onCall: async token =>
        {
            await stopping.CancelAsync();
            token.ThrowIfCancellationRequested();
        });
        var processor = CreateProcessor(handler, maxAttempts: 1);

        var decision = await processor.ProcessAsync(Subscription, Delivery(), stopping.Token);

        decision.ShouldBe(DeliveryDecision.Requeue);
    }

    [Fact]
    public async Task Duplicate_IsAcknowledged_WithoutRepeatingTheSideEffect()
    {
        var store = new CountingStore();
        var handler = new OrderPlacedEventHandler(store, NullLogger<OrderPlacedEventHandler>.Instance);
        var processor = CreateProcessor(handler);

        (await processor.ProcessAsync(Subscription, Delivery(), CancellationToken.None)).ShouldBe(DeliveryDecision.Ack);
        (await processor.ProcessAsync(Subscription, Delivery(redelivered: true), CancellationToken.None)).ShouldBe(DeliveryDecision.Ack);

        store.Marks.ShouldBe(1);
    }

    [Fact]
    public async Task AttemptTracker_And_ProcessedStore_AreBounded()
    {
        var tracker = new DeliveryAttemptTracker(capacity: 2);
        tracker.RecordFailure("s", "1").ShouldBe(1);
        tracker.RecordFailure("s", "1").ShouldBe(2);
        tracker.RecordFailure("s", "2");
        tracker.RecordFailure("s", "3"); // evicts "1"
        tracker.RecordFailure("s", "1").ShouldBe(1);
        tracker.Forget("s", "1");
        tracker.RecordFailure("s", "1").ShouldBe(1);

        var store = new InMemoryProcessedMessageStore(capacity: 2);
        await store.MarkProcessedAsync("s", "1", CancellationToken.None);
        await store.MarkProcessedAsync("s", "1", CancellationToken.None); // duplicate is an idempotent no-op
        await store.MarkProcessedAsync("s", "2", CancellationToken.None);
        await store.MarkProcessedAsync("s", "3", CancellationToken.None);
        (await store.WasProcessedAsync("s", "1", CancellationToken.None)).ShouldBeFalse();
        (await store.WasProcessedAsync("s", "3", CancellationToken.None)).ShouldBeTrue();
        (await store.WasProcessedAsync("other", "3", CancellationToken.None)).ShouldBeFalse();
    }

    private sealed class ScriptedHandler(int failures = 0, Func<CancellationToken, Task>? onCall = null) : IOrderPlacedEventHandler
    {
        private int remainingFailures = failures;

        public List<(OrderPlacedEvent Event, IncomingMessageContext Context)> Calls { get; } = [];

        public void FailNextCalls(int count) => remainingFailures = count;

        public async Task HandleAsync(OrderPlacedEvent orderPlaced, IncomingMessageContext context, CancellationToken cancellationToken)
        {
            Calls.Add((orderPlaced, context));
            if (onCall is not null)
                await onCall(cancellationToken);
            if (remainingFailures-- > 0)
                throw new InvalidOperationException("transient failure");
        }
    }

    private sealed class CountingStore : IProcessedMessageStore
    {
        private readonly InMemoryProcessedMessageStore inner = new();

        public int Marks { get; private set; }

        public Task<bool> WasProcessedAsync(string subscription, string messageId, CancellationToken cancellationToken) =>
            inner.WasProcessedAsync(subscription, messageId, cancellationToken);

        public Task MarkProcessedAsync(string subscription, string messageId, CancellationToken cancellationToken)
        {
            Marks++;
            return inner.MarkProcessedAsync(subscription, messageId, cancellationToken);
        }
    }

    private sealed class ScopeCapturingLogger : ILogger<OrderDeliveryProcessor>
    {
        private readonly AsyncLocal<IReadOnlyDictionary<string, object>?> scope = new();

        public List<(EventId EventId, string Text, IReadOnlyDictionary<string, object> Scope)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            var previous = scope.Value;
            scope.Value = state as IReadOnlyDictionary<string, object> ?? (state as IDictionary<string, object>)?.AsReadOnly();
            return new Restore(() => scope.Value = previous);
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((eventId, formatter(state, exception), scope.Value ?? new Dictionary<string, object>()));

        private sealed class Restore(Action restore) : IDisposable
        {
            public void Dispose() => restore();
        }
    }
}
