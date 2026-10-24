using System;
using System.Collections.Generic;
using Template.Domain.Common;

namespace Template.Domain.Purchasing;

/// <summary>Purchase-order lifecycle (docs/product/workflows.md §4).</summary>
public enum PurchaseOrderStatus
{
    Draft,
    Issued,
    PartiallyReceived,
    Completed,
    Cancelled
}

/// <summary>Issued-document sub-status, independent of the order state (PUR-007).</summary>
public enum PurchaseOrderDocumentStatus
{
    NotRequested,
    Pending,
    Ready,
    Failed
}

/// <summary>Supplier-notification sub-status, independent of the order state (PUR-007).</summary>
public enum PurchaseOrderNotificationStatus
{
    NotRequested,
    Pending,
    Sent,
    Failed
}

/// <summary>Effect of a receipt, correction or short close on completion (PUR-009).</summary>
public enum OrderCompletionChange
{
    None,
    Completed,
    Reopened
}

/// <summary>Supplier identity at draft creation (PUR-002); contact data is personal data (SUP-006, PUR-011).</summary>
public sealed record PurchaseOrderSupplierSnapshot(
    Guid SupplierId,
    string LegalName,
    string? TradeName,
    string Country,
    string TaxId,
    string? Address,
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone);

/// <summary>One awarded line offer copied by value with its requisition/RFQ line reference (CALC-006).</summary>
public sealed record PurchaseOrderLineInput(
    int RfqLineNumber,
    Guid? CatalogItemId,
    string Description,
    string Kind,
    Guid CategoryId,
    Guid UnitOfMeasureId,
    string UnitCode,
    int UnitAllowedDecimals,
    decimal Quantity,
    decimal UnitPrice,
    DateOnly PromisedDeliveryDate);

/// <summary>
/// Everything a draft copies from the approved award (PUR-001/002, CALC-006): the award, approval and requisition
/// references, the supplier and buyer snapshots, the quote revision's commercial terms and the CALC-007 rates.
/// </summary>
public sealed record PurchaseOrderDraftInput(
    Guid AwardId,
    Guid ApprovalInstanceId,
    Guid RfqId,
    string RfqNumber,
    Guid QuoteRevisionId,
    Guid RequisitionId,
    string RequisitionNumber,
    Guid RequesterId,
    Guid CostCenterId,
    Guid DepartmentId,
    PurchaseOrderSupplierSnapshot Supplier,
    Guid? BuyerId,
    string? BuyerName,
    string Currency,
    decimal Freight,
    decimal Discount,
    decimal TaxesIncluded,
    decimal AwardedTotal,
    string PaymentTerms,
    DateOnly QuoteValidUntil,
    int DeliveryDays,
    string BaseCurrency,
    decimal QuoteToBaseRate,
    string RequisitionCurrency,
    decimal RequisitionToBaseRate,
    decimal RequisitionCurrencyTotal,
    IReadOnlyList<PurchaseOrderLineInput> Lines);

/// <summary>
/// Ordered line with its immutable commercial snapshot (PUR-002) and the received, accepted and rejected totals
/// maintained from posted receipts and corrections (RCV-003/007).
/// </summary>
public sealed class PurchaseOrderLine
{
    public const int DescriptionMaxLength = 500;
    public const int UnitCodeMaxLength = 20;

    private PurchaseOrderLine()
    {
        Description = string.Empty;
        Kind = string.Empty;
        UnitCode = string.Empty;
    }

    internal PurchaseOrderLine(Guid id, Guid purchaseOrderId, int lineNumber, PurchaseOrderLineInput input, string currency)
        : this()
    {
        Id = id;
        PurchaseOrderId = purchaseOrderId;
        LineNumber = lineNumber;
        RequisitionLineNumber = input.RfqLineNumber;
        CatalogItemId = input.CatalogItemId;
        Description = input.Description;
        Kind = input.Kind;
        CategoryId = input.CategoryId;
        UnitOfMeasureId = input.UnitOfMeasureId;
        UnitCode = input.UnitCode;
        UnitAllowedDecimals = input.UnitAllowedDecimals;
        Quantity = input.Quantity;
        UnitPrice = input.UnitPrice;
        LineTotal = CalculationRules.LineTotal(input.Quantity, new Money(input.UnitPrice, currency)).Amount;
        PromisedDeliveryDate = input.PromisedDeliveryDate;
    }

    public Guid Id { get; private set; }
    public Guid PurchaseOrderId { get; private set; }
    public int LineNumber { get; private set; }

    /// <summary>Requisition line (equal to the RFQ snapshot line number, D29-2).</summary>
    public int RequisitionLineNumber { get; private set; }

