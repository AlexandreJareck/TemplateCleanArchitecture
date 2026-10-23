using Microsoft.Extensions.Diagnostics.HealthChecks;
using Template.Infrastructure.Files;

namespace Template.WebApi.Infrastructure.HealthChecks;

/// <summary>
/// Optional readiness dependency (NFR availability table): blob storage only affects report outputs and file downloads,
/// never procurement transitions. The output names the dependency only, never the endpoint or account.
/// </summary>
public sealed class FileStorageHealthCheck(IFileStorageProbe probe) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await probe.IsAvailableAsync(cancellationToken)
            ? HealthCheckResult.Healthy("The file storage is reachable.")
            : new HealthCheckResult(context.Registration.FailureStatus, "The file storage is unreachable.");
}
