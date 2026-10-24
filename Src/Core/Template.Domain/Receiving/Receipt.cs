using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Template.Domain.Common;

namespace Template.Domain.Receiving;

/// <summary>Receipts are created <c>Posted</c> and have no editable lifecycle (RCV-006, workflows §5).</summary>
public enum ReceiptStatus
{
    Posted
}

/// <summary>RCV-004 discrepancy types of a rejected quantity.</summary>
public enum DiscrepancyType
{
    Damaged,
    WrongItem,
    QualityIssue,
    Other
}

/// <summary>
/// <c>ReceivingPolicy</c> version 1: over-receipt tolerance 0 % (D23-18). The receipt pins the version it was posted
/// with; a later version never changes posted receipts (POL-001).
/// </summary>
public sealed record ReceivingPolicy(int Version, decimal OverReceiptTolerancePercent)
{
    public const string PolicyType = "ReceivingPolicy";

    public static ReceivingPolicy V1 { get; } = new(1, 0m);

    public string Reference => $"{PolicyType}:{Version.ToString(NumberFormatInfo.InvariantInfo)}";
}

/// <summary>
/// Evidence attachment limits of a receipt: the <c>FilePolicy</c> v1 values (10 attachments, 10 MB, the approved media
/// types). Only metadata is recorded; byte upload is outside Phase 32 like requisition attachments.
/// </summary>
public static class ReceiptEvidencePolicy
{
    public const int MaxAttachments = 10;
    public const long MaxBytes = 10L * 1024 * 1024;

    public static IReadOnlySet<string> AllowedMediaTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/png",
        "image/jpeg",
        "text/plain",
        "text/csv",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    };
}

/// <summary>Order line facts the receipt validates against (PO line number, unit decimals and description snapshot).</summary>
public sealed record ReceiptOrderLine(int LineNumber, string Description, string UnitCode, int UnitAllowedDecimals);

public sealed record ReceiptLineInput(int PoLineNumber, decimal Received, decimal Accepted, decimal Rejected, DiscrepancyType? Discrepancy, string? DiscrepancyNotes);

public sealed record ReceiptAttachmentInput(string? FileName, string? MediaType, long Length);

/// <summary>Header facts of a posting (RCV-005): the authenticated receiver, the local receipt date and its window.</summary>
public sealed record ReceiptPosting(
    Guid PurchaseOrderId,
    string PurchaseOrderNumber,
    IReadOnlyList<ReceiptOrderLine> OrderLines,
    DateOnly IssueDate,
    DateOnly Today,
    Guid ReceiverId,
    DateOnly ReceivedOn,
    string? Location,
    string? DeliveryReference,
    string? Notes,
    IReadOnlyList<ReceiptLineInput> Lines,
    IReadOnlyList<ReceiptAttachmentInput> Attachments);

/// <summary>
/// Posted receipt of an issued order (RCV-001..008): receiver, date, location and delivery reference, per-line
/// received/accepted/rejected quantities with discrepancy details, evidence metadata and the pinned receiving policy.
/// It is never edited or deleted (RCV-006); a correction appends a compensating accepted-quantity delta with reason
/// and actor (RCV-007). The order-side cap (RCV-003) is enforced by the purchase order in the same commit.
/// </summary>
public sealed class Receipt : VersionedAggregateRoot
{
    public const int NumberMaxLength = 20;
    public const int LocationMaxLength = 200;
    public const int DeliveryReferenceMaxLength = 100;
    public const int NotesMaxLength = 2000;
    public const int DiscrepancyNotesMaxLength = 1000;
    public const int MaxLines = 100;

    private readonly List<ReceiptLine> lines = [];
    private readonly List<ReceiptCorrection> corrections = [];
    private readonly List<ReceiptAttachment> attachments = [];
    private readonly DomainEventBuffer events = new();

    private Receipt()
    {
        Number = string.Empty;
        PurchaseOrderNumber = string.Empty;
        PolicyReference = string.Empty;
    }

    /// <summary><c>RCV-{yyyy}-{nnnnnn}</c> (gaps allowed, like requisition numbers).</summary>
    public string Number { get; private set; }

