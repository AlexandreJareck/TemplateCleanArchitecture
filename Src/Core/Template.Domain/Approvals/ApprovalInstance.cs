using System;
using System.Collections.Generic;
using System.Linq;
using Template.Domain.Common;

namespace Template.Domain.Approvals;

public enum ApprovalStatus
{
    Pending,
    Approved,
    Rejected,
    Cancelled,
    Expired,
    Invalidated
}

/// <summary>Step lifecycle; <c>Waiting</c> = a later level that is not assigned yet.</summary>
public enum ApprovalStepStatus
{
    Waiting,
    Pending,
    Approved,
    Rejected,
    Cancelled,
    Expired
}

public enum ApprovalDecision
{
    Approved,
    Rejected
}

/// <summary>
/// What the process needs next, derived from the persisted state only (APR-016). The orchestration reads this value and
/// never evaluates a rule itself.
/// </summary>
public enum ApprovalNextAction
{
    /// <summary>The current level has no assignee yet.</summary>
    AssignStep,

    /// <summary>The current step waits for a decision, a reminder or its deadline.</summary>
    Wait,

    /// <summary>A decision or the deadline settled the outcome; the cross-module effects are still to be applied.</summary>
    Finalize,

    /// <summary>The instance ended and every effect was applied.</summary>
    Completed
}

public enum ApprovalTimerKind
{
    Reminder,
    Deadline
}

/// <summary>Result of a deadline timer (APR-011).</summary>
public enum ApprovalDeadlineOutcome
{
    NotDue,
    Escalated,
    Expired
}

/// <summary>Result of a decision call (APR-014): a new decision, or the same actor repeating the same decision.</summary>
public enum ApprovalDecisionResult
{
    Recorded,
    Repeated
}

/// <summary>Award facts frozen when the instance starts; IDs and amounts only (NFR-SEC-7).</summary>
public sealed record ApprovalStartFacts(
    Guid AwardId,
    Guid RfqId,
    Guid RequisitionId,
    string RequisitionNumber,
    Guid RequesterId,
    Guid? SubmittedBy,
    Guid CostCenterId,
    Guid DepartmentId,
    Guid SupplierId,
    Money AmountInBaseCurrency,
    Money RequisitionCurrencyAmount);

/// <summary>The user acting on a step, with the roles and the decide permission read by the use case.</summary>
public sealed record ApprovalActor(Guid UserId, IReadOnlySet<string> Roles, bool CanDecide);

/// <summary>Exactly one of a user or a role group.</summary>
public sealed record ApprovalAssignee(Guid? UserId, string? Role)
{
    public static ApprovalAssignee ForUser(Guid userId) => new(userId, null);

    public static ApprovalAssignee ForRole(string role) => new(null, role);

    public bool IsValid => (UserId is { } id && id != Guid.Empty && Role is null)
        || (UserId is null && !string.IsNullOrWhiteSpace(Role) && Role.Length <= ApprovalPolicyLevel.RoleMaxLength);
}

/// <summary>A delegation or reassignment target as the directory sees it (APR-010).</summary>
public sealed record ApprovalCandidate(Guid UserId, bool IsActive, bool CanDecide);

/// <summary>
/// One approval process for one award (docs/product/domain-model.md §Approvals, workflows.md §3). It pins the policy
/// version and its durations when it starts (APR-001, APR-017), owns the steps of the required levels (APR-002) and the
/// append-only history, and enforces the decision, delegation and separation-of-duties rules (APR-004..010, APR-014).
/// The Durable orchestration only reacts to <see cref="NextAction"/>; time-based transitions (reminder, escalation,
/// expiry) are applied here by idempotent calls keyed by the level (APR-011). Every change increments the version (SP-1).
/// </summary>
public sealed class ApprovalInstance : VersionedAggregateRoot
{
    public const int CauseMaxLength = 40;
    public const string CauseRejected = "rejected";
    public const string CauseExpired = "expired";
    public const string CauseApproved = "approved";
    public const string CauseSupplierUnavailable = "supplier-unavailable";
    public const string CauseRequisitionCancelled = "requisition-cancelled";
    public const string CauseAdministrativeCancel = "administrative-cancel";

    public const string ActionStarted = "started";
    public const string ActionAssigned = "step-assigned";
    public const string ActionApproved = "step-approved";
    public const string ActionRejected = "step-rejected";
    public const string ActionDelegated = "step-delegated";
    public const string ActionReassigned = "step-reassigned";
    public const string ActionReminded = "step-reminded";
    public const string ActionEscalated = "step-escalated";
    public const string ActionStepExpired = "step-expired";
    public const string ActionCompleted = "approved";
    public const string ActionInstanceRejected = "rejected";
    public const string ActionInstanceExpired = "expired";
    public const string ActionInvalidated = "invalidated";
    public const string ActionCancelled = "cancelled";

