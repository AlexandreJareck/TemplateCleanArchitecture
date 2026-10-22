#nullable enable
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Template.Application.Logging;
using Template.Infrastructure.Messaging.Topology;

namespace Template.Infrastructure.Messaging.Consuming;

public sealed class RabbitMqProcurementConsumerService(
    IOptions<RabbitMqSettings> options,
    ProcurementDeliveryProcessor processor,
    ILogger<RabbitMqProcurementConsumerService> logger) : BackgroundService
{
    public const string AttemptHeader = ProcurementDeliveryProcessor.AttemptHeader;
    public const string DeadLetterReasonHeader = ProcurementDeliveryProcessor.DeadLetterReasonHeader;
    private int inFlight;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || !settings.ConsumersEnabled)
        {
            logger.LogInformation(LogEvents.MessagingConsumersDisabled, "Procurement consumers are disabled");
            return;
        }

        await Task.Yield();
        var delay = settings.ReconnectDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(settings, stoppingToken);
                delay = settings.ReconnectDelay;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) when (RabbitMqConnections.IsBrokerFailure(exception) || exception is OperationCanceledException)
            {
                logger.LogWarning(LogEvents.MessagingBrokerUnreachable,
                    "Procurement consumer cannot reach the broker ({ExceptionType}); retrying in {Delay}",
                    exception.GetType().Name, delay);
            }

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, settings.MaxReconnectDelay.Ticks));
        }
    }

    private async Task RunSessionAsync(RabbitMqSettings settings, CancellationToken stoppingToken)
    {
        IConnection? connection = null;
        IChannel? channel = null;
        var consumerTag = string.Empty;
        var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            connection = await RabbitMqConnections.CreateFactory(settings, "template-worker-consumers", automaticRecovery: true)
                .CreateConnectionAsync(stoppingToken);
            channel = await connection.CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), stoppingToken);
            await ProcurementMessagingTopology.DeclareAsync(channel, stoppingToken);
            await channel.BasicQosAsync(0, settings.PrefetchCount, false, stoppingToken);
            channel.ChannelShutdownAsync += (_, args) =>
            {
                lost.TrySetResult();
                return Task.CompletedTask;
            };
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (_, delivery) => OnReceivedAsync(channel, delivery, settings, stoppingToken);
            consumerTag = await channel.BasicConsumeAsync(ProcurementMessagingTopology.ProjectionQueue, false, consumer, stoppingToken);
            logger.LogInformation(LogEvents.MessagingConsumersStarted, "Procurement consumer started on {QueueCount} queue", 1);
            await lost.Task.WaitAsync(stoppingToken);
        }
        finally
        {
            if (channel is { IsOpen: true })
            {
                try { await channel.BasicCancelAsync(consumerTag).WaitAsync(RabbitMqConnections.CloseTimeout); }
                catch (Exception) { }
            }
            var deadline = DateTime.UtcNow + RabbitMqConnections.CloseTimeout;
            while (Volatile.Read(ref inFlight) > 0 && DateTime.UtcNow < deadline)
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            await RabbitMqConnections.AbortQuietlyAsync(channel, connection);
        }
    }

    private async Task OnReceivedAsync(IChannel channel, BasicDeliverEventArgs delivery, RabbitMqSettings settings, CancellationToken stoppingToken)
    {
        Interlocked.Increment(ref inFlight);
        try
        {
            await processor.ProcessAsync(
                new ProcurementDelivery(delivery.Body, delivery.BasicProperties, delivery.Exchange, delivery.RoutingKey, delivery.DeliveryTag),
                new RabbitProcurementDeliveryChannel(channel), settings, stoppingToken);
        }
        finally { Interlocked.Decrement(ref inFlight); }
    }

    private sealed class RabbitProcurementDeliveryChannel(IChannel channel) : IProcurementDeliveryChannel
    {
        public ValueTask AckAsync(ulong deliveryTag, CancellationToken cancellationToken) =>
            channel.BasicAckAsync(deliveryTag, false, cancellationToken);

        public ValueTask PublishAsync(
            string exchange, string routingKey, BasicProperties properties, ReadOnlyMemory<byte> body,
            CancellationToken cancellationToken) =>
            channel.BasicPublishAsync(exchange, routingKey, true, properties, body, cancellationToken);
    }
}