    public Guid PurchaseOrderId { get; private set; }
    public string PurchaseOrderNumber { get; private set; }
    public Guid ReceiverId { get; private set; }
    public DateOnly ReceivedOn { get; private set; }
    public string? Location { get; private set; }
    public string? DeliveryReference { get; private set; }
    public string? Notes { get; private set; }
    public ReceiptStatus Status { get; private set; }
    public string PolicyReference { get; private set; }
    public decimal TolerancePercent { get; private set; }
    public bool HasDiscrepancy { get; private set; }
    public DateTime PostedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<ReceiptLine> Lines => lines;
    public IReadOnlyCollection<ReceiptCorrection> Corrections => corrections;
    public IReadOnlyCollection<ReceiptAttachment> Attachments => attachments;
    public IReadOnlyList<DomainEvent> DomainEvents => events.Events;

    public void ClearDomainEvents() => events.Clear();

    public static string FormatNumber(int year, long sequence) =>
        $"RCV-{year.ToString("D4", NumberFormatInfo.InvariantInfo)}-{sequence.ToString("D6", NumberFormatInfo.InvariantInfo)}";

    /// <summary>RCV-002/004/005 validation and posting; nothing is created on a rejection.</summary>
    public static (Receipt? Receipt, DomainError? Error) Post(
        Guid id, string number, ReceiptPosting posting, ReceivingPolicy policy, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureId(id, nameof(id));
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentNullException.ThrowIfNull(posting);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        MasterDataRules.EnsureId(posting.PurchaseOrderId, nameof(posting.PurchaseOrderId));
        if (posting.ReceiverId == Guid.Empty)
            return (null, ReceivingErrors.Validation("receiverId", "The receiver is the authenticated actor.", "RCV-005"));
        if (posting.ReceivedOn < posting.IssueDate || posting.ReceivedOn > posting.Today)
            return (null, ReceivingErrors.Validation("receivedOn", "The receipt date is between the order issue date and today.", "RCV-005"));
        var (location, locationError) = Optional(posting.Location, LocationMaxLength, "location");
        var (reference, referenceError) = Optional(posting.DeliveryReference, DeliveryReferenceMaxLength, "deliveryReference");
        var (notes, notesError) = Optional(posting.Notes, NotesMaxLength, "notes");
        if ((locationError ?? referenceError ?? notesError) is { } headerError)
            return (null, headerError);
        if (posting.Lines is not { Count: > 0 } || posting.Lines.Count > MaxLines)
            return (null, ReceivingErrors.Validation("lines", $"A receipt has 1 to {MaxLines} lines.", "RCV-002"));
        if (posting.Lines.Select(line => line.PoLineNumber).Distinct().Count() != posting.Lines.Count)
            return (null, ReceivingErrors.Validation("lines", "Each order line appears at most once per receipt.", "RCV-002"));

        var orderLines = posting.OrderLines.ToDictionary(line => line.LineNumber);
        var receipt = new Receipt
        {
            Id = id,
            Number = number,
            PurchaseOrderId = posting.PurchaseOrderId,
            PurchaseOrderNumber = posting.PurchaseOrderNumber,
            ReceiverId = posting.ReceiverId,
            ReceivedOn = posting.ReceivedOn,
            Location = location,
            DeliveryReference = reference,
            Notes = notes,
            Status = ReceiptStatus.Posted,
            PolicyReference = policy.Reference,
            TolerancePercent = policy.OverReceiptTolerancePercent,
            PostedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
        foreach (var input in posting.Lines.OrderBy(line => line.PoLineNumber))
        {
            var field = $"lines[{input.PoLineNumber.ToString(NumberFormatInfo.InvariantInfo)}]";
            if (!orderLines.TryGetValue(input.PoLineNumber, out var orderLine))
                return (null, ReceivingErrors.Validation($"{field}.poLineNumber", "The line is not part of the order.", "RCV-002"));
            if (LineError(input, orderLine, field) is { } lineError)
                return (null, lineError);
            var discrepancyNotes = input.Rejected > 0 ? input.DiscrepancyNotes!.Trim() : null;
            receipt.lines.Add(new ReceiptLine(Guid.NewGuid(), id, orderLine, input, input.Rejected > 0 ? input.Discrepancy : null, discrepancyNotes));
        }

        if (posting.Attachments.Count > ReceiptEvidencePolicy.MaxAttachments)
            return (null, ReceivingErrors.Validation("attachments", $"A receipt has at most {ReceiptEvidencePolicy.MaxAttachments} attachments.", "FIL-002"));
        foreach (var attachment in posting.Attachments)
        {
            var name = attachment.FileName?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > ReceiptAttachment.FileNameMaxLength || name.IndexOfAny(['/', '\\']) >= 0)
                return (null, ReceivingErrors.Validation("attachments.fileName", "The file name must have 1 to 255 characters without path separators.", "FIL-002"));
            if (attachment.MediaType is null || !ReceiptEvidencePolicy.AllowedMediaTypes.Contains(attachment.MediaType))
                return (null, ReceivingErrors.FileTypeNotAllowed);
            if (attachment.Length <= 0 || attachment.Length > ReceiptEvidencePolicy.MaxBytes)
                return (null, ReceivingErrors.FileTooLarge);
            receipt.attachments.Add(new ReceiptAttachment(Guid.NewGuid(), id, name, attachment.MediaType.ToLowerInvariant(), attachment.Length,
                posting.ReceiverId, nowUtc));
        }

        receipt.HasDiscrepancy = receipt.lines.Any(line => line.RejectedQuantity > 0);
        receipt.events.Raise(new ReceiptPosted(nowUtc, id, posting.PurchaseOrderId, receipt.lines.Count, receipt.HasDiscrepancy));
        if (receipt.HasDiscrepancy)
            receipt.events.Raise(new ReceiptDiscrepancyRecorded(nowUtc, id, posting.PurchaseOrderId,
                receipt.lines.Where(line => line.RejectedQuantity > 0).Select(line => line.PoLineNumber).ToArray()));
        return (receipt, null);
    }

