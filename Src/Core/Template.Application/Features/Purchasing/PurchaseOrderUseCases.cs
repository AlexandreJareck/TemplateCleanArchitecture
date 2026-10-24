#nullable enable
using System.Globalization;
using FluentValidation;
using MediatR;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Features.Budgeting;
using Template.Application.Features.Reporting;
using Template.Application.Interfaces.Approvals;
using Template.Application.Interfaces.Budgeting;
using Template.Application.Interfaces.Files;
using Template.Application.Interfaces.Purchasing;
using Template.Application.Interfaces.Reporting;
using Template.Application.Interfaces.Requisitions;
using Template.Application.Interfaces.Sourcing;
using Template.Application.Interfaces.Suppliers;
using Template.Application.Security;
using Template.Application.Services.Procurement;
using Template.Application.Wrappers;
using Template.Domain.Approvals;
using Template.Domain.Budgeting;
using Template.Domain.Common;
using Template.Domain.Files;
using Template.Domain.Purchasing;
using Template.Domain.Reporting;
using Template.Domain.Requisitions;
using Template.Domain.Sourcing;
using Template.Domain.Suppliers;

namespace Template.Application.Features.Purchasing;

/// <summary>Promised delivery date of one line (PUR-002 editable field).</summary>
public sealed record PromisedDateChange(int LineNumber, DateOnly PromisedDeliveryDate);

/// <summary>PUR-002: delivery address, promised dates and notes of a Draft order; <c>null</c> keeps a value.</summary>
public sealed record UpdatePurchaseOrderDraftCommand(
    Guid Id, long? ExpectedVersion, string? DeliveryAddress, string? Notes, IReadOnlyList<PromisedDateChange>? PromisedDates)
    : IRequest<Result<PurchaseOrderView>>;

/// <summary>PUR-003/005/006/007: issue with the gapless number; the document and supplier notification are queued.</summary>
public sealed record IssuePurchaseOrderCommand(Guid Id, long? ExpectedVersion) : IRequest<Result<PurchaseOrderView>>;

/// <summary>PUR-008: cancellation with reason (draft: re-award; issued: nothing accepted, ProcurementManager scope).</summary>
public sealed record CancelPurchaseOrderCommand(Guid Id, long? ExpectedVersion, string? Reason) : IRequest<Result<PurchaseOrderView>>;

/// <summary>PUR-010: short close of a PartiallyReceived order with the <c>ShortClose</c> waiver reason.</summary>
public sealed record ShortClosePurchaseOrderCommand(Guid Id, long? ExpectedVersion, string? Reason) : IRequest<Result<PurchaseOrderView>>;

/// <summary>PUR-007: a failed document is generated again (new report job).</summary>
public sealed record RetryPurchaseOrderDocumentCommand(Guid Id, long? ExpectedVersion) : IRequest<Result<PurchaseOrderView>>;

/// <summary>PUR-007: a failed supplier notification is queued again.</summary>
public sealed record RetryPurchaseOrderNotificationCommand(Guid Id, long? ExpectedVersion) : IRequest<Result<PurchaseOrderView>>;

public sealed record GetPurchaseOrderQuery(Guid Id) : IRequest<Result<PurchaseOrderView>>;

public sealed record GetPurchaseOrderHistoryQuery(Guid Id) : IRequest<Result<PurchaseOrderHistoryView>>;

public sealed record DownloadPurchaseOrderDocumentQuery(Guid Id) : IRequest<Result<PurchaseOrderDocument>>;

/// <summary>Order list; visibility is always applied (PUR-011). <c>documentStatus</c>/<c>notificationStatus</c> filter the PUR-007 sub-statuses.</summary>
public sealed record ListPurchaseOrdersQuery(
    int? Page = null, int? PageSize = null, string? Sort = null, string? Status = null, Guid? SupplierId = null, Guid? RequisitionId = null,
    string? Q = null, string? DocumentStatus = null, string? NotificationStatus = null) : IRequest<Result<PagedResult<PurchaseOrderSummaryView>>>;

