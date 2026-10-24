using System.Collections.Concurrent;
using Template.Application.Interfaces.Approvals;
using Template.Application.Workflow;

namespace Template.Infrastructure.Workflow;

/// <summary>
/// Used when the workflow client is disabled: every call reports the host as unavailable, so award submission still
/// commits (the start waits for the reconciliation sweep) and decisions answer 503 without changing state (NFR).
/// </summary>
public sealed class UnavailableApprovalWorkflowClient : IApprovalWorkflowClient
{
    private static ApprovalWorkflowUnavailableException Disabled() => new("The approval workflow client is disabled.");

    public Task<ApprovalWorkflowStartResult> StartAsync(ApprovalWorkflowInput input, CancellationToken cancellationToken) => throw Disabled();

    public Task<ApprovalWorkflowSignalResult> SignalAsync(Guid awardId, ApprovalSignal signal, CancellationToken cancellationToken) => throw Disabled();

    public Task<ApprovalWorkflowStatus> GetStatusAsync(Guid awardId, CancellationToken cancellationToken) => throw Disabled();

    public Task<bool> TerminateAsync(Guid awardId, string reason, CancellationToken cancellationToken) => throw Disabled();
}

/// <summary>
/// Test-environment client (no Functions host): it records starts and signals and keeps a runtime status per instance,
/// so functional tests drive the activities in process and assert what the orchestration would have received.
/// <see cref="Unavailable"/> simulates an unreachable host.
/// </summary>
public sealed class InMemoryApprovalWorkflowClient : IApprovalWorkflowClient
{
    private readonly ConcurrentDictionary<string, ApprovalWorkflowRuntimeStatus> statuses = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<(Guid AwardId, ApprovalSignal Signal)> signals = new();
    private readonly ConcurrentQueue<ApprovalWorkflowInput> starts = new();

    public bool Unavailable { get; set; }

    public IReadOnlyList<ApprovalWorkflowInput> Starts => starts.ToArray();

    public IReadOnlyList<(Guid AwardId, ApprovalSignal Signal)> Signals => signals.ToArray();

    public Task<ApprovalWorkflowStartResult> StartAsync(ApprovalWorkflowInput input, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable(cancellationToken);
        var id = ApprovalWorkflowNames.InstanceIdFor(input.AwardId);
        starts.Enqueue(input);
        if (statuses.TryGetValue(id, out var current) && current is ApprovalWorkflowRuntimeStatus.Running or ApprovalWorkflowRuntimeStatus.Pending)
            return Task.FromResult(ApprovalWorkflowStartResult.AlreadyRunning);
        var existed = statuses.ContainsKey(id);
        statuses[id] = ApprovalWorkflowRuntimeStatus.Running;
        return Task.FromResult(existed ? ApprovalWorkflowStartResult.Restarted : ApprovalWorkflowStartResult.Started);
    }

    public Task<ApprovalWorkflowSignalResult> SignalAsync(Guid awardId, ApprovalSignal signal, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable(cancellationToken);
        if (!statuses.TryGetValue(ApprovalWorkflowNames.InstanceIdFor(awardId), out var status) || status != ApprovalWorkflowRuntimeStatus.Running)
            return Task.FromResult(ApprovalWorkflowSignalResult.NotRunning);
        signals.Enqueue((awardId, signal));
        return Task.FromResult(ApprovalWorkflowSignalResult.Delivered);
    }

    public Task<ApprovalWorkflowStatus> GetStatusAsync(Guid awardId, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable(cancellationToken);
        var id = ApprovalWorkflowNames.InstanceIdFor(awardId);
        return Task.FromResult(new ApprovalWorkflowStatus(
            id, statuses.TryGetValue(id, out var status) ? status : ApprovalWorkflowRuntimeStatus.NotFound, ApprovalWorkflowNames.CurrentVersion, null, null));
    }

    public Task<bool> TerminateAsync(Guid awardId, string reason, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable(cancellationToken);
        var id = ApprovalWorkflowNames.InstanceIdFor(awardId);
        if (!statuses.ContainsKey(id))
            return Task.FromResult(false);
        statuses[id] = ApprovalWorkflowRuntimeStatus.Terminated;
        return Task.FromResult(true);
    }

    /// <summary>Marks the orchestration of an award completed, as Durable does when the orchestrator returns.</summary>
    public void Complete(Guid awardId) => statuses[ApprovalWorkflowNames.InstanceIdFor(awardId)] = ApprovalWorkflowRuntimeStatus.Completed;

    private void ThrowIfUnavailable(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Unavailable)
            throw new ApprovalWorkflowUnavailableException("The approval workflow host is unavailable (simulated).");
    }
}
