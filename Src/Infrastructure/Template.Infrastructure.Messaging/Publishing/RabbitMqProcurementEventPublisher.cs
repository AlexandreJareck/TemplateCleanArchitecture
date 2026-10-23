#nullable enable
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Template.Application.Interfaces.Messaging;
using Template.Application.Logging;
using Template.Application.Messaging;
using Template.Application.Messaging.Procurement;
using Template.Infrastructure.Messaging.Topology;

namespace Template.Infrastructure.Messaging.Publishing;

public sealed class RabbitMqProcurementEventPublisher(
    IOptions<RabbitMqSettings> options,
    ILogger<RabbitMqProcurementEventPublisher> logger) : IProcurementEventPublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private IConnection? connection;
    private IChannel? channel;
    private bool disposed;

    public async Task PublishAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ProcurementIntegrationEventContract.Validate(envelope);
        var settings = options.Value;
        if (!settings.Enabled)
            throw new MessagePublishException("Procurement messaging is disabled.");

        await gate.WaitAsync(cancellationToken);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(settings.PublishTimeout);
            var publisher = await EnsureChannelAsync(settings, timeout.Token);
            var properties = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
                Type = ProcurementMessagingTopology.ContractType,
                MessageId = envelope.EventId.ToString("D"),
                CorrelationId = envelope.CorrelationId,
                Timestamp = new AmqpTimestamp(new DateTimeOffset(envelope.OccurredAtUtc).ToUnixTimeSeconds())
            };
            await publisher.BasicPublishAsync(
                ProcurementMessagingTopology.Exchange,
                envelope.EventType,
                mandatory: true,
                properties,
                ProcurementIntegrationEventContract.Serialize(envelope),
                timeout.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            await ResetAsync();
            throw new MessagePublishException("The broker did not confirm the procurement event in time.", exception);
        }
        catch (Exception exception) when (RabbitMqConnections.IsBrokerFailure(exception))
        {
            await ResetAsync();
            throw new MessagePublishException("The broker did not accept the procurement event.", exception);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<IChannel> EnsureChannelAsync(RabbitMqSettings settings, CancellationToken cancellationToken)
    {
        if (channel is { IsOpen: true })
            return channel;
        await ResetAsync();
        var factory = RabbitMqConnections.CreateFactory(settings, "template-worker-outbox", automaticRecovery: false);
        connection = await factory.CreateConnectionAsync(cancellationToken);
        channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), cancellationToken);
        await ProcurementMessagingTopology.DeclareAsync(channel, cancellationToken);
        logger.LogInformation(LogEvents.MessagingPublisherConnected, "Procurement Outbox publisher connected");
        return channel;
    }

    private async Task ResetAsync()
    {
        var oldChannel = channel;
        var oldConnection = connection;
        channel = null;
        connection = null;
        await RabbitMqConnections.AbortQuietlyAsync(oldChannel, oldConnection);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
            return;
        disposed = true;
        await gate.WaitAsync();
        try { await ResetAsync(); }
        finally { gate.Release(); gate.Dispose(); }
    }
}