    private readonly List<ApprovalStep> steps = [];
    private readonly List<ApprovalHistoryEntry> history = [];
    private readonly DomainEventBuffer events = new();

    private ApprovalInstance()
    {
        RequisitionNumber = string.Empty;
        BaseCurrency = string.Empty;
        RequisitionCurrency = string.Empty;
        EscalationRole = string.Empty;
        WorkflowInstanceId = string.Empty;
        WorkflowVersion = string.Empty;
    }

    public Guid AwardId { get; private set; }
    public Guid RfqId { get; private set; }
    public Guid RequisitionId { get; private set; }
    public string RequisitionNumber { get; private set; }
    public Guid RequesterId { get; private set; }

    /// <summary>The buyer who submitted the award (APR-008).</summary>
    public Guid? SubmittedBy { get; private set; }

    public Guid CostCenterId { get; private set; }
    public Guid DepartmentId { get; private set; }
    public Guid SupplierId { get; private set; }

    /// <summary>Amount evaluated against the thresholds (base currency, CALC-004).</summary>
    public decimal AmountEvaluated { get; private set; }

    public string BaseCurrency { get; private set; }

    /// <summary>Award amount in requisition currency (CALC-007), the amount committed on approval.</summary>
    public decimal RequisitionAmount { get; private set; }

    public string RequisitionCurrency { get; private set; }

    public Guid PolicyId { get; private set; }
    public int PolicyVersion { get; private set; }

    // Pinned copies of the policy durations (APR-011) so the process never reads a later version.
    public TimeSpan StepDuration { get; private set; }
    public TimeSpan ReminderAfter { get; private set; }
    public string EscalationRole { get; private set; }
    public int MaxEscalations { get; private set; }

    /// <summary>Opaque Durable instance ID derived from the award ID (APR-001).</summary>
    public string WorkflowInstanceId { get; private set; }

    /// <summary>Orchestration version that started the process (Durable <c>defaultVersion</c>).</summary>
    public string WorkflowVersion { get; private set; }

    public ApprovalStatus Status { get; private set; }

    /// <summary>Outcome settled by a decision or the deadline but not applied yet (<see cref="ApprovalNextAction.Finalize"/>).</summary>
    public ApprovalStatus? PendingOutcome { get; private set; }

