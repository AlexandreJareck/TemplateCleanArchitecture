using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Template.Domain.Common;

namespace Template.Domain.Purchasing;

/// <summary>A received line quantity applied to the order (RCV-002 already checked by the receipt).</summary>
public sealed record OrderReceiptLine(int LineNumber, decimal Received, decimal Accepted, decimal Rejected);

/// <summary>
/// Purchase order created once per approved award (PUR-001) with an immutable commercial snapshot copied from the
/// awarded quote revision (PUR-002, CALC-006). Only the delivery address, promised dates and notes change while
/// <c>Draft</c>. Issue allocates the gapless number (PUR-005, the caller supplies it inside the issue transaction) and
/// queues the document; document and notification outcomes are sub-statuses that never unissue or renumber the order
/// (PUR-007). Receipts and corrections maintain the accepted totals and recompute completion (PUR-009); a short close
/// is the only completion exception (PUR-010). Every change appends a history entry and increments the version.
/// </summary>
public sealed class PurchaseOrder : VersionedAggregateRoot
{
    public const int NumberMaxLength = 20;
    public const int DeliveryAddressMinLength = 5;
    public const int DeliveryAddressMaxLength = 500;
    public const int NotesMaxLength = 2000;
    public const int PaymentTermsMaxLength = 500;
    public const int TemplateVersionMaxLength = 40;
    public const int FailureCodeMaxLength = 60;
    public const int SnapshotTextMaxLength = 500;

    public const string ActionCreated = "created";
    public const string ActionUpdated = "updated";
    public const string ActionIssued = "issued";
    public const string ActionDocumentReady = "document-ready";
    public const string ActionDocumentFailed = "document-failed";
    public const string ActionDocumentRetried = "document-retried";
    public const string ActionNotificationSent = "notification-sent";
    public const string ActionNotificationFailed = "notification-failed";
    public const string ActionNotificationRetried = "notification-retried";
    public const string ActionReceiptPosted = "receipt-posted";
    public const string ActionReceiptCorrected = "receipt-corrected";
    public const string ActionCancelled = "cancelled";
    public const string ActionCompleted = "completed";
    public const string ActionReopened = "reopened";
    public const string ActionShortClosed = "short-closed";

    private readonly List<PurchaseOrderLine> lines = [];
    private readonly List<PurchaseOrderHistoryEntry> history = [];
    private readonly DomainEventBuffer events = new();

    private PurchaseOrder()
    {
        RfqNumber = string.Empty;
        RequisitionNumber = string.Empty;
        SupplierLegalName = string.Empty;
        SupplierCountry = string.Empty;
        SupplierTaxId = string.Empty;
        Currency = string.Empty;
        PaymentTerms = string.Empty;
        BaseCurrency = string.Empty;
        RequisitionCurrency = string.Empty;
    }

    /// <summary><c>PO-{yyyy}-{nnnnnn}</c>, allocated at issue (PUR-005); <c>null</c> while <c>Draft</c>.</summary>
    public string? Number { get; private set; }

