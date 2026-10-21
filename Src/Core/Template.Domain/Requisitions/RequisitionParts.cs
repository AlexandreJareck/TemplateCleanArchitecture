using System;
using Template.Domain.Common;

namespace Template.Domain.Requisitions;

/// <summary>Requisition lifecycle (docs/product/workflows.md §1).</summary>
public enum RequisitionStatus
{
    Draft,
    Submitted,
    InSourcing,
    PendingApproval,
    Approved,
    Ordered,
    Closed,
    Rejected,
    Cancelled,
    Expired
}

/// <summary>Good or service; the Catalog module's kind is copied by value (ADR-0001).</summary>
public enum RequisitionLineKind
{
    Good,
    Service
}

public enum AttachmentStatus
{
    /// <summary>Metadata only; bytes arrive with the Files module (FIL-002, Phase 30).</summary>
    Pending
}

/// <summary>Cost center identity, state and the snapshot fields kept on the requisition (domain-model.md §4).</summary>
public sealed record CostCenterSnapshot(Guid Id, string Code, string Name, Guid DepartmentId, bool IsActive);

/// <summary>Category identity, state and snapshot fields.</summary>
public sealed record CategorySnapshot(Guid Id, string Code, string Name, bool IsActive);

/// <summary>Unit identity, state, snapshot fields and its allowed decimals (CAT-003).</summary>
public sealed record UnitSnapshot(Guid Id, string Code, string Name, int AllowedDecimals, bool IsActive);

/// <summary>
/// Line input resolved by the use case: the catalog item (when present) and the category/unit snapshots come from the
/// Catalog module; quantity and price are the user's values (CAT-004 reference prices are advisory).
/// </summary>
public sealed record RequisitionLineInput(
    MasterDataReference? CatalogItem,
    string? Description,
    RequisitionLineKind Kind,
    CategorySnapshot Category,
    UnitSnapshot Unit,
    decimal Quantity,
    Money EstimatedUnitPrice);

/// <summary>Draft line (REQ-002). Line numbers are stable: removing a line never renumbers the others.</summary>
public sealed class RequisitionLine
{
    public const int DescriptionMinLength = 3;
    public const int DescriptionMaxLength = 500;

    private RequisitionLine()
    {
        Description = string.Empty;
        CategoryCode = string.Empty;
        CategoryName = string.Empty;
        UnitCode = string.Empty;
        UnitName = string.Empty;
    }

    internal RequisitionLine(Guid id, Guid requisitionId, int lineNumber)
        : this()
    {
        Id = id;
        RequisitionId = requisitionId;
        LineNumber = lineNumber;
    }

    public Guid Id { get; private set; }
    public Guid RequisitionId { get; private set; }
    public int LineNumber { get; private set; }
    public Guid? CatalogItemId { get; private set; }
    public string Description { get; private set; }
    public RequisitionLineKind Kind { get; private set; }
    public Guid CategoryId { get; private set; }
    public string CategoryCode { get; private set; }
    public string CategoryName { get; private set; }
    public Guid UnitOfMeasureId { get; private set; }
    public string UnitCode { get; private set; }
    public string UnitName { get; private set; }
    public int UnitAllowedDecimals { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal EstimatedUnitPrice { get; private set; }

    /// <summary>CALC-002: round(quantity × unit price), always computed by the server.</summary>
    public decimal LineTotal { get; private set; }

    internal void Apply(RequisitionLineInput input, string description, string currency)
    {
        CatalogItemId = input.CatalogItem?.Id;
        Description = description;
        Kind = input.Kind;
        CategoryId = input.Category.Id;
        CategoryCode = input.Category.Code;
        CategoryName = input.Category.Name;
        UnitOfMeasureId = input.Unit.Id;
        UnitCode = input.Unit.Code;
        UnitName = input.Unit.Name;
        UnitAllowedDecimals = input.Unit.AllowedDecimals;
        Quantity = input.Quantity;
        EstimatedUnitPrice = input.EstimatedUnitPrice.Amount;
        LineTotal = CalculationRules.LineTotal(input.Quantity, new Money(input.EstimatedUnitPrice.Amount, currency)).Amount;
    }

    internal RequisitionLine CopyTo(Guid requisitionId)
    {
        var copy = new RequisitionLine(Guid.NewGuid(), requisitionId, LineNumber)
        {
            CatalogItemId = CatalogItemId,
            Description = Description,
            Kind = Kind,
            CategoryId = CategoryId,
            CategoryCode = CategoryCode,
            CategoryName = CategoryName,
            UnitOfMeasureId = UnitOfMeasureId,
            UnitCode = UnitCode,
            UnitName = UnitName,
            UnitAllowedDecimals = UnitAllowedDecimals,
            Quantity = Quantity,
            EstimatedUnitPrice = EstimatedUnitPrice,
            LineTotal = LineTotal
        };
        return copy;
    }
}

/// <summary>
/// Attachment metadata in <see cref="AttachmentStatus.Pending"/> (FIL-002 placeholder until Phase 30). An attachment
/// that was part of a submitted revision is locked (FIL-004: append-only after submission).
/// </summary>
public sealed class RequisitionAttachment
{
    private RequisitionAttachment()
    {
        FileName = string.Empty;
        MediaType = string.Empty;
    }