    public int CurrentLevel { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? EndedAtUtc { get; private set; }
    public string? EndCause { get; private set; }
    public string? EndReason { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<ApprovalStep> Steps => steps;
    public IReadOnlyCollection<ApprovalHistoryEntry> History => history;
    public IReadOnlyList<DomainEvent> DomainEvents => events.Events;

    public string PolicyReference => $"{ApprovalPolicy.PolicyType}:{PolicyVersion.ToString(System.Globalization.NumberFormatInfo.InvariantInfo)}";

    public int FinalLevel => steps.Count == 0 ? 0 : steps.Max(step => step.Level);

    public ApprovalStep? CurrentStep => steps.FirstOrDefault(step => step.Level == CurrentLevel);

    public bool IsEnded => Status != ApprovalStatus.Pending;

    public ApprovalNextAction NextAction => Status != ApprovalStatus.Pending
        ? ApprovalNextAction.Completed
        : PendingOutcome is not null
            ? ApprovalNextAction.Finalize
            : CurrentStep is { Status: ApprovalStepStatus.Pending } ? ApprovalNextAction.Wait : ApprovalNextAction.AssignStep;

    /// <summary>The next durable timer of a waiting step: the reminder until it was sent, then the deadline.</summary>
    public (ApprovalTimerKind Kind, DateTime FireAtUtc)? NextTimer =>
        NextAction == ApprovalNextAction.Wait && CurrentStep is { } step
            ? step.RemindedAtUtc is null && step.ReminderDueAtUtc < step.DueAtUtc
                ? (ApprovalTimerKind.Reminder, step.ReminderDueAtUtc!.Value)
                : (ApprovalTimerKind.Deadline, step.DueAtUtc!.Value)
            : null;

    public void ClearDomainEvents() => events.Clear();

    /// <summary>Deterministic Durable instance ID of an award (APR-001): the same award always maps to the same ID.</summary>
    public static string WorkflowInstanceIdFor(Guid awardId) => $"approval-{awardId:N}";

    /// <summary>
    /// APR-001/002: one instance per award, pinned to <paramref name="policy"/> (the version effective at award
    /// submission) with one <c>Waiting</c> step per required level in ascending order. Level 1 becomes current; the
    /// assignment is a separate step (APR-003).
    /// </summary>
    public static (ApprovalInstance? Instance, DomainError? Error) Start(
        Guid id, ApprovalStartFacts facts, ApprovalPolicy policy, string workflowVersion, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureId(id, nameof(id));
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowVersion);
        MasterDataRules.EnsureId(facts.AwardId, nameof(facts));
        MasterDataRules.EnsureId(facts.RequisitionId, nameof(facts));
        MasterDataRules.EnsureId(facts.RequesterId, nameof(facts));
        if (!string.Equals(facts.AmountInBaseCurrency.Currency, policy.BaseCurrency, StringComparison.Ordinal))
            throw new ArgumentException("The evaluated amount must be in the policy base currency (CALC-004).", nameof(facts));
        var levels = policy.RequiredLevels(facts.AmountInBaseCurrency.Amount);
        if (levels.Count == 0)
            return (null, new DomainError("validation", DomainErrorKind.Validation, "The award amount must be greater than zero.", "APR-002", "amount"));

        var instance = new ApprovalInstance
        {
            Id = id,
            AwardId = facts.AwardId,
            RfqId = facts.RfqId,
            RequisitionId = facts.RequisitionId,
            RequisitionNumber = facts.RequisitionNumber,
            RequesterId = facts.RequesterId,
            SubmittedBy = facts.SubmittedBy == Guid.Empty ? null : facts.SubmittedBy,
            CostCenterId = facts.CostCenterId,
            DepartmentId = facts.DepartmentId,
            SupplierId = facts.SupplierId,
            AmountEvaluated = facts.AmountInBaseCurrency.Amount,
            BaseCurrency = facts.AmountInBaseCurrency.Currency,
            RequisitionAmount = facts.RequisitionCurrencyAmount.Amount,
            RequisitionCurrency = facts.RequisitionCurrencyAmount.Currency,
            PolicyId = policy.Id,
            PolicyVersion = policy.Version,
            StepDuration = policy.StepDuration,
            ReminderAfter = policy.ReminderAfter,
            EscalationRole = policy.EscalationRole,
            MaxEscalations = policy.MaxEscalations,
            WorkflowInstanceId = WorkflowInstanceIdFor(facts.AwardId),
            WorkflowVersion = workflowVersion,
            Status = ApprovalStatus.Pending,
            CurrentLevel = levels[0].Level,
            StartedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
        foreach (var level in levels)
            instance.steps.Add(new ApprovalStep(Guid.NewGuid(), id, level.Level, level.Resolver, level.Role));
        policy.MarkUsed(nowUtc);
        instance.Append(ActionStarted, null, null, null, nowUtc, null, correlationId, null);
        instance.Raise(new ApprovalStarted(nowUtc, id, facts.AwardId, facts.RequisitionId, levels.Select(level => level.Level).ToArray(), instance.PolicyReference));
        return (instance, null);
    }

    /// <summary>
    /// APR-005/007/008 for one user and level: never two levels by the same user; neither the requester nor the
    /// submitting buyer at the final level.
    /// </summary>
    public bool IsEligible(Guid userId, int level)
    {
        if (steps.Any(step => step.Level != level && step.DecidedBy == userId))
            return false;
        return level != FinalLevel || (userId != RequesterId && userId != SubmittedBy);
    }

    /// <summary>
    /// APR-003/006: assigns the current <c>Waiting</c> step. A user assignee must be eligible (the use case falls back to
    /// the escalation group otherwise). Returns <c>false</c> when the step is already assigned (idempotent activity).
    /// </summary>
    public (bool Changed, DomainError? Error) AssignCurrentStep(ApprovalAssignee assignee, string resolution, DateTime nowUtc, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(assignee);
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (NextAction != ApprovalNextAction.AssignStep)
            return (false, null);
        if (!assignee.IsValid)
            throw new ArgumentException("An assignee is exactly one user or one role group.", nameof(assignee));
        var step = CurrentStep!;
        if (assignee.UserId is { } userId && !IsEligible(userId, step.Level))
            return (false, ApprovalErrors.IneligibleAssignee);

        step.Assign(assignee, nowUtc, nowUtc + StepDuration, nowUtc + ReminderAfter);
        Append(ActionAssigned, step.Level, null, assignee, nowUtc, resolution, correlationId, null);
        Raise(new ApprovalStepAssigned(nowUtc, Id, step.Level, assignee.UserId, assignee.Role, step.DueAtUtc!.Value, step.AssignmentSequence));
        Touch(nowUtc);
        return (true, null);
    }

    /// <summary>
    /// APR-004..009 and APR-014. The same actor repeating the same decision on a decided step returns
    /// <see cref="ApprovalDecisionResult.Repeated"/> and changes nothing; any other decision on a decided step, or any
    /// decision on an ended instance, is a conflict. Approving a non-final step makes the next level current; approving
    /// the final step or rejecting any step settles the outcome for <see cref="Finalize"/>.
    /// </summary>
    public (ApprovalDecisionResult? Result, DomainError? Error) Decide(
        int level, ApprovalActor actor, ApprovalDecision decision, string? reason, Guid decisionId, DateTime nowUtc, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(actor);
        MasterDataRules.EnsureId(actor.UserId, nameof(actor));
        MasterDataRules.EnsureId(decisionId, nameof(decisionId));
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (!Enum.IsDefined(decision))
            throw new ArgumentOutOfRangeException(nameof(decision));
        var step = steps.FirstOrDefault(candidate => candidate.Level == level);
        if (step is null)
            return (null, ApprovalErrors.StepNotFound);
        if (step.Decision is { } existing)
            return existing == decision && step.DecidedBy == actor.UserId
                ? (ApprovalDecisionResult.Repeated, null)
                : (null, ApprovalErrors.StepAlreadyDecided);
        if (Status != ApprovalStatus.Pending || PendingOutcome is not null)
            return (null, ApprovalErrors.InstanceEnded(Status));
        if (level != CurrentLevel || step.Status != ApprovalStepStatus.Pending)
            return (null, ApprovalErrors.StepNotCurrent);
        if (!actor.CanDecide || !IsAssignee(step, actor))
            return (null, ApprovalErrors.NotAssignee);
        if (steps.Any(other => other.Level != level && other.DecidedBy == actor.UserId))
            return (null, ApprovalErrors.DuplicateApprover);
        if (decision == ApprovalDecision.Approved && level == FinalLevel)
        {
            if (actor.UserId == RequesterId)
                return (null, ApprovalErrors.SelfApproval);
            if (actor.UserId == SubmittedBy)
                return (null, ApprovalErrors.MakerChecker);
        }

        string? normalizedReason;
        if (decision == ApprovalDecision.Rejected)
        {
            normalizedReason = MasterDataRules.NormalizeReason(reason);
            if (normalizedReason is null)
                return (null, DomainError.ReasonRequired("APR-009", MasterDataRules.ReasonMinLength, MasterDataRules.ReasonMaxLength));
        }
        else
        {
            normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            if (normalizedReason is { Length: > MasterDataRules.ReasonMaxLength })
                return (null, new DomainError("validation", DomainErrorKind.Validation,
                    $"The comment has at most {MasterDataRules.ReasonMaxLength} characters.", "APR-004", "reason"));
        }

        step.Decide(decision, actor.UserId, decisionId, normalizedReason, nowUtc);
        Append(decision == ApprovalDecision.Approved ? ActionApproved : ActionRejected, level, actor.UserId, null, nowUtc, normalizedReason, correlationId,
            decisionId);
        if (decision == ApprovalDecision.Rejected)
            PendingOutcome = ApprovalStatus.Rejected;
        else if (level == FinalLevel)
            PendingOutcome = ApprovalStatus.Approved;
        else
            CurrentLevel = steps.Where(candidate => candidate.Level > level).Min(candidate => candidate.Level);
        Touch(nowUtc);
        return (ApprovalDecisionResult.Recorded, null);
    }

    /// <summary>
    /// APR-010: the current assignee hands the current step to another active user who holds <c>approvals.decide</c>, is
    /// not themselves and is eligible (APR-005/007/008). A reason is required; the due time is unchanged and the delegator
    /// stays in the history.
    /// </summary>
    public DomainError? Delegate(int level, ApprovalActor actor, ApprovalCandidate candidate, string? reason, DateTime nowUtc, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(candidate);
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (OpenStepError(level) is { } stateError)
            return stateError;
        var step = CurrentStep!;
        if (!IsAssignee(step, actor))
            return ApprovalErrors.NotAssignee;
        var normalizedReason = MasterDataRules.NormalizeReason(reason);
        if (normalizedReason is null)
            return DomainError.ReasonRequired("APR-010", MasterDataRules.ReasonMinLength, MasterDataRules.ReasonMaxLength);
        if (candidate.UserId == Guid.Empty || candidate.UserId == actor.UserId || candidate.UserId == step.AssigneeUserId
            || !candidate.IsActive || !candidate.CanDecide || !IsEligible(candidate.UserId, level))
            return ApprovalErrors.InvalidDelegate;

        var assignee = ApprovalAssignee.ForUser(candidate.UserId);
        step.Reassign(assignee);
        Append(ActionDelegated, level, actor.UserId, assignee, nowUtc, normalizedReason, correlationId, null);
        Raise(new ApprovalStepAssigned(nowUtc, Id, level, candidate.UserId, null, step.DueAtUtc!.Value, step.AssignmentSequence));
        Touch(nowUtc);
        return null;
    }

    /// <summary>
    /// APR-015: an administrator reassigns the current step to an eligible active user who holds
    /// <c>approvals.decide</c>, or to a role group, with a reason. The due time is unchanged; the administrator never decides.
    /// </summary>
    public DomainError? Reassign(int level, Guid administratorId, ApprovalAssignee assignee, ApprovalCandidate? candidate, string? reason,
        DateTime nowUtc, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(assignee);
        MasterDataRules.EnsureId(administratorId, nameof(administratorId));
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (OpenStepError(level) is { } stateError)
            return stateError;
        var normalizedReason = MasterDataRules.NormalizeReason(reason);
        if (normalizedReason is null)
            return DomainError.ReasonRequired("APR-015", MasterDataRules.ReasonMinLength, MasterDataRules.ReasonMaxLength);
        if (!assignee.IsValid)
            return ApprovalErrors.InvalidDelegate with { RuleId = "APR-015" };
        if (assignee.UserId is { } userId
            && (candidate is null || candidate.UserId != userId || !candidate.IsActive || !candidate.CanDecide || !IsEligible(userId, level)))
            return ApprovalErrors.InvalidDelegate with { RuleId = "APR-015" };

        var step = CurrentStep!;
        step.Reassign(assignee);
        Append(ActionReassigned, level, administratorId, assignee, nowUtc, normalizedReason, correlationId, null);
        Raise(new ApprovalStepAssigned(nowUtc, Id, level, assignee.UserId, assignee.Role, step.DueAtUtc!.Value, step.AssignmentSequence));
        Touch(nowUtc);
        return null;
    }

    /// <summary>APR-011 reminder of the current step, once per assignment window; <c>false</c> when not due or already sent.</summary>
    public bool RecordReminder(int level, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (OpenStepError(level) is not null)
            return false;
        var step = CurrentStep!;
        if (step.RemindedAtUtc is not null || nowUtc < step.ReminderDueAtUtc)
            return false;

        step.MarkReminded(nowUtc);
        Append(ActionReminded, level, null, null, nowUtc, null, correlationId, null);
        Raise(new ApprovalReminderDue(nowUtc, Id, level, step.ReminderCount, step.AssigneeUserId, step.AssigneeRole));
        Touch(nowUtc);
        return true;
    }

    /// <summary>
    /// APR-011 deadline of the current step: escalation to the pinned escalation group with a new full step duration while
    /// escalations remain; otherwise the step expires and the instance settles as <c>Expired</c>.
    /// </summary>
    public ApprovalDeadlineOutcome HandleDeadline(int level, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (OpenStepError(level) is not null)
            return ApprovalDeadlineOutcome.NotDue;
        var step = CurrentStep!;
        if (nowUtc < step.DueAtUtc)
            return ApprovalDeadlineOutcome.NotDue;

        if (step.EscalationCount < MaxEscalations)
        {
            step.Escalate(EscalationRole, nowUtc + StepDuration, nowUtc + ReminderAfter);
            var assignee = ApprovalAssignee.ForRole(EscalationRole);
            Append(ActionEscalated, level, null, assignee, nowUtc, null, correlationId, null);
            Raise(new ApprovalStepEscalated(nowUtc, Id, level, step.EscalationCount, EscalationRole, step.DueAtUtc!.Value));
            Touch(nowUtc);
            return ApprovalDeadlineOutcome.Escalated;
        }

        step.Expire();
        PendingOutcome = ApprovalStatus.Expired;
        Append(ActionStepExpired, level, null, null, nowUtc, null, correlationId, null);
        Touch(nowUtc);
        return ApprovalDeadlineOutcome.Expired;
    }

    /// <summary>
    /// Applies the settled outcome (APR-009, APR-011, APR-013): the instance ends <c>Approved</c>, <c>Rejected</c> or
    /// <c>Expired</c>, open steps are cancelled and the matching event is raised. The use case applies the award,
    /// requisition and budget effects in the same transaction. Idempotent: an ended instance returns <c>false</c>.
    /// </summary>
    public (bool Changed, DomainError? Error) Finalize(DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (Status != ApprovalStatus.Pending)
            return (false, null);
        if (PendingOutcome is not { } outcome)
            return (false, DomainError.InvalidTransition(Status.ToString(), "No outcome has been settled yet.", "APR-013"));

        var decisive = steps.Where(step => step.Status is ApprovalStepStatus.Rejected or ApprovalStepStatus.Expired).FirstOrDefault();
        var (action, cause) = outcome switch
        {
            ApprovalStatus.Approved => (ActionCompleted, CauseApproved),
            ApprovalStatus.Rejected => (ActionInstanceRejected, CauseRejected),
            _ => (ActionInstanceExpired, CauseExpired)
        };
        End(outcome, cause, outcome == ApprovalStatus.Rejected ? decisive?.Reason : null, nowUtc);
        Append(action, decisive?.Level, null, null, nowUtc, null, correlationId, null);
        Raise(outcome switch
        {
            ApprovalStatus.Approved => new ApprovalCompleted(nowUtc, Id, AwardId, RequisitionId, RequesterId),
            ApprovalStatus.Rejected => new ApprovalRejected(nowUtc, Id, decisive?.Level ?? CurrentLevel, RequisitionId, RequesterId),
            _ => new ApprovalExpired(nowUtc, Id, RequisitionId, RequesterId)
        });
        return (true, null);
    }

    /// <summary>
    /// APR-012: a material change (supplier no longer active, award withdrawn, RFQ cancelled) ends a pending instance
    /// <c>Invalidated</c>; a settled but unapplied outcome is discarded. Idempotent per instance.
    /// </summary>
    public bool Invalidate(string cause, DateTime nowUtc, string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cause);
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (Status != ApprovalStatus.Pending)
            return false;

        End(ApprovalStatus.Invalidated, cause, null, nowUtc);
        Append(ActionInvalidated, null, null, null, nowUtc, null, correlationId, null);
        Raise(new ApprovalInvalidated(nowUtc, Id, EndCause!, RequisitionId));
        return true;
    }

    /// <summary>
    /// REQ-008 cascade (system, <paramref name="administratorId"/> <c>null</c>) or APR-015 administrative cancellation
    /// (reason required). Idempotent: an ended instance returns <c>Changed = false</c>.
    /// </summary>
    public (bool Changed, DomainError? Error) Cancel(string cause, Guid? administratorId, string? reason, DateTime nowUtc, string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cause);
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        string? normalizedReason = null;
        if (administratorId is not null)
        {
            normalizedReason = MasterDataRules.NormalizeReason(reason);
            if (normalizedReason is null)
                return (false, DomainError.ReasonRequired("APR-015", MasterDataRules.ReasonMinLength, MasterDataRules.ReasonMaxLength));
        }

        if (Status != ApprovalStatus.Pending)
            return (false, administratorId is null ? null : ApprovalErrors.InstanceEnded(Status));

        End(ApprovalStatus.Cancelled, cause, normalizedReason, nowUtc);
        Append(ActionCancelled, null, administratorId, null, nowUtc, normalizedReason, correlationId, null);
        Raise(new ApprovalCancelled(nowUtc, Id, EndCause!, RequisitionId));
        return (true, null);
    }

