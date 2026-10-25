#nullable enable
using System.Globalization;
using MediatR;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Results;
using Template.Application.Features.Approvals;
using Template.Application.Features.Budgeting;
using Template.Application.Interfaces.Access;
using Template.Application.Features.Purchasing;
using Template.Application.Interfaces.Budgeting;
using Template.Application.Interfaces.Purchasing;
using Template.Application.Interfaces.Organization;
using Template.Application.Interfaces.Requisitions;
using Template.Application.Interfaces.Sourcing;
using Template.Application.Services.MasterData;
using Template.Application.Services.Procurement;
using Template.Domain.Budgeting;
using Template.Domain.Common;
using Template.Domain.Purchasing;
using Template.Domain.Requisitions;
using Template.Domain.Sourcing;

namespace Template.Application.Features.Requisitions;

/// <summary>
/// <c>Draft → Submitted</c> with the budget reservation in one transaction (REQ-004, REQ-005, BUD-004..008). On any
/// failure the requisition stays <c>Draft</c> unchanged and nothing is reserved. Permission <c>requisitions.submit</c> (own).
/// </summary>
public sealed record SubmitRequisitionCommand(Guid Id, long? ExpectedVersion) : IRequest<Result<RequisitionView>>;

/// <summary><c>Submitted → Draft</c>, revision + 1, reservation released as <c>Withdrawn</c> exactly once (REQ-007, BUD-007).</summary>
public sealed record WithdrawRequisitionCommand(Guid Id, long? ExpectedVersion) : IRequest<Result<RequisitionView>>;

/// <summary>Cancellation with reason; releases the reservation as <c>Cancelled</c> exactly once (REQ-008, BUD-007).</summary>
public sealed record CancelRequisitionCommand(Guid Id, long? ExpectedVersion, string? Reason) : IRequest<Result<RequisitionView>>;

/// <summary>REQ-012: copies a terminal requisition into a new draft of the actor.</summary>
public sealed record CopyRequisitionCommand(Guid Id) : IRequest<Result<RequisitionView>>;

