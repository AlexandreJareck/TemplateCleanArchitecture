using System;
using System.Text;

namespace Template.Domain.Suppliers;

/// <summary>
/// Normalization and structural validation of supplier tax identifiers (SUP-001). For Brazil the identifier is a CNPJ:
/// 14 characters after removing punctuation, the first 12 upper-case letters or digits (alphanumeric CNPJ of
/// IN RFB 2.229/2024, issued from July 2026) and the last 2 numeric check digits computed by modulo 11 with each
/// character valued as its ASCII code minus 48 (so digits keep their value). Purely numeric CNPJs are the special case
/// without letters. Other countries use a free format of 1–30 letters and digits.
/// <para>Structural validation is not government verification: a valid CNPJ may still be unregistered or inactive.</para>
/// </summary>
public static class TaxIdentifier
{
    public const string Brazil = "BR";
    public const int MaxLength = 30;
    public const int CnpjLength = 14;

    private static readonly int[] FirstWeights = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] SecondWeights = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    /// <summary>Upper-case ISO 3166-1 alpha-2 country, or <c>null</c> when malformed.</summary>
    public static string? NormalizeCountry(string? country)
    {
        var value = country?.Trim().ToUpperInvariant();
        return value is { Length: 2 } && value[0] is >= 'A' and <= 'Z' && value[1] is >= 'A' and <= 'Z' ? value : null;
    }

    /// <summary>
    /// Removes spaces and the separators <c>. / -</c>, upper-cases letters and validates the result for the country.
    /// Returns <c>null</c> when the identifier is structurally invalid.
    /// </summary>
    public static string? Normalize(string? country, string? value)
    {
        if (value is null || NormalizeCountry(country) is not { } normalizedCountry)
            return null;

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            if (character is '.' or '/' or '-' or ' ')
                continue;
            var upper = char.ToUpperInvariant(character);
            if (upper is not (>= '0' and <= '9' or >= 'A' and <= 'Z'))
                return null;
            builder.Append(upper);
        }

        var normalized = builder.ToString();
        if (normalizedCountry == Brazil)
            return IsValidCnpj(normalized) ? normalized : null;
        return normalized.Length is >= 1 and <= MaxLength ? normalized : null;
    }

    /// <summary>Structural CNPJ check of an already normalized value (no punctuation, upper case).</summary>
    public static bool IsValidCnpj(string? normalized)
    {
        if (normalized is not { Length: CnpjLength })
            return false;
        for (var index = 0; index < CnpjLength; index++)
        {
            var character = normalized[index];
            var allowed = index < 12
                ? character is >= '0' and <= '9' or >= 'A' and <= 'Z'
                : character is >= '0' and <= '9';
            if (!allowed)
                return false;
        }

        // Repeated digits (00000000000000, 11111111111111, ...) satisfy the arithmetic but are never issued.
        if (normalized.AsSpan().IndexOfAnyExcept(normalized[0]) < 0)
            return false;

        return CheckDigit(normalized.AsSpan(0, 12), FirstWeights) == normalized[12] - '0'
            && CheckDigit(normalized.AsSpan(0, 13), SecondWeights) == normalized[13] - '0';
    }

    /// <summary>Formats a normalized CNPJ as <c>AA.AAA.AAA/AAAA-DD</c>; other values are returned unchanged.</summary>
    public static string Format(string country, string normalized) =>
        country == Brazil && normalized.Length == CnpjLength
            ? $"{normalized[..2]}.{normalized[2..5]}.{normalized[5..8]}/{normalized[8..12]}-{normalized[12..]}"
            : normalized;

    private static int CheckDigit(ReadOnlySpan<char> characters, int[] weights)
    {
        var sum = 0;
        for (var index = 0; index < characters.Length; index++)
            sum += (characters[index] - '0') * weights[index];
        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