public sealed class UpdatePurchaseOrderDraftCommandValidator : AbstractValidator<UpdatePurchaseOrderDraftCommand>
{
    public UpdatePurchaseOrderDraftCommandValidator()
    {
        RuleFor(command => command.DeliveryAddress).MaximumLength(PurchaseOrder.DeliveryAddressMaxLength);
        RuleFor(command => command.Notes).MaximumLength(PurchaseOrder.NotesMaxLength);
        RuleFor(command => command.PromisedDates).Must(dates => dates is null || dates.Count <= 200)
            .WithMessage("At most 200 promised dates per request.");
    }
}

public sealed class PurchaseOrderHandlers(
    IPurchaseOrderRepository orders,
    IPurchaseOrderNumberAllocator numbers,
    IPurchaseRequisitionRepository requisitions,
    IBudgetAllocationRepository allocations,
    IRequestForQuotationRepository rfqs,
    IApprovalInstanceRepository approvals,
    IApproverDirectory directory,
    ISupplierRepository suppliers,
    IReportJobRepository jobs,
    IStoredFileRepository files,
    IFileStorage storage,
    PurchasingAccess access,
    ProcurementWriter writer) :
    IRequestHandler<UpdatePurchaseOrderDraftCommand, Result<PurchaseOrderView>>,
    IRequestHandler<IssuePurchaseOrderCommand, Result<PurchaseOrderView>>,
    IRequestHandler<CancelPurchaseOrderCommand, Result<PurchaseOrderView>>,
    IRequestHandler<ShortClosePurchaseOrderCommand, Result<PurchaseOrderView>>,
    IRequestHandler<RetryPurchaseOrderDocumentCommand, Result<PurchaseOrderView>>,
    IRequestHandler<RetryPurchaseOrderNotificationCommand, Result<PurchaseOrderView>>,
    IRequestHandler<GetPurchaseOrderQuery, Result<PurchaseOrderView>>,
    IRequestHandler<GetPurchaseOrderHistoryQuery, Result<PurchaseOrderHistoryView>>,
    IRequestHandler<DownloadPurchaseOrderDocumentQuery, Result<PurchaseOrderDocument>>,
    IRequestHandler<ListPurchaseOrdersQuery, Result<PagedResult<PurchaseOrderSummaryView>>>
{
    public async Task<Result<PurchaseOrderView>> Handle(UpdatePurchaseOrderDraftCommand request, CancellationToken cancellationToken)
    {
        var (order, failure) = await LoadAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        var dates = new Dictionary<int, DateOnly>();
        foreach (var change in request.PromisedDates ?? [])
        {
            if (!dates.TryAdd(change.LineNumber, change.PromisedDeliveryDate))
                return ApplicationError.Validation("promisedDates", "Each line appears at most once.");
        }

        var today = await writer.TodayAsync(cancellationToken);
        if (order!.UpdateDraft(request.DeliveryAddress, request.Notes, dates, today, writer.ActorId, writer.UtcNow, writer.CorrelationId) is { } error)
            return error.ToApplicationError();
        writer.Audit(PurchasingAudit.Updated, PurchasingAudit.Resource, order.Id, ruleIds: ["PUR-002"],
            details: new Dictionary<string, string>
            {
                ["fields"] = string.Join(',', new[]
                {
                    request.DeliveryAddress is null ? null : "deliveryAddress",
                    request.Notes is null ? null : "notes",
                    dates.Count == 0 ? null : "promisedDeliveryDate"
                }.Where(field => field is not null))
            });
        return await CommitAsync(order, null, null, null, cancellationToken);
    }

    public async Task<Result<PurchaseOrderView>> Handle(IssuePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var existing = await orders.FindAsync(request.Id, cancellationToken);
        if (existing is null || await access.ViewAsync(existing, cancellationToken) is not { } current)
            return PurchasingAudit.NotFound();
        if (existing.Number is not null)
            return current; // PUR-006: repeating the issue returns the same order and number.
        if (VersionPrecondition.Check(request.ExpectedVersion, existing.Version) is { } precondition)
            return precondition;

        var requisition = await requisitions.FindAsync(existing.RequisitionId, cancellationToken)
            ?? throw new InvalidOperationException("A purchase order references a missing requisition.");
        var supplier = await suppliers.FindAsync(existing.SupplierId, cancellationToken);
        var approval = await approvals.FindByAwardAsync(existing.AwardId, cancellationToken);
        var settings = await writer.SettingsAsync(cancellationToken);
        var now = writer.UtcNow;
        var today = ProcurementWriter.LocalDate(now, settings.TimeZone);
        var actor = writer.ActorId ?? throw new InvalidOperationException("Issuing an order needs an authenticated actor.");
        var buyerName = (await directory.GetDisplayNamesAsync([actor], cancellationToken)).GetValueOrDefault(actor);

        await writer.BeginAsync(cancellationToken);
        var order = await orders.FindForUpdateAsync(existing.Id, cancellationToken) ?? throw new InvalidOperationException("The order disappeared.");
        if (order.Number is not null)
        {
            await writer.RollbackAsync();
            return await ViewAsync(order, cancellationToken);
        }

        if (VersionPrecondition.Check(request.ExpectedVersion, order.Version) is { } lockedPrecondition)
            return await writer.FailAsync(lockedPrecondition);
        var allocation = requisition.ActiveAllocationId is { } allocationId
            ? await allocations.FindForUpdateAsync(allocationId, requisition.Id, cancellationToken)
            : null;
        var reservationCommitted = allocation?.Reservations.Any(reservation =>
            reservation.RequisitionId == requisition.Id && reservation.Status == BudgetReservationStatus.Committed) == true;
        var supplierActive = supplier?.Status == SupplierStatus.Active;
        var approved = approval?.Status == ApprovalStatus.Approved && requisition.Status == RequisitionStatus.Approved && requisition.AwardId == order.AwardId;
        if (order.CheckIssuable(supplierActive, approved, reservationCommitted) is { } guard)
            return await writer.FailAsync(guard.ToApplicationError());

        // PUR-005 / SP-2: the counter row lock is held until commit, so issues serialize and a rollback returns the number.
        var number = PurchaseOrder.FormatNumber(today.Year, await numbers.NextAsync(today.Year, cancellationToken));
        var (job, jobError) = RequestDocument(order, actor, settings.TimeZone, now);
        if (jobError is not null)
            return await writer.FailAsync(jobError.ToApplicationError());
        var (_, issueError) = order.Issue(number, today, job!.Id, supplierActive, approved, reservationCommitted, actor, now, writer.CorrelationId,
            buyerName);
        if (issueError is not null)
            return await writer.FailAsync(issueError.ToApplicationError());
        var (ordered, requisitionError) = requisition.MarkOrdered(order.Id, actor, now, writer.CorrelationId);
        if (requisitionError is not null)
            return await writer.FailAsync(requisitionError.ToApplicationError());
        jobs.Add(job);

        writer.Audit(PurchasingAudit.Issued, PurchasingAudit.Resource, order.Id, "Draft", "Issued", ruleIds: ["PUR-003", "PUR-005", "PUR-007"],
            details: new Dictionary<string, string>
            {
                ["number"] = number,
                ["supplierId"] = order.SupplierId.ToString(),
                ["total"] = PurchasingAudit.Amount(order.Total),
                ["currency"] = order.Currency,
                ["reportJobId"] = job.Id.ToString()
            });
        if (ordered)
            writer.Audit(PurchasingAudit.RequisitionOrdered, "purchase-requisition", requisition.Id, "Approved", "Ordered", ruleIds: ["PUR-003"],
                details: new Dictionary<string, string> { ["purchaseOrderId"] = order.Id.ToString(), ["number"] = requisition.Number });
        writer.Audit(ReportJobHandlers.RequestedAction, ReportJobHandlers.ReportResource, job.Id, toState: job.Status.ToString(), ruleIds: ["RPT-001", "PUR-007"],
            details: new Dictionary<string, string> { ["reportType"] = job.ReportType.ToString(), ["purchaseOrderId"] = order.Id.ToString() });
        return await CommitAsync(order, requisition, null, job, cancellationToken);
    }

    public async Task<Result<PurchaseOrderView>> Handle(CancelPurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var (loaded, failure) = await LoadAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        if (loaded!.Status == PurchaseOrderStatus.Cancelled)
            return await ViewAsync(loaded, cancellationToken);
        if (loaded.Status != PurchaseOrderStatus.Draft && !await access.MayCancelIssuedAsync(cancellationToken))
            return ProcurementWriter.Forbidden("Cancelling an issued order is limited to holders of requisitions.cancel-any (PUR-008).");

        var requisition = await requisitions.FindAsync(loaded.RequisitionId, cancellationToken)
            ?? throw new InvalidOperationException("A purchase order references a missing requisition.");
        var rfq = loaded.Status == PurchaseOrderStatus.Draft ? await rfqs.FindAsync(loaded.RfqId, cancellationToken) : null;
        var now = writer.UtcNow;
        var correlationId = writer.CorrelationId;
        await writer.BeginAsync(cancellationToken);
        var order = await orders.FindForUpdateAsync(loaded.Id, cancellationToken) ?? throw new InvalidOperationException("The order disappeared.");
        if (VersionPrecondition.Check(request.ExpectedVersion, order.Version) is { } precondition)
            return await writer.FailAsync(precondition);
        var allocation = requisition.ActiveAllocationId is { } allocationId
            ? await allocations.FindForUpdateAsync(allocationId, requisition.Id, cancellationToken)
            : null;

        var (from, changed, error) = order.Cancel(request.Reason, writer.ActorId, now, correlationId);
        if (error is not null)
            return await writer.FailAsync(error.ToApplicationError());
        if (!changed)
        {
            await writer.RollbackAsync();
            return await ViewAsync(order, cancellationToken);
        }

        var reason = order.CancellationReason;
        writer.Audit(PurchasingAudit.Cancelled, PurchasingAudit.Resource, order.Id, from.ToString(), "Cancelled", reason, ["PUR-008"],
            order.Number is null
                ? new Dictionary<string, string> { ["requisitionId"] = requisition.Id.ToString() }
                : new Dictionary<string, string> { ["requisitionId"] = requisition.Id.ToString(), ["number"] = order.Number });
        if (from == PurchaseOrderStatus.Draft)
        {
            // Draft: the award is withdrawn, the reservation returns to Reserved and the requisition to InSourcing for re-award.
            if (rfq is not null && rfq.WithdrawApprovedAward(order.AwardId, reason, writer.ActorId, now, correlationId))
                writer.Audit("award.withdrawn", "request-for-quotation", rfq.Id, "Awarded", "Closed", reason, ["PUR-008"],
                    new Dictionary<string, string> { ["awardId"] = order.AwardId.ToString(), ["purchaseOrderId"] = order.Id.ToString() });
            if (requisition.ReturnToSourcingFromOrder(order.AwardId, reason, writer.ActorId, now, correlationId))
                writer.Audit("requisition.returned-to-sourcing", "purchase-requisition", requisition.Id, "Approved", "InSourcing", reason, ["PUR-008"],
                    new Dictionary<string, string> { ["purchaseOrderId"] = order.Id.ToString(), ["number"] = requisition.Number });
            if (allocation?.Uncommit(requisition.Id, writer.ActorId, now, correlationId) is { Changed: true, Reservation: { } reservation })
                writer.Audit(BudgetMoney.ReservationUncommitted, BudgetMoney.ReservationResource, reservation.Id, "Committed", "Reserved", ruleIds: ["PUR-008"],
                    details: ReservationDetails(requisition.Id, reservation));
        }
        else
        {
            // Issued with nothing accepted: the commitment is released (OrderCancelled) and the requisition cancelled.
            var (_, requisitionChanged) = requisition.CancelForOrder(reason!, writer.ActorId, now, correlationId);
            if (requisitionChanged)
                writer.Audit("requisition.cancelled", "purchase-requisition", requisition.Id, "Ordered", "Cancelled", reason, ["PUR-008"],
                    new Dictionary<string, string> { ["purchaseOrderId"] = order.Id.ToString(), ["number"] = requisition.Number });
            if (allocation?.Release(requisition.Id, BudgetReleaseReason.OrderCancelled, writer.ActorId, now, correlationId) is { Changed: true, Reservation: { } reservation })
                writer.Audit(BudgetMoney.ReservationReleased, BudgetMoney.ReservationResource, reservation.Id, "Committed", "Released", ruleIds: ["PUR-008", "BUD-007"],
                    details: new Dictionary<string, string>(ReservationDetails(requisition.Id, reservation)) { ["releaseReason"] = nameof(BudgetReleaseReason.OrderCancelled) });
        }

        return await CommitAsync(order, requisition, allocation, null, cancellationToken, rfq);
    }

    public async Task<Result<PurchaseOrderView>> Handle(ShortClosePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var (loaded, failure) = await LoadAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        var requisition = await requisitions.FindAsync(loaded!.RequisitionId, cancellationToken)
            ?? throw new InvalidOperationException("A purchase order references a missing requisition.");
        var now = writer.UtcNow;
        var correlationId = writer.CorrelationId;
        await writer.BeginAsync(cancellationToken);
        var order = await orders.FindForUpdateAsync(loaded.Id, cancellationToken) ?? throw new InvalidOperationException("The order disappeared.");
        if (VersionPrecondition.Check(request.ExpectedVersion, order.Version) is { } precondition)
            return await writer.FailAsync(precondition);
        var allocation = requisition.ActiveAllocationId is { } allocationId
            ? await allocations.FindForUpdateAsync(allocationId, requisition.Id, cancellationToken)
            : null;
        var reservation = allocation?.Reservations.FirstOrDefault(candidate => candidate.RequisitionId == requisition.Id && candidate.IsActive);

        var (release, error) = order.ShortClose(request.Reason, reservation?.Amount ?? 0m, writer.ActorId, now, correlationId);
        if (error is not null)
            return await writer.FailAsync(error.ToApplicationError());
        if (reservation is not null && allocation!.ReleasePartially(requisition.Id, release, writer.ActorId, now, correlationId) is { Error: { } releaseError })
            return await writer.FailAsync(releaseError.ToApplicationError());
        var closed = requisition.FollowOrderCompletion(true, "ShortClosed", now, correlationId, "procurement.purchase-order.completed");

        writer.Audit(PurchasingAudit.WaiverRecorded, PurchasingAudit.WaiverResource, order.Id, reason: order.ShortCloseReason, ruleIds: ["PUR-010"],
            details: new Dictionary<string, string>
            {
                ["purchaseOrderId"] = order.Id.ToString(),
                ["waiverType"] = "ShortClose",
                ["permission"] = Permissions.PurchasingOrdersShortClose
            });
        writer.Audit(PurchasingAudit.ShortClosed, PurchasingAudit.Resource, order.Id, "PartiallyReceived", "Completed", order.ShortCloseReason, ["PUR-010", "CALC-008"],
            new Dictionary<string, string>
            {
                ["releasedAmount"] = PurchasingAudit.Amount(release.Amount),
                ["currency"] = release.Currency,
                ["shortClosedLines"] = PurchasingAudit.Text(order.Lines.Count(line => line.IsShortClosed))
            });
        if (closed)
            writer.Audit(PurchasingAudit.RequisitionClosed, "purchase-requisition", requisition.Id, "Ordered", "Closed", ruleIds: ["PUR-009", "PUR-010"],
                details: new Dictionary<string, string> { ["purchaseOrderId"] = order.Id.ToString(), ["number"] = requisition.Number });
        if (release.Amount > 0 && reservation is not null)
            writer.Audit(BudgetMoney.ReservationReleased, BudgetMoney.ReservationResource, reservation.Id, "Committed", "Committed", ruleIds: ["PUR-010", "CALC-008"],
                details: new Dictionary<string, string>(ReservationDetails(requisition.Id, reservation))
                {
                    ["releaseReason"] = nameof(BudgetReleaseReason.ShortClosed),
                    ["releasedAmount"] = PurchasingAudit.Amount(release.Amount)
                });
        return await CommitAsync(order, requisition, allocation, null, cancellationToken);
    }

    public async Task<Result<PurchaseOrderView>> Handle(RetryPurchaseOrderDocumentCommand request, CancellationToken cancellationToken)
    {
        var (order, failure) = await LoadAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        var settings = await writer.SettingsAsync(cancellationToken);
        var actor = writer.ActorId ?? throw new InvalidOperationException("Retrying a document needs an authenticated actor.");
        var now = writer.UtcNow;
        var previousJob = order!.DocumentJobId;
        var (job, jobError) = RequestDocument(order, actor, settings.TimeZone, now);
        if (jobError is not null)
            return jobError.ToApplicationError();
        if (order.RetryDocument(job!.Id, actor, now, writer.CorrelationId) is { } error)
            return error.ToApplicationError();
        jobs.Add(job);
        writer.Audit(PurchasingAudit.DocumentRetried, PurchasingAudit.Resource, order.Id, ruleIds: ["PUR-007"],
            details: new Dictionary<string, string>
            {
                ["attempt"] = PurchasingAudit.Text(order.DocumentAttempts),
                ["reportJobId"] = job.Id.ToString(),
                ["previousReportJobId"] = previousJob!.Value.ToString()
            });
        return await CommitAsync(order, null, null, job, cancellationToken);
    }

    public async Task<Result<PurchaseOrderView>> Handle(RetryPurchaseOrderNotificationCommand request, CancellationToken cancellationToken)
    {
        var (order, failure) = await LoadAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        if (order!.RetryNotification(writer.ActorId, writer.UtcNow, writer.CorrelationId) is { } error)
            return error.ToApplicationError();
        writer.Audit(PurchasingAudit.NotificationRetried, PurchasingAudit.Resource, order.Id, ruleIds: ["PUR-007"],
            details: new Dictionary<string, string> { ["attempt"] = PurchasingAudit.Text(order.NotificationAttempts) });
        return await CommitAsync(order, null, null, null, cancellationToken);
    }

    public async Task<Result<PurchaseOrderView>> Handle(GetPurchaseOrderQuery request, CancellationToken cancellationToken)
    {
        var order = await orders.FindAsync(request.Id, cancellationToken);
        return order is not null && await access.ViewAsync(order, cancellationToken) is { } view ? view : PurchasingAudit.NotFound();
    }

    public async Task<Result<PurchaseOrderHistoryView>> Handle(GetPurchaseOrderHistoryQuery request, CancellationToken cancellationToken)
    {
        var order = await orders.FindAsync(request.Id, cancellationToken);
        if (order is null || !(await access.VisibilityAsync(cancellationToken)).CanRead(order))
            return PurchasingAudit.NotFound();
        return PurchaseOrderMapping.ToHistory(order);
    }

    /// <summary>PUR-007: streams the exact issued document; prices are on it, so <c>purchasing.orders.read</c> is required (PUR-011).</summary>
    public async Task<Result<PurchaseOrderDocument>> Handle(DownloadPurchaseOrderDocumentQuery request, CancellationToken cancellationToken)
    {
        var order = await orders.FindAsync(request.Id, cancellationToken);
        if (order is null || !(await access.VisibilityAsync(cancellationToken)).ReadAll)
            return PurchasingAudit.NotFound();
        if (order.DocumentStatus != PurchaseOrderDocumentStatus.Ready || order.DocumentFileId is not { } fileId)
            return PurchasingErrors.DocumentNotReady(order.DocumentStatus.ToString()).ToApplicationError();
        var file = await files.FindAsync(fileId, cancellationToken);
        if (file is null || !file.IsDownloadable(writer.UtcNow))
            return PurchasingErrors.DocumentNotReady(file?.Status.ToString() ?? "Missing").ToApplicationError();
        Stream? content;
        try
        {
            content = await storage.OpenReadAsync(file.Id, cancellationToken);
        }
        catch (FileStorageUnavailableException)
        {
            content = null;
        }

        if (content is null)
            return new ApplicationError(ProblemCodes.FileUnavailable, ErrorCategory.Unavailable, "The stored document is temporarily unavailable.", "FIL-003");
        writer.Audit(PurchasingAudit.DocumentDownloaded, PurchasingAudit.Resource, order.Id, ruleIds: ["PUR-007", "PUR-011"],
            details: new Dictionary<string, string> { ["fileId"] = file.Id.ToString() });
        if (await writer.CommitAsync(_ => Task.FromResult<long?>(null), null, cancellationToken) is { } commit)
        {
            await content.DisposeAsync();
            return commit;
        }

        return new PurchaseOrderDocument(content, file.OriginalName, file.MediaType, file.Length ?? 0, order.DocumentTemplateVersion ?? string.Empty);
    }

    public async Task<Result<PagedResult<PurchaseOrderSummaryView>>> Handle(ListPurchaseOrdersQuery request, CancellationToken cancellationToken)
    {
        var page = PageRequest.Create(request.Page, request.PageSize, request.Sort, IPurchaseOrderRepository.SortFields, "-updatedAt");
        if (!page.IsSuccess)
            return page.Error!;
        var statuses = ProcurementQueries.ParseEnumList<PurchaseOrderStatus>(request.Status, "status");
        if (!statuses.IsSuccess)
            return statuses.Error!;
        var documents = ProcurementQueries.ParseEnumList<PurchaseOrderDocumentStatus>(request.DocumentStatus, "documentStatus");
        if (!documents.IsSuccess)
            return documents.Error!;
        var notifications = ProcurementQueries.ParseEnumList<PurchaseOrderNotificationStatus>(request.NotificationStatus, "notificationStatus");
        if (!notifications.IsSuccess)
            return notifications.Error!;
        var visibility = await access.VisibilityAsync(cancellationToken);
        var filter = new PurchaseOrderFilter(visibility, statuses.Value, request.SupplierId, request.RequisitionId,
            string.IsNullOrWhiteSpace(request.Q) ? null : request.Q.Trim(), documents.Value, notifications.Value);
        var result = await orders.ListAsync(filter, page.Value!, cancellationToken);
        return new PagedResult<PurchaseOrderSummaryView>(
            result.Items.Select(order => PurchaseOrderMapping.ToSummary(order, visibility.ReadAll)).ToArray(), result.Page, result.PageSize, result.TotalItems);
    }

    /// <summary>The Phase 30 job of the issued document (D32-4): system-only type, as-of the issue instant, no expiring output.</summary>
    private static (ReportJob? Job, DomainError? Error) RequestDocument(PurchaseOrder order, Guid actor, string timeZone, DateTime nowUtc)
    {
        var parameters = new ReportParameters(PurchaseOrderId: order.Id).ToCanonicalJson();
        var hash = ReportParameters.Fingerprint(ReportType.PurchaseOrderPdf,
            parameters + "\n" + (order.DocumentAttempts + 1).ToString(CultureInfo.InvariantCulture));
        return ReportJob.Request(Guid.NewGuid(), ReportType.PurchaseOrderPdf, actor, parameters, hash, nowUtc, timeZone, ReportPolicy.V1, nowUtc);
    }

    private async Task<(PurchaseOrder? Order, ApplicationError? Error)> LoadAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken)
    {
        var order = await orders.FindAsync(id, cancellationToken);
        if (order is null || !(await access.VisibilityAsync(cancellationToken)).CanRead(order))
            return (null, PurchasingAudit.NotFound());
        return VersionPrecondition.Check(expectedVersion, order.Version) is { } precondition ? (null, precondition) : (order, null);
    }

    private async Task<Result<PurchaseOrderView>> ViewAsync(PurchaseOrder order, CancellationToken cancellationToken) =>
        await access.ViewAsync(order, cancellationToken) is { } view ? view : PurchasingAudit.NotFound();

    private async Task<Result<PurchaseOrderView>> CommitAsync(
        PurchaseOrder order, PurchaseRequisition? requisition, BudgetAllocation? allocation, ReportJob? job, CancellationToken cancellationToken,
        RequestForQuotation? rfq = null)
    {
        var events = writer.Stage(order.DomainEvents.Concat(requisition?.DomainEvents ?? []).Concat(allocation?.DomainEvents ?? [])
            .Concat(job?.DomainEvents ?? []).Concat(rfq?.DomainEvents ?? []).ToArray());
        var commit = await writer.CommitAsync(token => orders.GetVersionAsync(order.Id, token), UniqueViolation, cancellationToken);
        if (commit is not null)
            return commit;

        writer.Record(events);
        order.ClearDomainEvents();
        requisition?.ClearDomainEvents();
        allocation?.ClearDomainEvents();
        job?.ClearDomainEvents();
        rfq?.ClearDomainEvents();
        return await ViewAsync(order, cancellationToken);
    }

    private static ApplicationError? UniqueViolation(string constraint) =>
        constraint == IPurchaseOrderRepository.NumberUniqueKey
            ? new ApplicationError(ProblemCodes.VersionMismatch, ErrorCategory.PreconditionFailed, "A concurrent issue won; read the order again.")
            : null;

    internal static IReadOnlyDictionary<string, string> ReservationDetails(Guid requisitionId, BudgetReservation reservation) => new Dictionary<string, string>
    {
        ["requisitionId"] = requisitionId.ToString(),
        ["allocationId"] = reservation.AllocationId.ToString(),
        ["amount"] = PurchasingAudit.Amount(reservation.Amount),
        ["currency"] = reservation.Currency
    };
}
