#nullable enable
using System.Globalization;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Results;
using Template.Application.Features.Catalog;
using Template.Application.Interfaces.Purchasing;
using Template.Application.Security;
using Template.Application.Services.Procurement;
using Template.Application.Wrappers;
using Template.Domain.Purchasing;
using Template.Domain.Receiving;

namespace Template.Application.Features.Purchasing;

public sealed record PurchaseOrderSupplierContactView(string? Name, string? Email, string? Phone);

/// <summary>Supplier snapshot taken at draft creation (PUR-002); address and contact are withheld from requesters (PUR-011).</summary>
public sealed record PurchaseOrderSupplierView(
    Guid SupplierId, string LegalName, string? TradeName, string Country, string TaxId, string? Address, PurchaseOrderSupplierContactView? Contact);

/// <summary>
/// One order line. Prices are <c>null</c> for actors without <c>purchasing.orders.read</c> (PUR-011); quantities are
/// invariant decimal strings. <see cref="ReceiptCap"/> is the CALC-009 cap of the pinned receiving policy.
/// </summary>
public sealed record PurchaseOrderLineView(
    int LineNumber,
    int RequisitionLineNumber,
    Guid? CatalogItemId,
    string Description,
    string Kind,
    string UnitCode,
    int UnitAllowedDecimals,
    string Quantity,
    MoneyView? UnitPrice,
    MoneyView? LineTotal,
    DateOnly PromisedDeliveryDate,
    string ReceivedQuantity,
    string AcceptedQuantity,
    string RejectedQuantity,
    string RemainingQuantity,
    string ReceiptCap,
    bool IsShortClosed);

/// <summary>Issued-document sub-status (PUR-007); <see cref="ContentUrl"/> is an API path, never a storage URL.</summary>
public sealed record PurchaseOrderDocumentView(
    string Status, int Attempts, string? FailureCode, string? TemplateVersion, Guid? FileId, DateTime? ReadyAt, string? ContentUrl);

public sealed record PurchaseOrderNotificationView(string Status, int Attempts, string? FailureCode, DateTime? NotifiedAt);

/// <summary>
/// Purchase-order representation (PUR-001..011). <see cref="PricesVisible"/> tells the client whether commercial values
/// are present; <see cref="AllowedActions"/> reflects state and permission only (content guards still apply).
/// </summary>
public sealed record PurchaseOrderView(
    Guid Id,
    string? Number,
    string Status,
    string Version,
    Guid AwardId,
    Guid ApprovalInstanceId,
    Guid RfqId,
    string RfqNumber,
    Guid RequisitionId,
    string RequisitionNumber,
    Guid RequesterId,
    Guid CostCenterId,
    PurchaseOrderSupplierView Supplier,
    Guid? BuyerId,
    string? BuyerName,
    string Currency,
    MoneyView? Subtotal,
    MoneyView? Freight,
    MoneyView? Discount,
    MoneyView? TaxesIncluded,
    MoneyView? Total,
    string PaymentTerms,
    DateOnly QuoteValidUntil,
    int DeliveryDays,
    string? DeliveryAddress,
    string? Notes,
    IReadOnlyList<PurchaseOrderLineView> Lines,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? IssuedAt,
    Guid? IssuedBy,
    DateOnly? IssueDate,
    DateTime? CancelledAt,
    string? CancellationReason,
    DateTime? CompletedAt,
    bool ShortClosed,
    string? ShortCloseReason,
    PurchaseOrderDocumentView Document,
    PurchaseOrderNotificationView Notification,
    string ReceivingPolicy,
    bool PricesVisible,
    IReadOnlyList<string> AllowedActions);

/// <summary>Work-queue row (approved-order queue and receiving queue).</summary>
public sealed record PurchaseOrderSummaryView(
    Guid Id,
    string? Number,
    string Status,
    Guid RequisitionId,
    string RequisitionNumber,
    Guid SupplierId,
    string SupplierLegalName,
    MoneyView? Total,
    int LineCount,
    string DocumentStatus,
    string NotificationStatus,
    DateOnly? EarliestPromisedDate,
    DateTime CreatedAt,
    DateTime? IssuedAt,
    DateTime UpdatedAt);

public sealed record PurchaseOrderHistoryEntryView(
    int Sequence, string Action, string? From, string To, Guid? ActorId, DateTime At, string? Reason, string? ReasonCode, string? EventType, Guid? SubjectId);

public sealed record PurchaseOrderHistoryView(Guid PurchaseOrderId, IReadOnlyList<PurchaseOrderHistoryEntryView> Entries);

/// <summary>Streamed issued document.</summary>
public sealed record PurchaseOrderDocument(Stream Content, string FileName, string MediaType, long Length, string TemplateVersion);

