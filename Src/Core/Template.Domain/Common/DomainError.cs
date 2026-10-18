using System.Collections.Generic;

namespace Template.Domain.Common;

/// <summary>How the Application layer classifies a <see cref="DomainError"/> (docs/product/api-guidelines.md §5).</summary>
public enum DomainErrorKind
{
    /// <summary>Malformed input that a validator could not catch (400).</summary>
    Validation,

    /// <summary>Valid input that violates an invariant or policy (422).</summary>
    BusinessRule,

    /// <summary>The current state does not allow the change (409).</summary>
    Conflict,

    /// <summary>A referenced child or resource does not exist (404).</summary>
    NotFound
}

/// <summary>
/// Expected rejection of a behavior method. Aggregates return it instead of throwing so use cases can answer with a
/// stable Problem Details <c>code</c> and the business-rule ID (docs/product/business-rules.md). Programming errors
/// (empty identifiers, non-UTC instants) still throw.
/// </summary>
public sealed record DomainError(
    string Code,
    DomainErrorKind Kind,
    string Message,
    string? RuleId = null,
    string? Field = null,
    IReadOnlyDictionary<string, string>? Details = null)
{
    public const string InvalidTransitionCode = "invalid-transition";
    public const string ReasonRequiredCode = "reason-required";

    public static DomainError InvalidTransition(string currentState, string message, string? ruleId = null) =>
        new(InvalidTransitionCode, DomainErrorKind.Conflict, message, ruleId,
            Details: new Dictionary<string, string> { ["currentState"] = currentState });

    public static DomainError ReasonRequired(string? ruleId, int minLength, int maxLength) =>
        new(ReasonRequiredCode, DomainErrorKind.Validation,
            $"A reason of {minLength} to {maxLength} characters is required.", ruleId, Field: "reason");
}
