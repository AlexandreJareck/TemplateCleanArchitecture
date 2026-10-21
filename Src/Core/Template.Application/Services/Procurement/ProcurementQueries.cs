#nullable enable
using Template.Application.Common.Results;

namespace Template.Application.Services.Procurement;

/// <summary>Shared list-query helpers of the procurement modules (docs/product/api-guidelines.md §3).</summary>
public static class ProcurementQueries
{
    /// <summary>
    /// Parses a comma-separated enum filter such as <c>status=Draft,Submitted</c> (case-insensitive names only);
    /// <c>null</c> or empty means "no filter". Unknown names are a validation error naming <paramref name="parameter"/>.
    /// </summary>
    public static Result<IReadOnlyCollection<TEnum>?> ParseEnumList<TEnum>(string? value, string parameter)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<IReadOnlyCollection<TEnum>?>.Success(null);

        var parsed = new List<TEnum>();
        foreach (var part in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Enum.TryParse<TEnum>(part, ignoreCase: true, out var item) || int.TryParse(part, out _))
                return ApplicationError.Validation(parameter, $"{parameter} accepts a comma-separated list of: {string.Join(", ", Enum.GetNames<TEnum>())}.");
            parsed.Add(item);
        }

        return Result<IReadOnlyCollection<TEnum>?>.Success(parsed.Distinct().ToArray());
    }
}
