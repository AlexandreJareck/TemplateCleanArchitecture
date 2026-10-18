#nullable enable
namespace Template.Application.Common.Paging;

/// <summary>
/// List envelope of new v2 endpoints (docs/product/api-guidelines.md §3):
/// <c>{ items, page, pageSize, totalItems, totalPages }</c>. The legacy <c>PagedResponse&lt;T&gt;</c> stays for the
/// sample product endpoints only.
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalItems)
{
    public long TotalPages => TotalItems == 0 ? 0 : (TotalItems + PageSize - 1) / PageSize;
}
