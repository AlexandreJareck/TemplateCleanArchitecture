using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Template.Application.Common.Paging;
using Template.Application.Interfaces.Budgeting;
using Template.Application.Interfaces.Organization;
using Template.Domain.Budgeting;
using Template.Infrastructure.Persistence.Contexts;

namespace Template.Infrastructure.Persistence.Repositories;

public sealed class FiscalPeriodRepository(ApplicationDbContext context) : IFiscalPeriodRepository
{
    private static readonly Dictionary<string, Expression<Func<FiscalPeriod, object>>> SortKeys = new(StringComparer.Ordinal)
    {
        ["code"] = period => period.Code,
        ["startDate"] = period => period.StartDate,
        ["status"] = period => period.Status,
        ["createdAt"] = period => period.CreatedAtUtc,
        ["updatedAt"] = period => period.UpdatedAtUtc
    };

    public void Add(FiscalPeriod period) => context.Add(period);

    public Task<FiscalPeriod?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<FiscalPeriod>().SingleOrDefaultAsync(period => period.Id == id, cancellationToken);

    public async Task<FiscalPeriod?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        await RowLocks.LockAsync(context, "BudgetFiscalPeriods", id, RowLocks.ForUpdate, cancellationToken);
        return await FindAsync(id, cancellationToken);
    }

    public async Task<FiscalPeriod?> FindOpenContainingForShareAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var id = await OpenContaining(date).Select(period => (Guid?)period.Id).FirstOrDefaultAsync(cancellationToken);
        if (id is null)
            return null;

        // Lock first, then read the tracked row, so a period closed meanwhile is seen as Closed (never a stale Open).
        await RowLocks.LockAsync(context, "BudgetFiscalPeriods", id.Value, RowLocks.ForShare, cancellationToken);
        return await FindAsync(id.Value, cancellationToken);
    }

    public Task<FiscalPeriod?> FindOpenContainingAsync(DateOnly date, CancellationToken cancellationToken) =>
        OpenContaining(date).FirstOrDefaultAsync(cancellationToken);

    public Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<FiscalPeriod>().AsNoTracking().Where(period => period.Id == id)
            .Select(period => (long?)period.Version).SingleOrDefaultAsync(cancellationToken);

    public Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken) =>
        context.Set<FiscalPeriod>().AsNoTracking().AnyAsync(period => period.Code == normalizedCode, cancellationToken);

    public Task<bool> OverlapsAsync(DateOnly startDate, DateOnly endDate, Guid? excludingId, CancellationToken cancellationToken) =>
        context.Set<FiscalPeriod>().AsNoTracking().AnyAsync(
            period => period.StartDate <= endDate && period.EndDate >= startDate && (excludingId == null || period.Id != excludingId),
            cancellationToken);

    public Task<PagedResult<FiscalPeriod>> ListAsync(FiscalPeriodFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = context.Set<FiscalPeriod>().AsNoTracking();
        if (MasterDataQueryExtensions.SearchTerm(filter.Q) is { } term)
            query = query.Where(period => period.Code.Contains(term) || period.Name.ToUpper().Contains(term));
        if (filter.Statuses is { Count: > 0 } statuses)
            query = query.Where(period => statuses.Contains(period.Status));
        return query.ToPagedAsync(page, SortKeys, period => period.Id, cancellationToken);
    }

    private IQueryable<FiscalPeriod> OpenContaining(DateOnly date) =>
        context.Set<FiscalPeriod>().AsNoTracking()
            .Where(period => period.Status == FiscalPeriodStatus.Open && period.StartDate <= date && period.EndDate >= date)
            .OrderBy(period => period.StartDate);
}

public sealed class BudgetAllocationRepository(ApplicationDbContext context) : IBudgetAllocationRepository
{
    private static readonly Dictionary<string, Expression<Func<BudgetAllocation, object>>> SortKeys = new(StringComparer.Ordinal)
    {
        ["createdAt"] = allocation => allocation.CreatedAtUtc,
        ["updatedAt"] = allocation => allocation.UpdatedAtUtc,
        ["currency"] = allocation => allocation.Currency,
        ["amount"] = allocation => allocation.Amount,
        ["available"] = allocation => allocation.Amount - allocation.ReservedAmount - allocation.CommittedAmount
    };

    private static readonly Dictionary<string, Expression<Func<BudgetReservation, object>>> ReservationSortKeys = new(StringComparer.Ordinal)
    {
        ["createdAt"] = reservation => reservation.CreatedAtUtc,
        ["amount"] = reservation => reservation.Amount,
        ["status"] = reservation => reservation.Status
    };

    public void Add(BudgetAllocation allocation) => context.Add(allocation);

    public Task<BudgetAllocation?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<BudgetAllocation>()
            .Include(allocation => allocation.AmountHistory)
            .AsSplitQuery()
            .SingleOrDefaultAsync(allocation => allocation.Id == id, cancellationToken);

    public async Task<BudgetAllocation?> FindForUpdateAsync(Guid id, Guid requisitionId, CancellationToken cancellationToken)
    {
        await RowLocks.LockAsync(context, "BudgetAllocations", id, RowLocks.ForUpdate, cancellationToken);
        return await context.Set<BudgetAllocation>()
            .Include(allocation => allocation.AmountHistory)
            .Include(allocation => allocation.Reservations.Where(reservation => reservation.RequisitionId == requisitionId))
            .ThenInclude(reservation => reservation.Changes)
            .AsSplitQuery()
            .SingleOrDefaultAsync(allocation => allocation.Id == id, cancellationToken);
    }

