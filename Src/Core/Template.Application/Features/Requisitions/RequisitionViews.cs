#nullable enable
using System.Globalization;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Results;
using Template.Application.Features.Budgeting;
using Template.Application.Features.Catalog;
using Template.Application.Wrappers;
using Template.Domain.Budgeting;
using Template.Domain.Requisitions;

namespace Template.Application.Features.Requisitions;

public sealed record CostCenterRefView(Guid Id, string Code, string Name, Guid DepartmentId);

/// <summary>Draft or snapshot line; quantities and prices are invariant decimal strings, the total is server-computed (CALC-002).</summary>
public sealed record RequisitionLineView(
    int LineNumber,
    Guid? CatalogItemId,
    string Description,
    string Kind,
    Guid CategoryId,
    string CategoryCode,
    Guid UnitOfMeasureId,
    string UnitCode,
    string Quantity,
    MoneyView EstimatedUnitPrice,
    MoneyView LineTotal);

public sealed record RequisitionAttachmentView(
    Guid Id, string FileName, string MediaType, long Length, string Status, Guid? AddedBy, DateTime AddedAt, int? LockedAtRevision);

public sealed record RequisitionSubmissionView(
    int Revision,
    DateTime SubmittedAt,
    Guid? SubmittedBy,
    string Purpose,
    DateOnly NeededBy,
    CostCenterRefView CostCenter,
    MoneyView EstimatedTotal,
    Guid ReservationId,
    Guid AllocationId,
    int PolicyVersion,
    IReadOnlyList<RequisitionLineView> Lines);

