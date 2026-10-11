using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Template.WebApi.Infrastructure.HealthChecks;

/// <summary>
/// Writes <c>/health/live</c> and <c>/health/ready</c>: <c>application/json</c> with the overall status and, per check,
/// its name, status, fixed description, whether it is required and its duration. An entry that failed with an
/// exception (including the framework's timeout) reports a generic description, so no exception text, host,
/// connection string or response body is ever exposed.
/// </summary>
public static class HealthResponseWriter
{
    public const string FailedDescription = "The check failed.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Readiness policy: a required check that is unhealthy makes the process unready (503); otherwise any check that
    /// is not healthy reports Degraded (200); otherwise Healthy (200).
    /// </summary>
    public static HealthStatus EvaluateReadiness(HealthReport report)
    {
        if (report.Entries.Values.Any(entry => IsRequired(entry) && entry.Status == HealthStatus.Unhealthy))
            return HealthStatus.Unhealthy;
        return report.Entries.Values.Any(entry => entry.Status != HealthStatus.Healthy)
            ? HealthStatus.Degraded
            : HealthStatus.Healthy;
    }

    public static Task WriteLivenessAsync(HttpContext context, HealthReport report) =>
        WriteAsync(context, report, report.Status);

    public static Task WriteReadinessAsync(HttpContext context, HealthReport report) =>
        WriteAsync(context, report, EvaluateReadiness(report));

    private static async Task WriteAsync(HttpContext context, HealthReport report, HealthStatus status)
    {
        context.RequestServices.GetService<HealthStatusTracker>()?.Observe(report);
        context.Response.StatusCode = status == HealthStatus.Unhealthy
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status200OK;
        context.Response.ContentType = "application/json";
        var body = new
        {
            status = status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            checks = report.Entries
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Exception is null ? entry.Value.Description ?? string.Empty : FailedDescription,
                    required = IsRequired(entry.Value),
                    durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 1)
                })
        };
        await JsonSerializer.SerializeAsync(context.Response.Body, body, JsonOptions, context.RequestAborted);
    }

    private static bool IsRequired(HealthReportEntry entry) => entry.Tags.Contains(HealthTags.Required);
}