    private static bool IsAssignee(ApprovalStep step, ApprovalActor actor) =>
        step.AssigneeUserId == actor.UserId || (step.AssigneeRole is { } role && actor.Roles.Contains(role));

    private DomainError? OpenStepError(int level)
    {
        if (Status != ApprovalStatus.Pending || PendingOutcome is not null)
            return ApprovalErrors.InstanceEnded(Status);
        var step = steps.FirstOrDefault(candidate => candidate.Level == level);
        if (step is null)
            return ApprovalErrors.StepNotFound;
        return level != CurrentLevel || step.Status != ApprovalStepStatus.Pending ? ApprovalErrors.StepNotCurrent : null;
    }

    private void End(ApprovalStatus status, string cause, string? reason, DateTime nowUtc)
    {
        foreach (var step in steps.Where(step => step.Status is ApprovalStepStatus.Waiting or ApprovalStepStatus.Pending))
            step.Cancel();
        Status = status;
        PendingOutcome = null;
        EndedAtUtc = nowUtc;
        EndCause = cause.Length > CauseMaxLength ? cause[..CauseMaxLength] : cause;
        EndReason = reason;
        Touch(nowUtc);
    }

    private void Append(
        string action, int? level, Guid? actorId, ApprovalAssignee? assignee, DateTime nowUtc, string? reason, string correlationId, Guid? decisionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        history.Add(new ApprovalHistoryEntry(
            Guid.NewGuid(), Id, history.Count + 1, action, level, actorId, assignee?.UserId, assignee?.Role, nowUtc, reason,
            correlationId.Length > ApprovalHistoryEntry.CorrelationIdMaxLength ? correlationId[..ApprovalHistoryEntry.CorrelationIdMaxLength] : correlationId,
            decisionId));
    }

