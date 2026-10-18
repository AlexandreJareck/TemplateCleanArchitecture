#nullable enable
namespace Template.Application.Security;

/// <summary>Audit actions of the Access module (SEC-001..010). Values are stable English identifiers.</summary>
public static class SecurityAuditActions
{
    public const string SessionCreated = "security.session.created";
    public const string SessionRefreshed = "security.session.refreshed";
    public const string SessionReuseDetected = "security.session.reuse-detected";
    public const string SessionRevoked = "security.session.revoked";
    public const string CsrfRejected = "security.csrf.rejected";
    public const string RateLimited = "security.rate-limited";
    public const string Forbidden = "security.forbidden";

    /// <summary>Resource type of session-family audit records.</summary>
    public const string SessionFamilyResource = "session-family";

    /// <summary>Resource type for events about an HTTP endpoint (rate limit, forbidden).</summary>
    public const string EndpointResource = "endpoint";
}
