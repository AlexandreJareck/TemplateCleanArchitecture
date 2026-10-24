using System.Collections.Generic;
using Template.Domain.Common;

namespace Template.Domain.Purchasing;

/// <summary>Stable rejections of the Purchasing module (PUR-*) and the order-side receipt rules (RCV-001/003/007), docs/product/business-rules.md.</summary>
public static class PurchasingErrors
{
    public const string ImmutableCode = "purchase-order.immutable";
    public const string NotIssuableCode = "purchase-order.not-issuable";
    public const string HasReceiptsCode = "purchase-order.has-receipts";
    public const string DocumentNotReadyCode = "purchase-order.document-not-ready";
    public const string NotRetryableCode = "purchase-order.not-retryable";
    public const string OrderNotReceivableCode = "receipt.order-not-receivable";
    public const string OverReceiptCode = "receipt.over-receipt";
    public const string CorrectionOutOfRangeCode = "receipt.correction-out-of-range";

    public static readonly DomainError Immutable = new(
        ImmutableCode, DomainErrorKind.Conflict,
        "The commercial snapshot is immutable; only the delivery address, promised dates and notes of a Draft order change.", "PUR-002");

    public static readonly DomainError HasReceipts = new(
        HasReceiptsCode, DomainErrorKind.Conflict, "An order with accepted quantity cannot be cancelled; short-close it instead.", "PUR-008");

    public static readonly DomainError OrderNotReceivable = new(
        OrderNotReceivableCode, DomainErrorKind.BusinessRule, "Receipts reference an Issued or PartiallyReceived order.", "RCV-001");

    /// <summary>PUR-003: every failed guard is listed in the <c>fields</c> detail.</summary>
    public static DomainError NotIssuable(IEnumerable<string> missing) => new(
        NotIssuableCode, DomainErrorKind.BusinessRule, "The order does not meet the issue requirements.", "PUR-003",
        Details: new Dictionary<string, string> { ["fields"] = string.Join(',', missing) });

    public static DomainError OverReceipt(int lineNumber, decimal cap, decimal resulting) => new(
        OverReceiptCode, DomainErrorKind.BusinessRule, "The accepted quantity would exceed the receipt cap of the line.", "RCV-003",
        Details: new Dictionary<string, string>
        {
            ["lineNumber"] = lineNumber.ToString(System.Globalization.NumberFormatInfo.InvariantInfo),
            ["cap"] = cap.ToString(System.Globalization.NumberFormatInfo.InvariantInfo),
            ["resulting"] = resulting.ToString(System.Globalization.NumberFormatInfo.InvariantInfo)
        });

    public static DomainError CorrectionOutOfRange(int lineNumber, decimal cap, decimal resulting) => new(
        CorrectionOutOfRangeCode, DomainErrorKind.BusinessRule, "The corrected accepted total must stay between zero and the receipt cap.", "RCV-007",
        Details: new Dictionary<string, string>
        {
            ["lineNumber"] = lineNumber.ToString(System.Globalization.NumberFormatInfo.InvariantInfo),
            ["cap"] = cap.ToString(System.Globalization.NumberFormatInfo.InvariantInfo),
            ["resulting"] = resulting.ToString(System.Globalization.NumberFormatInfo.InvariantInfo)
        });

    public static DomainError NotRetryable(string what, string currentState) => new(
        NotRetryableCode, DomainErrorKind.Conflict, $"The {what} is not in a failed state and cannot be retried.", "PUR-007",
        Details: new Dictionary<string, string> { ["currentState"] = currentState });

    public static DomainError DocumentNotReady(string currentState) => new(
        DocumentNotReadyCode, DomainErrorKind.Conflict, "The purchase-order document is not available.", "PUR-007",
        Details: new Dictionary<string, string> { ["currentState"] = currentState });

    public static DomainError Validation(string field, string message, string? ruleId = null) =>
        new("validation", DomainErrorKind.Validation, message, ruleId, field);
}
