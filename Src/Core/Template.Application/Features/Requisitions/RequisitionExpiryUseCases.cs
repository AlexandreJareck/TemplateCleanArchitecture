#nullable enable
using System.Globalization;
using MediatR;
using Template.Application.Common.Results;
using Template.Application.Features.Budgeting;
using Template.Application.Interfaces.Budgeting;
using Template.Application.Interfaces.Requisitions;
using Template.Application.Interfaces.Sourcing;
using Template.Application.Services.Procurement;
using Template.Domain.Budgeting;
using Template.Domain.Common;
using Template.Domain.Requisitions;
using Template.Domain.Sourcing;

namespace Template.Application.Features.Requisitions;

/// <summary>
/// REQ-009 system transition (Worker scheduled sweep, D33-1): expires one due requisition and releases its reservation
/// as <c>Expired</c>. Idempotent; retries an optimistic conflict with fresh state.
/// </summary>
public sealed record ExpireRequisitionCommand(Guid Id) : IRequest<Result<bool>>;

/// <summary>REQ-009 sweep: expires up to <see cref="Max"/> due requisitions; returns how many changed.</summary>
public sealed record ExpireDueRequisitionsCommand(int Max = 100) : IRequest<Result<int>>;

/// <summary>
/// System-actor expiry, separate from the user transitions so a scheduled host needs only the requisition, budget and
/// RFQ ports (no account directory, access or approval cascade).
/// </summary>
public sealed class RequisitionExpiryHandlers(
    IPurchaseRequisitionRepository requisitions,
    IBudgetAllocationRepository allocations,
    IRequestForQuotationRepository rfqs,
    ProcurementWriter writer) :
    IRequestHandler<ExpireRequisitionCommand, Result<bool>>,
    IRequestHandler<ExpireDueRequisitionsCommand, Result<int>>
{
    private const int SystemRetries = 3;

    public async Task<Result<bool>> Handle(ExpireRequisitionCommand request, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var requisition = await requisitions.FindAsync(request.Id, cancellationToken);
            if (requisition is null)
                return RequisitionAudit.NotFound();
            var settings = await writer.SettingsAsync(cancellationToken);
            var now = writer.UtcNow;
            var today = ProcurementWriter.LocalDate(now, settings.TimeZone);
            var allocationId = requisition.ActiveAllocationId;
            var from = requisition.Status.ToString();

            await writer.BeginAsync(cancellationToken);
            var allocation = allocationId is null ? null : await allocations.FindForUpdateAsync(allocationId.Value, requisition.Id, cancellationToken);
            var (_, changed, error) = requisition.Expire(today, RequisitionPolicy.V1, now, writer.CorrelationId);
            if (error is not null)
                return await writer.FailAsync(error.ToApplicationError());
            if (!changed)
            {
                await writer.RollbackAsync();
                return false;
            }

            var released = allocation?.Release(requisition.Id, BudgetReleaseReason.Expired, null, now, writer.CorrelationId);
            writer.Audit(RequisitionAudit.Expired, RequisitionAudit.Resource, requisition.Id, from, "Expired", ruleIds: ["REQ-009", "BUD-007"],
                details: new Dictionary<string, string> { ["revision"] = requisition.Revision.ToString(CultureInfo.InvariantCulture) });
            RequisitionCascades.AuditRelease(writer, requisition, released, BudgetReleaseReason.Expired);
            var rfq = await RequisitionCascades.CancelActiveRfqAsync(rfqs, writer, requisition, "RequisitionExpired", "REQ-009", cancellationToken);

            var events = writer.Stage(RequisitionCascades.Events(requisition, allocation, rfq));
            var commit = await writer.CommitAsync(token => requisitions.GetVersionAsync(requisition.Id, token), null, cancellationToken);
            if (commit is null)
            {
                writer.Record(events);
                RequisitionCascades.ClearEvents(requisition, allocation, rfq);
                return true;
            }

            if (attempt >= SystemRetries || commit.Category != Wrappers.ErrorCategory.PreconditionFailed)
                return commit;
        }
    }

    public async Task<Result<int>> Handle(ExpireDueRequisitionsCommand request, CancellationToken cancellationToken)
    {
        var settings = await writer.SettingsAsync(cancellationToken);
        var today = ProcurementWriter.LocalDate(writer.UtcNow, settings.TimeZone);
        var due = await requisitions.ListDueForExpiryAsync(today.AddDays(-RequisitionPolicy.V1.ExpiryGraceDays), Math.Clamp(request.Max, 1, 500), cancellationToken);
        var expired = 0;
        foreach (var id in due)
        {
            var result = await Handle(new ExpireRequisitionCommand(id), cancellationToken);
            if (result.IsSuccess && result.Value)
                expired++;
        }

        return expired;
    }
}

/// <summary>Cascade and audit steps shared by the user transitions and the system expiry of a requisition.</summary>
internal static class RequisitionCascades
{
    /// <summary>Cancels the requisition's non-terminal RFQ in the same commit (idempotent cascade).</summary>
    public static async Task<RequestForQuotation?> CancelActiveRfqAsync(
        IRequestForQuotationRepository rfqs, ProcurementWriter writer, PurchaseRequisition requisition, string reasonCode, string ruleId,
        CancellationToken cancellationToken)
    {
        var rfq = await rfqs.FindActiveByRequisitionAsync(requisition.Id, cancellationToken);
        if (rfq is null)
            return null;
        var from = rfq.Status.ToString();
        if (!rfq.CancelForRequisition(reasonCode, writer.ActorId, writer.UtcNow, writer.CorrelationId))
            return null;
        writer.Audit("rfq.cancelled", "request-for-quotation", rfq.Id, from, "Cancelled", reasonCode, [ruleId, "SRC-020"],
            new Dictionary<string, string> { ["requisitionId"] = requisition.Id.ToString(), ["code"] = reasonCode });
        return rfq;
    }

    public static void AuditRelease(
        ProcurementWriter writer, PurchaseRequisition requisition, (BudgetReservation? Reservation, bool Changed)? released, BudgetReleaseReason reason)
    {
        if (released is not { Changed: true, Reservation: { } reservation })
            return;
        writer.Audit(BudgetMoney.ReservationReleased, BudgetMoney.ReservationResource, reservation.Id, "Reserved", "Released", ruleIds: ["BUD-007"],
            details: new Dictionary<string, string>
            {
                ["requisitionId"] = requisition.Id.ToString(),
                ["allocationId"] = reservation.AllocationId.ToString(),
                ["amount"] = reservation.Amount.ToString(CultureInfo.InvariantCulture),
                ["currency"] = reservation.Currency,
                ["releaseReason"] = reason.ToString()
            });
    }

    /// <summary>Committed events were staged once; clearing them prevents a second Outbox row if the instance is reused.</summary>
    public static void ClearEvents(PurchaseRequisition requisition, BudgetAllocation? allocation, RequestForQuotation? rfq)
    {
        requisition.ClearDomainEvents();
        allocation?.ClearDomainEvents();
        rfq?.ClearDomainEvents();
    }

    public static IEnumerable<DomainEvent> Events(PurchaseRequisition requisition, BudgetAllocation? allocation, RequestForQuotation? rfq = null) =>
        requisition.DomainEvents.Concat(allocation?.DomainEvents ?? []).Concat(rfq?.DomainEvents ?? []).ToArray();
}
