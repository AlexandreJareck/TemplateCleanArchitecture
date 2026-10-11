#nullable enable
namespace Template.Application.Logging;

/// <summary>
/// Validates untrusted correlation values (message ids, correlation headers) before they are logged, returned or
/// used as keys: at most <see cref="MaxLength"/> characters of <c>[A-Za-z0-9._:-]</c>. Anything else is rejected, so a
/// caller falls back to a safe identifier instead of echoing attacker-controlled text.
/// </summary>
public static class CorrelationId
{
    public const int MaxLength = 128;

    public static bool IsValid(string? value) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= MaxLength
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '-');

    /// <summary>Returns <paramref name="value"/> when valid, otherwise <paramref name="fallback"/>.</summary>
    public static string Normalize(string? value, string fallback) => IsValid(value) ? value! : fallback;

    /// <summary>Creates a new safe identifier for work that has no usable inbound correlation.</summary>
    public static string New() => Guid.NewGuid().ToString("N");
}
