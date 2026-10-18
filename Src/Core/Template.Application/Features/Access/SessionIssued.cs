#nullable enable
namespace Template.Application.Features.Access;

/// <summary>
/// Result of a login or refresh. The HTTP boundary returns only the access token in the body and moves the refresh
/// and CSRF tokens into cookies; none of these values is ever logged or audited (NFR-SEC-2).
/// </summary>
public sealed record SessionIssued(
    Guid SessionFamilyId,
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    string CsrfToken,
    DateTime SessionExpiresAtUtc);
