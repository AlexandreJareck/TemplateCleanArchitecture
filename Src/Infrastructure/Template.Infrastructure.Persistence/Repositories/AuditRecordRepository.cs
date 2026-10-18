using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Template.Application.Common.Paging;
using Template.Application.Interfaces.Auditing;
using Template.Domain.Auditing;
using Template.Infrastructure.Persistence.Contexts;

namespace Template.Infrastructure.Persistence.Repositories;

/// <summary>
/// Append-only writer and paged reader of <see cref="AuditRecord"/>. There is intentionally no update or delete path
/// (AUD-002); the record is committed by the caller's unit of work (AUD-001).
/// </summary>
public sealed class AuditRecordRepository(ApplicationDbContext context) : IAuditTrail, IAuditRecordReader
{
    private static readonly Dictionary<string, Expression<Func<AuditRecord, object>>> SortKeys = new(StringComparer.Ordinal)
    {
        ["occurredAt"] = record => record.OccurredAtUtc,
        ["action"] = record => record.Action,
        ["resourceType"] = record => record.ResourceType
    };

    public void Append(AuditRecord record) => context.Add(record);

    public async Task<PagedResult<AuditRecordView>> ListAsync(AuditRecordFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = context.Set<AuditRecord>().AsNoTracking();
        if (filter.Action is not null)
            query = query.Where(record => record.Action == filter.Action);
        if (filter.ActorId is not null)
            query = query.Where(record => record.ActorId == filter.ActorId);
        if (filter.ResourceType is not null)
            query = query.Where(record => record.ResourceType == filter.ResourceType);
        if (filter.ResourceId is not null)
            query = query.Where(record => record.ResourceId == filter.ResourceId);
        if (filter.FromUtc is not null)
            query = query.Where(record => record.OccurredAtUtc >= filter.FromUtc);
        if (filter.ToUtc is not null)
            query = query.Where(record => record.OccurredAtUtc < filter.ToUtc);

        var total = await query.LongCountAsync(cancellationToken);

        IOrderedQueryable<AuditRecord>? ordered = null;
        foreach (var sort in page.Sort)
        {
            var key = SortKeys[sort.Field];
            ordered = ordered is null
                ? sort.Descending ? query.OrderByDescending(key) : query.OrderBy(key)
                : sort.Descending ? ordered.ThenByDescending(key) : ordered.ThenBy(key);
        }

        var stable = ordered is null ? query.OrderBy(record => record.Id) : ordered.ThenBy(record => record.Id);
        var items = await stable.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);

        return new PagedResult<AuditRecordView>(
            items.Select(record => new AuditRecordView(
                record.Id,
                record.OccurredAtUtc,
                record.ActorId,
                record.Action,
                record.ResourceType,
                record.ResourceId,
                record.FromState,
                record.ToState,
                record.RuleIds,
                record.Reason,
                record.CorrelationId,
                record.Details)).ToArray(),
            page.Page,
            page.PageSize,
            total);
    }
}