    /// <summary>
    /// RCV-007: appends a compensating accepted-quantity delta for one line of this receipt with a 10–500 character
    /// reason. The line's corrected accepted quantity stays between zero and its received quantity, so a correction can
    /// accept previously rejected goods (resolving a discrepancy) or reverse an acceptance. The purchase order checks the
    /// order-level cap in the same commit.
    /// </summary>
    public (ReceiptCorrection? Correction, DomainError? Error) Correct(
        Guid correctionId, int poLineNumber, decimal acceptedDelta, string? reason, Guid actorId, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureId(correctionId, nameof(correctionId));
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        var normalizedReason = MasterDataRules.NormalizeReason(reason);
        if (normalizedReason is null)
            return (null, DomainError.ReasonRequired("RCV-007", MasterDataRules.ReasonMinLength, MasterDataRules.ReasonMaxLength));
        if (actorId == Guid.Empty)
            return (null, ReceivingErrors.Validation("actor", "A correction is posted by an authenticated actor.", "RCV-007"));
        var line = lines.FirstOrDefault(candidate => candidate.PoLineNumber == poLineNumber);
        if (line is null)
            return (null, ReceivingErrors.Validation("poLineNumber", "The line is not part of this receipt.", "RCV-007"));
        var places = DecimalPrecision.Places(acceptedDelta);
        if (acceptedDelta == 0 || places > Math.Min(line.UnitAllowedDecimals, CalculationRules.MaxQuantityDecimals))
            return (null, ReceivingErrors.Validation("acceptedDelta",
                $"The delta is non-zero with at most {Math.Min(line.UnitAllowedDecimals, CalculationRules.MaxQuantityDecimals)} decimals.", "RCV-007"));
        var resulting = CorrectedAccepted(line) + acceptedDelta;
        if (resulting < 0 || resulting > line.ReceivedQuantity)
            return (null, ReceivingErrors.CorrectionOutOfRange(poLineNumber, line.ReceivedQuantity, resulting));

        var correction = new ReceiptCorrection(correctionId, Id, corrections.Count + 1, poLineNumber, acceptedDelta, normalizedReason, actorId, nowUtc,
            correlationId);
        corrections.Add(correction);
        UpdatedAtUtc = nowUtc;
        IncrementVersion();
        events.Raise(new ReceiptCorrected(nowUtc, Id, correctionId, PurchaseOrderId, poLineNumber, acceptedDelta));
        return (correction, null);
    }

    /// <summary>Accepted quantity of a line after this receipt's corrections.</summary>
    public decimal CorrectedAccepted(ReceiptLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return line.AcceptedQuantity + corrections.Where(correction => correction.PoLineNumber == line.PoLineNumber).Sum(correction => correction.AcceptedDelta);
    }

