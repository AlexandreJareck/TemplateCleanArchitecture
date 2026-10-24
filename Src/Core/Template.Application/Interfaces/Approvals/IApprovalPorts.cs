#nullable enable
using Template.Application.Common.Paging;
using Template.Application.Interfaces.Access;
using Template.Application.Workflow;
using Template.Domain.Approvals;

namespace Template.Application.Interfaces.Approvals;

public sealed record ApprovalInstanceFilter(
    IReadOnlyCollection<ApprovalStatus>? Statuses = null,
    Guid? AssigneeUserId = null,
    IReadOnlyCollection<string>? AssigneeGroups = null,
    Guid? ParticipantUserId = null,
    IReadOnlyCollection<string>? ParticipantGroups = null,
    Guid? RequisitionId = null,
    bool? Escalated = null,
    DateTime? DueBeforeUtc = null);

/// <summary>An award waiting for approval whose instance does not exist yet (APR-016 reconciliation).</summary>
public sealed record PendingAwardReference(Guid AwardId, Guid RfqId, Guid RequisitionId, DateTime SubmittedAtUtc);

public interface IApprovalInstanceRepository
{
    public const string AwardUniqueKey = "IX_ApprovalInstances_AwardId";

    public static readonly IReadOnlyCollection<string> SortFields = ["startedAt", "dueAt", "requisitionNumber", "amount", "status", "updatedAt"];

    void Add(ApprovalInstance instance);

    Task<ApprovalInstance?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<ApprovalInstance?> FindByAwardAsync(Guid awardId, CancellationToken cancellationToken);

    Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Filtered page. <see cref="ApprovalInstanceFilter.AssigneeUserId"/> and <see cref="ApprovalInstanceFilter.AssigneeGroups"/>
    /// together match instances whose current pending step is assigned to the user or to one of the roles;
    /// <see cref="ApprovalInstanceFilter.ParticipantUserId"/> and <see cref="ApprovalInstanceFilter.ParticipantGroups"/> match the
    /// requester, the submitting buyer, any assignee or decider of any step, or any step assigned to one of the roles.
    /// </summary>
    Task<PagedResult<ApprovalInstance>> ListAsync(ApprovalInstanceFilter filter, PageRequest page, CancellationToken cancellationToken);

    /// <summary>Pending instances that award <paramref name="supplierId"/> (APR-012).</summary>
    Task<IReadOnlyList<Guid>> ListPendingIdsForSupplierAsync(Guid supplierId, CancellationToken cancellationToken);

    /// <summary>Pending instances ordered by last update, oldest first (reconciliation batch).</summary>
    Task<IReadOnlyList<ApprovalInstance>> ListPendingAsync(int max, CancellationToken cancellationToken);

    /// <summary>Awards still <c>PendingApproval</c> submitted before <paramref name="submittedBeforeUtc"/> without an instance.</summary>
    Task<IReadOnlyList<PendingAwardReference>> ListAwardsWithoutInstanceAsync(DateTime submittedBeforeUtc, int max, CancellationToken cancellationToken);

    /// <summary>The pending award with its requisition, or <c>null</c> when the award is no longer pending.</summary>
    Task<PendingAwardReference?> FindPendingAwardAsync(Guid awardId, CancellationToken cancellationToken);
}

public interface IApprovalPolicyRepository
{
    public const string VersionUniqueKey = "IX_ApprovalPolicies_Version";

    void Add(ApprovalPolicy policy);

    /// <summary>The highest version whose effective instant is not after <paramref name="atUtc"/> (APR-001).</summary>
    Task<ApprovalPolicy?> FindEffectiveAsync(DateTime atUtc, CancellationToken cancellationToken);

    Task<ApprovalPolicy?> FindByVersionAsync(int version, CancellationToken cancellationToken);

    Task<IReadOnlyList<ApprovalPolicy>> ListAsync(CancellationToken cancellationToken);

    Task<int> GetLatestVersionAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Approver facts from the user store (Identity adapter): activity, roles, the <c>approvals.decide</c> permission and
/// display names. No claim, email or credential crosses this port.
/// </summary>
public interface IApproverDirectory
{
    /// <summary>The user as a delegation, reassignment or resolution target; <c>null</c> when the user does not exist.</summary>
    Task<ApprovalCandidate?> GetCandidateAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlySet<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Active users holding <c>approvals.decide</c> whose user or display name contains <paramref name="search"/>.</summary>
    Task<PagedResult<UserSummary>> SearchApproversAsync(string? search, PageRequest page, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}

public enum ApprovalWorkflowRuntimeStatus
{
    NotFound,
    Pending,
    Running,
    Completed,
    Failed,
    Terminated,
    Suspended,
    Unknown
}

public sealed record ApprovalWorkflowStatus(
    string InstanceId,
    ApprovalWorkflowRuntimeStatus RuntimeStatus,
    string? Version,
    DateTime? CreatedAtUtc,
    DateTime? LastUpdatedAtUtc)
{
    public bool IsActive => RuntimeStatus is ApprovalWorkflowRuntimeStatus.Pending or ApprovalWorkflowRuntimeStatus.Running
        or ApprovalWorkflowRuntimeStatus.Suspended;
}

public enum ApprovalWorkflowStartResult
{
    Started,
    AlreadyRunning,

    /// <summary>A previous run of the same instance ID had ended (failed, terminated, completed) and was replaced.</summary>
    Restarted
}

public enum ApprovalWorkflowSignalResult
{
    Delivered,

    /// <summary>No running instance; the signal is dropped (late or duplicate, APR-014).</summary>
    NotRunning
}

/// <summary>The WorkflowFunctions boundary did not answer (unreachable, timeout, server error or rejected credentials).</summary>
public sealed class ApprovalWorkflowUnavailableException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>
/// Application-owned client of the approval orchestration (ADR-0003 §5). The Infrastructure adapter calls the
/// WorkflowFunctions host through an authenticated internal endpoint; the browser never reaches it. Every operation is
/// idempotent per award and throws <see cref="ApprovalWorkflowUnavailableException"/> when the host is unavailable.
/// </summary>
public interface IApprovalWorkflowClient
{
    Task<ApprovalWorkflowStartResult> StartAsync(ApprovalWorkflowInput input, CancellationToken cancellationToken);

    Task<ApprovalWorkflowSignalResult> SignalAsync(Guid awardId, ApprovalSignal signal, CancellationToken cancellationToken);

    Task<ApprovalWorkflowStatus> GetStatusAsync(Guid awardId, CancellationToken cancellationToken);

    /// <summary>Administrative stop of a running orchestration; the database state is not changed (APR-016).</summary>
    Task<bool> TerminateAsync(Guid awardId, string reason, CancellationToken cancellationToken);
}
