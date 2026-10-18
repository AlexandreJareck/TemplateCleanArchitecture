#nullable enable
using Template.Application.Common.Paging;
using Template.Domain.Catalog;

namespace Template.Application.Interfaces.Catalog;

/// <summary><see cref="Q"/> matches code or name case-insensitively.</summary>
public sealed record CategoryFilter(string? Q = null, bool? IsActive = null);

public sealed record UnitOfMeasureFilter(string? Q = null, bool? IsActive = null);

/// <summary><see cref="Q"/> matches code or description case-insensitively.</summary>
public sealed record CatalogItemFilter(
    string? Q = null,
    bool? IsActive = null,
    CatalogItemKind? Kind = null,
    Guid? CategoryId = null,
    Guid? UnitOfMeasureId = null);

public interface ICategoryRepository
{
    public const string CodeUniqueKey = "IX_CatalogCategories_Code";

    public static readonly IReadOnlyList<string> SortFields = ["code", "name", "createdAt", "updatedAt"];

    void Add(Category category);

    Task<Category?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Untracked categories with the given IDs (unknown IDs are omitted).</summary>
    Task<IReadOnlyList<Category>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken);

    Task<PagedResult<Category>> ListAsync(CategoryFilter filter, PageRequest page, CancellationToken cancellationToken);
}

public interface IUnitOfMeasureRepository
{
    public const string CodeUniqueKey = "IX_CatalogUnitsOfMeasure_Code";

    public static readonly IReadOnlyList<string> SortFields = ["code", "name", "createdAt", "updatedAt"];

    void Add(UnitOfMeasure unit);

    Task<UnitOfMeasure?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken);

    Task<PagedResult<UnitOfMeasure>> ListAsync(UnitOfMeasureFilter filter, PageRequest page, CancellationToken cancellationToken);
}

public interface ICatalogItemRepository
{
    public const string CodeUniqueKey = "IX_CatalogItems_Code";

    public static readonly IReadOnlyList<string> SortFields = ["code", "description", "kind", "createdAt", "updatedAt"];

    void Add(CatalogItem item);

    Task<CatalogItem?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken);

    Task<PagedResult<CatalogItem>> ListAsync(CatalogItemFilter filter, PageRequest page, CancellationToken cancellationToken);
}