    private void Raise(DomainEvent domainEvent) => events.Raise(domainEvent);

    private void Touch(DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        IncrementVersion();
    }
}

/// <summary>One required level of an <see cref="ApprovalInstance"/> (domain-model.md §Approvals).</summary>
public sealed class ApprovalStep
{
    private ApprovalStep()
    {
    }

    internal ApprovalStep(Guid id, Guid instanceId, int level, ApproverResolver resolver, string? resolverRole)
    {
        Id = id;
        InstanceId = instanceId;
        Level = level;
        Resolver = resolver;
        ResolverRole = resolverRole;
        Status = ApprovalStepStatus.Waiting;
    }

    public Guid Id { get; private set; }
    public Guid InstanceId { get; private set; }
    public int Level { get; private set; }
    public ApproverResolver Resolver { get; private set; }
    public string? ResolverRole { get; private set; }
    public ApprovalStepStatus Status { get; private set; }
    public Guid? AssigneeUserId { get; private set; }
    public string? AssigneeRole { get; private set; }

    /// <summary>Grows with every assignment, delegation, reassignment and escalation (event key of <c>step-assigned</c>).</summary>
    public int AssignmentSequence { get; private set; }

    public DateTime? AssignedAtUtc { get; private set; }
    public DateTime? DueAtUtc { get; private set; }
    public DateTime? ReminderDueAtUtc { get; private set; }
    public DateTime? RemindedAtUtc { get; private set; }
    public int ReminderCount { get; private set; }
    public int EscalationCount { get; private set; }
    public ApprovalDecision? Decision { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public DateTime? DecidedAtUtc { get; private set; }

    /// <summary>Correlates the decision with the external event that woke the orchestration.</summary>
    public Guid? DecisionId { get; private set; }

    public string? Reason { get; private set; }

    internal void Assign(ApprovalAssignee assignee, DateTime nowUtc, DateTime dueAtUtc, DateTime reminderDueAtUtc)
    {
        Status = ApprovalStepStatus.Pending;
        AssignedAtUtc = nowUtc;
        DueAtUtc = dueAtUtc;
        ReminderDueAtUtc = reminderDueAtUtc;
        RemindedAtUtc = null;
        Reassign(assignee);
    }

    internal void Reassign(ApprovalAssignee assignee)
    {
        AssigneeUserId = assignee.UserId;
        AssigneeRole = assignee.Role;
        AssignmentSequence++;
    }

    internal void Escalate(string role, DateTime dueAtUtc, DateTime reminderDueAtUtc)
    {
        EscalationCount++;
        DueAtUtc = dueAtUtc;
        ReminderDueAtUtc = reminderDueAtUtc;
        RemindedAtUtc = null;
        Reassign(ApprovalAssignee.ForRole(role));
    }

    internal void MarkReminded(DateTime nowUtc)
    {
        RemindedAtUtc = nowUtc;
        ReminderCount++;
    }

    internal void Decide(ApprovalDecision decision, Guid actorId, Guid decisionId, string? reason, DateTime nowUtc)
    {
        Decision = decision;
        Status = decision == ApprovalDecision.Approved ? ApprovalStepStatus.Approved : ApprovalStepStatus.Rejected;
        DecidedBy = actorId;
        DecidedAtUtc = nowUtc;
        DecisionId = decisionId;
        Reason = reason;
    }

    internal void Expire() => Status = ApprovalStepStatus.Expired;

    internal void Cancel() => Status = ApprovalStepStatus.Cancelled;
}

/// <summary>Append-only history row of an approval instance (NFR-AUD-2): who, when, which level, assignee and reason.</summary>
public sealed class ApprovalHistoryEntry
{
    public const int ActionMaxLength = 40;
    public const int CorrelationIdMaxLength = 128;

