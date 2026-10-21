#nullable enable
using Template.Application.Wrappers;
using Template.Domain.Common;

namespace Template.Application.Common.Results;

/// <summary>Maps an aggregate's <see cref="DomainError"/> to the transport-neutral <see cref="ApplicationError"/>.</summary>
public static class DomainErrorMapping
{
    public static ApplicationError ToApplicationError(this DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var extensions = error.Details?.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
        return error.Kind switch
        {
            DomainErrorKind.Validation => new ApplicationError(
                error.Code,
                ErrorCategory.Validation,
                error.Message,
                error.RuleId,
                extensions,
                new Dictionary<string, string[]> { [error.Field ?? string.Empty] = [error.Message] }),
            DomainErrorKind.BusinessRule => new ApplicationError(error.Code, ErrorCategory.BusinessRule, error.Message, error.RuleId, extensions),
            DomainErrorKind.NotFound => new ApplicationError(ProblemCodes.NotFound, ErrorCategory.NotFound, error.Message),
            DomainErrorKind.TooLarge => new ApplicationError(error.Code, ErrorCategory.PayloadTooLarge, error.Message, error.RuleId, extensions),
            DomainErrorKind.UnsupportedMediaType => new ApplicationError(error.Code, ErrorCategory.UnsupportedMediaType, error.Message, error.RuleId, extensions),
            _ => new ApplicationError(error.Code, ErrorCategory.Conflict, error.Message, error.RuleId, extensions)
        };
    }
}
