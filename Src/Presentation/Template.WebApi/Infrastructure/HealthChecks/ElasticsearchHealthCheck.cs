using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Template.WebApi.Infrastructure.HealthChecks;

/// <summary>
/// Pings Elasticsearch (<c>HEAD /</c>, the same probe the previous health-check package used by default). It depends
/// only on the endpoint, not on the search index or the logging sink. Results carry a fixed description and never
/// the exception, endpoint or response body, so <c>/health</c> exposes no credentials or internal details.
/// </summary>
public sealed class ElasticsearchHealthCheck(HttpClient httpClient, Uri endpoint) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, endpoint);
        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Elasticsearch is reachable.")
                : new HealthCheckResult(context.Registration.FailureStatus, "Elasticsearch answered with an error status.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The health check service turns its own timeout into a failure result; a caller cancellation propagates.
            throw;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Elasticsearch is unreachable.");
        }
    }
}