public sealed record ReceiptLineView(
    int PoLineNumber,
    string Description,
    string UnitCode,
    string ReceivedQuantity,
    string AcceptedQuantity,
    string RejectedQuantity,
    string CorrectedAcceptedQuantity,
    string? DiscrepancyType,
    string? DiscrepancyNotes);

public sealed record ReceiptCorrectionView(Guid Id, int Sequence, int PoLineNumber, string AcceptedDelta, string Reason, Guid ActorId, DateTime At);

public sealed record ReceiptAttachmentView(Guid Id, string FileName, string MediaType, long Length, Guid AddedBy, DateTime AddedAt);

/// <summary>Posted receipt (RCV-001..008); there is no edit or delete (RCV-006), only appended corrections.</summary>
public sealed record ReceiptView(
    Guid Id,
    string Number,
    string Status,
    string Version,
    Guid PurchaseOrderId,
    string PurchaseOrderNumber,
    Guid ReceiverId,
    DateOnly ReceivedOn,
    string? Location,
    string? DeliveryReference,
    string? Notes,
    string Policy,
    bool HasDiscrepancy,
    DateTime PostedAt,
    IReadOnlyList<ReceiptLineView> Lines,
    IReadOnlyList<ReceiptCorrectionView> Corrections,
    IReadOnlyList<ReceiptAttachmentView> Attachments,
    IReadOnlyList<string> AllowedActions);

/// <summary>Result of posting a receipt or a correction: the receipt and the recomputed order.</summary>
public sealed record ReceiptPostingView(ReceiptView Receipt, PurchaseOrderView PurchaseOrder);

/// <summary>Audit actions, allowed actions and failures of Purchasing and Receiving (PUR-*, RCV-*).</summary>
public static class PurchasingAudit
{
    public const string Resource = "purchase-order";
    public const string ReceiptResource = "receipt";
    public const string WaiverResource = "waiver";

    public const string Created = "purchase-order.created";
    public const string Updated = "purchase-order.updated";
    public const string Issued = "purchase-order.issued";
    public const string Cancelled = "purchase-order.cancelled";
    public const string Completed = "purchase-order.completed";
    public const string Reopened = "purchase-order.reopened";
    public const string ShortClosed = "purchase-order.short-closed";
    public const string DocumentReady = "purchase-order.document.ready";
    public const string DocumentFailed = "purchase-order.document.failed";
    public const string DocumentRetried = "purchase-order.document.retried";
    public const string DocumentDownloaded = "purchase-order.document.downloaded";
    public const string NotificationSent = "purchase-order.notification.sent";
    public const string NotificationFailed = "purchase-order.notification.failed";
    public const string NotificationRetried = "purchase-order.notification.retried";
    public const string ReceiptPosted = "receipt.posted";
    public const string ReceiptDiscrepancyRecorded = "receipt.discrepancy-recorded";
    public const string ReceiptCorrected = "receipt.corrected";
    public const string WaiverRecorded = "waiver.recorded";
    public const string RequisitionOrdered = "requisition.ordered";
    public const string RequisitionClosed = "requisition.closed";
    public const string RequisitionReopened = "requisition.reopened";

    public const string ActionUpdate = "update";
    public const string ActionIssue = "issue";
    public const string ActionCancel = "cancel";
    public const string ActionShortClose = "short-close";
    public const string ActionRetryDocument = "retry-document";
    public const string ActionRetryNotification = "retry-notification";
    public const string ActionDownloadDocument = "download-document";
    public const string ActionReceive = "receive";
    public const string ActionCorrect = "correct";

    public const string SupplierNotificationTemplate = "purchasing.purchase-order-issued.v1";

    public static ApplicationError NotFound() => ProcurementWriter.NotFound("purchase order");

    public static ApplicationError ReceiptNotFound() => ProcurementWriter.NotFound("receipt");

