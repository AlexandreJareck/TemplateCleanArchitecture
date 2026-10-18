#nullable enable
using Template.Application.Common.Paging;
using Template.Domain.Suppliers;

namespace Template.Application.Interfaces.Suppliers;

/// <summary>
/// Supplier list filters: <see cref="Q"/> matches legal or trade name case-insensitively; <see cref="TaxId"/> is an
/// already normalized identifier matched exactly (with <see cref="Country"/> when given).
/// </summary>
public sealed record SupplierFilter(
    string? Q = null,
    IReadOnlyCollection<SupplierStatus>? Statuses = null,
    string? Country = null,
    string? TaxId = null,
    Guid? CategoryId = null);

public interface ISupplierRepository
{
    /// <summary>Unique index on (country, normalized tax ID) (SUP-001).</summary>
    public const string TaxIdUniqueKey = "IX_Suppliers_Country_TaxId";

    public static readonly IReadOnlyList<string> SortFields = ["legalName", "status", "createdAt", "updatedAt"];

    void Add(Supplier supplier);

    /// <summary>Tracked aggregate with contacts, addresses, categories and status history, or <c>null</c>.</summary>
    Task<Supplier?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Whether another supplier (not <paramref name="excludingId"/>) already uses the tax ID.</summary>
    Task<bool> TaxIdExistsAsync(string country, string normalizedTaxId, Guid? excludingId, CancellationToken cancellationToken);

    /// <summary>Untracked suppliers with their categories (no contacts or addresses).</summary>
    Task<PagedResult<Supplier>> ListAsync(SupplierFilter filter, PageRequest page, CancellationToken cancellationToken);
}
