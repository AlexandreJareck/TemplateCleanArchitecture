using System.Text.Json;

namespace Template.Infrastructure.Integrations.Http;

/// <summary>
/// Defensive reading of provider JSON: every value is optional, trimmed, whitespace-collapsed and length-checked, so a
/// schema change surfaces as an explicit <c>Invalid</c> result instead of an exception or unvalidated data.
/// </summary>
internal static class ProviderJson
{
    public static bool TryParseObject(string? body, out JsonDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(body))
            return false;
        try
        {
            document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
                return true;
            document.Dispose();
            document = null;
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Text of a string or number property; <c>null</c> when absent, null or blank. <paramref name="valid"/> becomes
    /// <c>false</c> when the value has an unexpected JSON type or exceeds <paramref name="maxLength"/>.
    /// </summary>
    public static string? Text(JsonElement element, string property, int maxLength, ref bool valid)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;

        string? raw = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
        if (raw is null)
        {
            valid = false;
            return null;
        }

        var collapsed = string.Join(' ', raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length == 0)
            return null;
        if (collapsed.Length > maxLength)
        {
            valid = false;
            return null;
        }

        return collapsed;
    }

    public static string Digits(string? value) => value is null ? string.Empty : new string(value.Where(char.IsAsciiDigit).ToArray());
}
