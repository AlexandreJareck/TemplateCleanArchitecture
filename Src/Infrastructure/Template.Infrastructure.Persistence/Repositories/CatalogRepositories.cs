using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Template.Application.Common.Paging;
using Template.Application.Interfaces.Catalog;
using Template.Domain.Catalog;
using Template.Infrastructure.Persistence.Contexts;

namespace Template.Infrastructure.Persistence.Repositories;

public sealed class CategoryRepository(ApplicationDbContext context) : ICategoryRepository
{
    private static readonly Dictionary<string, Expression<Func<Category, object>>> SortKeys = new(StringComparer.Ordinal)
    {
        ["code"] = category => category.Code,
        ["name"] = category => category.Name,
        ["createdAt"] = category => category.CreatedAtUtc,
        ["updatedAt"] = category => category.UpdatedAtUtc
    };

    public void Add(Category category) => context.Add(category);

    public Task<Category?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<Category>().SingleOrDefaultAsync(category => category.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Category>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        ids.Count == 0
            ? []
            : await context.Set<Category>().AsNoTracking().Where(category => ids.Contains(category.Id)).ToListAsync(cancellationToken);

    public Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<Category>().AsNoTracking().Where(category => category.Id == id)
            .Select(category => (long?)category.Version).SingleOrDefaultAsync(cancellationToken);

    public Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken) =>
        context.Set<Category>().AsNoTracking().AnyAsync(category => category.Code == normalizedCode, cancellationToken);

    public Task<PagedResult<Category>> ListAsync(CategoryFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = context.Set<Category>().AsNoTracking();
        if (MasterDataQueryExtensions.SearchTerm(filter.Q) is { } term)
            query = query.Where(category => category.Code.Contains(term) || category.Name.ToUpper().Contains(term));
        if (filter.IsActive is { } isActive)
            query = query.Where(category => category.IsActive == isActive);
        return query.ToPagedAsync(page, SortKeys, category => category.Id, cancellationToken);
    }
}

public sealed class UnitOfMeasureRepository(ApplicationDbContext context) : IUnitOfMeasureRepository
{
    private static readonly Dictionary<string, Expression<Func<UnitOfMeasure, object>>> SortKeys = new(StringComparer.Ordinal)
    {
        ["code"] = unit => unit.Code,
        ["name"] = unit => unit.Name,
        ["createdAt"] = unit => unit.CreatedAtUtc,
        ["updatedAt"] = unit => unit.UpdatedAtUtc
    };

    public void Add(UnitOfMeasure unit) => context.Add(unit);

    public Task<UnitOfMeasure?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<UnitOfMeasure>().SingleOrDefaultAsync(unit => unit.Id == id, cancellationToken);

    public Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<UnitOfMeasure>().AsNoTracking().Where(unit => unit.Id == id)
            .Select(unit => (long?)unit.Version).SingleOrDefaultAsync(cancellationToken);

    public Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken) =>
        context.Set<UnitOfMeasure>().AsNoTracking().AnyAsync(unit => unit.Code == normalizedCode, cancellationToken);

    public Task<PagedResult<UnitOfMeasure>> ListAsync(UnitOfMeasureFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = context.Set<UnitOfMeasure>().AsNoTracking();
        if (MasterDataQueryExtensions.SearchTerm(filter.Q) is { } term)
            query = query.Where(unit => unit.Code.Contains(term) || unit.Name.ToUpper().Contains(term));
        if (filter.IsActive is { } isActive)
            query = query.Where(unit => unit.IsActive == isActive);
        return query.ToPagedAsync(page, SortKeys, unit => unit.Id, cancellationToken);
    }
}

public sealed class CatalogItemRepository(ApplicationDbContext context) : ICatalogItemRepository
{
    private static readonly Dictionary<string, Expression<Func<CatalogItem, object>>> SortKeys = new(StringComparer.Ordinal)
    {
        ["code"] = item => item.Code,
        ["description"] = item => item.Description,
        ["kind"] = item => item.Kind,
        ["createdAt"] = item => item.CreatedAtUtc,
        ["updatedAt"] = item => item.UpdatedAtUtc
    };

    public void Add(CatalogItem item) => context.Add(item);

    public Task<CatalogItem?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<CatalogItem>().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<CatalogItem>().AsNoTracking().Where(item => item.Id == id)
            .Select(item => (long?)item.Version).SingleOrDefaultAsync(cancellationToken);

    public Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken) =>
        context.Set<CatalogItem>().AsNoTracking().AnyAsync(item => item.Code == normalizedCode, cancellationToken);

    public Task<PagedResult<CatalogItem>> ListAsync(CatalogItemFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = context.Set<CatalogItem>().AsNoTracking();
        if (MasterDataQueryExtensions.SearchTerm(filter.Q) is { } term)
            query = query.Where(item => item.Code.Contains(term) || item.Description.ToUpper().Contains(term));
        if (filter.IsActive is { } isActive)
            query = query.Where(item => item.IsActive == isActive);
        if (filter.Kind is { } kind)
            query = query.Where(item => item.Kind == kind);
        if (filter.CategoryId is { } categoryId)
            query = query.Where(item => item.CategoryId == categoryId);
        if (filter.UnitOfMeasureId is { } unitId)
            query = query.Where(item => item.UnitOfMeasureId == unitId);
        return query.ToPagedAsync(page, SortKeys, item => item.Id, cancellationToken);
    }
}