    private static DomainError? LineError(ReceiptLineInput input, ReceiptOrderLine orderLine, string field)
    {
        var decimals = Math.Min(orderLine.UnitAllowedDecimals, CalculationRules.MaxQuantityDecimals);
        foreach (var (name, value) in new[] { ("received", input.Received), ("accepted", input.Accepted), ("rejected", input.Rejected) })
        {
            if (value < 0 || value > CalculationRules.MaxAmount || DecimalPrecision.Places(value) > decimals)
                return ReceivingErrors.Validation($"{field}.{name}", $"Quantities are not negative with at most {decimals} decimals (RCV-008).", "RCV-002");
        }

        if (input.Received <= 0)
            return ReceivingErrors.Validation($"{field}.received", "The received quantity is greater than zero.", "RCV-002");
        if (input.Accepted + input.Rejected != input.Received)
            return ReceivingErrors.Validation($"{field}.accepted", "Accepted plus rejected equals received.", "RCV-002");
        if (input.Rejected > 0)
        {
            if (input.Discrepancy is not { } type || !Enum.IsDefined(type))
                return ReceivingErrors.Validation($"{field}.discrepancyType", "A rejected quantity needs a discrepancy type.", "RCV-004");
            var notes = input.DiscrepancyNotes?.Trim();
            if (string.IsNullOrEmpty(notes) || notes.Length > DiscrepancyNotesMaxLength)
                return ReceivingErrors.Validation($"{field}.discrepancyNotes",
                    $"A rejected quantity needs discrepancy notes of 1 to {DiscrepancyNotesMaxLength} characters.", "RCV-004");
        }

        return null;
    }

    private static (string? Value, DomainError? Error) Optional(string? value, int maxLength, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return (null, null);
        return trimmed.Length > maxLength
            ? (null, ReceivingErrors.Validation(field, $"The {field} has at most {maxLength} characters.", "RCV-005"))
            : (trimmed, null);
    }
}

/// <summary>Posted quantities of one order line (RCV-002) with the description and unit snapshot.</summary>
public sealed class ReceiptLine
{
    private ReceiptLine()
    {
        Description = string.Empty;
        UnitCode = string.Empty;
    }

    internal ReceiptLine(Guid id, Guid receiptId, ReceiptOrderLine orderLine, ReceiptLineInput input, DiscrepancyType? discrepancy, string? discrepancyNotes)
        : this()
    {
        Id = id;
        ReceiptId = receiptId;
        PoLineNumber = orderLine.LineNumber;
        Description = orderLine.Description;
        UnitCode = orderLine.UnitCode;
        UnitAllowedDecimals = orderLine.UnitAllowedDecimals;
        ReceivedQuantity = input.Received;
        AcceptedQuantity = input.Accepted;
        RejectedQuantity = input.Rejected;
        DiscrepancyType = discrepancy;
        DiscrepancyNotes = discrepancyNotes;
    }

    public Guid Id { get; private set; }
    public Guid ReceiptId { get; private set; }
    public int PoLineNumber { get; private set; }
    public string Description { get; private set; }
    public string UnitCode { get; private set; }
    public int UnitAllowedDecimals { get; private set; }
    public decimal ReceivedQuantity { get; private set; }
    public decimal AcceptedQuantity { get; private set; }
    public decimal RejectedQuantity { get; private set; }
    public DiscrepancyType? DiscrepancyType { get; private set; }
    public string? DiscrepancyNotes { get; private set; }
}

/// <summary>Append-only compensating record (RCV-007).</summary>
public sealed class ReceiptCorrection
{
    public const int CorrelationIdMaxLength = 128;

    private ReceiptCorrection()
    {
        Reason = string.Empty;
        CorrelationId = string.Empty;
    }

    internal ReceiptCorrection(
        Guid id, Guid receiptId, int sequence, int poLineNumber, decimal acceptedDelta, string reason, Guid actorId, DateTime atUtc, string correlationId)
        : this()
    {
        Id = id;
        ReceiptId = receiptId;
        Sequence = sequence;
        PoLineNumber = poLineNumber;
        AcceptedDelta = acceptedDelta;
        Reason = reason;
        ActorId = actorId;
        AtUtc = atUtc;
        CorrelationId = correlationId.Length > CorrelationIdMaxLength ? correlationId[..CorrelationIdMaxLength] : correlationId;
    }