    public Guid? CatalogItemId { get; private set; }
    public string Description { get; private set; }
    public string Kind { get; private set; }
    public Guid CategoryId { get; private set; }
    public Guid UnitOfMeasureId { get; private set; }
    public string UnitCode { get; private set; }
    public int UnitAllowedDecimals { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal LineTotal { get; private set; }
    public DateOnly PromisedDeliveryDate { get; private set; }
    public decimal ReceivedQuantity { get; private set; }
    public decimal AcceptedQuantity { get; private set; }
    public decimal RejectedQuantity { get; private set; }
    public bool IsShortClosed { get; private set; }

    /// <summary>Quantity still expected: zero once accepted in full or short-closed.</summary>
    public decimal RemainingQuantity => IsShortClosed ? 0m : Math.Max(0m, Quantity - AcceptedQuantity);

    /// <summary>PUR-009: the line is satisfied.</summary>
    public bool IsComplete => IsShortClosed || AcceptedQuantity >= Quantity;

    /// <summary>CALC-009 cap with the tolerance of the receiving policy.</summary>
    public decimal Cap(decimal tolerancePercent) => CalculationRules.ReceiptCap(Quantity, tolerancePercent, UnitAllowedDecimals);

    internal void ChangePromisedDate(DateOnly date) => PromisedDeliveryDate = date;

    internal void AddReceipt(decimal received, decimal accepted, decimal rejected)
    {
        ReceivedQuantity += received;
        AcceptedQuantity += accepted;
        RejectedQuantity += rejected;
    }

    internal void Correct(decimal acceptedDelta) => AcceptedQuantity += acceptedDelta;

    internal void ShortClose() => IsShortClosed = true;
}

/// <summary>Append-only timeline of the order: transitions and in-state actions (AUD-004, NFR-AUD-2).</summary>
public sealed class PurchaseOrderHistoryEntry
{
    public const int ActionMaxLength = 40;
    public const int ReasonCodeMaxLength = 40;
    public const int CorrelationIdMaxLength = 128;
    public const int EventTypeMaxLength = 100;

    private PurchaseOrderHistoryEntry()
    {
        Action = string.Empty;
        CorrelationId = string.Empty;
    }

    internal PurchaseOrderHistoryEntry(
        Guid id, Guid purchaseOrderId, int sequence, string action, PurchaseOrderStatus? from, PurchaseOrderStatus to, Guid? actorId, DateTime atUtc,
        string? reason, string? reasonCode, string correlationId, string? eventType, Guid? subjectId)
    {
        Id = id;
        PurchaseOrderId = purchaseOrderId;
        Sequence = sequence;
        Action = action;
        From = from;
        To = to;
        ActorId = actorId == Guid.Empty ? null : actorId;
        AtUtc = atUtc;
        Reason = reason;
        ReasonCode = reasonCode is { Length: > ReasonCodeMaxLength } ? reasonCode[..ReasonCodeMaxLength] : reasonCode;
        CorrelationId = correlationId.Length > CorrelationIdMaxLength ? correlationId[..CorrelationIdMaxLength] : correlationId;
        EventType = eventType;
        SubjectId = subjectId;
    }

    public Guid Id { get; private set; }
    public Guid PurchaseOrderId { get; private set; }
    public int Sequence { get; private set; }
    public string Action { get; private set; }

    /// <summary><c>null</c> for the creation entry.</summary>
    public PurchaseOrderStatus? From { get; private set; }

    public PurchaseOrderStatus To { get; private set; }

    /// <summary><c>null</c> for system actions (draft creation, document and notification outcomes).</summary>
    public Guid? ActorId { get; private set; }

    public DateTime AtUtc { get; private set; }
    public string? Reason { get; private set; }
    public string? ReasonCode { get; private set; }
    public string CorrelationId { get; private set; }
    public string? EventType { get; private set; }

    /// <summary>Receipt, report job or file the action refers to.</summary>
    public Guid? SubjectId { get; private set; }
}

/// <summary><c>procurement.purchase-order.drafted</c> (PUR-001).</summary>
public sealed record PurchaseOrderDrafted(DateTime OccurredAtUtc, Guid PurchaseOrderId, Guid AwardId, Guid RequisitionId, Guid? BuyerId)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.purchase-order.drafted";
}

/// <summary><c>procurement.purchase-order.issued</c> (PUR-003/005).</summary>
public sealed record PurchaseOrderIssued(DateTime OccurredAtUtc, Guid PurchaseOrderId, string Number, Guid SupplierId, Money Total, Guid RequisitionId)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.purchase-order.issued";
}

/// <summary><c>procurement.purchase-order.cancelled</c> (PUR-008); the reason code is <c>DraftCancelled</c> or <c>IssuedCancelled</c>.</summary>
public sealed record PurchaseOrderCancelled(DateTime OccurredAtUtc, Guid PurchaseOrderId, Guid RequisitionId, string ReasonCode)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.purchase-order.cancelled";
}

/// <summary><c>procurement.purchase-order.completed</c> (PUR-009/010); idempotency key purchase order + completion sequence.</summary>
public sealed record PurchaseOrderCompleted(DateTime OccurredAtUtc, Guid PurchaseOrderId, Guid RequisitionId, bool ShortClosed, int CompletionSequence)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.purchase-order.completed";
}

/// <summary><c>procurement.purchase-order.reopened</c> (PUR-009 after RCV-007).</summary>
public sealed record PurchaseOrderReopened(DateTime OccurredAtUtc, Guid PurchaseOrderId, Guid RequisitionId, int CompletionSequence)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.purchase-order.reopened";
}

/// <summary>
/// <c>procurement.purchase-order.notification-requested</c> (PUR-007, D32-5): the issued document is stored and the
/// supplier notification attempt <see cref="Attempt"/> is queued for the Worker. IDs only (NFR-SEC-7).
/// </summary>
public sealed record PurchaseOrderNotificationRequested(
    DateTime OccurredAtUtc, Guid PurchaseOrderId, Guid SupplierId, Guid DocumentFileId, int Attempt)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.purchase-order.notification-requested";
}
