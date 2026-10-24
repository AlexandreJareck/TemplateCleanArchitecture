#nullable enable
using System.Globalization;
using FluentValidation;
using MediatR;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Interfaces.Purchasing;
using Template.Application.Interfaces.Requisitions;
using Template.Application.Security;
using Template.Application.Services.Procurement;
using Template.Domain.Common;
using Template.Domain.Purchasing;
using Template.Domain.Receiving;
using Template.Domain.Requisitions;

namespace Template.Application.Features.Purchasing;

/// <summary>Quantities travel as invariant decimal strings (like quotes); the discrepancy type is required when rejected &gt; 0.</summary>
public sealed record ReceiptLineRequest(int PoLineNumber, string? Received, string? Accepted, string? Rejected, string? DiscrepancyType, string? DiscrepancyNotes);

public sealed record ReceiptAttachmentRequest(string? FileName, string? MediaType, long Length);

/// <summary>RCV-001..005/008: posts a receipt of an issued order; the receiver is the authenticated actor.</summary>
public sealed record PostReceiptCommand(
    Guid PurchaseOrderId,
    DateOnly? ReceivedOn,
    string? Location,
    string? DeliveryReference,
    string? Notes,
    IReadOnlyList<ReceiptLineRequest>? Lines,
    IReadOnlyList<ReceiptAttachmentRequest>? Attachments) : IRequest<Result<ReceiptPostingView>>;

/// <summary>RCV-007: a compensating accepted-quantity delta (signed decimal string) with reason.</summary>
public sealed record CorrectReceiptCommand(Guid ReceiptId, long? ExpectedVersion, int PoLineNumber, string? AcceptedDelta, string? Reason)
    : IRequest<Result<ReceiptPostingView>>;

public sealed record GetReceiptQuery(Guid Id) : IRequest<Result<ReceiptView>>;

public sealed record ListReceiptsQuery(
    int? Page = null, int? PageSize = null, string? Sort = null, Guid? PurchaseOrderId = null, bool? HasDiscrepancy = null, string? Q = null,
    DateOnly? ReceivedFrom = null, DateOnly? ReceivedTo = null) : IRequest<Result<PagedResult<ReceiptView>>>;

public sealed class PostReceiptCommandValidator : AbstractValidator<PostReceiptCommand>
{
    public PostReceiptCommandValidator()
    {
        RuleFor(command => command.PurchaseOrderId).NotEmpty();
        RuleFor(command => command.ReceivedOn).NotNull();
        RuleFor(command => command.Lines).NotEmpty();
        RuleFor(command => command.Lines!.Count).LessThanOrEqualTo(Receipt.MaxLines).When(command => command.Lines is not null);
    }
}

public sealed class CorrectReceiptCommandValidator : AbstractValidator<CorrectReceiptCommand>
{
    public CorrectReceiptCommandValidator()
    {
        RuleFor(command => command.PoLineNumber).GreaterThan(0);
        RuleFor(command => command.AcceptedDelta).NotEmpty().MaximumLength(30);
    }
}

