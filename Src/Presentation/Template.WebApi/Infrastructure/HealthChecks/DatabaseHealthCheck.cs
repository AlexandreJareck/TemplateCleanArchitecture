using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Template.WebApi.Infrastructure.HealthChecks;

/// <summary>
/// Readiness of a database through a connectivity probe owned by its Infrastructure project (EF Core
/// <c>CanConnectAsync</c>, which EF treats as an expected-error probe), so WebApi never touches the DbContext. The
/// result carries a fixed description, never the exception or connection string.
/// </summary>
public sealed class DatabaseHealthCheck(Func<CancellationToken, Task<bool>> canConnect) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await canConnect(cancellationToken)
                ? HealthCheckResult.Healthy("The database is reachable.")
                : new HealthCheckResult(context.Registration.FailureStatus, "The database is unreachable.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "The database is unreachable.");
        }
    }
}