    public Task<Guid?> FindIdAsync(Guid fiscalPeriodId, Guid costCenterId, string currency, CancellationToken cancellationToken) =>
        ByKey(fiscalPeriodId, costCenterId, currency).Select(allocation => (Guid?)allocation.Id).SingleOrDefaultAsync(cancellationToken);

    public Task<BudgetAllocation?> FindByKeyAsync(Guid fiscalPeriodId, Guid costCenterId, string currency, CancellationToken cancellationToken) =>
        ByKey(fiscalPeriodId, costCenterId, currency).SingleOrDefaultAsync(cancellationToken);

    public Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<BudgetAllocation>().AsNoTracking().Where(allocation => allocation.Id == id)
            .Select(allocation => (long?)allocation.Version).SingleOrDefaultAsync(cancellationToken);

    public Task<bool> ExistsAsync(Guid fiscalPeriodId, Guid costCenterId, string currency, CancellationToken cancellationToken) =>
        ByKey(fiscalPeriodId, costCenterId, currency).AnyAsync(cancellationToken);

    public Task<bool> AnyAsync(CancellationToken cancellationToken) =>
        context.Set<BudgetAllocation>().AsNoTracking().AnyAsync(cancellationToken);

    public Task<bool> AnyReservedInPeriodAsync(Guid fiscalPeriodId, CancellationToken cancellationToken) =>
        context.Set<BudgetAllocation>().AsNoTracking()
            .AnyAsync(allocation => allocation.FiscalPeriodId == fiscalPeriodId && allocation.ReservedAmount > 0, cancellationToken);

    public Task<PagedResult<BudgetAllocation>> ListAsync(BudgetAllocationFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = context.Set<BudgetAllocation>().AsNoTracking();
        if (filter.FiscalPeriodId is { } periodId)
            query = query.Where(allocation => allocation.FiscalPeriodId == periodId);
        if (filter.CostCenterId is { } costCenterId)
            query = query.Where(allocation => allocation.CostCenterId == costCenterId);
        if (filter.Currency is { } currency)
            query = query.Where(allocation => allocation.Currency == currency);
        if (filter.Status is { } status)
            query = query.Where(allocation => allocation.Status == status);
        return query.ToPagedAsync(page, SortKeys, allocation => allocation.Id, cancellationToken);
    }

    public Task<PagedResult<BudgetReservation>> ListReservationsAsync(
        Guid allocationId, BudgetReservationFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = context.Set<BudgetReservation>().AsNoTracking().Include(reservation => reservation.Changes)
            .Where(reservation => reservation.AllocationId == allocationId);
        if (filter.Statuses is { Count: > 0 } statuses)
            query = query.Where(reservation => statuses.Contains(reservation.Status));
        if (filter.RequisitionId is { } requisitionId)
            query = query.Where(reservation => reservation.RequisitionId == requisitionId);
        return query.AsSplitQuery().ToPagedAsync(page, ReservationSortKeys, reservation => reservation.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<BudgetReservation>> ListReservationsOfRequisitionAsync(Guid requisitionId, CancellationToken cancellationToken) =>
        await context.Set<BudgetReservation>().AsNoTracking().Include(reservation => reservation.Changes)
            .Where(reservation => reservation.RequisitionId == requisitionId)
            .OrderBy(reservation => reservation.CreatedAtUtc).ThenBy(reservation => reservation.Id)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    private IQueryable<BudgetAllocation> ByKey(Guid fiscalPeriodId, Guid costCenterId, string currency) =>
        context.Set<BudgetAllocation>().AsNoTracking().Where(allocation =>
            allocation.FiscalPeriodId == fiscalPeriodId && allocation.CostCenterId == costCenterId && allocation.Currency == currency);
}

/// <summary>ORG-004 lock: base currency and time zone freeze once the first budget allocation exists (replaces the Phase 25 placeholder).</summary>
public sealed class BudgetAllocationSettingsLock(IBudgetAllocationRepository allocations) : IOrganizationSettingsLock
{
    public Task<bool> HasBudgetAllocationsAsync(CancellationToken cancellationToken) => allocations.AnyAsync(cancellationToken);
}

/// <summary>
/// PostgreSQL row locks taken inside the unit-of-work transaction (BUD-006, BUD-011). Table names come from the fixed
/// set below, never from input; the identifier is a parameter. Non-relational providers (functional tests on EF
/// InMemory) have no locks, so concurrency there relies on the version token alone and is proven on PostgreSQL.
/// </summary>
internal static class RowLocks
{
    public const string ForUpdate = "FOR UPDATE";
    public const string ForShare = "FOR SHARE";

    private static readonly HashSet<string> Tables = new(StringComparer.Ordinal) { "BudgetFiscalPeriods", "BudgetAllocations" };

    public static async Task LockAsync(ApplicationDbContext context, string table, Guid id, string mode, CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational())
            return;
        if (!Tables.Contains(table) || mode is not (ForUpdate or ForShare))
            throw new ArgumentException("Unsupported row lock.", nameof(table));
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Row locks require an active unit-of-work transaction.");

#pragma warning disable EF1002 // table and mode come from the fixed allow-list above; the identifier is parameterized
        await context.Database.ExecuteSqlRawAsync($"SELECT 1 FROM \"{table}\" WHERE \"Id\" = {{0}} {mode}", [id], cancellationToken);
#pragma warning restore EF1002
    }
}
