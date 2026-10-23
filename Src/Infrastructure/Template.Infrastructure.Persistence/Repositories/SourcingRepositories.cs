using System.Collections.Concurrent;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Template.Application.Common.Paging;
using Template.Application.Interfaces.Sourcing;
using Template.Domain.Sourcing;
using Template.Domain.Suppliers;
using Template.Infrastructure.Persistence.Contexts;

namespace Template.Infrastructure.Persistence.Repositories;

public sealed class RequestForQuotationRepository(ApplicationDbContext context) : IRequestForQuotationRepository
{
    private static readonly Dictionary<string, Expression<Func<RequestForQuotation, object>>> SortKeys = new(StringComparer.Ordinal)
    {
        ["number"] = rfq => rfq.Number,
        ["status"] = rfq => rfq.Status,
        ["createdAt"] = rfq => rfq.CreatedAtUtc,
        ["updatedAt"] = rfq => rfq.UpdatedAtUtc,
        ["deadline"] = rfq => rfq.DeadlineUtc!,
        ["roundNumber"] = rfq => rfq.RoundNumber
    };

    public void Add(RequestForQuotation rfq) => context.Add(rfq);

    public Task<RequestForQuotation?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Full().SingleOrDefaultAsync(rfq => rfq.Id == id, cancellationToken);

    public Task<RequestForQuotation?> FindActiveByRequisitionAsync(Guid requisitionId, CancellationToken cancellationToken) =>
        Full().SingleOrDefaultAsync(rfq => rfq.RequisitionId == requisitionId && rfq.Status != RfqStatus.Cancelled, cancellationToken);

    public Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<RequestForQuotation>().AsNoTracking().Where(rfq => rfq.Id == id)
            .Select(rfq => (long?)rfq.Version).SingleOrDefaultAsync(cancellationToken);

    public Task<int> CountRoundsAsync(Guid requisitionId, CancellationToken cancellationToken) =>
        context.Set<RequestForQuotation>().AsNoTracking().CountAsync(rfq => rfq.RequisitionId == requisitionId, cancellationToken);

    public Task<PagedResult<RequestForQuotation>> ListAsync(RfqFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = context.Set<RequestForQuotation>().AsNoTracking().Include(rfq => rfq.Invitations).AsQueryable();
        if (filter.Statuses is { Count: > 0 } statuses)
            query = query.Where(rfq => statuses.Contains(rfq.Status));
        if (filter.RequisitionId is { } requisitionId)
            query = query.Where(rfq => rfq.RequisitionId == requisitionId);
        if (filter.Number is { } number)
            query = query.Where(rfq => rfq.Number.Contains(number) || rfq.RequisitionNumber.Contains(number));
        if (filter.SupplierId is { } supplierId)
            query = query.Where(rfq => rfq.Invitations.Any(invitation => invitation.SupplierId == supplierId && invitation.RemovedAtUtc == null));
        return query.ToPagedAsync(page, SortKeys, rfq => rfq.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListDueForCloseAsync(DateTime nowUtc, int max, CancellationToken cancellationToken) =>
        await context.Set<RequestForQuotation>().AsNoTracking()
            .Where(rfq => rfq.Status == RfqStatus.Published && rfq.DeadlineUtc <= nowUtc)
            .OrderBy(rfq => rfq.DeadlineUtc).ThenBy(rfq => rfq.Id)
            .Select(rfq => rfq.Id)
            .Take(max)
            .ToListAsync(cancellationToken);

    private IQueryable<RequestForQuotation> Full() =>
        context.Set<RequestForQuotation>()
            .Include(rfq => rfq.Lines)
            .Include(rfq => rfq.Invitations)
            .Include(rfq => rfq.Quotes).ThenInclude(quote => quote.Revisions).ThenInclude(revision => revision.Lines)
            .Include(rfq => rfq.Quotes).ThenInclude(quote => quote.Revisions).ThenInclude(revision => revision.Attachments)
            .Include(rfq => rfq.Waivers)
            .Include(rfq => rfq.ExchangeRates)
            .Include(rfq => rfq.Comparisons).ThenInclude(comparison => comparison.Entries)
            .Include(rfq => rfq.Awards)
            .Include(rfq => rfq.History)
            .AsSplitQuery();
}

/// <summary>
/// Per-year RFQ counter: an atomic PostgreSQL upsert in its own statement, like requisition numbers (gaps allowed;
/// only purchase orders are gapless, SP-2). Non-relational providers use a process-wide counter.
/// </summary>
public sealed class RfqNumberAllocator(ApplicationDbContext context) : IRfqNumberAllocator
{
    private static readonly ConcurrentDictionary<int, long> InMemoryCounters = new();

    public async Task<long> NextAsync(int year, CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational())
            return InMemoryCounters.AddOrUpdate(year, 1, (_, value) => value + 1);

        var values = await context.Database.SqlQuery<long>($"""
            INSERT INTO "SourcingRfqNumberCounters" ("Year", "LastValue") VALUES ({year}, 1)
            ON CONFLICT ("Year") DO UPDATE SET "LastValue" = "SourcingRfqNumberCounters"."LastValue" + 1
            RETURNING "LastValue" AS "Value"
            """).ToListAsync(cancellationToken);
        return values.Single();
    }
}

/// <summary>Current supplier identity and status (SUP-003) for sourcing, read without tracking; contacts are never read.</summary>
public sealed class SupplierSnapshotReader(ApplicationDbContext context) : ISupplierSnapshotReader
{
    public async Task<IReadOnlyDictionary<Guid, SupplierSnapshot>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return new Dictionary<Guid, SupplierSnapshot>();
        var rows = await context.Set<Supplier>().AsNoTracking().Where(supplier => ids.Contains(supplier.Id))
            .Select(supplier => new { supplier.Id, supplier.LegalName, supplier.Country, supplier.TaxId, supplier.Status })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(
            row => row.Id,
            row => new SupplierSnapshot(row.Id, row.LegalName, row.Country, row.TaxId, row.Status.ToString(), row.Status == SupplierStatus.Active));
    }
}