public sealed class RequisitionTransitionHandlers(
    IPurchaseRequisitionRepository requisitions,
    IBudgetAllocationRepository allocations,
    IFiscalPeriodRepository periods,
    ICostCenterRepository costCenters,
    IRequisitionReferenceReader references,
    IRequisitionNumberAllocator numbers,
    IAccountDirectory accounts,
    IRequestForQuotationRepository rfqs,
    IPurchaseOrderRepository orders,
    RequisitionAccess access,
    ApprovalCascade approvals,
    ProcurementWriter writer) :
    IRequestHandler<SubmitRequisitionCommand, Result<RequisitionView>>,
    IRequestHandler<WithdrawRequisitionCommand, Result<RequisitionView>>,
    IRequestHandler<CancelRequisitionCommand, Result<RequisitionView>>,
    IRequestHandler<CopyRequisitionCommand, Result<RequisitionView>>
{
    public async Task<Result<RequisitionView>> Handle(SubmitRequisitionCommand request, CancellationToken cancellationToken)
    {
        var (requisition, failure) = await LoadAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        if (!await access.CanSubmitAsync(requisition!, cancellationToken))
            return ProcurementWriter.Forbidden("Only the requester may submit the requisition.");

        var settings = await writer.SettingsAsync(cancellationToken);
        var now = writer.UtcNow;
        var today = ProcurementWriter.LocalDate(now, settings.TimeZone);
        var costCenter = await costCenters.FindAsync(requisition!.CostCenterId, cancellationToken)
            ?? throw new InvalidOperationException("A requisition references a missing cost center.");
        var referenceIds = requisition.Lines
            .SelectMany(line => new[] { line.CatalogItemId, line.CategoryId, line.UnitOfMeasureId })
            .OfType<Guid>().Distinct().ToArray();
        var context = new SubmissionContext(
            writer.ActorId, now, today, await accounts.IsActiveUserAsync(requisition.RequesterId, cancellationToken),
            new CostCenterSnapshot(costCenter.Id, costCenter.Code, costCenter.Name, costCenter.DepartmentId, costCenter.IsActive),
            await references.GetActiveStatesAsync(referenceIds, cancellationToken), writer.CorrelationId, RequisitionPolicy.V1);
        if (requisition.CheckSubmittable(context) is { } notSubmittable)
            return NotSubmittable(notSubmittable);

        // BUD-006: the period is read FOR SHARE (a concurrent close waits) and the allocation FOR UPDATE, so competing
        // submissions on one allocation are serialized and the loser sees the reduced availability.
        await writer.BeginAsync(cancellationToken);
        var period = await periods.FindOpenContainingForShareAsync(today, cancellationToken);
        var allocationId = period is null ? null : await allocations.FindIdAsync(period.Id, requisition.CostCenterId, requisition.Currency, cancellationToken);
        var allocation = allocationId is null ? null : await allocations.FindForUpdateAsync(allocationId.Value, requisition.Id, cancellationToken);
        if (period is null || allocation is null)
        {
            writer.LogBudgetRejection(BudgetErrors.NoOpenAllocationCode, requisition.Currency);
            return await writer.FailAsync(BudgetErrors.NoOpenAllocation.ToApplicationError());
        }

        var (reservation, _, reserveError) = allocation.Reserve(
            Guid.NewGuid(), requisition.Id, requisition.Revision, requisition.EstimatedTotalMoney, period, today, writer.ActorId, now, writer.CorrelationId);
        if (reserveError is not null)
        {
            writer.LogBudgetRejection(reserveError.Code, requisition.Currency);
            return await writer.FailAsync(reserveError.ToApplicationError());
        }

        if (requisition.Submit(context, Guid.NewGuid(), reservation!.Id, allocation.Id) is { } submitError)
            return await writer.FailAsync(NotSubmittable(submitError));

        writer.Audit(RequisitionAudit.Submitted, RequisitionAudit.Resource, requisition.Id, "Draft", "Submitted", ruleIds: ["REQ-004", "REQ-005"],
            details: new Dictionary<string, string>
            {
                ["number"] = requisition.Number,
                ["revision"] = Text(requisition.Revision),
                ["amount"] = Amount(requisition.EstimatedTotal),
                ["currency"] = requisition.Currency,
                ["reservationId"] = reservation.Id.ToString()
            });
        writer.Audit(BudgetMoney.ReservationCreated, BudgetMoney.ReservationResource, reservation.Id, toState: "Reserved",
            ruleIds: ["BUD-004", "BUD-005", "BUD-006", "BUD-008"],
            details: new Dictionary<string, string>
            {
                ["requisitionId"] = requisition.Id.ToString(),
                ["allocationId"] = allocation.Id.ToString(),
                ["revision"] = Text(requisition.Revision),
                ["amount"] = Amount(reservation.Amount),
                ["currency"] = reservation.Currency
            });
        return await CommitAsync(requisition, allocation, cancellationToken);
    }

    public async Task<Result<RequisitionView>> Handle(WithdrawRequisitionCommand request, CancellationToken cancellationToken)
    {
        var (requisition, failure) = await LoadAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        if (!await access.CanSubmitAsync(requisition!, cancellationToken))
            return ProcurementWriter.Forbidden("Only the requester may withdraw the requisition.");
        if (requisition!.Status != RequisitionStatus.Submitted)
            return DomainError.InvalidTransition(requisition.Status.ToString(), "Only a Submitted requisition can be withdrawn.", "REQ-007").ToApplicationError();

        return await ReleaseAndTransitionAsync(
            requisition, BudgetReleaseReason.Withdrawn,
            () => requisition.Withdraw(writer.ActorId, writer.UtcNow, writer.CorrelationId),
            RequisitionAudit.Withdrawn, "Submitted", "Draft", null, ["REQ-007", "BUD-007"], cancellationToken);
    }

    public async Task<Result<RequisitionView>> Handle(CancelRequisitionCommand request, CancellationToken cancellationToken)
    {
        var (requisition, failure) = await LoadAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        if (await access.CancellationAuthorityAsync(requisition!, cancellationToken) is not { } authority)
            return ProcurementWriter.Forbidden("Cancelling this requisition requires requisitions.cancel-own on an own requisition or requisitions.cancel-any.");

        var from = requisition!.Status.ToString();
        return await ReleaseAndTransitionAsync(
            requisition, BudgetReleaseReason.Cancelled,
            () => requisition.Cancel(authority, request.Reason, writer.ActorId, writer.UtcNow, writer.CorrelationId),
            RequisitionAudit.Cancelled, from, "Cancelled", MasterDataRules.NormalizeReason(request.Reason), ["REQ-008", "BUD-007"], cancellationToken);
    }

    public async Task<Result<RequisitionView>> Handle(CopyRequisitionCommand request, CancellationToken cancellationToken)
    {
        if (writer.ActorId is not { } actor)
            return ProcurementWriter.Forbidden();
        var source = await requisitions.FindAsync(request.Id, cancellationToken);
        if (source is null || !await access.CanReadAsync(source, cancellationToken))
            return RequisitionAudit.NotFound();
        var costCenter = await costCenters.FindAsync(source.CostCenterId, cancellationToken)
            ?? throw new InvalidOperationException("A requisition references a missing cost center.");
        var settings = await writer.SettingsAsync(cancellationToken);
        var today = ProcurementWriter.LocalDate(writer.UtcNow, settings.TimeZone);
        if (!source.IsTerminal)
            return DomainError.InvalidTransition(source.Status.ToString(), "Only a terminal requisition can be copied.", "REQ-012").ToApplicationError();

        var number = PurchaseRequisition.FormatNumber(today.Year, await numbers.NextAsync(today.Year, cancellationToken));
        var (copy, error) = PurchaseRequisition.CopyAsNewDraft(
            source, Guid.NewGuid(), number, actor,
            new CostCenterSnapshot(costCenter.Id, costCenter.Code, costCenter.Name, costCenter.DepartmentId, costCenter.IsActive),
            settings.SupportedCurrencies.Contains(source.Currency), actor, writer.UtcNow, writer.CorrelationId);
        if (error is not null)
            return error.ToApplicationError();

        requisitions.Add(copy!);
        writer.Audit(RequisitionAudit.Copied, RequisitionAudit.Resource, copy!.Id, toState: "Draft", ruleIds: ["REQ-012"],
            details: new Dictionary<string, string> { ["number"] = copy.Number, ["requisitionId"] = source.Id.ToString() });
        var commit = await writer.CommitAsync(_ => Task.FromResult<long?>(null), null, cancellationToken);
        return commit is null ? await access.ViewAsync(copy, cancellationToken) : commit;
    }

    /// <summary>
    /// Shared release path of withdraw/cancel (BUD-007): the allocation of the active reservation is locked FOR UPDATE,
    /// the requisition transition runs, the reservation is released (a repeated release is a no-op) and both audits are
    /// written in the same commit. A racing second request loses on the requisition version (412) and rolls back.
    /// </summary>
    private async Task<Result<RequisitionView>> ReleaseAndTransitionAsync(
        PurchaseRequisition requisition,
        BudgetReleaseReason releaseReason,
        Func<(Guid? AllocationId, DomainError? Error)> transition,
        string auditAction,
        string fromState,
        string toState,
        string? reason,
        string[] ruleIds,
        CancellationToken cancellationToken)
    {
        var allocationId = requisition.ActiveAllocationId;
        await writer.BeginAsync(cancellationToken);
        var allocation = allocationId is null ? null : await allocations.FindForUpdateAsync(allocationId.Value, requisition.Id, cancellationToken);

        var (_, error) = transition();
        if (error is not null)
            return await writer.FailAsync(error.ToApplicationError());
        var released = allocation?.Release(requisition.Id, releaseReason, writer.ActorId, writer.UtcNow, writer.CorrelationId);

        writer.Audit(auditAction, RequisitionAudit.Resource, requisition.Id, fromState, toState, reason, ruleIds,
            new Dictionary<string, string> { ["number"] = requisition.Number, ["revision"] = Text(requisition.Revision) });
        AuditRelease(requisition, released, releaseReason);
        // REQ-007 (a draft RFQ is cancelled on withdrawal) and REQ-008 (any open RFQ and pending award are cancelled).
        var rfq = await CancelActiveRfqAsync(
            requisition, releaseReason == BudgetReleaseReason.Withdrawn ? "RequisitionWithdrawn" : "RequisitionCancelled",
            releaseReason == BudgetReleaseReason.Withdrawn ? "REQ-007" : "REQ-008", cancellationToken);
        // Phase 31 (REQ-008): a pending approval of the cancelled award ends Cancelled in the same commit.
        var cascade = releaseReason == BudgetReleaseReason.Cancelled
            ? await approvals.CancelForRequisitionAsync(requisition.AwardId, cancellationToken)
            : ApprovalCascadeResult.None;
        // Phase 32 (REQ-008, PUR-008): the draft order of an Approved requisition is cancelled in the same commit.
        var order = releaseReason == BudgetReleaseReason.Cancelled ? await CancelDraftOrderAsync(requisition, reason, cancellationToken) : null;
        return await CommitAsync(requisition, allocation, cancellationToken, rfq, cascade, order);
    }

    private async Task<PurchaseOrder?> CancelDraftOrderAsync(PurchaseRequisition requisition, string? reason, CancellationToken cancellationToken)
    {
        if (requisition.PurchaseOrderId is not { } id || await orders.FindAsync(id, cancellationToken) is not { Status: PurchaseOrderStatus.Draft } order)
            return null;
        var (_, changed, _) = order.Cancel(reason, writer.ActorId, writer.UtcNow, writer.CorrelationId);
        if (!changed)
            return null;
        writer.Audit(PurchasingAudit.Cancelled, PurchasingAudit.Resource, order.Id, "Draft", "Cancelled", reason, ["REQ-008", "PUR-008"],
            new Dictionary<string, string> { ["requisitionId"] = requisition.Id.ToString(), ["reasonCode"] = "RequisitionCancelled" });
        return order;
    }

    private Task<RequestForQuotation?> CancelActiveRfqAsync(PurchaseRequisition requisition, string reasonCode, string ruleId, CancellationToken cancellationToken) =>
        RequisitionCascades.CancelActiveRfqAsync(rfqs, writer, requisition, reasonCode, ruleId, cancellationToken);

    private void AuditRelease(PurchaseRequisition requisition, (BudgetReservation? Reservation, bool Changed)? released, BudgetReleaseReason reason) =>
        RequisitionCascades.AuditRelease(writer, requisition, released, reason);

    /// <summary>404 when missing or unreadable (REQ-011), then 428/412.</summary>
    private async Task<(PurchaseRequisition? Requisition, ApplicationError? Error)> LoadAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken)
    {
        var requisition = await requisitions.FindAsync(id, cancellationToken);
        if (requisition is null || !await access.CanReadAsync(requisition, cancellationToken))
            return (null, RequisitionAudit.NotFound());
        return VersionPrecondition.Check(expectedVersion, requisition.Version) is { } precondition ? (null, precondition) : (requisition, null);
    }

    private async Task<Result<RequisitionView>> CommitAsync(
        PurchaseRequisition requisition, BudgetAllocation? allocation, CancellationToken cancellationToken, RequestForQuotation? rfq = null,
        ApprovalCascadeResult? cascade = null, PurchaseOrder? order = null)
    {
        var events = writer.Stage(Events(requisition, allocation, rfq).Concat(order?.DomainEvents ?? []).ToArray());
        var commit = await writer.CommitAsync(token => requisitions.GetVersionAsync(requisition.Id, token), null, cancellationToken);
        if (commit is not null)
            return commit;

        writer.Record(events);
        ClearEvents(requisition, allocation, rfq);
        order?.ClearDomainEvents();
        if (cascade is not null)
            await approvals.CompleteAsync(cascade, cancellationToken);
        return await access.ViewAsync(requisition, cancellationToken);
    }

    private static void ClearEvents(PurchaseRequisition requisition, BudgetAllocation? allocation, RequestForQuotation? rfq) =>
        RequisitionCascades.ClearEvents(requisition, allocation, rfq);

    private static IEnumerable<DomainEvent> Events(PurchaseRequisition requisition, BudgetAllocation? allocation, RequestForQuotation? rfq = null) =>
        RequisitionCascades.Events(requisition, allocation, rfq);

    /// <summary>REQ-004: the failed guards travel as the <c>fields</c> extension (string array).</summary>
    private static ApplicationError NotSubmittable(DomainError error)
    {
        var mapped = error.ToApplicationError();
        if (error.Details?.TryGetValue("fields", out var fields) != true)
            return mapped;
        return mapped with
        {
            Extensions = new Dictionary<string, object?> { ["fields"] = fields!.Split(',', StringSplitOptions.RemoveEmptyEntries) }
        };
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Amount(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