/// <summary>
/// Receiving use cases (workflows §5). Posting and correcting take the order row lock inside one transaction so the
/// accepted totals, the RCV-003 cap and the PUR-009 completion are evaluated against the latest receipts; the receipt,
/// the order, the requisition, audit records and Outbox events commit together.
/// </summary>
public sealed class ReceiptHandlers(
    IReceiptRepository receipts,
    IReceiptNumberAllocator numbers,
    IPurchaseOrderRepository orders,
    IPurchaseRequisitionRepository requisitions,
    PurchasingAccess access,
    ProcurementWriter writer) :
    IRequestHandler<PostReceiptCommand, Result<ReceiptPostingView>>,
    IRequestHandler<CorrectReceiptCommand, Result<ReceiptPostingView>>,
    IRequestHandler<GetReceiptQuery, Result<ReceiptView>>,
    IRequestHandler<ListReceiptsQuery, Result<PagedResult<ReceiptView>>>
{
    public async Task<Result<ReceiptPostingView>> Handle(PostReceiptCommand request, CancellationToken cancellationToken)
    {
        if (writer.ActorId is not { } receiver)
            return ProcurementWriter.Forbidden();
        var existing = await orders.FindAsync(request.PurchaseOrderId, cancellationToken);
        if (existing is null || !(await access.VisibilityAsync(cancellationToken)).CanRead(existing))
            return PurchasingAudit.NotFound();
        var (lines, lineError) = ParseLines(request.Lines ?? []);
        if (lineError is not null)
            return lineError;

        var settings = await writer.SettingsAsync(cancellationToken);
        var now = writer.UtcNow;
        var today = ProcurementWriter.LocalDate(now, settings.TimeZone);
        var number = Receipt.FormatNumber(today.Year, await numbers.NextAsync(today.Year, cancellationToken));
        var policy = ReceivingPolicy.V1;

        await writer.BeginAsync(cancellationToken);
        var order = await orders.FindForUpdateAsync(existing.Id, cancellationToken) ?? throw new InvalidOperationException("The order disappeared.");
        if (!order.IsReceivable || order.IssueDate is not { } issueDate || order.Number is null)
            return await writer.FailAsync(PurchasingErrors.OrderNotReceivable.ToApplicationError());
        var posting = new ReceiptPosting(
            order.Id, order.Number, order.Lines.Select(line => new ReceiptOrderLine(line.LineNumber, line.Description, line.UnitCode, line.UnitAllowedDecimals)).ToArray(),
            issueDate, today, receiver, request.ReceivedOn ?? default, request.Location, request.DeliveryReference, request.Notes, lines!,
            (request.Attachments ?? []).Select(attachment => new ReceiptAttachmentInput(attachment.FileName, attachment.MediaType, attachment.Length)).ToArray());
        var (receipt, receiptError) = Receipt.Post(Guid.NewGuid(), number, posting, policy, now, writer.CorrelationId);
        if (receiptError is not null)
            return await writer.FailAsync(receiptError.ToApplicationError());
        var from = order.Status.ToString();
        var (change, orderError) = order.ApplyReceipt(
            receipt!.Id, receipt.Lines.Select(line => new OrderReceiptLine(line.PoLineNumber, line.ReceivedQuantity, line.AcceptedQuantity, line.RejectedQuantity))
                .ToArray(),
            policy.OverReceiptTolerancePercent, receiver, now, writer.CorrelationId);
        if (orderError is not null)
            return await writer.FailAsync(orderError.ToApplicationError());
        receipts.Add(receipt);

        writer.Audit(PurchasingAudit.ReceiptPosted, PurchasingAudit.ReceiptResource, receipt.Id, toState: "Posted",
            ruleIds: ["RCV-001", "RCV-002", "RCV-003", "RCV-005"],
            details: new Dictionary<string, string>
            {
                ["number"] = receipt.Number,
                ["purchaseOrderId"] = order.Id.ToString(),
                ["lineCount"] = PurchasingAudit.Text(receipt.Lines.Count),
                ["policy"] = receipt.PolicyReference,
                ["receivedOn"] = receipt.ReceivedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            });
        if (receipt.HasDiscrepancy)
            writer.Audit(PurchasingAudit.ReceiptDiscrepancyRecorded, PurchasingAudit.ReceiptResource, receipt.Id, ruleIds: ["RCV-004"],
                details: new Dictionary<string, string>
                {
                    ["purchaseOrderId"] = order.Id.ToString(),
                    ["lines"] = string.Join(',', receipt.Lines.Where(line => line.RejectedQuantity > 0)
                        .Select(line => $"{PurchasingAudit.Text(line.PoLineNumber)}:{line.DiscrepancyType}"))
                });
        var requisition = await FollowAsync(order, change, from, cancellationToken);
        return await CommitAsync(receipt, order, requisition, cancellationToken);
    }

    public async Task<Result<ReceiptPostingView>> Handle(CorrectReceiptCommand request, CancellationToken cancellationToken)
    {
        if (writer.ActorId is not { } actor)
            return ProcurementWriter.Forbidden();
        if (!decimal.TryParse(request.AcceptedDelta, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var delta))
            return ApplicationError.Validation("acceptedDelta", "acceptedDelta is a signed decimal string such as -1 or 2.5.");
        var loaded = await receipts.FindAsync(request.ReceiptId, cancellationToken);
        if (loaded is null)
            return PurchasingAudit.ReceiptNotFound();
        if (VersionPrecondition.Check(request.ExpectedVersion, loaded.Version) is { } precondition)
            return precondition;

        var now = writer.UtcNow;
        await writer.BeginAsync(cancellationToken);
        var order = await orders.FindForUpdateAsync(loaded.PurchaseOrderId, cancellationToken)
            ?? throw new InvalidOperationException("A receipt references a missing order.");
        var receipt = await receipts.FindAsync(request.ReceiptId, cancellationToken) ?? throw new InvalidOperationException("The receipt disappeared.");
        if (VersionPrecondition.Check(request.ExpectedVersion, receipt.Version) is { } lockedPrecondition)
            return await writer.FailAsync(lockedPrecondition);
        var (correction, correctionError) = receipt.Correct(Guid.NewGuid(), request.PoLineNumber, delta, request.Reason, actor, now, writer.CorrelationId);
        if (correctionError is not null)
            return await writer.FailAsync(correctionError.ToApplicationError());
        var from = order.Status.ToString();
        var (change, orderError) = order.ApplyCorrection(receipt.Id, request.PoLineNumber, delta, receipt.TolerancePercent, actor, now, writer.CorrelationId);
        if (orderError is not null)
            return await writer.FailAsync(orderError.ToApplicationError());

        writer.Audit(PurchasingAudit.ReceiptCorrected, PurchasingAudit.ReceiptResource, receipt.Id, reason: correction!.Reason, ruleIds: ["RCV-007"],
            details: new Dictionary<string, string>
            {
                ["correctionId"] = correction.Id.ToString(),
                ["purchaseOrderId"] = order.Id.ToString(),
                ["poLineNumber"] = PurchasingAudit.Text(correction.PoLineNumber),
                ["acceptedDelta"] = PurchasingAudit.Quantity(correction.AcceptedDelta)
            });
        var requisition = await FollowAsync(order, change, from, cancellationToken);
        return await CommitAsync(receipt, order, requisition, cancellationToken);
    }

    public async Task<Result<ReceiptView>> Handle(GetReceiptQuery request, CancellationToken cancellationToken)
    {
        if (!await MayReadReceiptsAsync(cancellationToken))
            return PurchasingAudit.ReceiptNotFound();
        var receipt = await receipts.FindAsync(request.Id, cancellationToken);
        return receipt is null ? PurchasingAudit.ReceiptNotFound() : await access.ReceiptViewAsync(receipt, cancellationToken);
    }

    public async Task<Result<PagedResult<ReceiptView>>> Handle(ListReceiptsQuery request, CancellationToken cancellationToken)
    {
        if (!await MayReadReceiptsAsync(cancellationToken))
            return ProcurementWriter.Forbidden();
        var page = PageRequest.Create(request.Page, request.PageSize, request.Sort, IReceiptRepository.SortFields, "-postedAt");
        if (!page.IsSuccess)
            return page.Error!;
        if (request.ReceivedTo < request.ReceivedFrom)
            return ApplicationError.Validation("receivedTo", "The end date must be on or after the start date.");
        var result = await receipts.ListAsync(
            new ReceiptFilter(request.PurchaseOrderId, null, request.HasDiscrepancy, string.IsNullOrWhiteSpace(request.Q) ? null : request.Q.Trim(),
                request.ReceivedFrom, request.ReceivedTo),
            page.Value!, cancellationToken);
        var correct = await writer.HasPermissionAsync(Permissions.ReceivingReceiptsCorrect, cancellationToken);
        return new PagedResult<ReceiptView>(
            result.Items.Select(receipt => PurchaseOrderMapping.ToView(receipt, correct ? [PurchasingAudit.ActionCorrect] : [])).ToArray(),
            result.Page, result.PageSize, result.TotalItems);
    }

    /// <summary>PUR-009: a completion closes the requisition, a reopening returns it to <c>Ordered</c>, both audited.</summary>
    private async Task<PurchaseRequisition?> FollowAsync(PurchaseOrder order, OrderCompletionChange change, string from, CancellationToken cancellationToken)
    {
        if (change == OrderCompletionChange.None)
            return null;
        var completed = change == OrderCompletionChange.Completed;
        writer.Audit(completed ? PurchasingAudit.Completed : PurchasingAudit.Reopened, PurchasingAudit.Resource, order.Id, from, order.Status.ToString(),
            ruleIds: ["PUR-009"], details: new Dictionary<string, string> { ["completionSequence"] = PurchasingAudit.Text(order.CompletionSequence) });
        var requisition = await requisitions.FindAsync(order.RequisitionId, cancellationToken);
        var eventType = completed ? "procurement.purchase-order.completed" : "procurement.purchase-order.reopened";
        if (requisition is not null && requisition.FollowOrderCompletion(completed, completed ? "OrderCompleted" : "OrderReopened", writer.UtcNow,
                writer.CorrelationId, eventType))
            writer.Audit(completed ? PurchasingAudit.RequisitionClosed : PurchasingAudit.RequisitionReopened, "purchase-requisition", requisition.Id,
                completed ? "Ordered" : "Closed", completed ? "Closed" : "Ordered", ruleIds: ["PUR-009"],
                details: new Dictionary<string, string> { ["purchaseOrderId"] = order.Id.ToString(), ["number"] = requisition.Number });
        return requisition;
    }

    private async Task<Result<ReceiptPostingView>> CommitAsync(
        Receipt receipt, PurchaseOrder order, PurchaseRequisition? requisition, CancellationToken cancellationToken)
    {
        var events = writer.Stage(receipt.DomainEvents.Concat(order.DomainEvents).Concat(requisition?.DomainEvents ?? []).ToArray());
        var commit = await writer.CommitAsync(token => orders.GetVersionAsync(order.Id, token), null, cancellationToken);
        if (commit is not null)
            return commit;

        writer.Record(events);
        receipt.ClearDomainEvents();
        order.ClearDomainEvents();
        requisition?.ClearDomainEvents();
        var orderView = await access.ViewAsync(order, cancellationToken);
        return new ReceiptPostingView(await access.ReceiptViewAsync(receipt, cancellationToken), orderView!);
    }

    private async Task<bool> MayReadReceiptsAsync(CancellationToken cancellationToken)
    {
        var permissions = await writer.PermissionsAsync(cancellationToken);
        return permissions.Contains(Permissions.ReceivingReceiptsRead) || permissions.Contains(Permissions.ReceivingReceiptsCreate);
    }

    private static (IReadOnlyList<ReceiptLineInput>? Lines, ApplicationError? Error) ParseLines(IReadOnlyList<ReceiptLineRequest> requests)
    {
        var parsed = new List<ReceiptLineInput>();
        foreach (var line in requests)
        {
            var field = $"lines[{line.PoLineNumber.ToString(CultureInfo.InvariantCulture)}]";
            if (!TryQuantity(line.Received, out var received))
                return (null, ApplicationError.Validation($"{field}.received", "received is a decimal string."));
            if (!TryQuantity(line.Accepted, out var accepted))
                return (null, ApplicationError.Validation($"{field}.accepted", "accepted is a decimal string."));
            var rejected = 0m;
            if (line.Rejected is not null && !TryQuantity(line.Rejected, out rejected))
                return (null, ApplicationError.Validation($"{field}.rejected", "rejected is a decimal string."));
            DiscrepancyType? discrepancy = null;
            if (!string.IsNullOrWhiteSpace(line.DiscrepancyType))
            {
                if (!Enum.TryParse<DiscrepancyType>(line.DiscrepancyType.Trim(), true, out var type) || int.TryParse(line.DiscrepancyType, out _))
                    return (null, ApplicationError.Validation($"{field}.discrepancyType",
                        $"discrepancyType accepts: {string.Join(", ", Enum.GetNames<DiscrepancyType>())}."));
                discrepancy = type;
            }

            parsed.Add(new ReceiptLineInput(line.PoLineNumber, received, accepted, rejected, discrepancy, line.DiscrepancyNotes));
        }

        return (parsed, null);
    }

    private static bool TryQuantity(string? value, out decimal quantity) =>
        decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out quantity);
}
