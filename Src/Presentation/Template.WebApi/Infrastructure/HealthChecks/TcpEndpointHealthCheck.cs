using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Template.WebApi.Infrastructure.HealthChecks;

/// <summary>
/// Opens and closes a TCP connection to a broker endpoint. It proves reachability only (no AMQP handshake, so no
/// credentials are sent) and reports a fixed description, never the host, port or exception.
/// </summary>
public sealed class TcpEndpointHealthCheck(string host, int port, string dependency) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, cancellationToken);
            return HealthCheckResult.Healthy($"The {dependency} is reachable.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, $"The {dependency} is unreachable.");
        }
    }
}
