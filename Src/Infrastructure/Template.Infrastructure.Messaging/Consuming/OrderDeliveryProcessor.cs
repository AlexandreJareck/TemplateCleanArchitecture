using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Template.Application.Interfaces.Messaging;
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
            logger.LogWarning(
                "Rejecting message {MessageId} on {Subscription}: {Reason} ({BodyLength} bytes, type {MessageType})",
                delivery.MessageId, subscription.Name, reason, delivery.Body.Length, delivery.MessageType);
            return DeliveryDecision.DeadLetter(reason);
        }

        var orderPlaced = decoded.Event!;
        var messageId = string.IsNullOrEmpty(delivery.MessageId) ? orderPlaced.OrderId.ToString("D") : delivery.MessageId;
        var context = new IncomingMessageContext(subscription.Name, messageId, delivery.Redelivered);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<IOrderPlacedEventHandler>();
            await handler.HandleAsync(orderPlaced, context, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Processing of message {MessageId} on {Subscription} was cancelled; requeueing", messageId, subscription.Name);
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
                    exception,
                    "Message {MessageId} on {Subscription} failed {Attempt} of {MaxAttempts} attempts; dead-lettering",
                    messageId, subscription.Name, attempt, settings.MaxDeliveryAttempts);
                return DeliveryDecision.DeadLetter(RetriesExhaustedReason);
            }

            logger.LogWarning(
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
}
