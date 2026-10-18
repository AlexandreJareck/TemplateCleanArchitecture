#nullable enable
using Template.Application.Common.Results;
using Template.Application.Wrappers;

namespace Template.Application.Common.Concurrency;

/// <summary>
/// Optimistic-concurrency precondition of a write on a versioned aggregate (docs/product/api-guidelines.md §4).
/// The HTTP boundary parses <c>If-Match</c>; this type decides the outcome independently of transport.
/// </summary>
public static class VersionPrecondition
{
    /// <summary>Opaque version representation shared by the <c>version</c> field and the strong ETag value.</summary>
    public static string Format(long version) => version.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static bool TryParse(string? value, out long version) =>
        long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out version)
        && version > 0;

    /// <summary>
    /// <c>null</c> when the expected version matches; otherwise 428 (missing) or 412 (stale, with the current
    /// <c>version</c> extension). No change may be applied when an error is returned.
    /// </summary>
    public static ApplicationError? Check(long? expectedVersion, long currentVersion)
    {
        if (expectedVersion is null)
            return new ApplicationError(
                ProblemCodes.PreconditionRequired,
                ErrorCategory.PreconditionRequired,
                "This operation requires an If-Match header with the current version.");

        return expectedVersion == currentVersion
            ? null
            : new ApplicationError(
                ProblemCodes.VersionMismatch,
                ErrorCategory.PreconditionFailed,
                "The resource changed since it was read.",
                Extensions: new Dictionary<string, object?> { ["version"] = Format(currentVersion) });
    }
}