    private ApprovalHistoryEntry()
    {
        Action = string.Empty;
        CorrelationId = string.Empty;
    }

    internal ApprovalHistoryEntry(
        Guid id, Guid instanceId, int sequence, string action, int? level, Guid? actorId, Guid? assigneeUserId, string? assigneeRole,
        DateTime atUtc, string? reason, string correlationId, Guid? decisionId)
    {
        Id = id;
        InstanceId = instanceId;
        Sequence = sequence;
        Action = action;
        Level = level;
        ActorId = actorId;
        AssigneeUserId = assigneeUserId;
        AssigneeRole = assigneeRole;
        AtUtc = atUtc;
        Reason = reason;
        CorrelationId = correlationId;
        DecisionId = decisionId;
    }

    public Guid Id { get; private set; }
    public Guid InstanceId { get; private set; }
    public int Sequence { get; private set; }
    public string Action { get; private set; }
    public int? Level { get; private set; }
    public Guid? ActorId { get; private set; }
    public Guid? AssigneeUserId { get; private set; }
    public string? AssigneeRole { get; private set; }
    public DateTime AtUtc { get; private set; }
    public string? Reason { get; private set; }
    public string CorrelationId { get; private set; }
    public Guid? DecisionId { get; private set; }
}

/// <summary>Stable Problem Details codes of the approval rules (business-rules.md §Approval).</summary>
public static class ApprovalErrors
{
    public static readonly DomainError NotAssignee = new(
        "approval.not-assignee", DomainErrorKind.Forbidden, "The actor is not the current assignee of this step.", "APR-004");

