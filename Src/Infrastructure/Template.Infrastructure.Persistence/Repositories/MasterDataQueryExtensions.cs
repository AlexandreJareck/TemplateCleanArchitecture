using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Template.Application.Common.Paging;

namespace Template.Infrastructure.Persistence.Repositories;

/// <summary>
/// Allow-listed sorting with <c>id</c> as the final tie-breaker and counted paging (docs/product/api-guidelines.md §3),
/// shared by the master-data repositories.
/// </summary>
internal static class MasterDataQueryExtensions
{
    public static async Task<PagedResult<T>> ToPagedAsync<T>(
        this IQueryable<T> query,
        PageRequest page,
        IReadOnlyDictionary<string, Expression<Func<T, object>>> sortKeys,
        Expression<Func<T, Guid>> id,
        CancellationToken cancellationToken)
    {
        var total = await query.LongCountAsync(cancellationToken);

        IOrderedQueryable<T>? ordered = null;
        foreach (var sort in page.Sort)
        {
            var key = sortKeys[sort.Field];
            ordered = ordered is null
                ? sort.Descending ? query.OrderByDescending(key) : query.OrderBy(key)
                : sort.Descending ? ordered.ThenByDescending(key) : ordered.ThenBy(key);
        }

        var stable = ordered is null ? query.OrderBy(id) : ordered.ThenBy(id);
        var items = await stable.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);
        return new PagedResult<T>(items, page.Page, page.PageSize, total);
    }

    /// <summary>Upper-cased term for case-insensitive <c>Contains</c> that translates on PostgreSQL and InMemory alike.</summary>
    public static string? SearchTerm(string? q) => string.IsNullOrWhiteSpace(q) ? null : q.Trim().ToUpperInvariant();
}