    internal RequisitionAttachment(Guid id, Guid requisitionId, string fileName, string mediaType, long length, Guid? addedBy, DateTime addedAtUtc)
    {
        Id = id;
        RequisitionId = requisitionId;
        FileName = fileName;
        MediaType = mediaType;
        Length = length;
        Status = AttachmentStatus.Pending;
        AddedBy = addedBy;
        AddedAtUtc = addedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid RequisitionId { get; private set; }
    public string FileName { get; private set; }
    public string MediaType { get; private set; }
    public long Length { get; private set; }
    public AttachmentStatus Status { get; private set; }
    public Guid? AddedBy { get; private set; }
    public DateTime AddedAtUtc { get; private set; }

    /// <summary>First submitted revision that contained the attachment; locked attachments are never removed.</summary>
    public int? LockedAtRevision { get; private set; }

    internal void LockAt(int revision) => LockedAtRevision ??= revision;
}

/// <summary>
/// Appended on every transition, never edited (domain-model.md §2 <c>StateChange</c>): previous/new state, actor (proxy
/// or system), UTC time from <c>IClock</c>, reason, correlation and the emitted domain event type.
/// </summary>
public sealed class RequisitionStateChange
{
    public const int ReasonCodeMaxLength = 30;
    public const int CorrelationIdMaxLength = 128;
    public const int EventTypeMaxLength = 100;

    private RequisitionStateChange()
    {
        CorrelationId = string.Empty;
    }

    internal RequisitionStateChange(
        Guid id, Guid requisitionId, int sequence, RequisitionStatus? from, RequisitionStatus to, int revision, Guid? actorId,
        DateTime atUtc, string? reason, string? reasonCode, string correlationId, string? eventType)
    {
        Id = id;
        RequisitionId = requisitionId;
        Sequence = sequence;
        From = from;
        To = to;
        Revision = revision;
        ActorId = actorId == Guid.Empty ? null : actorId;
        AtUtc = atUtc;
        Reason = reason;
        ReasonCode = reasonCode;
        CorrelationId = correlationId;
        EventType = eventType;
    }

    public Guid Id { get; private set; }
    public Guid RequisitionId { get; private set; }
    public int Sequence { get; private set; }

    /// <summary><c>null</c> for the creation entry (<c>— → Draft</c>).</summary>
    public RequisitionStatus? From { get; private set; }

    public RequisitionStatus To { get; private set; }
    public int Revision { get; private set; }

    /// <summary><c>null</c> for system transitions (expiry).</summary>
    public Guid? ActorId { get; private set; }

