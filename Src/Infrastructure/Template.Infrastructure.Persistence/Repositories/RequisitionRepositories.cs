using System.Collections.Concurrent;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Template.Application.Common.Paging;
using Template.Application.Interfaces.Requisitions;
using Template.Domain.Catalog;
using Template.Domain.Organization;
using Template.Domain.Requisitions;
using Template.Infrastructure.Persistence.Contexts;

namespace Template.Infrastructure.Persistence.Repositories;

public sealed class PurchaseRequisitionRepository(ApplicationDbContext context) : IPurchaseRequisitionRepository
{
    private static readonly Dictionary<string, Expression<Func<PurchaseRequisition, object>>> SortKeys = new(StringComparer.Ordinal)
    {
        ["number"] = requisition => requisition.Number,
        ["status"] = requisition => requisition.Status,
        ["createdAt"] = requisition => requisition.CreatedAtUtc,
        ["updatedAt"] = requisition => requisition.UpdatedAtUtc,
        ["submittedAt"] = requisition => requisition.SubmittedAtUtc!,
        ["neededBy"] = requisition => requisition.NeededBy!,
        ["estimatedTotal"] = requisition => requisition.EstimatedTotal
    };

    public void Add(PurchaseRequisition requisition) => context.Add(requisition);

    public void Remove(PurchaseRequisition requisition) => context.Remove(requisition);

    public Task<PurchaseRequisition?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<PurchaseRequisition>()
            .Include(requisition => requisition.Lines)
            .Include(requisition => requisition.Attachments)
            .Include(requisition => requisition.StateChanges)
            .Include(requisition => requisition.Submissions).ThenInclude(submission => submission.Lines)
            .AsSplitQuery()
            .SingleOrDefaultAsync(requisition => requisition.Id == id, cancellationToken);

    public Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<PurchaseRequisition>().AsNoTracking().Where(requisition => requisition.Id == id)
            .Select(requisition => (long?)requisition.Version).SingleOrDefaultAsync(cancellationToken);

    public Task<PagedResult<PurchaseRequisition>> ListAsync(RequisitionFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = context.Set<PurchaseRequisition>().AsNoTracking();
        var visibility = filter.Visibility;
        if (!visibility.ReadAll)
        {
            var userId = visibility.UserId;
            var costCenters = visibility.ManagedCostCenterIds;
            var departments = visibility.HeadedDepartmentIds;
            query = query.Where(requisition => requisition.RequesterId == userId
                || costCenters.Contains(requisition.CostCenterId)
                || departments.Contains(requisition.DepartmentId));
        }

        if (filter.RequesterId is { } requesterId)
            query = query.Where(requisition => requisition.RequesterId == requesterId);
        if (filter.Statuses is { Count: > 0 } statuses)
            query = query.Where(requisition => statuses.Contains(requisition.Status));
        if (filter.CostCenterId is { } costCenterId)
            query = query.Where(requisition => requisition.CostCenterId == costCenterId);
        if (filter.Number is { } number)
            query = query.Where(requisition => requisition.Number.Contains(number));
        if (filter.CreatedFromUtc is { } from)
            query = query.Where(requisition => requisition.CreatedAtUtc >= from);
        if (filter.CreatedToUtc is { } to)
            query = query.Where(requisition => requisition.CreatedAtUtc < to);
        return query.ToPagedAsync(page, SortKeys, requisition => requisition.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListDueForExpiryAsync(DateOnly neededByBefore, int max, CancellationToken cancellationToken) =>
        await context.Set<PurchaseRequisition>().AsNoTracking()
            .Where(requisition => (requisition.Status == RequisitionStatus.Submitted || requisition.Status == RequisitionStatus.InSourcing)
                && requisition.NeededBy < neededByBefore)
            .OrderBy(requisition => requisition.NeededBy).ThenBy(requisition => requisition.Id)
            .Select(requisition => requisition.Id)
            .Take(max)
            .ToListAsync(cancellationToken);
}

/// <summary>
/// Per-year counter: on PostgreSQL an atomic upsert (<c>INSERT … ON CONFLICT DO UPDATE … RETURNING</c>) that runs in its
/// own statement, so concurrent drafts never share a number; a rolled-back draft leaves a gap, which is allowed for
/// requisitions (only purchase orders are gapless, SP-2). Non-relational providers use a process-wide counter.
/// </summary>
public sealed class RequisitionNumberAllocator(ApplicationDbContext context) : IRequisitionNumberAllocator
{
    private static readonly ConcurrentDictionary<int, long> InMemoryCounters = new();

    public async Task<long> NextAsync(int year, CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational())
            return InMemoryCounters.AddOrUpdate(year, 1, (_, value) => value + 1);

        var values = await context.Database.SqlQuery<long>($"""
            INSERT INTO "PurchaseRequisitionNumberCounters" ("Year", "LastValue") VALUES ({year}, 1)
            ON CONFLICT ("Year") DO UPDATE SET "LastValue" = "PurchaseRequisitionNumberCounters"."LastValue" + 1
            RETURNING "LastValue" AS "Value"
            """).ToListAsync(cancellationToken);
        return values.Single();
    }
}

/// <summary>REQ-011 team scope from Organization data: managed cost centers and headed departments.</summary>
public sealed class OrganizationScopeReader(ApplicationDbContext context) : IOrganizationScopeReader
{
    public async Task<OrganizationScope> GetScopeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var costCenters = await context.Set<CostCenter>().AsNoTracking()
            .Where(costCenter => costCenter.ManagerUserId == userId).Select(costCenter => costCenter.Id).ToListAsync(cancellationToken);
        var departments = await context.Set<Department>().AsNoTracking()
            .Where(department => department.HeadUserId == userId).Select(department => department.Id).ToListAsync(cancellationToken);
        return new OrganizationScope(costCenters, departments);
    }
}

