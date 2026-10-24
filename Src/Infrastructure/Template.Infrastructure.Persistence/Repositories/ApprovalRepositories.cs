using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Template.Application.Common.Paging;
using Template.Application.Interfaces.Approvals;
using Template.Domain.Approvals;
using Template.Domain.Sourcing;
using Template.Infrastructure.Persistence.Contexts;

namespace Template.Infrastructure.Persistence.Repositories;

public sealed class ApprovalInstanceRepository(ApplicationDbContext context) : IApprovalInstanceRepository
{
    private static readonly Dictionary<string, Expression<Func<ApprovalInstance, object>>> SortKeys = new(StringComparer.Ordinal)
    {
        ["startedAt"] = instance => instance.StartedAtUtc,
        ["dueAt"] = instance => instance.Steps.Where(step => step.Level == instance.CurrentLevel).Select(step => step.DueAtUtc).FirstOrDefault()!,
        ["requisitionNumber"] = instance => instance.RequisitionNumber,
        ["amount"] = instance => instance.AmountEvaluated,
        ["status"] = instance => instance.Status,
        ["updatedAt"] = instance => instance.UpdatedAtUtc
    };

    public void Add(ApprovalInstance instance) => context.Add(instance);

    public Task<ApprovalInstance?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Full().SingleOrDefaultAsync(instance => instance.Id == id, cancellationToken);

    public Task<ApprovalInstance?> FindByAwardAsync(Guid awardId, CancellationToken cancellationToken) =>
        Full().SingleOrDefaultAsync(instance => instance.AwardId == awardId, cancellationToken);

    public Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<ApprovalInstance>().AsNoTracking().Where(instance => instance.Id == id)
            .Select(instance => (long?)instance.Version).SingleOrDefaultAsync(cancellationToken);