    public static readonly DomainError StepNotCurrent = new(
        "approval.step-not-current", DomainErrorKind.Conflict, "The step is not the current pending step.", "APR-004");

    public static readonly DomainError SelfApproval = new(
        "approval.self-approval", DomainErrorKind.Forbidden, "The requester cannot give the final approval of their own requisition.", "APR-005");

    public static readonly DomainError DuplicateApprover = new(
        "approval.duplicate-approver", DomainErrorKind.Forbidden, "The same user cannot decide two levels of one approval.", "APR-007");

    public static readonly DomainError MakerChecker = new(
        "approval.maker-checker", DomainErrorKind.Forbidden, "The buyer who submitted the award cannot give the final approval.", "APR-008");

    public static readonly DomainError InvalidDelegate = new(
        "approval.invalid-delegate", DomainErrorKind.BusinessRule,
        "The delegate must be another active approver who is eligible for this step.", "APR-010", "delegateUserId");

    public static readonly DomainError StepAlreadyDecided = new(
        "approval.step-already-decided", DomainErrorKind.Conflict, "The step has already been decided.", "APR-014");

    public static readonly DomainError IneligibleAssignee = new(
        "approval.ineligible-assignee", DomainErrorKind.BusinessRule, "The resolved assignee is not eligible for this step.", "APR-006");

