#nullable enable
using Template.Application.Common.Results;
using Template.Application.Wrappers;

namespace Template.Application.Features.Access;

/// <summary>Deterministic failures of the session use cases (SEC-001..005). Messages never reveal which check failed.</summary>
public static class AccessErrors
{
    public static readonly ApplicationError InvalidCredentials = new(
        ProblemCodes.InvalidCredentials, ErrorCategory.Unauthorized, "The user name or password is invalid.", "SEC-001");

    public static readonly ApplicationError SessionExpired = new(
        ProblemCodes.SessionExpired, ErrorCategory.Unauthorized, "The session is missing or expired.", "SEC-002");

    public static readonly ApplicationError SessionRevoked = new(
        ProblemCodes.SessionRevoked, ErrorCategory.Unauthorized, "The session was revoked.", "SEC-003");

    public static readonly ApplicationError RefreshInProgress = new(
        ProblemCodes.RefreshInProgress, ErrorCategory.Conflict, "The session is being refreshed; retry once with the current cookie.", "SEC-004");

    public static readonly ApplicationError CsrfInvalid = new(
        ProblemCodes.CsrfInvalid, ErrorCategory.Forbidden, "The CSRF token or request origin is invalid.", "SEC-005");

    public static readonly ApplicationError NotAuthenticated = new(
        ProblemCodes.SessionExpired, ErrorCategory.Unauthorized, "Authentication is required.");
}
