#nullable enable
using Template.Application.Wrappers;

namespace Template.Application.Common.Results;

/// <summary>
/// Transport-neutral failure of a Phase 24+ use case. <see cref="Code"/> is the stable English Problem Details
/// <c>code</c> (see <see cref="ProblemCodes"/>); <see cref="Category"/> selects the HTTP status at the boundary.
/// <see cref="Message"/> is a safe human-readable detail; it never carries credentials, tokens or internal text.
/// </summary>
public sealed record ApplicationError(
    string Code,
    ErrorCategory Category,
    string Message,
    string? RuleId = null,
    IReadOnlyDictionary<string, object?>? Extensions = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null)
{
    public static ApplicationError Validation(IReadOnlyDictionary<string, string[]> fieldErrors) =>
        new(ProblemCodes.Validation, ErrorCategory.Validation, "One or more validation errors occurred.", FieldErrors: fieldErrors);

    public static ApplicationError Validation(string field, string message) =>
        Validation(new Dictionary<string, string[]> { [field] = [message] });
}