    public static readonly DomainError StepNotFound = new("not-found", DomainErrorKind.NotFound, "The approval step does not exist.");

    public static DomainError InstanceEnded(ApprovalStatus status) =>
        new("approval.instance-ended", DomainErrorKind.Conflict, "The approval has ended.", "APR-014",
            Details: new Dictionary<string, string> { ["currentState"] = status.ToString() });
}

/// <summary><c>procurement.approval.started</c>.</summary>
public sealed record ApprovalStarted(DateTime OccurredAtUtc, Guid ApprovalInstanceId, Guid AwardId, Guid RequisitionId, IReadOnlyList<int> RequiredLevels, string PolicyRef)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.approval.started";
}

/// <summary><c>procurement.approval.step-assigned</c> (assignment, delegation and reassignment).</summary>
public sealed record ApprovalStepAssigned(
    DateTime OccurredAtUtc, Guid ApprovalInstanceId, int Level, Guid? AssigneeUserId, string? AssigneeRole, DateTime DueAtUtc, int AssignmentSequence)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.approval.step-assigned";
}

/// <summary><c>procurement.approval.reminder-due</c>.</summary>
public sealed record ApprovalReminderDue(
    DateTime OccurredAtUtc, Guid ApprovalInstanceId, int Level, int ReminderNumber, Guid? AssigneeUserId, string? AssigneeRole)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.approval.reminder-due";
}

/// <summary><c>procurement.approval.step-escalated</c>.</summary>
public sealed record ApprovalStepEscalated(DateTime OccurredAtUtc, Guid ApprovalInstanceId, int Level, int EscalationCount, string AssigneeRole, DateTime DueAtUtc)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.approval.step-escalated";
}

/// <summary><c>procurement.approval.completed</c> (final approval, APR-013).</summary>
public sealed record ApprovalCompleted(DateTime OccurredAtUtc, Guid ApprovalInstanceId, Guid AwardId, Guid RequisitionId, Guid RequesterId)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.approval.completed";
}

/// <summary><c>procurement.approval.rejected</c> (APR-009).</summary>
public sealed record ApprovalRejected(DateTime OccurredAtUtc, Guid ApprovalInstanceId, int Level, Guid RequisitionId, Guid RequesterId)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.approval.rejected";
}

/// <summary><c>procurement.approval.expired</c> (APR-011).</summary>
public sealed record ApprovalExpired(DateTime OccurredAtUtc, Guid ApprovalInstanceId, Guid RequisitionId, Guid RequesterId) : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.approval.expired";
}

/// <summary><c>procurement.approval.invalidated</c> (APR-012).</summary>
public sealed record ApprovalInvalidated(DateTime OccurredAtUtc, Guid ApprovalInstanceId, string Cause, Guid RequisitionId) : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.approval.invalidated";
}

/// <summary><c>procurement.approval.cancelled</c> (REQ-008 cascade, APR-015).</summary>
public sealed record ApprovalCancelled(DateTime OccurredAtUtc, Guid ApprovalInstanceId, string Cause, Guid RequisitionId) : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.approval.cancelled";
}
