namespace Template.Domain.Common;

/// <summary>ISO 4217 alphabetic code format (three upper-case ASCII letters); the supported list is configuration.</summary>
public static class CurrencyCodes
{
    /// <summary>Trimmed and upper-cased code, or <c>null</c> when it is not three ASCII letters.</summary>
    public static string? Normalize(string? value)
    {
        var code = value?.Trim().ToUpperInvariant();
        if (code is not { Length: 3 })
            return null;
        foreach (var character in code)
        {
            if (character is < 'A' or > 'Z')
                return null;
        }

        return code;
    }
}
