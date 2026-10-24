#nullable enable
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Template.Application.Common.Results;
using Template.Application.Interfaces.Approvals;
using Template.Application.Interfaces.Messaging;
using Template.Application.Logging;
using Template.Application.Messaging.Procurement;
using Template.Application.Services.Procurement;
using Template.Application.Workflow;
using Template.Domain.Approvals;

namespace Template.Application.Features.Approvals;

/// <summary>APR-016 report (<c>approvals.administer</c>): divergences between the database and the orchestration, without repair.</summary>
public sealed record GetApprovalReconciliationQuery(int Max = 100) : IRequest<Result<ApprovalReconciliationView>>;

/// <summary>APR-016 repair (Worker sweep and <c>approvals.administer</c>): starts missing orchestrations and re-signals stalled ones.</summary>
public sealed record RepairApprovalWorkflowsCommand(int Max = 100) : IRequest<Result<ApprovalReconciliationView>>;

public sealed class ApprovalReconciliationHandlers(ApprovalReconciliation reconciliation) :
    IRequestHandler<GetApprovalReconciliationQuery, Result<ApprovalReconciliationView>>,
    IRequestHandler<RepairApprovalWorkflowsCommand, Result<ApprovalReconciliationView>>
{
    public async Task<Result<ApprovalReconciliationView>> Handle(GetApprovalReconciliationQuery request, CancellationToken cancellationToken) =>
        await reconciliation.RunAsync(repair: false, request.Max, cancellationToken);

    public async Task<Result<ApprovalReconciliationView>> Handle(RepairApprovalWorkflowsCommand request, CancellationToken cancellationToken) =>
        await reconciliation.RunAsync(repair: true, request.Max, cancellationToken);
}

/// <summary>
/// APR-016 / NFR-REC-4: compares the database (business truth) with the Durable runtime status. Because every activity
/// re-reads the database, restarting an orchestration for an existing instance resumes it from the persisted state, so
/// the safe repairs are: start a missing or ended orchestration of a pending award, and re-signal an orchestration
/// whose instance needs progress (a lost signal). Divergences that cannot be repaired safely are only reported.
/// </summary>
public sealed class ApprovalReconciliation(
    IApprovalInstanceRepository instances,
    IApprovalWorkflowClient workflow,
    ProcurementWriter writer,
    ILogger<ApprovalReconciliation> logger)
{
    /// <summary>Normal start and progress latency; younger records are not divergent yet (NFR-PERF-7 is 10 s).</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

    public async Task<ApprovalReconciliationView> RunAsync(bool repair, int max, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(max, 1, 500);
        var now = writer.UtcNow;
        var items = new List<ApprovalReconciliationItemView>();
        var checkedCount = 0;
        try
        {
            foreach (var award in await instances.ListAwardsWithoutInstanceAsync(now - Grace, limit, cancellationToken))
            {
                checkedCount++;
                var status = await workflow.GetStatusAsync(award.AwardId, cancellationToken);
                if (status.IsActive)
                    continue;
                var action = repair ? await StartAsync(award.AwardId, award.RequisitionId, award.SubmittedAtUtc, cancellationToken) : "none";
                items.Add(new ApprovalReconciliationItemView(award.AwardId, null, "AwardPendingApproval", status.RuntimeStatus.ToString(), "missing-workflow", action));
            }

            foreach (var instance in await instances.ListPendingAsync(limit, cancellationToken))
            {
                checkedCount++;
                var status = await workflow.GetStatusAsync(instance.AwardId, cancellationToken);
                if (!status.IsActive)
                {
                    var action = repair ? await StartAsync(instance.AwardId, instance.RequisitionId, instance.StartedAtUtc, cancellationToken) : "none";
                    items.Add(new ApprovalReconciliationItemView(instance.AwardId, instance.Id, instance.Status.ToString(), status.RuntimeStatus.ToString(),
                        "workflow-not-running", action));
                }
                else if (instance.NextAction != ApprovalNextAction.Wait && instance.UpdatedAtUtc < now - Grace)
                {
                    var action = "none";
                    if (repair)
                        action = await workflow.SignalAsync(instance.AwardId, new ApprovalSignal(Guid.NewGuid(), ApprovalSignalKinds.Reconcile, writer.CorrelationId),
                            cancellationToken) == ApprovalWorkflowSignalResult.Delivered ? "signaled" : "not-running";
                    items.Add(new ApprovalReconciliationItemView(instance.AwardId, instance.Id, instance.Status.ToString(), status.RuntimeStatus.ToString(),
                        "workflow-stalled", action));
                }
            }
        }
        catch (ApprovalWorkflowUnavailableException)
        {
            items.Add(new ApprovalReconciliationItemView(Guid.Empty, null, "Unknown", ApprovalWorkflowRuntimeStatus.Unknown.ToString(), "workflow-unavailable", "none"));
            logger.LogWarning(LogEvents.ApprovalReconciliationRepairFailed, "Approval reconciliation stopped: the workflow host is unavailable");
        }

        logger.LogInformation(LogEvents.ApprovalReconciliationCompleted, "Approval reconciliation checked {Checked} records, {Divergent} divergent, repair {Repair}",
            checkedCount, items.Count, repair);
        return new ApprovalReconciliationView(now, checkedCount, items);
    }

    private async Task<string> StartAsync(Guid awardId, Guid requisitionId, DateTime submittedAtUtc, CancellationToken cancellationToken)
    {
        var result = await workflow.StartAsync(
            new ApprovalWorkflowInput(ApprovalWorkflowInput.Version1, awardId, requisitionId, submittedAtUtc, writer.CorrelationId), cancellationToken);
        return result switch
        {
            ApprovalWorkflowStartResult.Started => "started",
            ApprovalWorkflowStartResult.Restarted => "restarted",
            _ => "already-running"
        };
    }
}

/// <summary>
/// APR-001 start (event catalog §3): the Worker approval consumer turns <c>procurement.award.submitted</c> into an
/// idempotent orchestration start with the deterministic instance ID. An unavailable WorkflowFunctions host throws, so
/// the broker retries the message within its bounded policy; the reconciliation sweep repairs a start lost after that.
/// </summary>
public sealed class ApprovalWorkflowStarter(IApprovalWorkflowClient workflow, ILogger<ApprovalWorkflowStarter> logger)
{
    public async Task<InboxApplyResult> HandleAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.EventType != "procurement.award.submitted")
            throw new JsonException("The approval consumer only receives award submissions.");
        if (!envelope.Payload.TryGetProperty("awardId", out var award) || !envelope.Payload.TryGetProperty("requisitionId", out var requisition))
            throw new JsonException("The award payload has no award or requisition.");

        var input = new ApprovalWorkflowInput(
            ApprovalWorkflowInput.Version1, award.GetGuid(), requisition.GetGuid(), envelope.OccurredAtUtc, envelope.CorrelationId);
        var result = await workflow.StartAsync(input, cancellationToken);
        logger.LogInformation(LogEvents.ApprovalWorkflowStartRequested, "Approval workflow for award {AwardId} start result {Result}", input.AwardId, result);
        return result == ApprovalWorkflowStartResult.AlreadyRunning ? InboxApplyResult.Duplicate : InboxApplyResult.Applied;
    }
}
