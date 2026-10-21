#nullable enable
using Template.Application.Common.Paging;
using Template.Domain.Requisitions;

namespace Template.Application.Interfaces.Requisitions;

/// <summary>
/// What an actor may read (REQ-011): own requisitions, the cost centers they manage and the departments they head
/// (<c>requisitions.read-team</c>), or everything (<c>requisitions.read-all</c>).
/// </summary>
public sealed record RequisitionVisibility(
    Guid UserId,
    bool ReadAll,
    IReadOnlyCollection<Guid> ManagedCostCenterIds,
    IReadOnlyCollection<Guid> HeadedDepartmentIds)
{
    public bool CanRead(Guid requesterId, Guid costCenterId, Guid departmentId) =>
        ReadAll || requesterId == UserId || ManagedCostCenterIds.Contains(costCenterId) || HeadedDepartmentIds.Contains(departmentId);
}

/// <summary>
/// List filters; <see cref="Visibility"/> is always applied (collections filter silently, REQ-011).
/// <see cref="RequesterId"/> narrows to one requester (<c>scope=mine</c> uses the actor).
/// </summary>
public sealed record RequisitionFilter(
    RequisitionVisibility Visibility,
    Guid? RequesterId = null,
    IReadOnlyCollection<RequisitionStatus>? Statuses = null,
    Guid? CostCenterId = null,
    string? Number = null,
    DateTime? CreatedFromUtc = null,
    DateTime? CreatedToUtc = null);

public interface IPurchaseRequisitionRepository
{
    public const string NumberUniqueKey = "IX_PurchaseRequisitions_Number";

    public static readonly IReadOnlyList<string> SortFields =
        ["number", "status", "createdAt", "updatedAt", "submittedAt", "neededBy", "estimatedTotal"];

    void Add(PurchaseRequisition requisition);

    /// <summary>Deletes a never-submitted draft (REQ-010); the use case checks the rule first.</summary>
    void Remove(PurchaseRequisition requisition);

    /// <summary>Tracked aggregate with lines, attachments, state changes and submissions, or <c>null</c>.</summary>
    Task<PurchaseRequisition?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Untracked requisitions without children.</summary>
    Task<PagedResult<PurchaseRequisition>> ListAsync(RequisitionFilter filter, PageRequest page, CancellationToken cancellationToken);

    /// <summary>REQ-009 sweep: <c>Submitted</c>/<c>InSourcing</c> requisitions whose needed-by date is before <paramref name="neededByBefore"/>.</summary>
    Task<IReadOnlyList<Guid>> ListDueForExpiryAsync(DateOnly neededByBefore, int max, CancellationToken cancellationToken);
}

/// <summary>Allocates requisition numbers <c>REQ-{yyyy}-{nnnnnn}</c> from a per-year counter (gaps are allowed).</summary>
public interface IRequisitionNumberAllocator
{
    Task<long> NextAsync(int year, CancellationToken cancellationToken);
}

/// <summary>Organization scope of a user for REQ-011 team visibility.</summary>
public sealed record OrganizationScope(IReadOnlyCollection<Guid> ManagedCostCenterIds, IReadOnlyCollection<Guid> HeadedDepartmentIds);

public interface IOrganizationScopeReader
{
    /// <summary>Cost centers managed and departments headed by <paramref name="userId"/>.</summary>
    Task<OrganizationScope> GetScopeAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Catalog item, category and unit reference resolved for a requisition line.</summary>
public sealed record CatalogReferenceState(Guid Id, string Code, string Name, bool IsActive);

public sealed record UnitReferenceState(Guid Id, string Code, string Name, int AllowedDecimals, bool IsActive);

public sealed record CatalogItemReferenceState(
    Guid Id, string Description, string Kind, Guid CategoryId, Guid UnitOfMeasureId, bool IsActive, decimal? ReferencePrice, string? ReferenceCurrency);

/// <summary>Reads the Catalog module's current reference state for requisition lines (REQ-004, CAT-002).</summary>
public interface IRequisitionReferenceReader
{
    Task<CatalogItemReferenceState?> FindCatalogItemAsync(Guid id, CancellationToken cancellationToken);

    Task<CatalogReferenceState?> FindCategoryAsync(Guid id, CancellationToken cancellationToken);

    Task<UnitReferenceState?> FindUnitAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Active flag of every known catalog item, category and unit among <paramref name="ids"/>; unknown IDs are omitted.</summary>
    Task<IReadOnlyDictionary<Guid, bool>> GetActiveStatesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}
