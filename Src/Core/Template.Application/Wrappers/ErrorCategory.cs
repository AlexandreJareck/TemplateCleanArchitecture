#nullable enable
namespace Template.Application.Wrappers;

/// <summary>
/// Transport-neutral classification of application failures.
/// It is not serialized: public responses keep exposing <see cref="ErrorCode"/>
/// with its existing numeric values. HTTP mapping stays in Presentation.
/// </summary>
public enum ErrorCategory
{
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden,
    Unexpected
}

/// <summary>
/// Compatibility adapter between the public <see cref="ErrorCode"/> values and
/// <see cref="ErrorCategory"/>. No <see cref="ErrorCode"/> is renumbered.
/// </summary>
public static class ErrorCategoryExtensions
{
    public static ErrorCategory ToCategory(this ErrorCode errorCode) => errorCode switch
    {
        ErrorCode.ModelStateNotValid => ErrorCategory.Validation,
        ErrorCode.FieldDataInvalid => ErrorCategory.Validation,
        ErrorCode.NotFound => ErrorCategory.NotFound,
        ErrorCode.AccessDenied => ErrorCategory.Forbidden,
        ErrorCode.ErrorInIdentity => ErrorCategory.Unauthorized,
        ErrorCode.Exception => ErrorCategory.Unexpected,
        _ => ErrorCategory.Unexpected
    };

    public static ErrorCategory GetCategory(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.ErrorCode.ToCategory();
    }

    /// <summary>
    /// Category of the first error, mirroring how the HTTP boundary picks the status code.
    /// Returns <c>null</c> for successful results; failures without errors are <see cref="ErrorCategory.Unexpected"/>.
    /// </summary>
    public static ErrorCategory? GetErrorCategory(this BaseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Success)
            return null;

        return result.Errors is { Count: > 0 } errors
            ? errors[0].GetCategory()
            : ErrorCategory.Unexpected;
    }
}
