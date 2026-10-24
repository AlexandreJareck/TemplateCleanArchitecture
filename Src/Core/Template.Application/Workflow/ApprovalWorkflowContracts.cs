#nullable enable
namespace Template.Application.Workflow;

/// <summary>
/// Names shared by the WorkflowFunctions host and the Infrastructure client (ADR-0003). Changing an orchestrator,
/// activity or event name breaks in-flight instances; add a new name and a new orchestration version instead.
/// </summary>
public static class ApprovalWorkflowNames
{
    public const string Orchestrator = "ApprovalOrchestrator";
    public const string StartActivity = "StartApprovalActivity";
    public const string AssignStepActivity = "AssignApprovalStepActivity";
    public const string ProgressActivity = "GetApprovalProgressActivity";
    public const string TimerActivity = "ProcessApprovalTimerActivity";
    public const string FinalizeActivity = "FinalizeApprovalActivity";

    /// <summary>External event that wakes a waiting orchestration; the payload is a hint, the database is the truth.</summary>
    public const string SignalEvent = "ApprovalSignal";

    /// <summary>
    /// Orchestration version stamped on new instances (Durable <c>defaultVersion</c>). Instances keep the version they
    /// started with; code paths of older versions stay unchanged until no instance uses them.
    /// </summary>
    public const string CurrentVersion = "1.0";

    /// <summary>Deterministic Durable instance ID of an award (APR-001); duplicate starts resolve to the same instance.</summary>
    public static string InstanceIdFor(Guid awardId) => $"approval-{awardId:N}";

    /// <summary>The award ID encoded in an instance ID, or <c>null</c> for any other value.</summary>
    public static Guid? AwardIdFrom(string? instanceId) =>
        instanceId is { Length: 41 } && instanceId.StartsWith("approval-", StringComparison.Ordinal)
        && Guid.TryParseExact(instanceId[9..], "N", out var awardId) && awardId != Guid.Empty
            ? awardId
            : null;
}

/// <summary>
/// Orchestration input (NFR-SEC-7): IDs, the award submission instant (selects the policy version effective then,
/// APR-001) and the correlation ID only. No amount, name or commercial detail is stored in Durable history.
/// </summary>
public sealed record ApprovalWorkflowInput(int InputVersion, Guid AwardId, Guid RequisitionId, DateTime AwardSubmittedAtUtc, string CorrelationId)
{
    public const int Version1 = 1;
}

/// <summary>Start activity input: the orchestration input plus the version Durable stamped on this instance.</summary>
public sealed record ApprovalStartRequest(ApprovalWorkflowInput Input, string WorkflowVersion);

/// <summary>What the orchestration does next; computed by Application from the persisted state (APR-016).</summary>
public enum ApprovalWorkflowStep
{
    AssignStep,
    Wait,
    Finalize,
    Completed
}

public enum ApprovalWorkflowTimer
{
    Reminder,
    Deadline
}

/// <summary>
/// Activity output: the next step, the current level and, while waiting, the durable timer to race against the signal
/// event. <see cref="Status"/> is the persisted instance status (or <c>NotStarted</c> when the award left approval
/// before the instance existed).
/// </summary>
public sealed record ApprovalProgress(
    Guid AwardId,
    Guid? InstanceId,
    string Status,
    ApprovalWorkflowStep Next,
    int Level,
    ApprovalWorkflowTimer? TimerKind,
    DateTime? TimerAtUtc,
    string CorrelationId)
{
    public const string NotStarted = "NotStarted";
}

/// <summary>Activity input of the reminder and deadline timers; keyed by award and level so a replayed timer is a no-op.</summary>
public sealed record ApprovalTimerRequest(Guid AwardId, int Level, ApprovalWorkflowTimer Kind, DateTime FireAtUtc, string CorrelationId);

/// <summary>Activity input of the start-independent activities.</summary>
public sealed record ApprovalActivityRequest(Guid AwardId, string CorrelationId);

/// <summary>Payload of <see cref="ApprovalWorkflowNames.SignalEvent"/>; duplicates and late signals are harmless.</summary>
public sealed record ApprovalSignal(Guid SignalId, string Kind, string CorrelationId);

public static class ApprovalSignalKinds
{
    public const string Decision = "decision";
    public const string Delegated = "delegated";
    public const string Reassigned = "reassigned";
    public const string Ended = "ended";
    public const string Reconcile = "reconcile";
}

/// <summary>
/// Internal HTTP contract of the WorkflowFunctions host (ADR-0003 §5): reachable only on the private network, every
/// request carries <see cref="KeyHeader"/> with the shared internal key, and nothing here is part of the public v2 API.
/// </summary>
public static class ApprovalWorkflowHttp
{
    public const string KeyHeader = "X-Workflow-Key";
    public const string CorrelationHeader = "X-Correlation-Id";
    public const string WorkflowsRoute = "approval-workflows";

    public static string InstanceRoute(string instanceId) => $"{WorkflowsRoute}/{Uri.EscapeDataString(instanceId)}";
}

public sealed record ApprovalWorkflowStartResponse(string InstanceId, string Result);

public sealed record ApprovalWorkflowSignalResponse(string InstanceId, string Result);

public sealed record ApprovalWorkflowStatusResponse(string InstanceId, string RuntimeStatus, string? Version, DateTime? CreatedAt, DateTime? LastUpdatedAt);

public sealed record ApprovalWorkflowTerminateRequest(string Reason);
