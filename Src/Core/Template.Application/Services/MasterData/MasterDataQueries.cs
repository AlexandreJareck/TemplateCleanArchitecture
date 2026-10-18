#nullable enable
using Template.Application.Common.Paging;
using Template.Application.Common.Results;

namespace Template.Application.Services.MasterData;

/// <summary>Shared list-query helpers of the master-data modules (docs/product/api-guidelines.md §3).</summary>
public static class MasterDataQueries
{
    public const int MaxSearchLength = 100;

    /// <summary>The free-text filter <c>q</c> has at most 100 characters.</summary>
    public static ApplicationError? InvalidQ(string? q) =>
        q is { Length: > MaxSearchLength } ? ApplicationError.Validation("q", $"q has at most {MaxSearchLength} characters.") : null;

    public static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static PagedResult<TView> Map<TEntity, TView>(PagedResult<TEntity> page, Func<TEntity, TView> map) =>
        new(page.Items.Select(map).ToArray(), page.Page, page.PageSize, page.TotalItems);
}
