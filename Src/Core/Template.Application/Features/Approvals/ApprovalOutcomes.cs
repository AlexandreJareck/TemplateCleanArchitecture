#nullable enable
using System.Globalization;
using Template.Application.Common.Results;
using Template.Application.Features.Budgeting;
using Template.Application.Interfaces.Budgeting;
using Template.Application.Interfaces.Requisitions;
using Template.Application.Interfaces.Sourcing;
using Template.Application.Services.Procurement;
using Template.Domain.Approvals;
using Template.Domain.Budgeting;
using Template.Domain.Common;
using Template.Domain.Requisitions;
using Template.Domain.Sourcing;

namespace Template.Application.Features.Approvals;

/// <summary>Audit actions and resource of the Approvals module (business-rules.md §Approval, AUD-001).</summary>
public static class ApprovalAudit
{
    public const string Resource = "approval-instance";
    public const string PolicyResource = "approval-policy";
    public const string Started = "approval.started";
    public const string StepAssigned = "approval.step.assigned";
    public const string StepApproved = "approval.step.approved";
    public const string StepRejected = "approval.step.rejected";
    public const string StepDelegated = "approval.step.delegated";
    public const string StepReminded = "approval.step.reminded";
    public const string StepEscalated = "approval.step.escalated";
    public const string Approved = "approval.approved";
    public const string Rejected = "approval.rejected";
    public const string Expired = "approval.expired";
    public const string Invalidated = "approval.invalidated";
    public const string Cancelled = "approval.cancelled";
    public const string Administered = "approval.administered";
    public const string DecisionDenied = "approval.decision.denied";
    public const string PolicyVersionCreated = "policy.version.created";

    public static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Amount(decimal value) => value.ToString("0.00##", CultureInfo.InvariantCulture);

    public static ApplicationError NotFound() => ProcurementWriter.NotFound("approval instance");
}

