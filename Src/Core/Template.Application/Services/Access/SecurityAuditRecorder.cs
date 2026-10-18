#nullable enable
using Template.Application.Interfaces;
using Template.Application.Interfaces.Auditing;
using Template.Application.Security;
using Template.Domain.Auditing;

namespace Template.Application.Services.Access;

/// <summary>
/// Builds Access-module audit records with the request correlation and the injected clock, and appends them to the
/// audit trail. The caller commits them with its own change (AUD-001). Only allow-listed details are accepted (AUD-003).
/// </summary>
public sealed class SecurityAuditRecorder(IAuditTrail auditTrail, IClock clock, IRequestContext requestContext)
{
    public void SessionEvent(string action, Guid? actorId, Guid sessionFamilyId, string? reason = null, string? toState = null) =>
        Append(action, actorId, SecurityAuditActions.SessionFamilyResource, sessionFamilyId.ToString(), toState, reason, details: null);

    public void EndpointEvent(string action, Guid? actorId, string endpoint, IReadOnlyDictionary<string, string>? details = null) =>
        Append(action, actorId, SecurityAuditActions.EndpointResource, endpoint, toState: null, reason: null, details);

    private void Append(
        string action,
        Guid? actorId,
        string resourceType,
        string resourceId,
        string? toState,
        string? reason,
        IReadOnlyDictionary<string, string>? details) =>
        auditTrail.Append(AuditRecord.Create(
            Guid.NewGuid(),
            clock.UtcNow,
            actorId,
            action,
            resourceType,
            resourceId,
            requestContext.CorrelationId,
            toState: toState,
            reason: reason,
            details: details));
}
