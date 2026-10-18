#nullable enable
using Template.Application.Common.Results;

namespace Template.Application.Common.Paging;

/// <summary>One validated sort key; <see cref="Field"/> is always an allow-listed name.</summary>
public sealed record SortField(string Field, bool Descending);

/// <summary>
/// Validated paging and sorting of a list request (docs/product/api-guidelines.md §3): <c>page</c> ≥ 1 (default 1),
/// <c>pageSize</c> 1–100 (default 20), <c>sort</c> as comma-separated allow-listed fields with an optional <c>-</c>
/// prefix for descending order. The data source appends <c>id</c> as the final tie-breaker.
/// </summary>
public sealed record PageRequest(int Page, int PageSize, IReadOnlyList<SortField> Sort)
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public int Skip => (Page - 1) * PageSize;

    /// <summary>
    /// Parses raw query values. Unknown sort fields, duplicates and out-of-range values produce a validation error
    /// naming the parameter; <paramref name="defaultSort"/> applies when <paramref name="sort"/> is empty.
    /// </summary>
    public static Result<PageRequest> Create(
        int? page,
        int? pageSize,
        string? sort,
        IReadOnlyCollection<string> allowedSortFields,
        string defaultSort)
    {
        var errors = new Dictionary<string, string[]>();
        var resolvedPage = page ?? DefaultPage;
        var resolvedPageSize = pageSize ?? DefaultPageSize;

        if (resolvedPage < 1)
            errors["page"] = ["page must be greater than or equal to 1."];
        if (resolvedPageSize is < 1 or > MaxPageSize)
            errors["pageSize"] = [$"pageSize must be between 1 and {MaxPageSize}."];

        var fields = new List<SortField>();
        var rawSort = string.IsNullOrWhiteSpace(sort) ? defaultSort : sort;
        foreach (var part in rawSort.Split(',', StringSplitOptions.TrimEntries))
        {
            var descending = part.StartsWith('-');
            var name = descending ? part[1..] : part;
            var allowed = allowedSortFields.FirstOrDefault(field => string.Equals(field, name, StringComparison.OrdinalIgnoreCase));
            if (allowed is null || fields.Any(field => field.Field == allowed))
            {
                errors["sort"] = [$"sort accepts a comma-separated list of: {string.Join(", ", allowedSortFields)}."];
                break;
            }

            fields.Add(new SortField(allowed, descending));
        }

        return errors.Count > 0
            ? ApplicationError.Validation(errors)
            : new PageRequest(resolvedPage, resolvedPageSize, fields);
    }
}