/// <summary>Current Catalog state for requisition lines (REQ-004, CAT-002), read without tracking.</summary>
public sealed class RequisitionReferenceReader(ApplicationDbContext context) : IRequisitionReferenceReader
{
    public Task<CatalogItemReferenceState?> FindCatalogItemAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<CatalogItem>().AsNoTracking().Where(item => item.Id == id)
            .Select(item => new CatalogItemReferenceState(
                item.Id, item.Description, item.Kind.ToString(), item.CategoryId, item.UnitOfMeasureId, item.IsActive,
                item.ReferencePriceAmount, item.ReferencePriceCurrency))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<CatalogReferenceState?> FindCategoryAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<Category>().AsNoTracking().Where(category => category.Id == id)
            .Select(category => new CatalogReferenceState(category.Id, category.Code, category.Name, category.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<UnitReferenceState?> FindUnitAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<UnitOfMeasure>().AsNoTracking().Where(unit => unit.Id == id)
            .Select(unit => new UnitReferenceState(unit.Id, unit.Code, unit.Name, unit.AllowedDecimals, unit.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, bool>> GetActiveStatesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var states = new Dictionary<Guid, bool>();
        if (ids.Count == 0)
            return states;
        foreach (var item in await context.Set<CatalogItem>().AsNoTracking().Where(item => ids.Contains(item.Id))
                     .Select(item => new { item.Id, item.IsActive }).ToListAsync(cancellationToken))
            states[item.Id] = item.IsActive;
        foreach (var category in await context.Set<Category>().AsNoTracking().Where(category => ids.Contains(category.Id))
                     .Select(category => new { category.Id, category.IsActive }).ToListAsync(cancellationToken))
            states[category.Id] = category.IsActive;
        foreach (var unit in await context.Set<UnitOfMeasure>().AsNoTracking().Where(unit => ids.Contains(unit.Id))
                     .Select(unit => new { unit.Id, unit.IsActive }).ToListAsync(cancellationToken))
            states[unit.Id] = unit.IsActive;
        return states;
    }
}
