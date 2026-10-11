using System.Collections.Concurrent;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Template.Application.Logging;

namespace Template.WebApi.Infrastructure.HealthChecks;

/// <summary>
/// Logs <see cref="LogEvents.HealthDependencyStatusChanged"/> only when a check's status differs from the last one
/// observed, so an unchanged outage is logged once however often orchestrators probe. A first observation is logged
/// only when it is not healthy.
/// </summary>
public sealed class HealthStatusTracker(ILogger<HealthStatusTracker> logger)
{
    private readonly ConcurrentDictionary<string, HealthStatus> lastStatus = new(StringComparer.Ordinal);

    public void Observe(HealthReport report)
    {
        foreach (var (name, entry) in report.Entries)
        {
            var hadPrevious = lastStatus.TryGetValue(name, out var previous);
            if (hadPrevious && previous == entry.Status)
                continue;
            var changed = hadPrevious
                ? lastStatus.TryUpdate(name, entry.Status, previous)
                : lastStatus.TryAdd(name, entry.Status);
            if (!changed || (!hadPrevious && entry.Status == HealthStatus.Healthy))
                continue;

            logger.Log(
                entry.Status == HealthStatus.Healthy ? LogLevel.Information : LogLevel.Warning,
                LogEvents.HealthDependencyStatusChanged,
                "Health check {HealthCheckName} changed from {PreviousHealthStatus} to {HealthStatus}",
                name,
                hadPrevious ? previous.ToString() : "Unknown",
                entry.Status.ToString());
        }
    }
}