    public DateTime AtUtc { get; private set; }
    public string? Reason { get; private set; }
    public string? ReasonCode { get; private set; }
    public string CorrelationId { get; private set; }
    public string? EventType { get; private set; }
}

/// <summary>Immutable snapshot of one submitted revision (REQ-006), with its lines and reservation link.</summary>
public sealed class RequisitionSubmission
{
    private readonly System.Collections.Generic.List<RequisitionSubmissionLine> lines = [];

    private RequisitionSubmission()
    {
        Purpose = string.Empty;
        CostCenterCode = string.Empty;
        CostCenterName = string.Empty;
        Currency = string.Empty;
    }

    internal RequisitionSubmission(PurchaseRequisition requisition, Guid id, Guid? submittedBy, DateTime submittedAtUtc, Guid reservationId, Guid allocationId, int policyVersion)
        : this()
    {
        Id = id;
        RequisitionId = requisition.Id;
        Revision = requisition.Revision;
        SubmittedAtUtc = submittedAtUtc;
        SubmittedBy = submittedBy;
        Purpose = requisition.Purpose!;
        NeededBy = requisition.NeededBy!.Value;
        CostCenterId = requisition.CostCenterId;
        CostCenterCode = requisition.CostCenterCode;
        CostCenterName = requisition.CostCenterName;
        DepartmentId = requisition.DepartmentId;
        Currency = requisition.Currency;
        EstimatedTotal = requisition.EstimatedTotal;
        ReservationId = reservationId;
        AllocationId = allocationId;
        PolicyVersion = policyVersion;
        foreach (var line in requisition.Lines)
            lines.Add(new RequisitionSubmissionLine(Guid.NewGuid(), id, line));
    }

    public Guid Id { get; private set; }
    public Guid RequisitionId { get; private set; }
    public int Revision { get; private set; }
    public DateTime SubmittedAtUtc { get; private set; }
    public Guid? SubmittedBy { get; private set; }
    public string Purpose { get; private set; }
    public DateOnly NeededBy { get; private set; }
    public Guid CostCenterId { get; private set; }
    public string CostCenterCode { get; private set; }
    public string CostCenterName { get; private set; }
    public Guid DepartmentId { get; private set; }
    public string Currency { get; private set; }
    public decimal EstimatedTotal { get; private set; }
    public Guid ReservationId { get; private set; }
    public Guid AllocationId { get; private set; }

    /// <summary>Pinned <see cref="RequisitionPolicy"/> version.</summary>
    public int PolicyVersion { get; private set; }

    public System.Collections.Generic.IReadOnlyCollection<RequisitionSubmissionLine> Lines => lines;
}

/// <summary>Line copy inside a <see cref="RequisitionSubmission"/>.</summary>
public sealed class RequisitionSubmissionLine
{
    private RequisitionSubmissionLine()
    {
        Description = string.Empty;
        CategoryCode = string.Empty;
        UnitCode = string.Empty;
    }

    internal RequisitionSubmissionLine(Guid id, Guid submissionId, RequisitionLine line)
    {
        Id = id;
        SubmissionId = submissionId;
        LineNumber = line.LineNumber;
        CatalogItemId = line.CatalogItemId;
        Description = line.Description;
        Kind = line.Kind;
        CategoryId = line.CategoryId;
        CategoryCode = line.CategoryCode;
        UnitOfMeasureId = line.UnitOfMeasureId;
        UnitCode = line.UnitCode;
        Quantity = line.Quantity;
        EstimatedUnitPrice = line.EstimatedUnitPrice;
        LineTotal = line.LineTotal;
    }

    public Guid Id { get; private set; }
    public Guid SubmissionId { get; private set; }
    public int LineNumber { get; private set; }
    public Guid? CatalogItemId { get; private set; }
    public string Description { get; private set; }
    public RequisitionLineKind Kind { get; private set; }
    public Guid CategoryId { get; private set; }
    public string CategoryCode { get; private set; }
    public Guid UnitOfMeasureId { get; private set; }
    public string UnitCode { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal EstimatedUnitPrice { get; private set; }
    public decimal LineTotal { get; private set; }
}
