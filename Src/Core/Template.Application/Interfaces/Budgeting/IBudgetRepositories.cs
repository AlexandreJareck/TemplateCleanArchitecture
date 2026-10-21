#nullable enable
using Template.Application.Common.Paging;
using Template.Domain.Budgeting;

namespace Template.Application.Interfaces.Budgeting;

/// <summary><see cref="Q"/> matches code or name case-insensitively.</summary>
public sealed record FiscalPeriodFilter(string? Q = null, IReadOnlyCollection<FiscalPeriodStatus>? Statuses = null);

public sealed record BudgetAllocationFilter(
    Guid? FiscalPeriodId = null,
    Guid? CostCenterId = null,
    string? Currency = null,
    BudgetAllocationStatus? Status = null);

public sealed record BudgetReservationFilter(IReadOnlyCollection<BudgetReservationStatus>? Statuses = null, Guid? RequisitionId = null);

/// <summary>
/// Aggregate repository of <see cref="FiscalPeriod"/>. Lock-taking reads apply only on relational providers and must
/// run inside the unit-of-work transaction; the lock is held until it commits or rolls back.
/// </summary>
public interface IFiscalPeriodRepository
{
    public const string CodeUniqueKey = "IX_BudgetFiscalPeriods_Code";

    /// <summary>PostgreSQL exclusion constraint that forbids overlapping date ranges (BUD-001).</summary>
    public const string NoOverlapKey = "EX_BudgetFiscalPeriods_NoOverlap";

    public static readonly IReadOnlyList<string> SortFields = ["code", "startDate", "status", "createdAt", "updatedAt"];

    void Add(FiscalPeriod period);

    Task<FiscalPeriod?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Tracked period read with <c>FOR UPDATE</c>: serializes closing with reservations in the period (BUD-011).</summary>
    Task<FiscalPeriod?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Tracked <c>Open</c> period containing <paramref name="date"/>, read with <c>FOR SHARE</c> so a concurrent close
    /// waits for the reserving transaction (BUD-004, BUD-011); <c>null</c> when none.
    /// </summary>
    Task<FiscalPeriod?> FindOpenContainingForShareAsync(DateOnly date, CancellationToken cancellationToken);

    /// <summary>Untracked <c>Open</c> period containing <paramref name="date"/> without a lock (read paths).</summary>
    Task<FiscalPeriod?> FindOpenContainingAsync(DateOnly date, CancellationToken cancellationToken);

    Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken);

    Task<bool> OverlapsAsync(DateOnly startDate, DateOnly endDate, Guid? excludingId, CancellationToken cancellationToken);

    Task<PagedResult<FiscalPeriod>> ListAsync(FiscalPeriodFilter filter, PageRequest page, CancellationToken cancellationToken);
}

/// <summary>Aggregate repository of <see cref="BudgetAllocation"/> and read access to its reservations.</summary>
public interface IBudgetAllocationRepository
{
    /// <summary>Unique (fiscal period, cost center, currency) (BUD-002).</summary>
    public const string UniqueKey = "IX_BudgetAllocations_FiscalPeriodId_CostCenterId_Currency";

    /// <summary>Partial unique index: at most one <c>Reserved</c>/<c>Committed</c> reservation per requisition (BUD-008).</summary>
    public const string ActiveReservationUniqueKey = "IX_BudgetReservations_RequisitionId_Active";

    public static readonly IReadOnlyList<string> SortFields = ["createdAt", "updatedAt", "currency", "amount", "available"];

    public static readonly IReadOnlyList<string> ReservationSortFields = ["createdAt", "amount", "status"];

    void Add(BudgetAllocation allocation);

    /// <summary>Tracked allocation with its amount history (no reservations).</summary>
    Task<BudgetAllocation?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Tracked allocation read with <c>FOR UPDATE</c> (BUD-006) together with its amount history and every reservation of
    /// <paramref name="requisitionId"/> (none for <see cref="Guid.Empty"/>). Totals on the allocation stay authoritative.
    /// </summary>
    Task<BudgetAllocation?> FindForUpdateAsync(Guid id, Guid requisitionId, CancellationToken cancellationToken);

    /// <summary>Allocation identity for (period, cost center, currency), or <c>null</c>.</summary>
    Task<Guid?> FindIdAsync(Guid fiscalPeriodId, Guid costCenterId, string currency, CancellationToken cancellationToken);

    /// <summary>Untracked allocation for (period, cost center, currency) without children, or <c>null</c>.</summary>
    Task<BudgetAllocation?> FindByKeyAsync(Guid fiscalPeriodId, Guid costCenterId, string currency, CancellationToken cancellationToken);

    Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid fiscalPeriodId, Guid costCenterId, string currency, CancellationToken cancellationToken);

    /// <summary>ORG-004 lock source.</summary>
    Task<bool> AnyAsync(CancellationToken cancellationToken);

    /// <summary>BUD-011: any <c>Reserved</c> reservation on an allocation of the period.</summary>
    Task<bool> AnyReservedInPeriodAsync(Guid fiscalPeriodId, CancellationToken cancellationToken);

    Task<PagedResult<BudgetAllocation>> ListAsync(BudgetAllocationFilter filter, PageRequest page, CancellationToken cancellationToken);

    /// <summary>Untracked reservations of one allocation with their history.</summary>
    Task<PagedResult<BudgetReservation>> ListReservationsAsync(
        Guid allocationId, BudgetReservationFilter filter, PageRequest page, CancellationToken cancellationToken);

    /// <summary>Untracked reservations of one requisition (every revision) with their history, oldest first.</summary>
    Task<IReadOnlyList<BudgetReservation>> ListReservationsOfRequisitionAsync(Guid requisitionId, CancellationToken cancellationToken);
}
