using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Template.WebApi.Infrastructure.HealthChecks;

/// <summary>
/// Reads a fixed probe key from the distributed cache backend (Redis outside Test). The product cache is fail-soft,
/// so this check is optional: a failure degrades readiness without making the process unready.
/// </summary>
public sealed class DistributedCacheHealthCheck(IDistributedCache cache) : IHealthCheck
{
    public const string ProbeKey = "template:health:probe";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // WaitAsync bounds a client that ignores the token while it connects.
            await cache.GetAsync(ProbeKey, cancellationToken).WaitAsync(cancellationToken);
            return HealthCheckResult.Healthy("The cache is reachable.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "The cache is unreachable.");
        }
    }
}
