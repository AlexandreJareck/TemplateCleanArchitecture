#nullable enable
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Template.Application.Interfaces.Messaging;
using Template.Application.Logging;
using Template.Application.Messaging.Procurement;
using Template.Infrastructure.Messaging.Topology;

namespace Template.Infrastructure.Messaging.Consuming;

/// <summary>
/// One broker delivery and the queue/Inbox consumer it arrived for (Phase 29 adds the notification consumer; the
/// defaults keep the Phase 28 projection consumer).
/// </summary>
public sealed record ProcurementDelivery(
    ReadOnlyMemory<byte> Body,
    IReadOnlyBasicProperties Properties,
    string Exchange,
    string RoutingKey,
    ulong DeliveryTag,
    string Queue = ProcurementMessagingTopology.ProjectionQueue,
    string Consumer = ProcurementMessagingTopology.ProjectionConsumer);

public interface IProcurementDeliveryChannel
{
    ValueTask AckAsync(ulong deliveryTag, CancellationToken cancellationToken);
    ValueTask PublishAsync(
        string exchange,
        string routingKey,
        BasicProperties properties,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken);
}

public sealed class ProcurementDeliveryProcessor(
    IServiceScopeFactory scopes,
    ILogger<ProcurementDeliveryProcessor> logger)
{
    public const string AttemptHeader = "x-template-delivery-attempt";
    public const string DeadLetterReasonHeader = "x-dead-letter-reason";

    public async Task ProcessAsync(
        ProcurementDelivery delivery,
        IProcurementDeliveryChannel channel,
        RabbitMqSettings settings,
        CancellationToken stoppingToken)
    {
        try
        {
            if (stoppingToken.IsCancellationRequested)
                return;

            var body = delivery.Body.ToArray();
            IntegrationEventEnvelope envelope;
            try
            {
                if (!string.Equals(delivery.Properties.ContentType, "application/json", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(delivery.Properties.Type, ProcurementMessagingTopology.ContractType, StringComparison.Ordinal))
                    throw new InvalidDataException("Incompatible contract metadata.");
                envelope = ProcurementIntegrationEventContract.Deserialize(body);
                if (!string.Equals(delivery.Properties.MessageId, envelope.EventId.ToString("D"), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Message id and envelope event id differ.");
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException)
            {
                await DeadLetterAsync(channel, delivery, body, "invalid-or-incompatible-contract", settings, stoppingToken);
                return;
            }

            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<IProcurementIntegrationEventHandler>();
                var result = await handler.HandleAsync(delivery.Consumer, envelope, stoppingToken);
                logger.LogInformation(result == InboxApplyResult.Applied ? LogEvents.InboxEffectApplied : LogEvents.InboxDuplicateSkipped,
                    "Procurement event {EventId} handled with {Outcome}", envelope.EventId, result);
                await channel.AckAsync(delivery.DeliveryTag, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                var attempt = ReadAttempt(delivery.Properties.Headers) + 1;
                if (attempt >= settings.MaxDeliveryAttempts)
                {
                    await DeadLetterAsync(channel, delivery, body, "max-delivery-attempts-exceeded", settings, stoppingToken);
                    return;
                }

                await Task.Delay(TimeSpan.FromTicks(settings.RetryDelay.Ticks * attempt), stoppingToken);
                var properties = CopyProperties(delivery.Properties);
                properties.Headers![AttemptHeader] = attempt;
                // D29-9: a retry goes back only to the queue that failed (default exchange, routing key = queue), so
                // it never fans out to the other procurement consumers again.
                await channel.PublishAsync(string.Empty, delivery.Queue, properties, body, stoppingToken);
                await channel.AckAsync(delivery.DeliveryTag, stoppingToken);
                logger.LogWarning(LogEvents.MessageRetryScheduled,
                    "Procurement event {MessageId} failed with {ExceptionType}; retry {Attempt} scheduled",
                    delivery.Properties.MessageId, exception.GetType().Name, attempt);
            }
        }
        catch (Exception exception) when (RabbitMqConnections.IsBrokerFailure(exception) || exception is OperationCanceledException)
        {
            logger.LogWarning(LogEvents.MessageSettleFailed,
                "Procurement delivery could not be settled ({ExceptionType}); it will be redelivered", exception.GetType().Name);
        }
    }

    private async Task DeadLetterAsync(
        IProcurementDeliveryChannel channel,
        ProcurementDelivery delivery,
        byte[] body,
        string reason,
        RabbitMqSettings settings,
        CancellationToken cancellationToken)
    {
        var properties = CopyProperties(delivery.Properties);
        properties.Headers![DeadLetterReasonHeader] = reason;
        properties.Headers["x-original-exchange"] = delivery.Exchange;
        properties.Headers["x-original-routing-key"] = delivery.RoutingKey;
        properties.Headers["x-original-queue"] = delivery.Queue;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.PublishTimeout);
        await channel.PublishAsync(ProcurementMessagingTopology.DeadLetterExchange,
            delivery.Queue, properties, body, timeout.Token);
        await channel.AckAsync(delivery.DeliveryTag, cancellationToken);
        logger.LogWarning(LogEvents.ProcurementMessageDeadLettered,
            "Procurement message {MessageId} dead-lettered with {Reason}", delivery.Properties.MessageId, reason);
    }

    private static BasicProperties CopyProperties(IReadOnlyBasicProperties source) => new()
    {
        Persistent = true,
        ContentType = source.ContentType,
        Type = source.Type,
        MessageId = source.MessageId,
        CorrelationId = source.CorrelationId,
        Timestamp = source.Timestamp,
        Headers = source.Headers is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(source.Headers)
    };

    private static int ReadAttempt(IDictionary<string, object?>? headers)
    {
        if (headers?.TryGetValue(AttemptHeader, out var value) != true)
            return 0;
        return value switch
        {
            byte number => number,
            short number => number,
            int number => number,
            long number when number <= int.MaxValue => (int)number,
            byte[] text when int.TryParse(Encoding.UTF8.GetString(text), out var number) => number,
            _ => 0
        };
    }
}
