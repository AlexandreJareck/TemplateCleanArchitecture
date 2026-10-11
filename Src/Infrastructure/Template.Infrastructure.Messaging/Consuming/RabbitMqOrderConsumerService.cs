using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Template.Application.Logging;
using Template.Infrastructure.Messaging.Topology;

namespace Template.Infrastructure.Messaging.Consuming;

/// <summary>
/// Hosts one consumer per order subscription on a single auto-recovering connection (one channel per consumer,
/// manual acknowledgements, bounded prefetch). Nothing connects in the constructor: the connection is opened in
/// <see cref="ExecuteAsync"/>, retried with capped exponential backoff while the broker is unavailable, and closed
/// when the host stops. Broker failures never stop the host.
/// </summary>
public sealed class RabbitMqOrderConsumerService(
    IOptions<RabbitMqSettings> options,
    OrderDeliveryProcessor processor,
    ILogger<RabbitMqOrderConsumerService> logger) : BackgroundService
{
    public const string DeadLetterReasonHeader = "x-dead-letter-reason";
    public const string OriginalExchangeHeader = "x-original-exchange";
    public const string OriginalRoutingKeyHeader = "x-original-routing-key";
    public const string OriginalQueueHeader = "x-original-queue";

    private TaskCompletionSource consuming = NewSignal();
    private int inFlight;

    /// <summary>Completes once every subscription has a registered consumer in the current session.</summary>
    public Task WaitUntilConsumingAsync(CancellationToken cancellationToken) =>
        Volatile.Read(ref consuming).Task.WaitAsync(cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || !settings.ConsumersEnabled)
        {
            logger.LogInformation(LogEvents.MessagingConsumersDisabled, "Order consumers are disabled by configuration");
            return;
        }

        await Task.Yield();
        var delay = settings.ReconnectDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            var established = false;
            try
            {
                established = await RunSessionAsync(settings, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (RabbitMqConnections.IsBrokerFailure(exception) || exception is OperationCanceledException)
            {
                logger.LogWarning(
                    LogEvents.MessagingBrokerUnreachable,
                    "Order consumers cannot reach the message broker ({ExceptionType}); retrying in {Delay}",
                    exception.GetType().Name, delay);
            }

            if (established)
                delay = settings.ReconnectDelay;

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, settings.MaxReconnectDelay.Ticks));
        }

        logger.LogInformation(LogEvents.MessagingConsumersStopped, "Order consumers stopped");
    }

    /// <summary>Runs until a consumer channel is lost (returns true) or the host stops (throws).</summary>
    private async Task<bool> RunSessionAsync(RabbitMqSettings settings, CancellationToken stoppingToken)
    {
        var sessionLost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumers = new List<(IChannel Channel, string? ConsumerTag)>();
        IConnection? connection = null;
        try
        {
            var factory = RabbitMqConnections.CreateFactory(settings, "template-webapi-consumers", automaticRecovery: true);
            connection = await factory.CreateConnectionAsync(stoppingToken);
            connection.RecoverySucceededAsync += (_, _) =>
            {
                logger.LogInformation(LogEvents.MessagingConnectionRecovered, "Order consumers reconnected to the message broker");
                return Task.CompletedTask;
            };
            connection.ConnectionShutdownAsync += (_, args) =>
            {
                if (args.Initiator != ShutdownInitiator.Application)
                    logger.LogWarning(LogEvents.MessagingConnectionLost, "Order consumer connection lost ({ReplyCode}); automatic recovery is active", args.ReplyCode);
                return Task.CompletedTask;
            };

            await using (var setup = await connection.CreateChannelAsync(cancellationToken: stoppingToken))
            {
                await OrderMessagingTopology.DeclareAsync(setup, stoppingToken);
                await setup.CloseAsync(stoppingToken);
            }

            foreach (var subscription in OrderMessagingTopology.Subscriptions)
            {
                var channel = await connection.CreateChannelAsync(
                    new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                    stoppingToken);
                consumers.Add((channel, null));
                var owner = connection;
                channel.ChannelShutdownAsync += (_, args) =>
                {
                    // Connection-level losses are handled by automatic recovery; a channel closed by the broker while
                    // the connection is still open is not recovered, so the session is rebuilt.
                    if (args.Initiator != ShutdownInitiator.Application && owner.IsOpen)
                        sessionLost.TrySetResult();
                    return Task.CompletedTask;
                };

                await channel.BasicQosAsync(0, settings.PrefetchCount, global: false, stoppingToken);
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += (_, delivery) => OnReceivedAsync(subscription, channel, delivery, settings, stoppingToken);
                var consumerTag = await channel.BasicConsumeAsync(subscription.Queue, autoAck: false, consumer, stoppingToken);
                consumers[^1] = (channel, consumerTag);
            }

            logger.LogInformation(LogEvents.MessagingConsumersStarted, "Order consumers started on {QueueCount} queues", consumers.Count);
            Volatile.Read(ref consuming).TrySetResult();

            await sessionLost.Task.WaitAsync(stoppingToken);
            logger.LogWarning(LogEvents.MessagingChannelClosed, "An order consumer channel was closed by the broker; restarting consumers");
            return true;
        }
        finally
        {
            // Waiters registered from now on wait for the next session.
            if (Volatile.Read(ref consuming).Task.IsCompleted)
                Volatile.Write(ref consuming, NewSignal());
            await StopSessionAsync(consumers, connection);
        }
    }

    private async Task OnReceivedAsync(
        OrderSubscription subscription,
        IChannel channel,
        BasicDeliverEventArgs delivery,
        RabbitMqSettings settings,
        CancellationToken stoppingToken)
    {
        Interlocked.Increment(ref inFlight);
        try
        {
            // The body memory belongs to the client and is only valid until this handler returns: copy it first.
            var received = new ReceivedDelivery(
                delivery.Body.ToArray(),
                delivery.BasicProperties.ContentType,
                delivery.BasicProperties.Type,
                delivery.BasicProperties.MessageId,
                delivery.Redelivered);

            // During shutdown a delivery is left unacknowledged: the broker requeues it when the channel closes.
            // Nacking it here would hand it straight back to this consumer before basic.cancel takes effect.
            if (stoppingToken.IsCancellationRequested)
                return;

            var decision = await processor.ProcessAsync(subscription, received, stoppingToken);
            switch (decision.Action)
            {
                case DeliveryAction.Ack:
                    await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
                    break;
                case DeliveryAction.Requeue when stoppingToken.IsCancellationRequested:
                    break;
                case DeliveryAction.Requeue:
                    await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true);
                    break;
                case DeliveryAction.DeadLetter:
                    await DeadLetterAsync(subscription, channel, delivery, received, decision.DeadLetterReason!, settings, stoppingToken);
                    break;
            }
        }
        catch (Exception exception) when (RabbitMqConnections.IsBrokerFailure(exception) || exception is OperationCanceledException)
        {
            // The acknowledgement could not be sent; the broker redelivers the message (at-least-once).
            logger.LogWarning(
                LogEvents.MessageSettleFailed,
                "Could not settle a delivery on {Subscription} ({ExceptionType}); the broker will redeliver it",
                subscription.Name, exception.GetType().Name);
        }
        finally
        {
            Interlocked.Decrement(ref inFlight);
        }
    }

    private async Task DeadLetterAsync(
        OrderSubscription subscription,
        IChannel channel,
        BasicDeliverEventArgs delivery,
        ReceivedDelivery received,
        string reason,
        RabbitMqSettings settings,
        CancellationToken stoppingToken)
    {
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = received.ContentType,
            Type = received.MessageType,
            MessageId = received.MessageId,
            Headers = new Dictionary<string, object?>
            {
                [DeadLetterReasonHeader] = reason,
                [OriginalExchangeHeader] = delivery.Exchange,
                [OriginalRoutingKeyHeader] = delivery.RoutingKey,
                [OriginalQueueHeader] = subscription.Queue
            }
        };

        try
        {
            using var timeout = new CancellationTokenSource(settings.PublishTimeout);
            // Mandatory + confirm: the original is acknowledged only after the dead-letter copy is safely stored.
            await channel.BasicPublishAsync(
                OrderMessagingTopology.DeadLetterExchange,
                subscription.Queue,
                mandatory: true,
                properties,
                received.Body,
                timeout.Token);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
            logger.LogWarning(LogEvents.MessageDeadLettered, "Message {MessageId} on {Subscription} dead-lettered: {Reason}", received.MessageId, subscription.Name, reason);
        }
        catch (Exception exception) when (RabbitMqConnections.IsBrokerFailure(exception) || exception is OperationCanceledException)
        {
            logger.LogError(
                LogEvents.MessageDeadLetterFailed,
                "Dead-lettering message {MessageId} on {Subscription} failed ({ExceptionType}); requeueing it",
                received.MessageId, subscription.Name, exception.GetType().Name);
            try
            {
                await Task.Delay(settings.RetryDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }

            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true);
        }
    }

    private async Task StopSessionAsync(List<(IChannel Channel, string? ConsumerTag)> consumers, IConnection? connection)
    {
        foreach (var (channel, consumerTag) in consumers)
        {
            if (consumerTag is null || !channel.IsOpen)
                continue;

            try
            {
                await channel.BasicCancelAsync(consumerTag).WaitAsync(RabbitMqConnections.CloseTimeout);
            }
            catch (Exception)
            {
                // Closing the channel below also cancels the consumer.
            }
        }

        // Let in-flight deliveries settle (they observe the stopping token); anything left unacknowledged is requeued
        // by the broker when the channel closes.
        var deadline = DateTime.UtcNow + RabbitMqConnections.CloseTimeout;
        while (Volatile.Read(ref inFlight) > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(TimeSpan.FromMilliseconds(50));

        foreach (var (channel, _) in consumers)
        {
            try
            {
                if (channel.IsOpen)
                    await channel.CloseAsync().WaitAsync(RabbitMqConnections.CloseTimeout);
            }
            catch (Exception)
            {
                // Best effort; disposal follows.
            }

            await RabbitMqConnections.DisposeQuietlyAsync(channel);
        }

        if (connection is null)
            return;

        try
        {
            if (connection.IsOpen)
                await connection.CloseAsync(RabbitMqConnections.CloseTimeout).WaitAsync(RabbitMqConnections.CloseTimeout + RabbitMqConnections.CloseTimeout);
        }
        catch (Exception)
        {
            // Best effort; abort and disposal follow.
        }

        await RabbitMqConnections.AbortQuietlyAsync(null, connection);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
