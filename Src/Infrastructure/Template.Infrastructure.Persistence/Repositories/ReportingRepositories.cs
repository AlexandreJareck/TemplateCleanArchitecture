using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Template.Application.Common.Paging;
using Template.Application.Interfaces.Files;
using Template.Application.Interfaces.Reporting;
using Template.Domain.Budgeting;
using Template.Domain.Files;
using Template.Domain.Organization;
using Template.Domain.Reporting;
using Template.Domain.Sourcing;
using Template.Infrastructure.Persistence.Contexts;

namespace Template.Infrastructure.Persistence.Repositories;

public sealed class ReportJobRepository(ApplicationDbContext context) : IReportJobRepository, IReportJobQueue
{
    private static readonly Dictionary<string, Expression<Func<ReportJob, object>>> SortKeys = new(StringComparer.Ordinal)
    {
        ["requestedAt"] = job => job.RequestedAtUtc,
        ["status"] = job => job.Status,
        ["reportType"] = job => job.ReportType,
        ["completedAt"] = job => job.CompletedAtUtc!
    };

    public void Add(ReportJob job) => context.Add(job);

    public Task<ReportJob?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<ReportJob>().SingleOrDefaultAsync(job => job.Id == id, cancellationToken);

    public Task<ReportJob?> FindActiveAsync(Guid requestedBy, ReportType type, string parametersHash, CancellationToken cancellationToken) =>
        context.Set<ReportJob>().AsNoTracking().FirstOrDefaultAsync(job => job.RequestedBy == requestedBy && job.ReportType == type
            && job.ParametersHash == parametersHash && (job.Status == ReportJobStatus.Pending || job.Status == ReportJobStatus.Running), cancellationToken);

    public Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<ReportJob>().AsNoTracking().Where(job => job.Id == id).Select(job => (long?)job.Version).SingleOrDefaultAsync(cancellationToken);

    public Task<PagedResult<ReportJob>> ListAsync(ReportJobFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = context.Set<ReportJob>().AsNoTracking();
        if (filter.RequestedBy is { } requestedBy)
            query = query.Where(job => job.RequestedBy == requestedBy);
        if (filter.Statuses is { Count: > 0 } statuses)
            query = query.Where(job => statuses.Contains(job.Status));
        if (filter.Types is { Count: > 0 } types)
            query = query.Where(job => types.Contains(job.ReportType));
        return query.ToPagedAsync(page, SortKeys, job => job.Id, cancellationToken);
    }