    public Guid AwardId { get; private set; }
    public Guid ApprovalInstanceId { get; private set; }
    public Guid RfqId { get; private set; }
    public string RfqNumber { get; private set; }
    public Guid QuoteRevisionId { get; private set; }
    public Guid RequisitionId { get; private set; }
    public string RequisitionNumber { get; private set; }
    public Guid RequesterId { get; private set; }
    public Guid CostCenterId { get; private set; }
    public Guid DepartmentId { get; private set; }
    public Guid SupplierId { get; private set; }
    public string SupplierLegalName { get; private set; }
    public string? SupplierTradeName { get; private set; }
    public string SupplierCountry { get; private set; }
    public string SupplierTaxId { get; private set; }
    public string? SupplierAddress { get; private set; }
    public string? SupplierContactName { get; private set; }
    public string? SupplierContactEmail { get; private set; }
    public string? SupplierContactPhone { get; private set; }
    public Guid? BuyerId { get; private set; }
    public string? BuyerName { get; private set; }
    public string Currency { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal Freight { get; private set; }
    public decimal Discount { get; private set; }

    /// <summary>Explicit quoted amount already contained in prices; informational (CALC-002, no tax engine).</summary>
    public decimal TaxesIncluded { get; private set; }

    public decimal Total { get; private set; }
    public string PaymentTerms { get; private set; }
    public DateOnly QuoteValidUntil { get; private set; }
    public int DeliveryDays { get; private set; }
    public string BaseCurrency { get; private set; }
    public decimal QuoteToBaseRate { get; private set; }
    public string RequisitionCurrency { get; private set; }
    public decimal RequisitionToBaseRate { get; private set; }

    /// <summary>Award amount in the requisition currency (CALC-007), the committed budget amount.</summary>
    public decimal RequisitionCurrencyTotal { get; private set; }

    public string? DeliveryAddress { get; private set; }
    public string? Notes { get; private set; }
    public PurchaseOrderStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? IssuedAtUtc { get; private set; }
    public Guid? IssuedBy { get; private set; }

    /// <summary>Organization-local issue date (RCV-005 lower bound).</summary>
    public DateOnly? IssueDate { get; private set; }

    public DateTime? CancelledAtUtc { get; private set; }
    public Guid? CancelledBy { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>Number of completions; the idempotency key part of completed/reopened events.</summary>
    public int CompletionSequence { get; private set; }

    public bool ShortClosed { get; private set; }
    public Guid? ShortClosedBy { get; private set; }
    public string? ShortCloseReason { get; private set; }
    public decimal? ShortCloseReleasedAmount { get; private set; }

    public PurchaseOrderDocumentStatus DocumentStatus { get; private set; }

    /// <summary>Report job rendering the current document attempt (Phase 30 queue).</summary>
    public Guid? DocumentJobId { get; private set; }

    /// <summary>The exact issued document (PUR-007); kept as a business record, never expired.</summary>
    public Guid? DocumentFileId { get; private set; }

    public string? DocumentTemplateVersion { get; private set; }
    public int DocumentAttempts { get; private set; }
    public string? DocumentFailureCode { get; private set; }
    public DateTime? DocumentReadyAtUtc { get; private set; }
    public PurchaseOrderNotificationStatus NotificationStatus { get; private set; }
    public int NotificationAttempts { get; private set; }
    public string? NotificationFailureCode { get; private set; }
    public DateTime? NotifiedAtUtc { get; private set; }

    public IReadOnlyCollection<PurchaseOrderLine> Lines => lines;
    public IReadOnlyCollection<PurchaseOrderHistoryEntry> History => history;
    public IReadOnlyList<DomainEvent> DomainEvents => events.Events;

    public Money TotalMoney => new(Total, Currency);

    /// <summary>Σ accepted quantity of every line; PUR-008 forbids cancelling once it is positive.</summary>
    public bool HasAcceptedQuantity => lines.Any(line => line.AcceptedQuantity > 0);

    public bool IsReceivable => Status is PurchaseOrderStatus.Issued or PurchaseOrderStatus.PartiallyReceived;

    public void ClearDomainEvents() => events.Clear();

    public static string FormatNumber(int year, long sequence) =>
        $"PO-{year.ToString("D4", NumberFormatInfo.InvariantInfo)}-{sequence.ToString("D6", NumberFormatInfo.InvariantInfo)}";

    /// <summary>
    /// PUR-001/002: the <c>Draft</c> of one approved award. CALC-006 requires the snapshot total to equal the award total
    /// exactly; a mismatch means corrupted source data and throws.
    /// </summary>
    public static PurchaseOrder CreateDraft(Guid id, PurchaseOrderDraftInput input, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureId(id, nameof(id));
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        MasterDataRules.EnsureId(input.AwardId, nameof(input.AwardId));
        MasterDataRules.EnsureId(input.RequisitionId, nameof(input.RequisitionId));
        if (input.Lines is not { Count: > 0 })
            throw new ArgumentException("An order copies at least one awarded line.", nameof(input));

        var order = new PurchaseOrder
        {
            Id = id,
            AwardId = input.AwardId,
            ApprovalInstanceId = input.ApprovalInstanceId,
            RfqId = input.RfqId,
            RfqNumber = input.RfqNumber,
            QuoteRevisionId = input.QuoteRevisionId,
            RequisitionId = input.RequisitionId,
            RequisitionNumber = input.RequisitionNumber,
            RequesterId = input.RequesterId,
            CostCenterId = input.CostCenterId,
            DepartmentId = input.DepartmentId,
            SupplierId = input.Supplier.SupplierId,
            SupplierLegalName = input.Supplier.LegalName,
            SupplierTradeName = input.Supplier.TradeName,
            SupplierCountry = input.Supplier.Country,
            SupplierTaxId = input.Supplier.TaxId,
            SupplierAddress = Clip(input.Supplier.Address),
            SupplierContactName = Clip(input.Supplier.ContactName),
            SupplierContactEmail = Clip(input.Supplier.ContactEmail),
            SupplierContactPhone = Clip(input.Supplier.ContactPhone),
            BuyerId = input.BuyerId,
            BuyerName = Clip(input.BuyerName),
            Currency = input.Currency,
            Freight = input.Freight,
            Discount = input.Discount,
            TaxesIncluded = input.TaxesIncluded,
            PaymentTerms = input.PaymentTerms,
            QuoteValidUntil = input.QuoteValidUntil,
            DeliveryDays = input.DeliveryDays,
            BaseCurrency = input.BaseCurrency,
            QuoteToBaseRate = input.QuoteToBaseRate,
            RequisitionCurrency = input.RequisitionCurrency,
            RequisitionToBaseRate = input.RequisitionToBaseRate,
            RequisitionCurrencyTotal = input.RequisitionCurrencyTotal,
            Status = PurchaseOrderStatus.Draft,
            DocumentStatus = PurchaseOrderDocumentStatus.NotRequested,
            NotificationStatus = PurchaseOrderNotificationStatus.NotRequested,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
        var number = 1;
        foreach (var line in input.Lines.OrderBy(line => line.RfqLineNumber))
            order.lines.Add(new PurchaseOrderLine(Guid.NewGuid(), id, number++, line, input.Currency));
        order.Subtotal = order.lines.Aggregate(Money.Zero(input.Currency), (sum, line) => sum.Add(new Money(line.LineTotal, input.Currency))).Amount;
        order.Total = order.Subtotal + order.Freight - order.Discount;
        if (order.Total != input.AwardedTotal)
            throw new InvalidOperationException("CALC-006: the order total must equal the awarded total exactly.");

        order.Append(ActionCreated, null, PurchaseOrderStatus.Draft, null, nowUtc, null, null, correlationId, "procurement.purchase-order.drafted", input.AwardId);
        order.events.Raise(new PurchaseOrderDrafted(nowUtc, id, input.AwardId, input.RequisitionId, input.BuyerId));
        return order;
    }

    /// <summary>
    /// PUR-002: in <c>Draft</c> only the delivery address, the promised delivery dates (not before
    /// <paramref name="today"/>) and the notes change; <c>null</c> keeps a value. Anything else, or any change after
    /// issue, is <c>purchase-order.immutable</c>.
    /// </summary>
    public DomainError? UpdateDraft(
        string? deliveryAddress, string? notes, IReadOnlyDictionary<int, DateOnly>? promisedDates, DateOnly today, Guid? actorId, DateTime nowUtc,
        string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (Status != PurchaseOrderStatus.Draft)
            return PurchasingErrors.Immutable;
        string? address = null;
        if (deliveryAddress is not null)
        {
            address = deliveryAddress.Trim();
            if (address.Length is < DeliveryAddressMinLength or > DeliveryAddressMaxLength)
                return PurchasingErrors.Validation("deliveryAddress",
                    $"The delivery address must have {DeliveryAddressMinLength} to {DeliveryAddressMaxLength} characters.", "PUR-002");
        }

        string? trimmedNotes = null;
        if (notes is not null)
        {
            trimmedNotes = notes.Trim();
            if (trimmedNotes.Length > NotesMaxLength)
                return PurchasingErrors.Validation("notes", $"The notes have at most {NotesMaxLength} characters.", "PUR-002");
        }

        foreach (var (lineNumber, date) in promisedDates ?? new Dictionary<int, DateOnly>())
        {
            var field = $"lines[{lineNumber.ToString(NumberFormatInfo.InvariantInfo)}].promisedDeliveryDate";
            if (lines.All(line => line.LineNumber != lineNumber))
                return PurchasingErrors.Validation(field, "The line is not part of the order.", "PUR-002");
            if (date < today)
                return PurchasingErrors.Validation(field, "The promised delivery date cannot be in the past.", "PUR-002");
        }

        if (address is not null)
            DeliveryAddress = address;
        if (trimmedNotes is not null)
            Notes = trimmedNotes.Length == 0 ? null : trimmedNotes;
        foreach (var (lineNumber, date) in promisedDates ?? new Dictionary<int, DateOnly>())
            lines.Single(line => line.LineNumber == lineNumber).ChangePromisedDate(date);
        Append(ActionUpdated, Status, Status, actorId, nowUtc, null, null, correlationId, null, null);
        Touch(nowUtc);
        return null;
    }

    /// <summary>
    /// PUR-003 guards that depend on other modules' current state (supplier <c>Active</c>, approval <c>Approved</c>,
    /// reservation <c>Committed</c>) plus the complete snapshot; the supplier guard answers <c>supplier.not-eligible</c>.
    /// </summary>
    public DomainError? CheckIssuable(bool supplierActive, bool approvalApproved, bool reservationCommitted)
    {
        if (Status != PurchaseOrderStatus.Draft)
            return DomainError.InvalidTransition(Status.ToString(), "Only a Draft order is issued.", "PUR-003");
        if (!supplierActive)
            return new DomainError("supplier.not-eligible", DomainErrorKind.BusinessRule,
                "Only an Active supplier receives a new purchase order (SUP-003).", "PUR-003", "supplierId");
        var missing = new List<string>();
        if (!approvalApproved)
            missing.Add("approval");
        if (!reservationCommitted)
            missing.Add("reservation");
        if (string.IsNullOrWhiteSpace(DeliveryAddress))
            missing.Add("deliveryAddress");
        if (string.IsNullOrWhiteSpace(PaymentTerms))
            missing.Add("paymentTerms");
        if (lines.Count == 0)
            missing.Add("lines");
        return missing.Count == 0 ? null : PurchasingErrors.NotIssuable(missing);
    }

    /// <summary>
    /// PUR-003/005/007: <c>Draft → Issued</c> with the number allocated in the same transaction, the issue local date,
    /// the queued document job and the issuing buyer's name snapshot. Repeating the issue returns <c>Changed = false</c> with the same number (PUR-006).
    /// </summary>
    public (bool Changed, DomainError? Error) Issue(
        string number, DateOnly issueDate, Guid documentJobId, bool supplierActive, bool approvalApproved, bool reservationCommitted, Guid? actorId,
        DateTime nowUtc, string correlationId, string? buyerName = null)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        MasterDataRules.EnsureId(documentJobId, nameof(documentJobId));
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        if (Status != PurchaseOrderStatus.Draft && Number is not null)
            return (false, null);
        if (CheckIssuable(supplierActive, approvalApproved, reservationCommitted) is { } error)
            return (false, error);

        Number = number;
        IssueDate = issueDate;
        IssuedAtUtc = nowUtc;
        IssuedBy = actorId;
        // The issuing buyer is the buyer of record printed on the document (D32-3).
        if (actorId is { } issuer)
        {
            BuyerId = issuer;
            BuyerName = Clip(buyerName) ?? BuyerName;
        }

        DocumentStatus = PurchaseOrderDocumentStatus.Pending;
        DocumentJobId = documentJobId;
        DocumentAttempts = 1;
        Transition(ActionIssued, PurchaseOrderStatus.Issued, actorId, nowUtc, null, null, correlationId, documentJobId,
            new PurchaseOrderIssued(nowUtc, Id, number, SupplierId, TotalMoney, RequisitionId));
        Touch(nowUtc);
        return (true, null);
    }

    /// <summary>
    /// PUR-007: the Worker stored the rendered document of job <paramref name="jobId"/>. Records the file and template
    /// version and queues supplier notification attempt 1. A stale job or a repeated outcome is a no-op.
    /// </summary>
    public bool RecordDocumentReady(Guid jobId, Guid fileId, string templateVersion, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        MasterDataRules.EnsureId(fileId, nameof(fileId));
        ArgumentException.ThrowIfNullOrWhiteSpace(templateVersion);
        if (DocumentJobId != jobId || DocumentStatus == PurchaseOrderDocumentStatus.Ready || Number is null)
            return false;

        DocumentStatus = PurchaseOrderDocumentStatus.Ready;
        DocumentFileId = fileId;
        DocumentTemplateVersion = templateVersion.Length <= TemplateVersionMaxLength ? templateVersion : templateVersion[..TemplateVersionMaxLength];
        DocumentFailureCode = null;
        DocumentReadyAtUtc = nowUtc;
        Append(ActionDocumentReady, Status, Status, null, nowUtc, null, null, correlationId, "procurement.report.ready", fileId);
        RequestNotification(nowUtc);
        Touch(nowUtc);
        return true;
    }

    /// <summary>PUR-007: the document job ended <c>Failed</c>; the order stays issued and the failure is visible and retryable.</summary>
    public bool RecordDocumentFailed(Guid jobId, string failureCode, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        if (DocumentJobId != jobId || DocumentStatus != PurchaseOrderDocumentStatus.Pending)
            return false;

        DocumentStatus = PurchaseOrderDocumentStatus.Failed;
        DocumentFailureCode = failureCode.Length <= FailureCodeMaxLength ? failureCode : failureCode[..FailureCodeMaxLength];
        Append(ActionDocumentFailed, Status, Status, null, nowUtc, null, DocumentFailureCode, correlationId, "procurement.report.failed", jobId);
        Touch(nowUtc);
        return true;
    }

    /// <summary>PUR-007 retry: a failed document gets a new report job; the number and state never change.</summary>
    public DomainError? RetryDocument(Guid newJobId, Guid? actorId, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        MasterDataRules.EnsureId(newJobId, nameof(newJobId));
        if (DocumentStatus != PurchaseOrderDocumentStatus.Failed || Status == PurchaseOrderStatus.Cancelled)
            return PurchasingErrors.NotRetryable("document", DocumentStatus.ToString());

        DocumentStatus = PurchaseOrderDocumentStatus.Pending;
        DocumentJobId = newJobId;
        DocumentAttempts++;
        DocumentFailureCode = null;
        Append(ActionDocumentRetried, Status, Status, actorId, nowUtc, null, null, correlationId, null, newJobId);
        Touch(nowUtc);
        return null;
    }

    /// <summary>PUR-007: the Worker delivered notification attempt <paramref name="attempt"/>; stale or repeated outcomes are no-ops.</summary>
    public bool RecordNotificationSent(int attempt, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (attempt != NotificationAttempts || NotificationStatus != PurchaseOrderNotificationStatus.Pending)
            return false;

        NotificationStatus = PurchaseOrderNotificationStatus.Sent;
        NotificationFailureCode = null;
        NotifiedAtUtc = nowUtc;
        Append(ActionNotificationSent, Status, Status, null, nowUtc, null, null, correlationId, null, SupplierId);
        Touch(nowUtc);
        return true;
    }

    public bool RecordNotificationFailed(int attempt, string failureCode, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        if (attempt != NotificationAttempts || NotificationStatus != PurchaseOrderNotificationStatus.Pending)
            return false;

        NotificationStatus = PurchaseOrderNotificationStatus.Failed;
        NotificationFailureCode = failureCode.Length <= FailureCodeMaxLength ? failureCode : failureCode[..FailureCodeMaxLength];
        Append(ActionNotificationFailed, Status, Status, null, nowUtc, null, NotificationFailureCode, correlationId, null, SupplierId);
        Touch(nowUtc);
        return true;
    }

    /// <summary>PUR-007 retry: a failed notification is queued again as the next attempt.</summary>
    public DomainError? RetryNotification(Guid? actorId, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (NotificationStatus != PurchaseOrderNotificationStatus.Failed || Status == PurchaseOrderStatus.Cancelled)
            return PurchasingErrors.NotRetryable("notification", NotificationStatus.ToString());

        Append(ActionNotificationRetried, Status, Status, actorId, nowUtc, null, null, correlationId, null, SupplierId);
        RequestNotification(nowUtc);
        Touch(nowUtc);
        return null;
    }

    /// <summary>
    /// PUR-008 with a 10–500 character reason: a <c>Draft</c> cancels for re-award; an <c>Issued</c> order only while no
    /// quantity was accepted (<c>purchase-order.has-receipts</c> otherwise). Returns the state the order left;
    /// <c>Changed = false</c> when it was already cancelled.
    /// </summary>
    public (PurchaseOrderStatus From, bool Changed, DomainError? Error) Cancel(string? reason, Guid? actorId, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (Status == PurchaseOrderStatus.Cancelled)
            return (Status, false, null);
        var normalizedReason = MasterDataRules.NormalizeReason(reason);
        if (normalizedReason is null)
            return (Status, false, DomainError.ReasonRequired("PUR-008", MasterDataRules.ReasonMinLength, MasterDataRules.ReasonMaxLength));
        if (HasAcceptedQuantity || Status is PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Completed)
            return (Status, false, PurchasingErrors.HasReceipts);

        var from = Status;
        CancelledAtUtc = nowUtc;
        CancelledBy = actorId;
        CancellationReason = normalizedReason;
        var reasonCode = from == PurchaseOrderStatus.Draft ? "DraftCancelled" : "IssuedCancelled";
        Transition(ActionCancelled, PurchaseOrderStatus.Cancelled, actorId, nowUtc, normalizedReason, reasonCode, correlationId, null,
            new PurchaseOrderCancelled(nowUtc, Id, RequisitionId, reasonCode));
        Touch(nowUtc);
        return (from, true, null);
    }

    /// <summary>
    /// RCV-001/003: applies a posted receipt. Every line must belong to the order and keep the accepted total within
    /// the CALC-009 cap; nothing changes on a rejection. Completion is then recomputed (PUR-009).
    /// </summary>
    public (OrderCompletionChange Change, DomainError? Error) ApplyReceipt(
        Guid receiptId, IReadOnlyList<OrderReceiptLine> receiptLines, decimal tolerancePercent, Guid? actorId, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        MasterDataRules.EnsureId(receiptId, nameof(receiptId));
        ArgumentNullException.ThrowIfNull(receiptLines);
        if (!IsReceivable)
            return (OrderCompletionChange.None, PurchasingErrors.OrderNotReceivable);
        foreach (var group in receiptLines.GroupBy(line => line.LineNumber))
        {
            var line = lines.FirstOrDefault(candidate => candidate.LineNumber == group.Key);
            if (line is null)
                return (OrderCompletionChange.None, PurchasingErrors.Validation(
                    $"lines[{group.Key.ToString(NumberFormatInfo.InvariantInfo)}].lineNumber", "The line is not part of the order.", "RCV-002"));
            if (line.IsShortClosed)
                return (OrderCompletionChange.None, PurchasingErrors.OverReceipt(line.LineNumber, line.AcceptedQuantity, line.AcceptedQuantity + group.Sum(item => item.Accepted)));
            var resulting = line.AcceptedQuantity + group.Sum(item => item.Accepted);
            var cap = line.Cap(tolerancePercent);
            if (resulting > cap)
                return (OrderCompletionChange.None, PurchasingErrors.OverReceipt(line.LineNumber, cap, resulting));
        }

        foreach (var receiptLine in receiptLines)
            lines.Single(line => line.LineNumber == receiptLine.LineNumber).AddReceipt(receiptLine.Received, receiptLine.Accepted, receiptLine.Rejected);
        Append(ActionReceiptPosted, Status, Status, actorId, nowUtc, null, null, correlationId, "procurement.receipt.posted", receiptId);
        var change = Recompute(actorId, nowUtc, correlationId);
        Touch(nowUtc);
        return (change, null);
    }

    /// <summary>
    /// RCV-007: applies a compensating accepted-quantity delta; the resulting accepted total stays between zero and
    /// the cap. A short-closed order takes no correction (D32-9). Completion is recomputed (PUR-009).
    /// </summary>
    public (OrderCompletionChange Change, DomainError? Error) ApplyCorrection(
        Guid receiptId, int lineNumber, decimal acceptedDelta, decimal tolerancePercent, Guid? actorId, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Cancelled || ShortClosed)
            return (OrderCompletionChange.None, DomainError.InvalidTransition(
                ShortClosed ? "ShortClosed" : Status.ToString(), "Only an issued, not short-closed order takes receipt corrections.", "RCV-007"));
        var line = lines.FirstOrDefault(candidate => candidate.LineNumber == lineNumber);
        if (line is null)
            return (OrderCompletionChange.None, PurchasingErrors.Validation("poLineNumber", "The line is not part of the order.", "RCV-007"));
        var resulting = line.AcceptedQuantity + acceptedDelta;
        var cap = line.Cap(tolerancePercent);
        if (acceptedDelta == 0 || resulting < 0 || resulting > cap)
            return (OrderCompletionChange.None, PurchasingErrors.CorrectionOutOfRange(lineNumber, cap, resulting));

        line.Correct(acceptedDelta);
        Append(ActionReceiptCorrected, Status, Status, actorId, nowUtc, null, null, correlationId, "procurement.receipt.corrected", receiptId);
        var change = Recompute(actorId, nowUtc, correlationId);
        Touch(nowUtc);
        return (change, null);
    }

    /// <summary>
    /// PUR-010: <c>PartiallyReceived → Completed</c> with the <c>ShortClose</c> waiver reason; unreceived lines are
    /// short-closed and the CALC-008 release (capped at <paramref name="committedAmount"/>) is returned for the budget.
    /// </summary>
    public (Money Release, DomainError? Error) ShortClose(string? reason, decimal committedAmount, Guid? actorId, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        var normalizedReason = MasterDataRules.NormalizeReason(reason);
        if (normalizedReason is null)
            return (Money.Zero(RequisitionCurrency), DomainError.ReasonRequired("PUR-010", MasterDataRules.ReasonMinLength, MasterDataRules.ReasonMaxLength));
        if (Status != PurchaseOrderStatus.PartiallyReceived)
            return (Money.Zero(RequisitionCurrency), DomainError.InvalidTransition(Status.ToString(), "Only a PartiallyReceived order is short-closed.", "PUR-010"));

        var release = CalculationRules.ShortCloseRelease(
            lines.Where(line => !line.IsComplete).Select(line => (line.Quantity, line.AcceptedQuantity, line.UnitPrice)), Currency, QuoteToBaseRate,
            RequisitionCurrency, RequisitionToBaseRate, BaseCurrency, committedAmount);
        foreach (var line in lines.Where(line => !line.IsComplete))
            line.ShortClose();
        ShortClosed = true;
        ShortClosedBy = actorId;
        ShortCloseReason = normalizedReason;
        ShortCloseReleasedAmount = release.Amount;
        CompletedAtUtc = nowUtc;
        CompletionSequence++;
        Transition(ActionShortClosed, PurchaseOrderStatus.Completed, actorId, nowUtc, normalizedReason, "ShortClosed", correlationId, null,
            new PurchaseOrderCompleted(nowUtc, Id, RequisitionId, true, CompletionSequence));
        Touch(nowUtc);
        return (release, null);
    }

    /// <summary>PUR-009: Completed when every line is satisfied, PartiallyReceived when something was accepted, else Issued.</summary>
    private OrderCompletionChange Recompute(Guid? actorId, DateTime nowUtc, string correlationId)
    {
        var target = lines.All(line => line.IsComplete)
            ? PurchaseOrderStatus.Completed
            : lines.Any(line => line.AcceptedQuantity > 0) ? PurchaseOrderStatus.PartiallyReceived : PurchaseOrderStatus.Issued;
        if (target == Status)
            return OrderCompletionChange.None;

        var wasCompleted = Status == PurchaseOrderStatus.Completed;
        if (target == PurchaseOrderStatus.Completed)
        {
            CompletionSequence++;
            CompletedAtUtc = nowUtc;
            Transition(ActionCompleted, target, actorId, nowUtc, null, null, correlationId, null,
                new PurchaseOrderCompleted(nowUtc, Id, RequisitionId, false, CompletionSequence));
            return OrderCompletionChange.Completed;
        }

        if (wasCompleted)
        {
            CompletedAtUtc = null;
            Transition(ActionReopened, target, actorId, nowUtc, null, null, correlationId, null,
                new PurchaseOrderReopened(nowUtc, Id, RequisitionId, CompletionSequence));
            return OrderCompletionChange.Reopened;
        }

        var from = Status;
        Status = target;
        Append(target == PurchaseOrderStatus.PartiallyReceived ? "partially-received" : "receipts-reverted", from, target, actorId, nowUtc, null, null,
            correlationId, null, null);
        return OrderCompletionChange.None;
    }

    private void RequestNotification(DateTime nowUtc)
    {
        NotificationStatus = PurchaseOrderNotificationStatus.Pending;
        NotificationAttempts++;
        NotificationFailureCode = null;
        events.Raise(new PurchaseOrderNotificationRequested(nowUtc, Id, SupplierId, DocumentFileId!.Value, NotificationAttempts));
    }

    private void Transition(
        string action, PurchaseOrderStatus to, Guid? actorId, DateTime nowUtc, string? reason, string? reasonCode, string correlationId, Guid? subjectId,
        DomainEvent domainEvent)
    {
        var from = Status;
        Status = to;
        Append(action, from, to, actorId, nowUtc, reason, reasonCode, correlationId, domainEvent.EventType, subjectId);
        events.Raise(domainEvent);
    }

    private void Append(
        string action, PurchaseOrderStatus? from, PurchaseOrderStatus to, Guid? actorId, DateTime nowUtc, string? reason, string? reasonCode,
        string correlationId, string? eventType, Guid? subjectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        history.Add(new PurchaseOrderHistoryEntry(
            Guid.NewGuid(), Id, history.Count + 1, action, from, to, actorId, nowUtc, reason, reasonCode, correlationId, eventType, subjectId));
    }

    private void Touch(DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        IncrementVersion();
    }

    private static string? Clip(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= SnapshotTextMaxLength ? value : value[..SnapshotTextMaxLength];
}