/// <summary>
/// Requisition representation (immutable DTO snapshot; never the entity). <see cref="AllowedActions"/> lists the actions
/// the server would accept from the current actor in the current state, so clients hide unavailable actions without
/// reproducing the rules; the API stays authoritative. <see cref="Version"/> equals the strong ETag value.
/// </summary>
public sealed record RequisitionView(
    Guid Id,
    string Number,
    Guid RequesterId,
    CostCenterRefView CostCenter,
    string? Purpose,
    DateOnly? NeededBy,
    string Currency,
    MoneyView EstimatedTotal,
    string Status,
    int Revision,
    Guid? ActiveReservationId,
    Guid? SourceRequisitionId,
    DateTime CreatedAt,
    Guid? CreatedBy,
    DateTime UpdatedAt,
    DateTime? SubmittedAt,
    IReadOnlyList<RequisitionLineView> Lines,
    IReadOnlyList<RequisitionAttachmentView> Attachments,
    IReadOnlyList<RequisitionSubmissionView> Submissions,
    IReadOnlyList<string> AllowedActions,
    string Version,
    Guid? ActiveRfqId = null,
    RequisitionAwardView? Award = null)
{
    public static RequisitionView From(PurchaseRequisition requisition, IReadOnlyList<string> allowedActions) => new(
        requisition.Id,
        requisition.Number,
        requisition.RequesterId,
        new CostCenterRefView(requisition.CostCenterId, requisition.CostCenterCode, requisition.CostCenterName, requisition.DepartmentId),
        requisition.Purpose,
        requisition.NeededBy,
        requisition.Currency,
        BudgetMoney.Of(requisition.EstimatedTotal, requisition.Currency),
        requisition.Status.ToString(),
        requisition.Revision,
        requisition.ActiveReservationId,
        requisition.SourceRequisitionId,
        requisition.CreatedAtUtc,
        requisition.CreatedBy,
        requisition.UpdatedAtUtc,
        requisition.SubmittedAtUtc,
        requisition.Lines.OrderBy(line => line.LineNumber).Select(line => Line(line, requisition.Currency)).ToArray(),
        requisition.Attachments.OrderBy(attachment => attachment.AddedAtUtc)
            .Select(attachment => new RequisitionAttachmentView(
                attachment.Id, attachment.FileName, attachment.MediaType, attachment.Length, attachment.Status.ToString(), attachment.AddedBy,
                attachment.AddedAtUtc, attachment.LockedAtRevision))
            .ToArray(),
        requisition.Submissions.OrderBy(submission => submission.Revision)
            .Select(submission => new RequisitionSubmissionView(
                submission.Revision, submission.SubmittedAtUtc, submission.SubmittedBy, submission.Purpose, submission.NeededBy,
                new CostCenterRefView(submission.CostCenterId, submission.CostCenterCode, submission.CostCenterName, submission.DepartmentId),
                BudgetMoney.Of(submission.EstimatedTotal, submission.Currency), submission.ReservationId, submission.AllocationId,
                submission.PolicyVersion,
                submission.Lines.OrderBy(line => line.LineNumber)
                    .Select(line => new RequisitionLineView(
                        line.LineNumber, line.CatalogItemId, line.Description, line.Kind.ToString(), line.CategoryId, line.CategoryCode,
                        line.UnitOfMeasureId, line.UnitCode, Quantity(line.Quantity), Price(line.EstimatedUnitPrice, submission.Currency),
                        BudgetMoney.Of(line.LineTotal, submission.Currency)))
                    .ToArray()))
            .ToArray(),
        allowedActions,
        VersionPrecondition.Format(requisition.Version),
        requisition.ActiveRfqId,
        requisition.AwardId is { } awardId && requisition.AwardedSupplierId is { } supplierId
            ? new RequisitionAwardView(awardId, supplierId, requisition.AwardedSupplierName ?? string.Empty)
            : null);

    private static RequisitionLineView Line(RequisitionLine line, string currency) => new(
        line.LineNumber, line.CatalogItemId, line.Description, line.Kind.ToString(), line.CategoryId, line.CategoryCode, line.UnitOfMeasureId,
        line.UnitCode, Quantity(line.Quantity), Price(line.EstimatedUnitPrice, currency), BudgetMoney.Of(line.LineTotal, currency));

    internal static string Quantity(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    internal static MoneyView Price(decimal value, string currency) => new(value.ToString("0.00##", CultureInfo.InvariantCulture), currency);
}

/// <summary>
/// SRC-021: the awarded supplier as requesters may see it (identity only; quote amounts and terms need
/// <c>sourcing.quotes.read</c> on the RFQ resource).
/// </summary>
public sealed record RequisitionAwardView(Guid AwardId, Guid SupplierId, string SupplierName);

/// <summary>List item without lines, attachments or snapshots.</summary>
public sealed record RequisitionSummaryView(
    Guid Id,
    string Number,
    Guid RequesterId,
    CostCenterRefView CostCenter,
    DateOnly? NeededBy,
    MoneyView EstimatedTotal,
    string Status,
    int Revision,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? SubmittedAt,
    string Version)
{
    public static RequisitionSummaryView From(PurchaseRequisition requisition) => new(
        requisition.Id, requisition.Number, requisition.RequesterId,
        new CostCenterRefView(requisition.CostCenterId, requisition.CostCenterCode, requisition.CostCenterName, requisition.DepartmentId),
        requisition.NeededBy, BudgetMoney.Of(requisition.EstimatedTotal, requisition.Currency), requisition.Status.ToString(), requisition.Revision,
        requisition.CreatedAtUtc, requisition.UpdatedAtUtc, requisition.SubmittedAtUtc, VersionPrecondition.Format(requisition.Version));
}

/// <summary>
/// One timeline entry (NFR-AUD-2): a requisition state change (<c>kind = "state"</c>) or a reservation change of any
/// revision (<c>kind = "reservation"</c>), with actor, UTC instant, reason and the emitted event type.
/// </summary>
public sealed record RequisitionTimelineEntryView(
    string Kind,
    DateTime At,
    Guid? ActorId,
    string? From,
    string To,
    int? Revision,
    string? Reason,
    string? ReasonCode,
    string? EventType,
    MoneyView? Amount,
    Guid? ReservationId,
    string CorrelationId);

public sealed record RequisitionTimelineView(Guid RequisitionId, IReadOnlyList<RequisitionTimelineEntryView> Entries)
{
    public static RequisitionTimelineView From(PurchaseRequisition requisition, IReadOnlyList<BudgetReservation> reservations)
    {
        var states = requisition.StateChanges.Select(change => new RequisitionTimelineEntryView(
            "state", change.AtUtc, change.ActorId, change.From?.ToString(), change.To.ToString(), change.Revision, change.Reason, change.ReasonCode,
            change.EventType, null, null, change.CorrelationId));
        var budget = reservations.SelectMany(reservation => reservation.Changes.Select(change => new RequisitionTimelineEntryView(
            "reservation", change.AtUtc, change.ActorId, change.From?.ToString(), change.To.ToString(), reservation.RequisitionRevision, null,
            change.Reason?.ToString(), ReservationEventType(change.Operation), BudgetMoney.Of(change.Amount, change.Currency), reservation.Id,
            change.CorrelationId)));
        return new RequisitionTimelineView(
            requisition.Id,
            states.Concat(budget).OrderBy(entry => entry.At).ThenBy(entry => entry.Kind == "reservation" ? 0 : 1).ToArray());
    }

    private static string ReservationEventType(BudgetReservationOperation operation) => operation switch
    {
        BudgetReservationOperation.Reserved => "procurement.budget.reservation-created",
        BudgetReservationOperation.Committed => "procurement.budget.reservation-committed",
        _ => "procurement.budget.reservation-released"
    };
}

/// <summary>Audit actions, actions and failures of the Requisitions module (REQ-*).</summary>
public static class RequisitionAudit
{
    public const string Resource = "purchase-requisition";

    public const string DraftCreated = "requisition.draft.created";
    public const string DraftUpdated = "requisition.draft.updated";
    public const string DraftDiscarded = "requisition.draft.discarded";
    public const string Submitted = "requisition.submitted";
    public const string Withdrawn = "requisition.withdrawn";
    public const string Cancelled = "requisition.cancelled";
    public const string Expired = "requisition.expired";
    public const string Copied = "requisition.copied";
    public const string AttachmentAdded = "file.attached";
    public const string AttachmentRemoved = "file.detached";

    public const string ActionEdit = "edit";
    public const string ActionDiscard = "discard";
    public const string ActionSubmit = "submit";
    public const string ActionWithdraw = "withdraw";
    public const string ActionCancel = "cancel";
    public const string ActionCopy = "copy";
    public const string ActionAttach = "attach";

    public static ApplicationError NotFound() => new(ProblemCodes.NotFound, ErrorCategory.NotFound, "The purchase requisition was not found.");
}