    public static string Quantity(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    public static MoneyView Money(decimal amount, string currency) => new(amount.ToString("0.00##", CultureInfo.InvariantCulture), currency);

    public static string Amount(decimal value) => value.ToString("0.00##", CultureInfo.InvariantCulture);

    public static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Purchasing and Receiving authorization after the endpoint permission check (docs/product/permissions.md, PUR-011):
/// order visibility and price visibility, the issued-order cancel scope (PUR-008, D32-7) and the server-computed
/// allowed actions. It never bypasses a domain rule.
/// </summary>
public sealed class PurchasingAccess(ProcurementWriter writer)
{
    public async Task<PurchaseOrderVisibility> VisibilityAsync(CancellationToken cancellationToken)
    {
        var permissions = await writer.PermissionsAsync(cancellationToken);
        return new PurchaseOrderVisibility(
            writer.ActorId,
            permissions.Contains(Permissions.PurchasingOrdersRead),
            permissions.Contains(Permissions.ReceivingReceiptsCreate) || permissions.Contains(Permissions.ReceivingReceiptsRead));
    }

    /// <summary>PUR-011: <c>null</c> (answer 404) when the actor may not read the order.</summary>
    public async Task<PurchaseOrderView?> ViewAsync(PurchaseOrder order, CancellationToken cancellationToken)
    {
        var visibility = await VisibilityAsync(cancellationToken);
        if (!visibility.CanRead(order))
            return null;
        var permissions = await writer.PermissionsAsync(cancellationToken);
        var prices = visibility.ReadAll;
        var contact = visibility.ReadAll || visibility.ReadIssued;
        return PurchaseOrderMapping.ToView(order, prices, contact, Allowed(order, permissions), writer.UtcNow);
    }

    /// <summary>PUR-008 (D32-7): cancelling an issued order also needs <c>requisitions.cancel-any</c> (ProcurementManager).</summary>
    public async Task<bool> MayCancelIssuedAsync(CancellationToken cancellationToken) =>
        await writer.HasPermissionAsync(Permissions.RequisitionsCancelAny, cancellationToken);

    public static IReadOnlyList<string> Allowed(PurchaseOrder order, IReadOnlySet<string> permissions)
    {
        var actions = new List<string>();
        var manage = permissions.Contains(Permissions.PurchasingOrdersManage);
        var issue = permissions.Contains(Permissions.PurchasingOrdersIssue);
        var cancel = permissions.Contains(Permissions.PurchasingOrdersCancel);
        switch (order.Status)
        {
            case PurchaseOrderStatus.Draft:
                if (manage)
                    actions.Add(PurchasingAudit.ActionUpdate);
                if (issue)
                    actions.Add(PurchasingAudit.ActionIssue);
                if (cancel)
                    actions.Add(PurchasingAudit.ActionCancel);
                break;
            case PurchaseOrderStatus.Issued or PurchaseOrderStatus.PartiallyReceived:
                if (permissions.Contains(Permissions.ReceivingReceiptsCreate))
                    actions.Add(PurchasingAudit.ActionReceive);
                if (order.Status == PurchaseOrderStatus.Issued && !order.HasAcceptedQuantity && cancel
                    && permissions.Contains(Permissions.RequisitionsCancelAny))
                    actions.Add(PurchasingAudit.ActionCancel);
                if (order.Status == PurchaseOrderStatus.PartiallyReceived && permissions.Contains(Permissions.PurchasingOrdersShortClose))
                    actions.Add(PurchasingAudit.ActionShortClose);
                break;
        }

        if (order.Status != PurchaseOrderStatus.Cancelled && issue)
        {
            if (order.DocumentStatus == PurchaseOrderDocumentStatus.Failed)
                actions.Add(PurchasingAudit.ActionRetryDocument);
            if (order.NotificationStatus == PurchaseOrderNotificationStatus.Failed)
                actions.Add(PurchasingAudit.ActionRetryNotification);
        }

        if (order.DocumentStatus == PurchaseOrderDocumentStatus.Ready && permissions.Contains(Permissions.PurchasingOrdersRead))
            actions.Add(PurchasingAudit.ActionDownloadDocument);
        return actions;
    }

    public async Task<ReceiptView> ReceiptViewAsync(Receipt receipt, CancellationToken cancellationToken)
    {
        var permissions = await writer.PermissionsAsync(cancellationToken);
        return PurchaseOrderMapping.ToView(receipt, permissions.Contains(Permissions.ReceivingReceiptsCorrect) ? [PurchasingAudit.ActionCorrect] : []);
    }
}

internal static class PurchaseOrderMapping
{
    public static PurchaseOrderView ToView(PurchaseOrder order, bool prices, bool contact, IReadOnlyList<string> allowedActions, DateTime nowUtc)
    {
        var tolerance = ReceivingPolicy.V1.OverReceiptTolerancePercent;
        MoneyView? Price(decimal value) => prices ? PurchasingAudit.Money(value, order.Currency) : null;
        return new PurchaseOrderView(
            order.Id, order.Number, order.Status.ToString(), VersionPrecondition.Format(order.Version), order.AwardId, order.ApprovalInstanceId, order.RfqId, order.RfqNumber,
            order.RequisitionId, order.RequisitionNumber, order.RequesterId, order.CostCenterId,
            new PurchaseOrderSupplierView(order.SupplierId, order.SupplierLegalName, order.SupplierTradeName, order.SupplierCountry, order.SupplierTaxId,
                contact ? order.SupplierAddress : null,
                contact ? new PurchaseOrderSupplierContactView(order.SupplierContactName, order.SupplierContactEmail, order.SupplierContactPhone) : null),
            order.BuyerId, order.BuyerName, order.Currency, Price(order.Subtotal), Price(order.Freight), Price(order.Discount), Price(order.TaxesIncluded),
            Price(order.Total), order.PaymentTerms, order.QuoteValidUntil, order.DeliveryDays, order.DeliveryAddress, order.Notes,
            order.Lines.OrderBy(line => line.LineNumber).Select(line => new PurchaseOrderLineView(
                line.LineNumber, line.RequisitionLineNumber, line.CatalogItemId, line.Description, line.Kind, line.UnitCode, line.UnitAllowedDecimals,
                PurchasingAudit.Quantity(line.Quantity), Price(line.UnitPrice), Price(line.LineTotal), line.PromisedDeliveryDate,
                PurchasingAudit.Quantity(line.ReceivedQuantity), PurchasingAudit.Quantity(line.AcceptedQuantity), PurchasingAudit.Quantity(line.RejectedQuantity),
                PurchasingAudit.Quantity(line.RemainingQuantity), PurchasingAudit.Quantity(line.Cap(tolerance)), line.IsShortClosed)).ToArray(),
            order.CreatedAtUtc, order.UpdatedAtUtc, order.IssuedAtUtc, order.IssuedBy, order.IssueDate, order.CancelledAtUtc, order.CancellationReason,
            order.CompletedAtUtc, order.ShortClosed, order.ShortCloseReason,
            new PurchaseOrderDocumentView(order.DocumentStatus.ToString(), order.DocumentAttempts, order.DocumentFailureCode, order.DocumentTemplateVersion,
                prices ? order.DocumentFileId : null, order.DocumentReadyAtUtc,
                prices && order.DocumentStatus == PurchaseOrderDocumentStatus.Ready ? $"/api/v2/purchase-orders/{order.Id}/document" : null),
            new PurchaseOrderNotificationView(order.NotificationStatus.ToString(), order.NotificationAttempts, order.NotificationFailureCode, order.NotifiedAtUtc),
            ReceivingPolicy.V1.Reference, prices, allowedActions);
    }

    public static PurchaseOrderSummaryView ToSummary(PurchaseOrder order, bool prices) => new(
        order.Id, order.Number, order.Status.ToString(), order.RequisitionId, order.RequisitionNumber, order.SupplierId, order.SupplierLegalName,
        prices ? PurchasingAudit.Money(order.Total, order.Currency) : null, order.Lines.Count, order.DocumentStatus.ToString(),
        order.NotificationStatus.ToString(),
        order.Lines.Where(line => line.RemainingQuantity > 0).Select(line => (DateOnly?)line.PromisedDeliveryDate).Min(),
        order.CreatedAtUtc, order.IssuedAtUtc, order.UpdatedAtUtc);

    public static ReceiptView ToView(Receipt receipt, IReadOnlyList<string> allowedActions) => new(
        receipt.Id, receipt.Number, receipt.Status.ToString(), VersionPrecondition.Format(receipt.Version), receipt.PurchaseOrderId, receipt.PurchaseOrderNumber, receipt.ReceiverId,
        receipt.ReceivedOn, receipt.Location, receipt.DeliveryReference, receipt.Notes, receipt.PolicyReference, receipt.HasDiscrepancy, receipt.PostedAtUtc,
        receipt.Lines.OrderBy(line => line.PoLineNumber).Select(line => new ReceiptLineView(
            line.PoLineNumber, line.Description, line.UnitCode, PurchasingAudit.Quantity(line.ReceivedQuantity), PurchasingAudit.Quantity(line.AcceptedQuantity),
            PurchasingAudit.Quantity(line.RejectedQuantity), PurchasingAudit.Quantity(receipt.CorrectedAccepted(line)), line.DiscrepancyType?.ToString(),
            line.DiscrepancyNotes)).ToArray(),
        receipt.Corrections.OrderBy(correction => correction.Sequence).Select(correction => new ReceiptCorrectionView(
            correction.Id, correction.Sequence, correction.PoLineNumber, PurchasingAudit.Quantity(correction.AcceptedDelta), correction.Reason,
            correction.ActorId, correction.AtUtc)).ToArray(),
        receipt.Attachments.OrderBy(attachment => attachment.AddedAtUtc).Select(attachment => new ReceiptAttachmentView(
            attachment.Id, attachment.FileName, attachment.MediaType, attachment.Length, attachment.AddedBy, attachment.AddedAtUtc)).ToArray(),
        allowedActions);

    public static PurchaseOrderHistoryView ToHistory(PurchaseOrder order) => new(
        order.Id,
        order.History.OrderBy(entry => entry.Sequence).Select(entry => new PurchaseOrderHistoryEntryView(
            entry.Sequence, entry.Action, entry.From?.ToString(), entry.To.ToString(), entry.ActorId, entry.AtUtc, entry.Reason, entry.ReasonCode,
            entry.EventType, entry.SubjectId)).ToArray());
}
