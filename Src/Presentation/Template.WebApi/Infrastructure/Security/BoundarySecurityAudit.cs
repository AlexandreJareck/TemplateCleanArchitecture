using MediatR;
using Template.Application.Features.Access.Commands;
using Template.Application.Logging;

namespace Template.WebApi.Infrastructure.Security;

/// <summary>
/// Records sampled security events detected at the HTTP boundary (rate limit, permission denial, origin rejection)
/// through <see cref="RecordSecurityEventCommand"/>. An audit failure never changes the HTTP outcome; it is logged
/// without request data.
/// </summary>
public sealed class BoundarySecurityAudit(
    ISender sender,
    SecurityEventSampler sampler,
    ILogger<BoundarySecurityAudit> logger)
{
    public async Task RecordAsync(
        string action,
        HttpContext context,
        string sampleKey,
        IReadOnlyDictionary<string, string>? details = null)
    {
        if (!sampler.ShouldRecord($"{action}|{sampleKey}"))
            return;

        var endpoint = EndpointName(context);
        try
        {
            await sender.Send(new RecordSecurityEventCommand(action, endpoint, details), context.RequestAborted);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(LogEvents.SecurityAuditFailed, "Security audit {AuditAction} could not be written ({FailureCategory})",
                action, error.GetType().Name);
        }
    }

    /// <summary>Method plus route template (never the raw path, which may carry identifiers).</summary>
    public static string EndpointName(HttpContext context)
    {
        var template = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
        var name = $"{context.Request.Method} /{template.TrimStart('/')}";
        return name.Length <= 100 ? name : name[..100];
    }
}
