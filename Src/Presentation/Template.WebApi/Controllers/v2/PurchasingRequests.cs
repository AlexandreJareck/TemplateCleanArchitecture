using Template.Application.Features.Purchasing;

namespace Template.WebApi.Controllers.v2;

public sealed record PromisedDateBody(int LineNumber, DateOnly PromisedDeliveryDate);

/// <summary>
/// Draft-only editable fields (PUR-002): delivery address (5–500 characters), notes (≤ 2,000) and per-line promised
/// delivery dates (not in the past). Omitted fields keep their value; any commercial field is immutable.
/// </summary>
public sealed record UpdatePurchaseOrderRequest(string? DeliveryAddress, string? Notes, IReadOnlyList<PromisedDateBody>? PromisedDates)
{
    public IReadOnlyList<PromisedDateChange>? ToChanges() =>
        PromisedDates?.Select(date => new PromisedDateChange(date.LineNumber, date.PromisedDeliveryDate)).ToArray();
}

/// <summary>
/// One received order line (RCV-002/004/008): quantities as invariant decimal strings within the unit decimals,
/// <c>accepted + rejected = received</c>; a rejected quantity needs <c>discrepancyType</c> (<c>Damaged</c>,
/// <c>WrongItem</c>, <c>QualityIssue</c>, <c>Other</c>) and notes.
/// </summary>
public sealed record ReceiptLineBody(int PoLineNumber, string? Received, string? Accepted, string? Rejected, string? DiscrepancyType, string? DiscrepancyNotes);

public sealed record ReceiptAttachmentBody(string? FileName, string? MediaType, long Length);

/// <summary>
/// Receipt of an issued order (RCV-001..005): the receiver is the authenticated actor; <c>receivedOn</c> is an
/// organization-local date between the issue date and today; location, delivery reference, notes and evidence
/// metadata are optional.
/// </summary>
public sealed record PostReceiptRequest(
    Guid PurchaseOrderId,
    DateOnly? ReceivedOn,
    string? Location,
    string? DeliveryReference,
    string? Notes,
    IReadOnlyList<ReceiptLineBody>? Lines,
    IReadOnlyList<ReceiptAttachmentBody>? Attachments)
{
    public PostReceiptCommand ToCommand() => new(
        PurchaseOrderId, ReceivedOn, Location, DeliveryReference, Notes,
        Lines?.Select(line => new ReceiptLineRequest(line.PoLineNumber, line.Received, line.Accepted, line.Rejected, line.DiscrepancyType, line.DiscrepancyNotes))
            .ToArray(),
        Attachments?.Select(attachment => new ReceiptAttachmentRequest(attachment.FileName, attachment.MediaType, attachment.Length)).ToArray());
}

/// <summary>Compensating correction (RCV-007): signed accepted-quantity delta as a decimal string and a 10–500 character reason.</summary>
public sealed record CorrectReceiptRequest(int PoLineNumber, string? AcceptedDelta, string? Reason);