    public Guid Id { get; private set; }
    public Guid ReceiptId { get; private set; }
    public int Sequence { get; private set; }
    public int PoLineNumber { get; private set; }

    /// <summary>Signed accepted-quantity delta.</summary>
    public decimal AcceptedDelta { get; private set; }

    public string Reason { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTime AtUtc { get; private set; }
    public string CorrelationId { get; private set; }
}

/// <summary>Evidence metadata (photo, delivery note); bytes are outside Phase 32.</summary>
public sealed class ReceiptAttachment
{
    public const int FileNameMaxLength = 255;

    private ReceiptAttachment()
    {
        FileName = string.Empty;
        MediaType = string.Empty;
    }

    internal ReceiptAttachment(Guid id, Guid receiptId, string fileName, string mediaType, long length, Guid addedBy, DateTime addedAtUtc)
        : this()
    {
        Id = id;
        ReceiptId = receiptId;
        FileName = fileName;
        MediaType = mediaType;
        Length = length;
        AddedBy = addedBy;
        AddedAtUtc = addedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid ReceiptId { get; private set; }
    public string FileName { get; private set; }
    public string MediaType { get; private set; }
    public long Length { get; private set; }
    public Guid AddedBy { get; private set; }
    public DateTime AddedAtUtc { get; private set; }
}

/// <summary>Stable rejections of the Receiving module (RCV-*).</summary>
public static class ReceivingErrors
{
    public const string ImmutableCode = "receipt.immutable";
    public const string CorrectionOutOfRangeCode = "receipt.correction-out-of-range";

    public static readonly DomainError Immutable = new(
        ImmutableCode, DomainErrorKind.Conflict, "Posted receipts are immutable; post a correction instead.", "RCV-006");

    public static readonly DomainError FileTooLarge = new(
        "file.too-large", DomainErrorKind.TooLarge, $"An attachment has at most {ReceiptEvidencePolicy.MaxBytes} bytes.", "FIL-002", "attachments.length");

    public static readonly DomainError FileTypeNotAllowed = new(
        "file.type-not-allowed", DomainErrorKind.UnsupportedMediaType, "The media type is not allowed by the file policy.", "FIL-002", "attachments.mediaType");

    public static DomainError CorrectionOutOfRange(int lineNumber, decimal received, decimal resulting) => new(
        CorrectionOutOfRangeCode, DomainErrorKind.BusinessRule,
        "The corrected accepted quantity of the receipt line must stay between zero and its received quantity.", "RCV-007",
        Details: new Dictionary<string, string>
        {
            ["lineNumber"] = lineNumber.ToString(NumberFormatInfo.InvariantInfo),
            ["received"] = received.ToString(NumberFormatInfo.InvariantInfo),
            ["resulting"] = resulting.ToString(NumberFormatInfo.InvariantInfo)
        });

    public static DomainError Validation(string field, string message, string? ruleId = "RCV-002") =>
        new("validation", DomainErrorKind.Validation, message, ruleId, field);
}

/// <summary><c>procurement.receipt.posted</c> (RCV-001..005); <c>hasDiscrepancy</c> when a quantity was rejected.</summary>
public sealed record ReceiptPosted(DateTime OccurredAtUtc, Guid ReceiptId, Guid PurchaseOrderId, int LineCount, bool HasDiscrepancy)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.receipt.posted";
}

/// <summary><c>procurement.receipt.discrepancy-recorded</c> (RCV-004, D32-8): the lines with a rejected quantity.</summary>
public sealed record ReceiptDiscrepancyRecorded(DateTime OccurredAtUtc, Guid ReceiptId, Guid PurchaseOrderId, IReadOnlyList<int> LineNumbers)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.receipt.discrepancy-recorded";
}

/// <summary><c>procurement.receipt.corrected</c> (RCV-007); idempotency key the correction ID.</summary>
public sealed record ReceiptCorrected(DateTime OccurredAtUtc, Guid ReceiptId, Guid CorrectionId, Guid PurchaseOrderId, int PoLineNumber, decimal AcceptedDelta)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.receipt.corrected";
}
