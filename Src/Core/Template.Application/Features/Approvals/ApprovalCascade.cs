#nullable enable
using Microsoft.Extensions.Logging;
using Template.Application.Interfaces.Approvals;
using Template.Application.Logging;
using Template.Application.Services.Procurement;
using Template.Application.Workflow;
using Template.Domain.Common;

namespace Template.Application.Features.Approvals;

/// <summary>Approval changes staged by another module's use case; completed after that use case commits.</summary>
public sealed record ApprovalCascadeResult(IReadOnlyList<DomainEvent> Events, IReadOnlyList<Guid> AwardIds)
{
    public static readonly ApprovalCascadeResult None = new([], []);
}

/// <summary>
/// Ends pending approvals inside another module's transaction: REQ-008 requisition cancellation (<c>Cancelled</c>) and
/// APR-012 supplier suspension or inactivation (<c>Invalidated</c>). After the caller commits,
/// <see cref="CompleteAsync"/> records the events and signals the orchestrations so they finish promptly; a lost signal
/// only delays that until the next timer or the reconciliation sweep (APR-016).
/// </summary>
public sealed class ApprovalCascade(
    IApprovalInstanceRepository instances,
    ApprovalOutcomes outcomes,
    IApprovalWorkflowClient workflow,
    ProcurementWriter writer,
    ILogger<ApprovalCascade> logger)
{
    public async Task<ApprovalCascadeResult> CancelForRequisitionAsync(Guid? awardId, CancellationToken cancellationToken)
    {
        if (awardId is not { } id || await instances.FindByAwardAsync(id, cancellationToken) is not { IsEnded: false } instance)
            return ApprovalCascadeResult.None;
        var events = outcomes.CancelForRequisition(instance);
        return events.Count == 0 ? ApprovalCascadeResult.None : new ApprovalCascadeResult(events, [instance.AwardId]);
    }

    public async Task<ApprovalCascadeResult> InvalidateForSupplierAsync(Guid supplierId, CancellationToken cancellationToken)
    {
        var events = new List<DomainEvent>();
        var awards = new List<Guid>();
        foreach (var id in await instances.ListPendingIdsForSupplierAsync(supplierId, cancellationToken))
        {
            if (await instances.FindAsync(id, cancellationToken) is not { IsEnded: false } instance)
                continue;
            var staged = await outcomes.InvalidateAsync(instance, Domain.Approvals.ApprovalInstance.CauseSupplierUnavailable, cancellationToken);
            if (staged.Count == 0)
                continue;
            events.AddRange(staged);
            awards.Add(instance.AwardId);
        }

        return awards.Count == 0 ? ApprovalCascadeResult.None : new ApprovalCascadeResult(events, awards);
    }

    public async Task CompleteAsync(ApprovalCascadeResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        writer.Record(result.Events);
        foreach (var awardId in result.AwardIds)
        {
            try
            {
                await workflow.SignalAsync(awardId, new ApprovalSignal(Guid.NewGuid(), ApprovalSignalKinds.Ended, writer.CorrelationId), cancellationToken);
            }
            catch (ApprovalWorkflowUnavailableException)
            {
                logger.LogWarning(LogEvents.ApprovalSignalFailed, "Approval for award {AwardId} end signal was not delivered", awardId);
            }
        }
    }
}
