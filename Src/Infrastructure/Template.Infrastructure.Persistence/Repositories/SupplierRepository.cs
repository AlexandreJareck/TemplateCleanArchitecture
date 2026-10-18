using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Template.Application.Common.Paging;
using Template.Application.Interfaces.Suppliers;
using Template.Domain.Suppliers;
using Template.Infrastructure.Persistence.Contexts;

namespace Template.Infrastructure.Persistence.Repositories;

public sealed class SupplierRepository(ApplicationDbContext context) : ISupplierRepository
{
    private static readonly Dictionary<string, Expression<Func<Supplier, object>>> SortKeys = new(StringComparer.Ordinal)
    {
        ["legalName"] = supplier => supplier.LegalName,
        ["status"] = supplier => supplier.Status,
        ["createdAt"] = supplier => supplier.CreatedAtUtc,
        ["updatedAt"] = supplier => supplier.UpdatedAtUtc
    };

    public void Add(Supplier supplier) => context.Add(supplier);

    public Task<Supplier?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<Supplier>()
            .Include(supplier => supplier.Contacts)
            .Include(supplier => supplier.Addresses)
            .Include(supplier => supplier.Categories)
            .Include(supplier => supplier.StatusHistory)
            .AsSplitQuery()
            .SingleOrDefaultAsync(supplier => supplier.Id == id, cancellationToken);

    public Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<Supplier>().AsNoTracking().Where(supplier => supplier.Id == id)
            .Select(supplier => (long?)supplier.Version).SingleOrDefaultAsync(cancellationToken);

    public Task<bool> TaxIdExistsAsync(string country, string normalizedTaxId, Guid? excludingId, CancellationToken cancellationToken) =>
        context.Set<Supplier>().AsNoTracking().AnyAsync(
            supplier => supplier.Country == country && supplier.TaxId == normalizedTaxId && (excludingId == null || supplier.Id != excludingId),
            cancellationToken);

    public Task<PagedResult<Supplier>> ListAsync(SupplierFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = context.Set<Supplier>().AsNoTracking().Include(supplier => supplier.Categories).AsQueryable();
        if (MasterDataQueryExtensions.SearchTerm(filter.Q) is { } term)
            query = query.Where(supplier => supplier.LegalName.ToUpper().Contains(term)
                || (supplier.TradeName != null && supplier.TradeName.ToUpper().Contains(term)));
        if (filter.Statuses is { Count: > 0 } statuses)
            query = query.Where(supplier => statuses.Contains(supplier.Status));
        if (filter.Country is { } country)
            query = query.Where(supplier => supplier.Country == country);
        if (filter.TaxId is { } taxId)
            query = query.Where(supplier => supplier.TaxId == taxId);
        if (filter.CategoryId is { } categoryId)
            query = query.Where(supplier => supplier.Categories.Any(link => link.CategoryId == categoryId));
        return query.ToPagedAsync(page, SortKeys, supplier => supplier.Id, cancellationToken);
    }
}