    public Task<PagedResult<ApprovalInstance>> ListAsync(ApprovalInstanceFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = context.Set<ApprovalInstance>().AsNoTracking().Include(instance => instance.Steps).AsQueryable();
        if (filter.Statuses is { Count: > 0 } statuses)
            query = query.Where(instance => statuses.Contains(instance.Status));
        if (filter.RequisitionId is { } requisitionId)
            query = query.Where(instance => instance.RequisitionId == requisitionId);
        if (filter.AssigneeUserId is not null || filter.AssigneeGroups is { Count: > 0 })
        {
            var user = filter.AssigneeUserId;
            var roles = (filter.AssigneeGroups ?? []).ToArray();
            query = query.Where(instance => instance.Status == ApprovalStatus.Pending && instance.PendingOutcome == null
                && instance.Steps.Any(step => step.Level == instance.CurrentLevel && step.Status == ApprovalStepStatus.Pending
                    && (step.AssigneeUserId == user || roles.Contains(step.AssigneeRole!))));
        }

        if (filter.ParticipantUserId is { } participant)
        {
            var roles = (filter.ParticipantGroups ?? []).ToArray();
            query = query.Where(instance => instance.RequesterId == participant || instance.SubmittedBy == participant
                || instance.Steps.Any(step => step.AssigneeUserId == participant || step.DecidedBy == participant || roles.Contains(step.AssigneeRole!))
                || instance.History.Any(entry => entry.ActorId == participant || entry.AssigneeUserId == participant));
        }

        if (filter.Escalated is { } escalated)
            query = query.Where(instance => instance.Steps.Any(step => step.Level == instance.CurrentLevel && step.EscalationCount > 0) == escalated);
        if (filter.DueBeforeUtc is { } dueBefore)
            query = query.Where(instance => instance.Steps.Any(step => step.Level == instance.CurrentLevel && step.DueAtUtc < dueBefore));
        return query.AsSplitQuery().ToPagedAsync(page, SortKeys, instance => instance.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListPendingIdsForSupplierAsync(Guid supplierId, CancellationToken cancellationToken) =>
        await context.Set<ApprovalInstance>().AsNoTracking()
            .Where(instance => instance.SupplierId == supplierId && instance.Status == ApprovalStatus.Pending)
            .OrderBy(instance => instance.StartedAtUtc).ThenBy(instance => instance.Id)
            .Select(instance => instance.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ApprovalInstance>> ListPendingAsync(int max, CancellationToken cancellationToken) =>
        await context.Set<ApprovalInstance>().AsNoTracking()
            .Include(instance => instance.Steps)
            .Where(instance => instance.Status == ApprovalStatus.Pending)
            .OrderBy(instance => instance.UpdatedAtUtc).ThenBy(instance => instance.Id)
            .Take(max)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PendingAwardReference>> ListAwardsWithoutInstanceAsync(DateTime submittedBeforeUtc, int max, CancellationToken cancellationToken) =>
        await PendingAwards()
            .Where(row => row.SubmittedAtUtc < submittedBeforeUtc && !context.Set<ApprovalInstance>().Any(instance => instance.AwardId == row.AwardId))
            .OrderBy(row => row.SubmittedAtUtc).ThenBy(row => row.AwardId)
            .Take(max)
            .Select(row => new PendingAwardReference(row.AwardId, row.RfqId, row.RequisitionId, row.SubmittedAtUtc))
            .ToListAsync(cancellationToken);

    public Task<PendingAwardReference?> FindPendingAwardAsync(Guid awardId, CancellationToken cancellationToken) =>
        PendingAwards()
            .Where(row => row.AwardId == awardId)
            .Select(row => new PendingAwardReference(row.AwardId, row.RfqId, row.RequisitionId, row.SubmittedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>Pending awards of RFQs still awaiting approval; filtered and ordered before the record projection (SQL-translatable).</summary>
    private IQueryable<PendingAwardRow> PendingAwards() =>
        from award in context.Set<RfqAward>().AsNoTracking()
        join rfq in context.Set<RequestForQuotation>().AsNoTracking() on award.RfqId equals rfq.Id
        where award.Status == AwardStatus.PendingApproval && rfq.Status == RfqStatus.AwardPending
        select new PendingAwardRow { AwardId = award.Id, RfqId = rfq.Id, RequisitionId = rfq.RequisitionId, SubmittedAtUtc = award.SubmittedAtUtc };

    private sealed class PendingAwardRow
    {
        public Guid AwardId { get; init; }
        public Guid RfqId { get; init; }
        public Guid RequisitionId { get; init; }
        public DateTime SubmittedAtUtc { get; init; }
    }

    private IQueryable<ApprovalInstance> Full() =>
        context.Set<ApprovalInstance>()
            .Include(instance => instance.Steps)
            .Include(instance => instance.History)
            .AsSplitQuery();
}

public sealed class ApprovalPolicyRepository(ApplicationDbContext context) : IApprovalPolicyRepository
{
    public void Add(ApprovalPolicy policy) => context.Add(policy);

    public Task<ApprovalPolicy?> FindEffectiveAsync(DateTime atUtc, CancellationToken cancellationToken) =>
        context.Set<ApprovalPolicy>().Include(policy => policy.Levels)
            .Where(policy => policy.EffectiveFromUtc <= atUtc)
            .OrderByDescending(policy => policy.Version)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<ApprovalPolicy?> FindByVersionAsync(int version, CancellationToken cancellationToken) =>
        context.Set<ApprovalPolicy>().AsNoTracking().Include(policy => policy.Levels)
            .SingleOrDefaultAsync(policy => policy.Version == version, cancellationToken);

    public async Task<IReadOnlyList<ApprovalPolicy>> ListAsync(CancellationToken cancellationToken) =>
        await context.Set<ApprovalPolicy>().AsNoTracking().Include(policy => policy.Levels)
            .OrderBy(policy => policy.Version)
            .ToListAsync(cancellationToken);

    public async Task<int> GetLatestVersionAsync(CancellationToken cancellationToken) =>
        await context.Set<ApprovalPolicy>().AsNoTracking().MaxAsync(policy => (int?)policy.Version, cancellationToken) ?? 0;
}
