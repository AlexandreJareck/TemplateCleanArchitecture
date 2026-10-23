using System.Text.Json.Serialization;
using Template.Application.Features.Sourcing;
using Template.Domain.Sourcing;

namespace Template.WebApi.Controllers.v2;

/// <summary>New RFQ round for a <c>Submitted</c> requisition; <c>method</c> is <c>Competitive</c> (default) or <c>Direct</c>.</summary>
public sealed record CreateRfqRequest(Guid RequisitionId, string? Method);

public sealed record InviteSupplierRequest(Guid SupplierId);

/// <summary>Publication: UTC deadline 24 h–30 days ahead (SRC-004); a <c>Direct</c> RFQ also needs the waiver reason (SRC-006).</summary>
public sealed record PublishRfqRequest(DateTime? Deadline, string? WaiverReason);

/// <summary>New UTC deadline (later, at most +30 days) and a 10–500 character reason (SRC-011).</summary>
public sealed record ExtendRfqDeadlineRequest(DateTime? Deadline, string? Reason);

/// <summary>Close; the reason (10–500 characters) is required only before the deadline (SRC-012).</summary>
public sealed record CloseRfqRequest(string? Reason);

/// <summary>Manual rate: base units per one unit of <c>currency</c> as a decimal string (≤ 6 decimals), its date and the waiver reason (SRC-013).</summary>
public sealed record ManualExchangeRateRequest(string? Currency, string? Rate, DateOnly? QuoteDate, string? Reason);

/// <summary>
/// Award of one quote revision for the whole RFQ (D23-15). Waiver reasons are needed only when the server answers
/// <c>award.insufficient-quotations</c> (SRC-017) or <c>award.non-leading-requires-waiver</c> (SRC-018).
/// </summary>
public sealed record SubmitAwardRequest(Guid QuoteRevisionId, string? Justification, string? InsufficientQuotationsReason, string? NonLeadingReason);

public sealed record QuoteLineBody(int RfqLineNumber, string? QuantityOffered, string? UnitPrice);

public sealed record QuoteAttachmentBody(string? FileName, string? MediaType, long Length);

/// <summary>
/// Buyer-entered quote (D23-3): currency, offered lines (decimal strings), optional freight/discount/taxes-included
/// amounts, validity date, delivery days, payment terms, notes, source and the UTC instant the quote was received.
/// Totals are server-computed (CALC-002); taxes are an explicit quoted amount only (no tax engine). <c>supplierId</c>
/// names the invited supplier when a quote is entered and is ignored for revisions.
/// </summary>
public sealed record QuoteBody(
    Guid? SupplierId,
    string? Currency,
    IReadOnlyList<QuoteLineBody>? Lines,
    string? Freight,
    string? Discount,
    string? TaxesIncluded,
    DateOnly? ValidUntil,
    int? DeliveryDays,
    string? PaymentTerms,
    string? Notes,
    [property: JsonConverter(typeof(JsonStringEnumConverter<QuoteSource>))] QuoteSource? Source,
    DateTime? ReceivedAt,
    IReadOnlyList<QuoteAttachmentBody>? Attachments)
{
    public QuoteRequest ToRequest() => new(
        Currency,
        Lines?.Select(line => new QuoteLineRequest(line.RfqLineNumber, line.QuantityOffered, line.UnitPrice)).ToArray(),
        Freight,
        Discount,
        TaxesIncluded,
        ValidUntil,
        DeliveryDays,
        PaymentTerms,
        Notes,
        Source,
        ReceivedAt,
        Attachments?.Select(attachment => new QuoteAttachmentRequest(attachment.FileName, attachment.MediaType, attachment.Length)).ToArray());
}
