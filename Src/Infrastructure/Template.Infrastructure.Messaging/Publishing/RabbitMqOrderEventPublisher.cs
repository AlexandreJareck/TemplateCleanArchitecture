using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Template.Application.Interfaces.Messaging;
using Template.Application.Messaging;
using Template.Application.Messaging.Orders;
using Template.Infrastructure.Messaging.Serialization;
using Template.Infrastructure.Messaging.Topology;

namespace Template.Infrastructure.Messaging.Publishing;

/// <summary>
/// Publishes persistent messages over one long-lived connection and one confirm-enabled channel, serialized by a
/// gate because channels are not safe for concurrent publishing. The connection is opened lazily on first publish and
/// discarded after any broker failure, so the next publish reconnects. Each publish is bounded by
/// <see cref="RabbitMqSettings.PublishTimeout"/> and returns only after the broker confirmed the message.
/// Publishing is not mandatory: a message that matches no binding is confirmed and dropped, as before Phase 9.
/// </summary>
public sealed class RabbitMqOrderEventPublisher(IOptions<RabbitMqSettings> options, ILogger<RabbitMqOrderEventPublisher> logger)
    : IOrderEventPublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private IConnection? connection;
    private IChannel? channel;
    private bool disposed;

    public Task PublishBroadcastAsync(OrderPlacedEvent orderPlaced, CancellationToken cancellationToken) =>
        PublishAsync(OrderMessagingTopology.FanoutExchange, string.Empty, orderPlaced, cancellationToken);

    public Task PublishDirectAsync(OrderPlacedEvent orderPlaced, string routingKey, CancellationToken cancellationToken) =>
        PublishAsync(OrderMessagingTopology.DirectExchange, routingKey, orderPlaced, cancellationToken);

    public Task PublishTopicAsync(OrderPlacedEvent orderPlaced, string routingKey, CancellationToken cancellationToken) =>
        PublishAsync(OrderMessagingTopology.TopicExchange, routingKey, orderPlaced, cancellationToken);

    public static BasicProperties CreateProperties(OrderPlacedEvent orderPlaced) => new()
    {
        Persistent = true,
        ContentType = OrderPlacedMessageSerializer.ContentType,
        Type = OrderPlacedMessageSerializer.MessageType,
        MessageId = orderPlaced.OrderId.ToString("D")
    };

    private async Task PublishAsync(string exchange, string routingKey, OrderPlacedEvent orderPlaced, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderPlaced);
        ArgumentNullException.ThrowIfNull(routingKey);
        ObjectDisposedException.ThrowIf(disposed, this);

        var settings = options.Value;
        var body = OrderPlacedMessageSerializer.Serialize(orderPlaced);
        var properties = CreateProperties(orderPlaced);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.PublishTimeout);

        try
        {
            await gate.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw TimedOut(exchange, orderPlaced, exception);
        }

        try
        {
            var publishChannel = await EnsureChannelAsync(settings, timeout.Token);
            await publishChannel.BasicPublishAsync(exchange, routingKey, mandatory: false, properties, body, timeout.Token);
            logger.LogDebug("Order {OrderId} confirmed by the broker on {Exchange}", orderPlaced.OrderId, exchange);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ResetAsync();
            throw;
        }
        catch (OperationCanceledException exception)
        {
            await ResetAsync();
            throw TimedOut(exchange, orderPlaced, exception);
        }
        catch (Exception exception) when (RabbitMqConnections.IsBrokerFailure(exception))
        {
            await ResetAsync();
            logger.LogWarning(
                "Order {OrderId} was not accepted by the broker on {Exchange}: {ExceptionType}",
                orderPlaced.OrderId, exchange, exception.GetType().Name);
            throw new MessagePublishException("The message broker did not accept the message.", exception);
        }
        finally
        {
            gate.Release();
        }
    }

    private MessagePublishException TimedOut(string exchange, OrderPlacedEvent orderPlaced, Exception exception)
    {
        logger.LogWarning("Publishing order {OrderId} to {Exchange} timed out", orderPlaced.OrderId, exchange);
        return new MessagePublishException("The message broker did not confirm the message in time.", exception);
    }

    private async Task<IChannel> EnsureChannelAsync(RabbitMqSettings settings, CancellationToken cancellationToken)
    {
        if (channel is { IsOpen: true })
            return channel;

        await ResetAsync();
        var factory = RabbitMqConnections.CreateFactory(settings, "template-webapi-publisher", automaticRecovery: false);
        connection = await factory.CreateConnectionAsync(cancellationToken);
        channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        await OrderMessagingTopology.DeclareAsync(channel, cancellationToken);
        logger.LogInformation("Publisher connected to the message broker");
        return channel;
    }

    private async Task ResetAsync()
    {
        var (oldChannel, oldConnection) = (channel, connection);
        (channel, connection) = (null, null);
        await RabbitMqConnections.AbortQuietlyAsync(oldChannel, oldConnection);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
            return;

        disposed = true;
        await gate.WaitAsync();
        try
        {
            if (channel is not null)
            {
                try
                {
                    await channel.CloseAsync().WaitAsync(RabbitMqConnections.CloseTimeout);
                }
                catch (Exception)
                {
                    // Fall through to abort.
                }
            }

            if (connection is not null)
            {
                try
                {
                    await connection.CloseAsync(RabbitMqConnections.CloseTimeout).WaitAsync(RabbitMqConnections.CloseTimeout + RabbitMqConnections.CloseTimeout);
                }
                catch (Exception)
                {
                    // Fall through to abort.
                }
            }

            await ResetAsync();
        }
        finally
        {
            gate.Release();
        }
    }
}