/// <summary>
/// Cross-module effects of an approval ending, applied in the caller's unit of work so the instance, the award, the RFQ,
/// the requisition and the budget reservation change atomically with their audit records and Outbox events (APR-009,
/// APR-011, APR-012, APR-013, APR-015, REQ-008). Every method is idempotent: an aggregate that already left the
/// approval is not changed again, so a replayed activity never applies an effect twice. Events are staged here; the
/// caller commits and then calls <see cref="ProcurementWriter.Record"/> with the returned events.
/// </summary>
public sealed class ApprovalOutcomes(
    IRequestForQuotationRepository rfqs,
    IPurchaseRequisitionRepository requisitions,
    IBudgetAllocationRepository allocations,
    ProcurementWriter writer)
{
    /// <summary>
    /// Applies the settled outcome inside a transaction that holds the allocation row lock (BUD-006): final approval
    /// commits the reservation (BUD-010, APR-013); rejection and expiry release it (APR-009, APR-011). Returns the staged
    /// events, or the rejection after rolling back.
    /// </summary>
    public async Task<(IReadOnlyList<DomainEvent> Events, ApplicationError? Error)> FinalizeAsync(ApprovalInstance instance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var outcome = instance.PendingOutcome;
        var rfq = await rfqs.FindAsync(instance.RfqId, cancellationToken);
        var requisition = await requisitions.FindAsync(instance.RequisitionId, cancellationToken)
            ?? throw new InvalidOperationException("An approval instance references a missing requisition.");
        var allocationId = requisition.ActiveAllocationId;
        await writer.BeginAsync(cancellationToken);
        var allocation = allocationId is null ? null : await allocations.FindForUpdateAsync(allocationId.Value, requisition.Id, cancellationToken);

        var now = writer.UtcNow;
        var correlationId = writer.CorrelationId;
        var (changed, error) = instance.Finalize(now, correlationId);
        if (error is not null)
            return ([], await writer.FailAsync(error.ToApplicationError()));
        if (!changed)
        {
            await writer.RollbackAsync();
            return ([], null);
        }

        var instanceEvent = instance.DomainEvents.Last().EventType;
        switch (outcome)
        {
            case ApprovalStatus.Approved:
            {
                if (rfq?.ApproveAward(instance.AwardId, now, correlationId, instanceEvent) is { Error: { } rfqError })
                    return ([], await writer.FailAsync(rfqError.ToApplicationError()));
                if (requisition.ApproveAward(instance.AwardId, now, correlationId, instanceEvent) is { Error: { } requisitionError })
                    return ([], await writer.FailAsync(requisitionError.ToApplicationError()));
                if (allocation is null)
                    return ([], await writer.FailAsync(DomainError.InvalidTransition("Released", "The requisition has no active reservation.", "BUD-010")
                        .ToApplicationError()));
                var (reservation, committed, commitError) = allocation.Commit(requisition.Id, null, now, correlationId);
                if (commitError is not null)
                    return ([], await writer.FailAsync(commitError.ToApplicationError()));
                writer.Audit(ApprovalAudit.Approved, ApprovalAudit.Resource, instance.Id, "Pending", "Approved", ruleIds: ["APR-013", "BUD-010"],
                    details: Details(instance));
                writer.Audit("requisition.approved", "purchase-requisition", requisition.Id, "PendingApproval", "Approved", ruleIds: ["APR-013"],
                    details: new Dictionary<string, string> { ["approvalInstanceId"] = instance.Id.ToString(), ["number"] = requisition.Number });
                if (committed)
                    writer.Audit(BudgetMoney.ReservationCommitted, BudgetMoney.ReservationResource, reservation!.Id, "Reserved", "Committed", ruleIds: ["BUD-010"],
                        details: ReservationDetails(requisition.Id, reservation));
                break;
            }

            case ApprovalStatus.Rejected:
            case ApprovalStatus.Expired:
            {
                var rejected = outcome == ApprovalStatus.Rejected;
                var reasonCode = rejected ? "ApprovalRejected" : "ApprovalExpired";
                rfq?.EndPendingAward(instance.AwardId, rejected ? AwardStatus.Rejected : AwardStatus.Cancelled, reopen: false, reasonCode, now, correlationId,
                    instanceEvent);
                var (_, requisitionChanged) = requisition.EndApproval(
                    instance.AwardId, rejected ? RequisitionStatus.Rejected : RequisitionStatus.Expired, instance.EndReason, now, correlationId, instanceEvent);
                var releaseReason = rejected ? BudgetReleaseReason.Rejected : BudgetReleaseReason.Expired;
                var released = requisitionChanged ? allocation?.Release(requisition.Id, releaseReason, null, now, correlationId) : null;
                writer.Audit(rejected ? ApprovalAudit.Rejected : ApprovalAudit.Expired, ApprovalAudit.Resource, instance.Id, "Pending", outcome.ToString(),
                    instance.EndReason, rejected ? ["APR-009", "BUD-007"] : ["APR-011", "BUD-007"], Details(instance));
                if (requisitionChanged)
                    writer.Audit(rejected ? "requisition.rejected" : "requisition.expired", "purchase-requisition", requisition.Id, "PendingApproval",
                        outcome.ToString(), instance.EndReason, rejected ? ["APR-009"] : ["APR-011"],
                        new Dictionary<string, string> { ["approvalInstanceId"] = instance.Id.ToString(), ["number"] = requisition.Number });
                if (released is { Changed: true, Reservation: { } reservation })
                    writer.Audit(BudgetMoney.ReservationReleased, BudgetMoney.ReservationResource, reservation.Id, "Reserved", "Released", ruleIds: ["BUD-007"],
                        details: new Dictionary<string, string>(ReservationDetails(requisition.Id, reservation)) { ["releaseReason"] = releaseReason.ToString() });
                break;
            }
        }

        return (StageAll(instance, rfq, requisition, allocation), null);
    }

    /// <summary>
    /// APR-012: ends a pending instance <c>Invalidated</c>, the award <c>Invalidated</c> and the RFQ back to <c>Closed</c>
    /// for a new award; the requisition returns to <c>InSourcing</c> and the reservation stays <c>Reserved</c>.
    /// </summary>
    public async Task<IReadOnlyList<DomainEvent>> InvalidateAsync(ApprovalInstance instance, string cause, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var now = writer.UtcNow;
        if (!instance.Invalidate(cause, now, writer.CorrelationId))
            return [];
        var (rfq, requisition) = await ReopenAsync(instance, AwardStatus.Invalidated, "ApprovalInvalidated", null, null, cancellationToken);
        writer.Audit(ApprovalAudit.Invalidated, ApprovalAudit.Resource, instance.Id, "Pending", "Invalidated", cause, ["APR-012"], Details(instance));
        return StageAll(instance, rfq, requisition, null);
    }

    /// <summary>
    /// APR-015: administrative cancellation with reason; the award is <c>Withdrawn</c>, the RFQ returns to <c>Closed</c>
    /// and the requisition to <c>InSourcing</c> with the reservation still <c>Reserved</c>.
    /// </summary>
    public async Task<(IReadOnlyList<DomainEvent> Events, ApplicationError? Error)> CancelAdministrativelyAsync(
        ApprovalInstance instance, Guid administratorId, string? reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var (changed, error) = instance.Cancel(ApprovalInstance.CauseAdministrativeCancel, administratorId, reason, writer.UtcNow, writer.CorrelationId);
        if (error is not null)
            return ([], error.ToApplicationError());
        if (!changed)
            return ([], null);
        var (rfq, requisition) = await ReopenAsync(instance, AwardStatus.Withdrawn, "ApprovalCancelled", instance.EndReason, administratorId, cancellationToken);
        writer.Audit(ApprovalAudit.Administered, ApprovalAudit.Resource, instance.Id, "Pending", "Cancelled", instance.EndReason, ["APR-015"],
            new Dictionary<string, string>(Details(instance)) { ["operation"] = "cancel" });
        return (StageAll(instance, rfq, requisition, null), null);
    }

    /// <summary>
    /// REQ-008 cascade: the requisition use case already cancelled the RFQ and pending award and releases the
    /// reservation; the pending instance ends <c>Cancelled</c> in the same commit.
    /// </summary>
    public IReadOnlyList<DomainEvent> CancelForRequisition(ApprovalInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var (changed, _) = instance.Cancel(ApprovalInstance.CauseRequisitionCancelled, null, null, writer.UtcNow, writer.CorrelationId);
        if (!changed)
            return [];
        writer.Audit(ApprovalAudit.Cancelled, ApprovalAudit.Resource, instance.Id, "Pending", "Cancelled", ApprovalInstance.CauseRequisitionCancelled,
            ["REQ-008"], Details(instance));
        return StageAll(instance, null, null, null);
    }

    private async Task<(RequestForQuotation? Rfq, PurchaseRequisition? Requisition)> ReopenAsync(
        ApprovalInstance instance, AwardStatus awardOutcome, string reasonCode, string? reason, Guid? actorId, CancellationToken cancellationToken)
    {
        var now = writer.UtcNow;
        var correlationId = writer.CorrelationId;
        var eventType = instance.DomainEvents.Last().EventType;
        var rfq = await rfqs.FindAsync(instance.RfqId, cancellationToken);
        if (rfq is not null && rfq.EndPendingAward(instance.AwardId, awardOutcome, reopen: true, reasonCode, now, correlationId, eventType))
            writer.Audit("award.invalidated", "request-for-quotation", rfq.Id, "AwardPending", "Closed", reasonCode, ["APR-012", "APR-015"],
                new Dictionary<string, string> { ["awardId"] = instance.AwardId.ToString(), ["awardStatus"] = awardOutcome.ToString() });
        var requisition = await requisitions.FindAsync(instance.RequisitionId, cancellationToken);
        if (requisition is not null && requisition.ReturnToSourcingFromApproval(instance.AwardId, reasonCode, reason, actorId, now, correlationId, eventType))
            writer.Audit("requisition.returned-to-sourcing", "purchase-requisition", requisition.Id, "PendingApproval", "InSourcing", reason ?? reasonCode,
                ["APR-012", "APR-015"], new Dictionary<string, string> { ["approvalInstanceId"] = instance.Id.ToString(), ["number"] = requisition.Number });
        return (rfq, requisition);
    }

    private IReadOnlyList<DomainEvent> StageAll(
        ApprovalInstance instance, RequestForQuotation? rfq, PurchaseRequisition? requisition, BudgetAllocation? allocation)
    {
        var staged = writer.Stage(instance.DomainEvents.Concat(rfq?.DomainEvents ?? []).Concat(requisition?.DomainEvents ?? [])
            .Concat(allocation?.DomainEvents ?? []).ToArray());
        instance.ClearDomainEvents();
        rfq?.ClearDomainEvents();
        requisition?.ClearDomainEvents();
        allocation?.ClearDomainEvents();
        return staged;
    }

    internal static IReadOnlyDictionary<string, string> Details(ApprovalInstance instance) => new Dictionary<string, string>
    {
        ["awardId"] = instance.AwardId.ToString(),
        ["requisitionId"] = instance.RequisitionId.ToString(),
        ["policy"] = instance.PolicyReference,
        ["workflowInstanceId"] = instance.WorkflowInstanceId
    };

    private static IReadOnlyDictionary<string, string> ReservationDetails(Guid requisitionId, BudgetReservation reservation) => new Dictionary<string, string>
    {
        ["requisitionId"] = requisitionId.ToString(),
        ["allocationId"] = reservation.AllocationId.ToString(),
        ["amount"] = ApprovalAudit.Amount(reservation.Amount),
        ["currency"] = reservation.Currency
    };
}
