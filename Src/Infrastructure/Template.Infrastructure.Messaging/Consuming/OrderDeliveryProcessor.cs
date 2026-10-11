using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Template.Application.Interfaces.Messaging;
using Template.Application.Logging;
using Template.Application.Messaging;
using Template.Infrastructure.Messaging.Serialization;
using Template.Infrastructure.Messaging.Topology;

namespace Template.Infrastructure.Messaging.Consuming;

/// <summary>A delivery whose body was already copied out of the client-owned buffer.</summary>
public sealed record ReceivedDelivery(byte[] Body, string? ContentType, string? MessageType, string? MessageId, bool Redelivered);

public enum DeliveryAction
{
    Ack,
    Requeue,
    DeadLetter
}

public readonly record struct DeliveryDecision(DeliveryAction Action, string? DeadLetterReason = null)
{
    public static DeliveryDecision Ack { get; } = new(DeliveryAction.Ack);

    public static DeliveryDecision Requeue { get; } = new(DeliveryAction.Requeue);

    public static DeliveryDecision DeadLetter(string reason) => new(DeliveryAction.DeadLetter, reason);
}

/// <summary>
/// Transport-independent delivery policy: decode, run the application handler in its own scope and decide between
/// ack (processed), requeue (transient failure or shutdown) and dead-letter (invalid, incompatible or retries exhausted).
/// </summary>
public sealed class OrderDeliveryProcessor(
    IServiceScopeFactory scopeFactory,
    DeliveryAttemptTracker attempts,
    IOptions<RabbitMqSettings> options,
    ILogger<OrderDeliveryProcessor> logger)
{
    public const string InvalidPayloadReason = "invalid-payload";
    public const string IncompatibleContractReason = "incompatible-contract";
    public const string RetriesExhaustedReason = "max-delivery-attempts-exceeded";

    public async Task<DeliveryDecision> ProcessAsync(
        OrderSubscription subscription,
        ReceivedDelivery delivery,
        CancellationToken cancellationToken)
    {
        var decoded = OrderPlacedMessageSerializer.Decode(delivery.Body, delivery.ContentType, delivery.MessageType);
        if (decoded.Status != DecodeStatus.Decoded)
        {
            var reason = decoded.Status == DecodeStatus.IncompatibleContract ? IncompatibleContractReason : InvalidPayloadReason;
            // The inbound id is untrusted: a missing or malformed one is replaced by a new safe identifier.
            var rejectedId = CorrelationId.Normalize(delivery.MessageId, CorrelationId.New());
            using var rejectedScope = BeginCorrelationScope(rejectedId, subscription);
            logger.LogWarning(
                LogEvents.MessageRejected,
                "Rejecting message {MessageId} on {Subscription}: {Reason} ({BodyLength} bytes, type {MessageType})",
                rejectedId, subscription.Name, reason, delivery.Body.Length, CorrelationId.Normalize(delivery.MessageType, "invalid"));
            return DeliveryDecision.DeadLetter(reason);
        }

        var orderPlaced = decoded.Event!;
        // A missing or malformed inbound id falls back to the order id, which is deterministic across redeliveries.
        var messageId = CorrelationId.Normalize(delivery.MessageId, orderPlaced.OrderId.ToString("D"));
        using var correlationScope = BeginCorrelationScope(messageId, subscription);
        var context = new IncomingMessageContext(subscription.Name, messageId, delivery.Redelivered);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<IOrderPlacedEventHandler>();
            await handler.HandleAsync(orderPlaced, context, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(LogEvents.MessageProcessingCanceled, "Processing of message {MessageId} on {Subscription} was cancelled; requeueing", messageId, subscription.Name);
            return DeliveryDecision.Requeue;
        }
        catch (Exception exception)
        {
            var settings = options.Value;
            var attempt = attempts.RecordFailure(subscription.Name, messageId);
            if (attempt >= settings.MaxDeliveryAttempts)
            {
                attempts.Forget(subscription.Name, messageId);
                logger.LogError(
                    LogEvents.MessageRetriesExhausted,
                    exception,
                    "Message {MessageId} on {Subscription} failed {Attempt} of {MaxAttempts} attempts; dead-lettering",
                    messageId, subscription.Name, attempt, settings.MaxDeliveryAttempts);
                return DeliveryDecision.DeadLetter(RetriesExhaustedReason);
            }

            logger.LogWarning(
                LogEvents.MessageRetryScheduled,
                exception,
                "Message {MessageId} on {Subscription} failed attempt {Attempt} of {MaxAttempts}; requeueing",
                messageId, subscription.Name, attempt, settings.MaxDeliveryAttempts);
            try
            {
                // Back off before requeueing: classic queues redeliver a requeued message immediately.
                await Task.Delay(settings.RetryDelay * attempt, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }

            return DeliveryDecision.Requeue;
        }

        attempts.Forget(subscription.Name, messageId);
        return DeliveryDecision.Ack;
    }

    /// <summary>Every log written while the message is processed, including the handler's, carries its correlation.</summary>
    private IDisposable? BeginCorrelationScope(string correlationId, OrderSubscription subscription) =>
        logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["Subscription"] = subscription.Name
        });
}