    public async Task<ReportJob?> LockNextDueAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational())
            return await context.Set<ReportJob>()
                .Where(job => (job.Status == ReportJobStatus.Pending && job.NextAttemptAtUtc <= nowUtc)
                    || (job.Status == ReportJobStatus.Running && job.ClaimExpiresAtUtc <= nowUtc))
                .OrderBy(job => job.NextAttemptAtUtc).ThenBy(job => job.RequestedAtUtc).ThenBy(job => job.Id)
                .FirstOrDefaultAsync(cancellationToken);
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Claiming a report job requires an active unit-of-work transaction.");

        // SKIP LOCKED: a row another Worker is claiming is skipped, never waited for; the version token still guards the update.
        var rows = await context.Set<ReportJob>().FromSqlInterpolated($$"""
            SELECT job.*
            FROM "ReportingReportJobs" AS job
            WHERE (job."Status" = 'Pending' AND job."NextAttemptAtUtc" <= {{nowUtc}})
               OR (job."Status" = 'Running' AND job."ClaimExpiresAtUtc" <= {{nowUtc}})
            ORDER BY job."NextAttemptAtUtc", job."RequestedAtUtc", job."Id"
            LIMIT 1
            FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);
        return rows.FirstOrDefault();
    }

    public async Task<IReadOnlyList<Guid>> ListExpiredReadyAsync(DateTime nowUtc, int max, CancellationToken cancellationToken) =>
        await context.Set<ReportJob>().AsNoTracking()
            .Where(job => job.Status == ReportJobStatus.Ready && job.ExpiresAtUtc <= nowUtc)
            .OrderBy(job => job.ExpiresAtUtc).ThenBy(job => job.Id)
            .Select(job => job.Id).Take(Math.Clamp(max, 1, 1000)).ToListAsync(cancellationToken);
}

public sealed class StoredFileRepository(ApplicationDbContext context) : IStoredFileRepository
{
    public void Add(StoredFile file) => context.Add(file);

    public Task<StoredFile?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<StoredFile>().SingleOrDefaultAsync(file => file.Id == id, cancellationToken);

    public async Task<IReadOnlyList<StoredFile>> ListCleanupCandidatesAsync(
        FileRetentionPolicy policy, DateTime nowUtc, int max, CancellationToken cancellationToken)
    {
        var pendingBefore = nowUtc - policy.PendingUploadLifetime;
        return await context.Set<StoredFile>()
            .Where(file => (file.Status == StoredFileStatus.Pending && file.CreatedAtUtc <= pendingBefore)
                || (file.Status == StoredFileStatus.Available && file.ExpiresAtUtc <= nowUtc))
            .OrderBy(file => file.CreatedAtUtc).ThenBy(file => file.Id)
            .Take(Math.Clamp(max, 1, 1000))
            .ToListAsync(cancellationToken);
    }
}

public sealed class ReportSourceReader(ApplicationDbContext context) : IReportSourceReader
{
    public Task<FiscalPeriod?> FindFiscalPeriodAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<FiscalPeriod>().AsNoTracking().SingleOrDefaultAsync(period => period.Id == id, cancellationToken);

    public async Task<IReadOnlyList<BudgetAllocation>> ListAllocationsAsync(
        Guid fiscalPeriodId, string? currency, Guid? costCenterId, int max, CancellationToken cancellationToken)
    {
        var query = context.Set<BudgetAllocation>().AsNoTracking()
            .Include(allocation => allocation.AmountHistory)
            .Include(allocation => allocation.Reservations).ThenInclude(reservation => reservation.Changes)
            .Where(allocation => allocation.FiscalPeriodId == fiscalPeriodId);
        if (currency is not null)
            query = query.Where(allocation => allocation.Currency == currency);
        if (costCenterId is { } id)
            query = query.Where(allocation => allocation.CostCenterId == id);
        return await query.OrderBy(allocation => allocation.Id).Take(max + 1).AsSplitQuery().ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, (string Code, string Name)>> GetCostCenterLabelsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        (await context.Set<CostCenter>().AsNoTracking().Where(costCenter => ids.Contains(costCenter.Id))
            .Select(costCenter => new { costCenter.Id, costCenter.Code, costCenter.Name }).ToListAsync(cancellationToken))
        .ToDictionary(costCenter => costCenter.Id, costCenter => (costCenter.Code, costCenter.Name));

    public async Task<IReadOnlyList<RequestForQuotation>> ListRfqsWithQuotesAsync(
        DateTime fromUtc, DateTime toUtc, Guid? supplierId, int max, CancellationToken cancellationToken)
    {
        var query = context.Set<RequestForQuotation>().AsNoTracking()
            .Where(rfq => rfq.Quotes.Any(quote => quote.Revisions.Any(revision => revision.ReceivedAtUtc >= fromUtc && revision.ReceivedAtUtc < toUtc)));
        if (supplierId is { } id)
            query = query.Where(rfq => rfq.Invitations.Any(invitation => invitation.SupplierId == id));
        return await query
            .Include(rfq => rfq.Invitations)
            .Include(rfq => rfq.Quotes).ThenInclude(quote => quote.Revisions)
            .Include(rfq => rfq.Comparisons).ThenInclude(comparison => comparison.Entries)
            .Include(rfq => rfq.Awards)
            .OrderBy(rfq => rfq.CreatedAtUtc).ThenBy(rfq => rfq.Id)
            .Take(max + 1)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
    }
}
