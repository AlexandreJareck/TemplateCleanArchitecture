using System.Net.Sockets;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Template.Infrastructure.Messaging;

internal static class RabbitMqConnections
{
    /// <summary>Bound for closing or aborting connections and channels during teardown.</summary>
    public static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);

    public static ConnectionFactory CreateFactory(RabbitMqSettings settings, string clientName, bool automaticRecovery) => new()
    {
        HostName = settings.HostName,
        Port = settings.Port,
        UserName = settings.UserName,
        Password = settings.Password,
        ClientProvidedName = clientName,
        RequestedConnectionTimeout = settings.ConnectionTimeout,
        ContinuationTimeout = settings.ConnectionTimeout,
        HandshakeContinuationTimeout = settings.ConnectionTimeout,
        AutomaticRecoveryEnabled = automaticRecovery,
        TopologyRecoveryEnabled = automaticRecovery,
        NetworkRecoveryInterval = settings.ReconnectDelay
    };

    /// <summary>Operational broker failures, as opposed to cancellation or programming errors.</summary>
    public static bool IsBrokerFailure(Exception exception) =>
        exception is RabbitMQClientException or OperationInterruptedException or IOException or SocketException or TimeoutException
        || exception.GetType().Namespace == typeof(RabbitMQClientException).Namespace;

    public static async Task AbortQuietlyAsync(IChannel? channel, IConnection? connection)
    {
        if (channel is not null)
        {
            try
            {
                await channel.AbortAsync().WaitAsync(CloseTimeout);
            }
            catch (Exception)
            {
                // Best effort: the channel is being discarded.
            }

            await DisposeQuietlyAsync(channel);
        }

        if (connection is not null)
        {
            try
            {
                await connection.AbortAsync(CloseTimeout).WaitAsync(CloseTimeout + CloseTimeout);
            }
            catch (Exception)
            {
                // Best effort: the connection is being discarded.
            }

            await DisposeQuietlyAsync(connection);
        }
    }

    public static async Task DisposeQuietlyAsync(IAsyncDisposable disposable)
    {
        try
        {
            await disposable.DisposeAsync().AsTask().WaitAsync(CloseTimeout);
        }
        catch (Exception)
        {
            // Best effort: disposal must not mask the original failure or block shutdown.
        }
    }
}
