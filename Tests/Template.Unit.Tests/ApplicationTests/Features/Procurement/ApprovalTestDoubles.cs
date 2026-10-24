using Template.Application.Common.Paging;
using Template.Application.Interfaces.Access;
using Template.Application.Interfaces.Approvals;
using Template.Application.Workflow;
using Template.Domain.Approvals;
using Template.Domain.Sourcing;

namespace Template.Unit.Tests.ApplicationTests.Features.Procurement;

/// <summary>Phase 31 port fakes: approval instances and policies in memory, an approver directory and a recording workflow client.</summary>
internal static class ApprovalTestDoubles
{
    internal sealed class FakeApprovalInstances(ProcurementTestDoubles.FakeRfqs rfqs) : IApprovalInstanceRepository
    {
        public List<ApprovalInstance> Items { get; } = [];

        public void Add(ApprovalInstance instance) => Items.Add(instance);

        public Task<ApprovalInstance?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Items.SingleOrDefault(item => item.Id == id));

        public Task<ApprovalInstance?> FindByAwardAsync(Guid awardId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(item => item.AwardId == awardId));

        public Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Where(item => item.Id == id).Select(item => (long?)item.Version).SingleOrDefault());

        public ApprovalInstanceFilter? LastFilter { get; private set; }

        public Task<PagedResult<ApprovalInstance>> ListAsync(ApprovalInstanceFilter filter, PageRequest page, CancellationToken cancellationToken)
        {
            LastFilter = filter;
            var roles = filter.AssigneeGroups ?? [];
            var items = Items.Where(item => (filter.Statuses is null || filter.Statuses.Contains(item.Status))
                && (filter.AssigneeUserId is null && roles.Count == 0
                    || item.CurrentStep is { Status: ApprovalStepStatus.Pending } step && item.PendingOutcome is null
                    && (step.AssigneeUserId == filter.AssigneeUserId || step.AssigneeRole is { } role && roles.Contains(role)))).ToArray();
            return Task.FromResult(new PagedResult<ApprovalInstance>(items, page.Page, page.PageSize, items.Length));
        }

        public Task<IReadOnlyList<Guid>> ListPendingIdsForSupplierAsync(Guid supplierId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(Items.Where(item => item.SupplierId == supplierId && item.Status == ApprovalStatus.Pending).Select(item => item.Id).ToArray());

        public Task<IReadOnlyList<ApprovalInstance>> ListPendingAsync(int max, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ApprovalInstance>>(Items.Where(item => item.Status == ApprovalStatus.Pending).Take(max).ToArray());

        public Task<IReadOnlyList<PendingAwardReference>> ListAwardsWithoutInstanceAsync(DateTime submittedBeforeUtc, int max, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PendingAwardReference>>(PendingAwards()
                .Where(award => award.SubmittedAtUtc < submittedBeforeUtc && Items.All(item => item.AwardId != award.AwardId)).Take(max).ToArray());

        public Task<PendingAwardReference?> FindPendingAwardAsync(Guid awardId, CancellationToken cancellationToken) =>
            Task.FromResult(PendingAwards().SingleOrDefault(award => award.AwardId == awardId));

        private IEnumerable<PendingAwardReference> PendingAwards() =>
            rfqs.Items.Where(rfq => rfq.Status == RfqStatus.AwardPending)
                .SelectMany(rfq => rfq.Awards.Where(award => award.Status == AwardStatus.PendingApproval)
                    .Select(award => new PendingAwardReference(award.Id, rfq.Id, rfq.RequisitionId, award.SubmittedAtUtc)));
    }

    internal sealed class FakeApprovalPolicies : IApprovalPolicyRepository
    {
        public List<ApprovalPolicy> Items { get; } = [ApprovalPolicy.Version1("FinanceApprover", "ProcurementManager")];

        public void Add(ApprovalPolicy policy) => Items.Add(policy);

        public Task<ApprovalPolicy?> FindEffectiveAsync(DateTime atUtc, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Where(item => item.EffectiveFromUtc <= atUtc).OrderByDescending(item => item.Version).FirstOrDefault());

        public Task<ApprovalPolicy?> FindByVersionAsync(int version, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(item => item.Version == version));

        public Task<IReadOnlyList<ApprovalPolicy>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ApprovalPolicy>>(Items.ToArray());

        public Task<int> GetLatestVersionAsync(CancellationToken cancellationToken) => Task.FromResult(Items.Max(item => item.Version));
    }

    /// <summary>Users with roles; approvers (roles holding approvals.decide) are those added through <see cref="AddApprover"/>.</summary>
    internal sealed class FakeApprovers : IApproverDirectory
    {
        public Dictionary<Guid, (bool Active, bool CanDecide, HashSet<string> Roles)> Users { get; } = [];

        public void AddApprover(Guid userId, params string[] roles) => Users[userId] = (true, true, roles.ToHashSet(StringComparer.Ordinal));

        public void AddUser(Guid userId, params string[] roles) => Users[userId] = (true, false, roles.ToHashSet(StringComparer.Ordinal));

        public void Deactivate(Guid userId) => Users[userId] = Users[userId] with { Active = false };

        public Task<ApprovalCandidate?> GetCandidateAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Users.TryGetValue(userId, out var user) ? new ApprovalCandidate(userId, user.Active, user.CanDecide) : null);

        public Task<IReadOnlySet<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(Users.TryGetValue(userId, out var user) ? user.Roles : new HashSet<string>());

        public Task<PagedResult<UserSummary>> SearchApproversAsync(string? search, PageRequest page, CancellationToken cancellationToken)
        {
            var items = Users.Where(pair => pair.Value is { Active: true, CanDecide: true })
                .Select(pair => new UserSummary(pair.Key, $"user-{pair.Key:N}"[..13], null, true)).ToArray();
            return Task.FromResult(new PagedResult<UserSummary>(items, page.Page, page.PageSize, items.Length));
        }

        public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(userIds.Where(Users.ContainsKey).ToDictionary(id => id, id => $"Synthetic {id.ToString()[..4]}"));
    }

    /// <summary>Records calls; <see cref="Unavailable"/> simulates an unreachable WorkflowFunctions host.</summary>
    internal sealed class FakeWorkflow : IApprovalWorkflowClient
    {
        public bool Unavailable { get; set; }

        /// <summary>Only signals fail (the host answered the availability check, then the signal was lost).</summary>
        public bool SignalsFail { get; set; }

        public ApprovalWorkflowStartResult StartResult { get; set; } = ApprovalWorkflowStartResult.Started;
        public ApprovalWorkflowSignalResult SignalResult { get; set; } = ApprovalWorkflowSignalResult.Delivered;
        public ApprovalWorkflowRuntimeStatus RuntimeStatus { get; set; } = ApprovalWorkflowRuntimeStatus.Running;
        public List<ApprovalWorkflowInput> Starts { get; } = [];
        public List<(Guid AwardId, ApprovalSignal Signal)> Signals { get; } = [];

        public Task<ApprovalWorkflowStartResult> StartAsync(ApprovalWorkflowInput input, CancellationToken cancellationToken)
        {
            Fail();
            Starts.Add(input);
            return Task.FromResult(StartResult);
        }

        public Task<ApprovalWorkflowSignalResult> SignalAsync(Guid awardId, ApprovalSignal signal, CancellationToken cancellationToken)
        {
            Fail();
            if (SignalsFail)
                throw new ApprovalWorkflowUnavailableException("synthetic lost signal");
            Signals.Add((awardId, signal));
            return Task.FromResult(SignalResult);
        }

        public Task<ApprovalWorkflowStatus> GetStatusAsync(Guid awardId, CancellationToken cancellationToken)
        {
            Fail();
            return Task.FromResult(new ApprovalWorkflowStatus(ApprovalWorkflowNames.InstanceIdFor(awardId), RuntimeStatus, "1.0", null, null));
        }

        public Task<bool> TerminateAsync(Guid awardId, string reason, CancellationToken cancellationToken)
        {
            Fail();
            return Task.FromResult(true);
        }

        private void Fail()
        {
            if (Unavailable)
                throw new ApprovalWorkflowUnavailableException("synthetic outage");
        }
    }
}
