#nullable enable
namespace Template.Application.Common.Results;

/// <summary>
/// Stable Problem Details <c>code</c> values (docs/product/api-guidelines.md §5). Clients branch on status and code
/// only; never rename a published value.
/// </summary>
public static class ProblemCodes
{
    public const string Validation = "validation";
    public const string NotFound = "not-found";
    public const string InvalidTransition = "invalid-transition";

    public const string InvalidCredentials = "auth.invalid-credentials";
    public const string SessionExpired = "auth.session-expired";
    public const string SessionRevoked = "auth.session-revoked";
    public const string RefreshInProgress = "auth.refresh-in-progress";
    public const string CsrfInvalid = "auth.csrf-invalid";
    public const string Forbidden = "auth.forbidden";
    public const string RateLimited = "auth.rate-limited";

    public const string VersionMismatch = "concurrency.version-mismatch";
    public const string PreconditionRequired = "concurrency.precondition-required";

    public const string IdempotencyKeyRequired = "idempotency.key-required";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
    public const string IdempotencyInProgress = "idempotency.in-progress";
}
