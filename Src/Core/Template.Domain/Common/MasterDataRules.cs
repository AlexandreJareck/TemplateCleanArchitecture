using System;
using System.Linq;

namespace Template.Domain.Common;

/// <summary>
/// Shared master-data rules (Phase 25): stable codes (ORG-001, CAT-001), names, reasons and UTC instants. Codes are
/// matched case-insensitively by storing them upper-cased, which also makes the unique indexes case-insensitive.
/// </summary>
public static class MasterDataRules
{
    public const int NameMaxLength = 200;
    public const int ReasonMinLength = 10;
    public const int ReasonMaxLength = 500;

    /// <summary>ORG-001: department and cost-center codes.</summary>
    public const string OrganizationCodePattern = "^[A-Z0-9-]{2,20}$";

    /// <summary>CAT-001: category and unit codes (for example <c>UN</c>, <c>KG</c>, <c>H</c>).</summary>
    public const string CatalogCodePattern = "^[A-Z0-9-]{1,20}$";

    /// <summary>CAT-001: catalog item codes/SKUs, which commonly carry dots and underscores.</summary>
    public const string CatalogItemCodePattern = "^[A-Z0-9][A-Z0-9._-]{0,39}$";

    /// <summary>Trims and upper-cases (invariant) a code; <c>null</c> stays <c>null</c>.</summary>
    public static string? NormalizeCode(string? code) => code?.Trim().ToUpperInvariant();

    /// <summary><see cref="OrganizationCodePattern"/>.</summary>
    public static bool IsOrganizationCode(string? normalizedCode) =>
        normalizedCode is { Length: >= 2 and <= 20 } && normalizedCode.All(character => IsUpperAlphanumeric(character) || character == '-');

    /// <summary><see cref="CatalogCodePattern"/>.</summary>
    public static bool IsCatalogCode(string? normalizedCode) =>
        normalizedCode is { Length: >= 1 and <= 20 } && normalizedCode.All(character => IsUpperAlphanumeric(character) || character == '-');

    /// <summary><see cref="CatalogItemCodePattern"/>.</summary>
    public static bool IsCatalogItemCode(string? normalizedCode) =>
        normalizedCode is { Length: >= 1 and <= 40 }
        && IsUpperAlphanumeric(normalizedCode[0])
        && normalizedCode.All(character => IsUpperAlphanumeric(character) || character is '.' or '_' or '-');

    /// <summary>Trimmed name of 1 to <paramref name="maxLength"/> characters, or <c>null</c> when invalid.</summary>
    public static string? NormalizeName(string? value, int maxLength = NameMaxLength)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > maxLength ? null : trimmed;
    }

    /// <summary>Trimmed reason of 10 to 500 characters (SUP-005, ORG-003), or <c>null</c> when invalid.</summary>
    public static string? NormalizeReason(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed is null || trimmed.Length < ReasonMinLength || trimmed.Length > ReasonMaxLength ? null : trimmed;
    }

    public static void EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Master-data instants must be UTC.", parameterName);
    }

    public static void EnsureId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("A non-empty identifier is required.", parameterName);
    }

    private static bool IsUpperAlphanumeric(char character) => character is >= 'A' and <= 'Z' or >= '0' and <= '9';
}
