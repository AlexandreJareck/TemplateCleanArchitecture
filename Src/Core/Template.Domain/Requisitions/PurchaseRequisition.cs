using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Template.Domain.Common;

namespace Template.Domain.Requisitions;

/// <summary>Who cancels (REQ-008): the requester for an own requisition, or a <c>requisitions.cancel-any</c> holder.</summary>
public enum CancellationAuthority
{
    Requester,
    CancelAny
}

/// <summary>
/// Everything a submission needs besides the aggregate itself, resolved by the use case under the reservation lock:
/// actor, instants from <c>IClock</c> and the organization time zone, the current cost-center snapshot, the active
/// state of every referenced catalog item/category/unit (REQ-004) and the pinned policy.
/// </summary>
public sealed record SubmissionContext(
    Guid? ActorId,
    DateTime NowUtc,
    DateOnly LocalDate,
    bool RequesterActive,
    CostCenterSnapshot CostCenter,
    IReadOnlyDictionary<Guid, bool> ReferenceStates,
    string CorrelationId,
    RequisitionPolicy Policy);

/// <summary>
/// Purchase requisition aggregate (docs/product/domain-model.md §3, workflows.md §1). Only <c>Draft</c> content changes
/// (REQ-006); every transition appends a <see cref="RequisitionStateChange"/> and raises a <see cref="DomainEvent"/>;
/// totals are always recalculated here (CALC-002, REQ-003). Budget effects (reserve/release) are applied by the use case
/// on the <c>BudgetAllocation</c> in the same transaction (REQ-005); this aggregate keeps only the active reservation's
/// identity. State changes only through behavior methods (no externally writable property).
/// </summary>
public sealed class PurchaseRequisition : VersionedAggregateRoot
{
    public const int PurposeMinLength = 10;
    public const int PurposeMaxLength = 2000;
    public const int NumberMaxLength = 20;

    private readonly List<RequisitionLine> lines = [];
    private readonly List<RequisitionAttachment> attachments = [];
    private readonly List<RequisitionStateChange> stateChanges = [];
    private readonly List<RequisitionSubmission> submissions = [];
    private readonly DomainEventBuffer events = new();

    private PurchaseRequisition()
    {
        Number = string.Empty;
        CostCenterCode = string.Empty;
        CostCenterName = string.Empty;
        Currency = string.Empty;
    }

    /// <summary><c>REQ-{yyyy}-{nnnnnn}</c>; a filterable field, never a route key.</summary>
    public string Number { get; private set; }

    public Guid RequesterId { get; private set; }
    public Guid CostCenterId { get; private set; }
    public string CostCenterCode { get; private set; }
    public string CostCenterName { get; private set; }
    public Guid DepartmentId { get; private set; }
    public string? Purpose { get; private set; }
    public DateOnly? NeededBy { get; private set; }
    public string Currency { get; private set; }

    /// <summary>Σ line totals (CALC-002); never accepted from a client.</summary>
    public decimal EstimatedTotal { get; private set; }

    public RequisitionStatus Status { get; private set; }

    /// <summary>Starts at 1; a withdrawal increments it (REQ-007).</summary>
    public int Revision { get; private set; }

    public Guid? ActiveReservationId { get; private set; }
    public Guid? ActiveAllocationId { get; private set; }
    public Guid? SourceRequisitionId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? SubmittedAtUtc { get; private set; }

    /// <summary>Published RFQ of the current sourcing round (Phase 29), kept for history after it ends.</summary>
    public Guid? ActiveRfqId { get; private set; }

    /// <summary>Award submitted for approval (SRC-019); <c>AwardSnapshot</c> of domain-model.md §3.</summary>
    public Guid? AwardId { get; private set; }

    public Guid? AwardedSupplierId { get; private set; }
    public string? AwardedSupplierName { get; private set; }

    /// <summary>Award amount in the requisition currency (CALC-007).</summary>
    public decimal? AwardAmount { get; private set; }

    public IReadOnlyCollection<RequisitionLine> Lines => lines;
    public IReadOnlyCollection<RequisitionAttachment> Attachments => attachments;
    public IReadOnlyCollection<RequisitionStateChange> StateChanges => stateChanges;
    public IReadOnlyCollection<RequisitionSubmission> Submissions => submissions;
    public IReadOnlyList<DomainEvent> DomainEvents => events.Events;

    public Money EstimatedTotalMoney => new(EstimatedTotal, Currency);

    /// <summary>REQ-012: <c>Rejected</c>, <c>Cancelled</c>, <c>Expired</c> and <c>Closed</c>.</summary>
    public bool IsTerminal => Status is RequisitionStatus.Rejected or RequisitionStatus.Cancelled or RequisitionStatus.Expired or RequisitionStatus.Closed;

    /// <summary>REQ-010: discardable only while it was never submitted.</summary>
    public bool WasEverSubmitted => submissions.Count > 0 || Revision > 1;

    public void ClearDomainEvents() => events.Clear();

    public static string FormatNumber(int year, long sequence) =>
        $"REQ-{year.ToString("D4", NumberFormatInfo.InvariantInfo)}-{sequence.ToString("D6", NumberFormatInfo.InvariantInfo)}";

    /// <summary>Creates revision 1 in <c>Draft</c> on an active cost center (ORG-003); purpose and needed-by may come later.</summary>
    public static (PurchaseRequisition? Requisition, DomainError? Error) CreateDraft(
        Guid id, string number, Guid requesterId, CostCenterSnapshot costCenter, string? currency, bool isSupportedCurrency,
        string? purpose, DateOnly? neededBy, Guid? actorId, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureId(id, nameof(id));
        MasterDataRules.EnsureId(requesterId, nameof(requesterId));
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentNullException.ThrowIfNull(costCenter);
        var header = ValidateHeader(costCenter, currency, isSupportedCurrency, purpose, requireActiveCostCenter: true);
        if (header.Error is not null)
            return (null, header.Error);

        var requisition = new PurchaseRequisition
        {
            Id = id,
            Number = number,
            RequesterId = requesterId,
            Currency = header.Currency!,
            Purpose = header.Purpose,
            NeededBy = neededBy,
            Status = RequisitionStatus.Draft,
            Revision = 1,
            CreatedAtUtc = nowUtc,
            CreatedBy = actorId,
            UpdatedAtUtc = nowUtc
        };
        requisition.ApplyCostCenter(costCenter);
        requisition.AppendStateChange(null, RequisitionStatus.Draft, actorId, nowUtc, null, null, correlationId, null);
        return (requisition, null);
    }

    /// <summary>REQ-012: a new draft referencing a terminal source; lines are copied and re-validated at submission.</summary>
    public static (PurchaseRequisition? Requisition, DomainError? Error) CopyAsNewDraft(
        PurchaseRequisition source, Guid id, string number, Guid requesterId, CostCenterSnapshot costCenter, bool isSupportedCurrency,
        Guid? actorId, DateTime nowUtc, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.IsTerminal)
            return (null, DomainError.InvalidTransition(source.Status.ToString(), "Only a terminal requisition can be copied.", "REQ-012"));

        var (copy, error) = CreateDraft(
            id, number, requesterId, costCenter, source.Currency, isSupportedCurrency, source.Purpose, null, actorId, nowUtc, correlationId);
        if (copy is null)
            return (null, error);

        copy.SourceRequisitionId = source.Id;
        copy.lines.AddRange(source.lines.OrderBy(line => line.LineNumber).Select(line => line.CopyTo(id)));
        copy.Recalculate();
        return (copy, null);
    }

    /// <summary>Changes the draft header (REQ-001 ownership is checked by the use case). The currency is fixed once lines exist.</summary>
    public DomainError? UpdateHeader(
        CostCenterSnapshot costCenter, string? currency, bool isSupportedCurrency, string? purpose, DateOnly? neededBy, DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentNullException.ThrowIfNull(costCenter);
        if (Status != RequisitionStatus.Draft)
            return RequisitionErrors.Immutable;
        var header = ValidateHeader(costCenter, currency, isSupportedCurrency, purpose, requireActiveCostCenter: costCenter.Id != CostCenterId);
        if (header.Error is not null)
            return header.Error;
        if (header.Currency != Currency && lines.Count > 0)
            return RequisitionErrors.Validation("currency", "The currency cannot change while the draft has lines.");

        var before = (CostCenterId, CostCenterCode, CostCenterName, DepartmentId, Currency, Purpose, NeededBy);
        ApplyCostCenter(costCenter);
        Currency = header.Currency!;
        Purpose = header.Purpose;
        NeededBy = neededBy;
        if (before != (CostCenterId, CostCenterCode, CostCenterName, DepartmentId, Currency, Purpose, NeededBy))
            Touch(nowUtc);
        return null;
    }

    public (RequisitionLine? Line, DomainError? Error) AddLine(RequisitionLineInput input, RequisitionPolicy policy, DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentNullException.ThrowIfNull(policy);
        if (Status != RequisitionStatus.Draft)
            return (null, RequisitionErrors.Immutable);
        if (lines.Count >= policy.MaxLines)
            return (null, RequisitionErrors.Validation("lines", $"A requisition has at most {policy.MaxLines} lines."));
        var validated = ValidateLine(input);
        if (validated.Error is not null)
            return (null, validated.Error);

        var line = new RequisitionLine(Guid.NewGuid(), Id, lines.Count == 0 ? 1 : lines.Max(existing => existing.LineNumber) + 1);
        line.Apply(input, validated.Description!, Currency);
        lines.Add(line);
        Recalculate();
        Touch(nowUtc);
        return (line, null);
    }

    public DomainError? UpdateLine(int lineNumber, RequisitionLineInput input, DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (Status != RequisitionStatus.Draft)
            return RequisitionErrors.Immutable;
        var line = lines.FirstOrDefault(candidate => candidate.LineNumber == lineNumber);
        if (line is null)
            return RequisitionErrors.NotFound("line");
        var validated = ValidateLine(input);
        if (validated.Error is not null)
            return validated.Error;

        line.Apply(input, validated.Description!, Currency);
        Recalculate();
        Touch(nowUtc);
        return null;
    }

    public DomainError? RemoveLine(int lineNumber, DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (Status != RequisitionStatus.Draft)
            return RequisitionErrors.Immutable;
        if (lines.RemoveAll(line => line.LineNumber == lineNumber) == 0)
            return RequisitionErrors.NotFound("line");

        Recalculate();
        Touch(nowUtc);
        return null;
    }

    /// <summary>FIL-002 metadata in <c>Pending</c>; allowed while the requisition is non-terminal (FIL-004 append-only).</summary>
    public (RequisitionAttachment? Attachment, DomainError? Error) AddAttachment(
        Guid attachmentId, string? fileName, string? mediaType, long length, Guid? actorId, DateTime nowUtc)
    {
        MasterDataRules.EnsureId(attachmentId, nameof(attachmentId));
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (IsTerminal)
            return (null, DomainError.InvalidTransition(Status.ToString(), "A terminal requisition accepts no attachment.", "FIL-004"));
        var name = FilePolicy.SanitizeFileName(fileName);
        if (name is null)
            return (null, RequisitionErrors.Validation("fileName", $"The file name must have 1 to {FilePolicy.FileNameMaxLength} usable characters.", "FIL-002"));
        var type = mediaType?.Trim().ToLowerInvariant();
        if (type is null || !FilePolicy.AllowedMediaTypes.Contains(type))
            return (null, RequisitionErrors.FileTypeNotAllowed);
        if (length <= 0)
            return (null, RequisitionErrors.Validation("length", "The length must be greater than zero.", "FIL-002"));
        if (length > FilePolicy.MaxBytes)
            return (null, RequisitionErrors.FileTooLarge);
        if (attachments.Count >= FilePolicy.MaxAttachmentsPerOwner)
            return (null, RequisitionErrors.FileLimitReached);

        var attachment = new RequisitionAttachment(attachmentId, Id, name, type, length, actorId, nowUtc);
        if (Status != RequisitionStatus.Draft)
            attachment.LockAt(Revision);
        attachments.Add(attachment);
        Touch(nowUtc);
        return (attachment, null);
    }

    /// <summary>Only unlocked attachments of a <c>Draft</c> can be removed (FIL-004).</summary>
    public DomainError? RemoveAttachment(Guid attachmentId, DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        var attachment = attachments.FirstOrDefault(candidate => candidate.Id == attachmentId);
        if (attachment is null)
            return RequisitionErrors.NotFound("attachment");
        if (Status != RequisitionStatus.Draft || attachment.LockedAtRevision is not null)
            return RequisitionErrors.FileImmutable;

        attachments.Remove(attachment);
        Touch(nowUtc);
        return null;
    }

    /// <summary>REQ-010: <c>null</c> when the draft may be deleted.</summary>
    public DomainError? EnsureDiscardable() =>
        Status != RequisitionStatus.Draft || WasEverSubmitted ? RequisitionErrors.NeverDiscardable : null;

    /// <summary>
    /// REQ-004 guards, all reported together: at least one line, purpose, needed-by ≥ submission local date, active
    /// cost center, active catalog/category/unit references and an active requester. <c>null</c> when submittable.
    /// </summary>
    public DomainError? CheckSubmittable(SubmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Status != RequisitionStatus.Draft)
            return DomainError.InvalidTransition(Status.ToString(), "Only a Draft requisition can be submitted.", "REQ-004");
        if (context.CostCenter.Id != CostCenterId)
            throw new ArgumentException("The cost-center snapshot belongs to another cost center.", nameof(context));

        var failures = new List<string>();
        if (lines.Count == 0)
            failures.Add("lines");
        if (lines.Count > context.Policy.MaxLines)
            failures.Add("lines");
        if (Purpose is null)
            failures.Add("purpose");
        if (NeededBy is null || NeededBy < context.LocalDate)
            failures.Add("neededBy");
        if (!context.CostCenter.IsActive)
            failures.Add("costCenterId");
        if (!context.RequesterActive)
            failures.Add("requesterId");
        foreach (var line in lines.OrderBy(line => line.LineNumber))
        {
            if (line.CatalogItemId is { } itemId && !IsActive(context, itemId))
                failures.Add($"lines[{line.LineNumber}].catalogItemId");
            if (!IsActive(context, line.CategoryId))
                failures.Add($"lines[{line.LineNumber}].categoryId");
            if (!IsActive(context, line.UnitOfMeasureId))
                failures.Add($"lines[{line.LineNumber}].unitOfMeasureId");
        }

        return failures.Count == 0 ? null : RequisitionErrors.NotSubmittable(failures.Distinct());
    }

    /// <summary>
    /// <c>Draft → Submitted</c> (REQ-004..006): stores the immutable snapshot of this revision, locks the attachments and
    /// keeps the reservation identity created on the allocation in the same transaction (REQ-005).
    /// </summary>
    public DomainError? Submit(SubmissionContext context, Guid submissionId, Guid reservationId, Guid allocationId)
    {
        MasterDataRules.EnsureId(submissionId, nameof(submissionId));
        MasterDataRules.EnsureId(reservationId, nameof(reservationId));
        MasterDataRules.EnsureId(allocationId, nameof(allocationId));
        MasterDataRules.EnsureUtc(context.NowUtc, nameof(context));
        if (CheckSubmittable(context) is { } error)
            return error;

        ApplyCostCenter(context.CostCenter);
        Recalculate();
        submissions.Add(new RequisitionSubmission(this, submissionId, context.ActorId, context.NowUtc, reservationId, allocationId, context.Policy.Version));
        foreach (var attachment in attachments)
            attachment.LockAt(Revision);
        ActiveReservationId = reservationId;
        ActiveAllocationId = allocationId;
        SubmittedAtUtc = context.NowUtc;
        Transition(RequisitionStatus.Submitted, context.ActorId, context.NowUtc, null, null, context.CorrelationId,
            new RequisitionSubmitted(context.NowUtc, Id, Number, Revision, CostCenterId, EstimatedTotalMoney, NeededBy!.Value));
        return null;
    }

    /// <summary>
    /// REQ-007: <c>Submitted → Draft</c>, revision + 1. Returns the allocation whose reservation the use case releases
    /// with reason <c>Withdrawn</c> in the same transaction.
    /// </summary>
    public (Guid? AllocationId, DomainError? Error) Withdraw(Guid? actorId, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (Status != RequisitionStatus.Submitted)
            return (null, DomainError.InvalidTransition(Status.ToString(), "Only a Submitted requisition without a published RFQ can be withdrawn.", "REQ-007"));

        var allocationId = DetachReservation();
        var from = Revision;
        Revision++;
        Transition(RequisitionStatus.Draft, actorId, nowUtc, null, null, correlationId, new RequisitionWithdrawn(nowUtc, Id, from));
        return (allocationId, null);
    }

    /// <summary>
    /// REQ-008 with a 10–500 character reason. The requester may cancel <c>Draft</c>, <c>Submitted</c> or
    /// <c>InSourcing</c>; a cancel-any holder any non-terminal state before <c>Ordered</c>. Returns the allocation whose
    /// reservation must be released (<c>Cancelled</c>), or <c>null</c> when nothing is reserved.
    /// </summary>
    public (Guid? AllocationId, DomainError? Error) Cancel(CancellationAuthority authority, string? reason, Guid? actorId, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        var normalizedReason = MasterDataRules.NormalizeReason(reason);
        if (normalizedReason is null)
            return (null, DomainError.ReasonRequired("REQ-008", MasterDataRules.ReasonMinLength, MasterDataRules.ReasonMaxLength));
        var allowed = authority == CancellationAuthority.CancelAny
            ? Status is RequisitionStatus.Draft or RequisitionStatus.Submitted or RequisitionStatus.InSourcing or RequisitionStatus.PendingApproval or RequisitionStatus.Approved
            : Status is RequisitionStatus.Draft or RequisitionStatus.Submitted or RequisitionStatus.InSourcing;
        if (!allowed)
            return (null, DomainError.InvalidTransition(Status.ToString(), "The requisition cannot be cancelled in its current state.", "REQ-008"));

        var allocationId = DetachReservation();
        Transition(RequisitionStatus.Cancelled, actorId, nowUtc, normalizedReason, "Cancelled", correlationId,
            new RequisitionCancelled(nowUtc, Id, Revision, actorId));
        return (allocationId, null);
    }

    /// <summary>
    /// REQ-009 (system actor): a <c>Submitted</c> or <c>InSourcing</c> requisition whose needed-by date plus the pinned
    /// policy grace has passed becomes <c>Expired</c>. Returns <c>Changed = false</c> when it is not due (idempotent sweep).
    /// </summary>
    public (Guid? AllocationId, bool Changed, DomainError? Error) Expire(DateOnly localDate, RequisitionPolicy policy, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentNullException.ThrowIfNull(policy);
        if (Status is not (RequisitionStatus.Submitted or RequisitionStatus.InSourcing))
            return (null, false, Status == RequisitionStatus.Expired
                ? null
                : DomainError.InvalidTransition(Status.ToString(), "Only a Submitted or InSourcing requisition can expire.", "REQ-009"));
        if (NeededBy is null || NeededBy.Value.AddDays(policy.ExpiryGraceDays) >= localDate)
            return (null, false, null);

        var allocationId = DetachReservation();
        Transition(RequisitionStatus.Expired, null, nowUtc, null, "Expired", correlationId, new RequisitionExpired(nowUtc, Id, Revision));
        return (allocationId, true, null);
    }

    /// <summary>
    /// <c>Submitted → InSourcing</c> when the requisition's RFQ is published (SRC-001, workflows §1). The RFQ raises the
    /// integration event; the state change records its type.
    /// </summary>
    public DomainError? StartSourcing(Guid rfqId, Guid? actorId, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureId(rfqId, nameof(rfqId));
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (Status != RequisitionStatus.Submitted)
            return DomainError.InvalidTransition(Status.ToString(), "Only a Submitted requisition enters sourcing.", "SRC-001");

        ActiveRfqId = rfqId;
        Transition(RequisitionStatus.InSourcing, actorId, nowUtc, null, null, correlationId, "procurement.rfq.published");
        return null;
    }

    /// <summary>SRC-020: <c>InSourcing → Submitted</c> when the RFQ is cancelled; the reservation stays <c>Reserved</c>.</summary>
    public DomainError? ReturnFromSourcing(string? reason, Guid? actorId, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (Status != RequisitionStatus.InSourcing)
            return DomainError.InvalidTransition(Status.ToString(), "Only an InSourcing requisition returns to Submitted.", "SRC-020");

        Transition(RequisitionStatus.Submitted, actorId, nowUtc, reason, "RfqCancelled", correlationId, "procurement.rfq.cancelled");
        return null;
    }

    /// <summary>
    /// SRC-019: <c>InSourcing → PendingApproval</c> with the award snapshot (supplier and amount in the requisition
    /// currency, CALC-007). The use case adjusts the reservation (BUD-009) in the same commit.
    /// </summary>
    public DomainError? SubmitAward(Guid awardId, Guid supplierId, string supplierName, Money amount, Guid? actorId, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureId(awardId, nameof(awardId));
        MasterDataRules.EnsureId(supplierId, nameof(supplierId));
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentException.ThrowIfNullOrWhiteSpace(supplierName);
        if (Status != RequisitionStatus.InSourcing)
            return DomainError.InvalidTransition(Status.ToString(), "Only an InSourcing requisition receives an award.", "SRC-019");
        if (!string.Equals(amount.Currency, Currency, StringComparison.Ordinal))
            throw new ArgumentException("The award amount must be in the requisition currency (CALC-007).", nameof(amount));

        AwardId = awardId;
        AwardedSupplierId = supplierId;
        AwardedSupplierName = supplierName;
        AwardAmount = amount.Amount;
        Transition(RequisitionStatus.PendingApproval, actorId, nowUtc, null, null, correlationId, "procurement.award.submitted");
        return null;
    }

    /// <summary>
    /// APR-013 (Phase 31): <c>PendingApproval → Approved</c> for the pending award; the use case commits the reservation
    /// (BUD-010) in the same transaction. Idempotent for the same award.
    /// </summary>
    public (bool Changed, DomainError? Error) ApproveAward(Guid awardId, DateTime nowUtc, string correlationId, string eventType)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (Status == RequisitionStatus.Approved && AwardId == awardId)
            return (false, null);
        if (Status != RequisitionStatus.PendingApproval || AwardId != awardId)
            return (false, DomainError.InvalidTransition(Status.ToString(), "Only a requisition pending approval of this award is approved.", "APR-013"));

        Transition(RequisitionStatus.Approved, null, nowUtc, null, null, correlationId, eventType);
        return (true, null);
    }

    /// <summary>
    /// APR-009 rejection or APR-011 expiry (Phase 31): <c>PendingApproval → Rejected/Expired</c>. Returns the allocation
    /// whose reservation the use case releases with the matching reason in the same transaction; <c>Changed = false</c>
    /// when the requisition already left the approval of this award (idempotent).
    /// </summary>
    public (Guid? AllocationId, bool Changed) EndApproval(
        Guid awardId, RequisitionStatus outcome, string? reason, DateTime nowUtc, string correlationId, string eventType)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (outcome is not (RequisitionStatus.Rejected or RequisitionStatus.Expired))
            throw new ArgumentOutOfRangeException(nameof(outcome));
        if (Status != RequisitionStatus.PendingApproval || AwardId != awardId)
            return (null, false);

        var allocationId = DetachReservation();
        Transition(outcome, null, nowUtc, reason, outcome == RequisitionStatus.Rejected ? "ApprovalRejected" : "ApprovalExpired", correlationId, eventType);
        return (allocationId, true);
    }

    /// <summary>
    /// APR-012 invalidation or APR-015 administrative cancel (Phase 31): <c>PendingApproval → InSourcing</c>; the award
    /// snapshot is cleared and the reservation stays <c>Reserved</c> for the next award. Idempotent per award.
    /// </summary>
    public bool ReturnToSourcingFromApproval(
        Guid awardId, string reasonCode, string? reason, Guid? actorId, DateTime nowUtc, string correlationId, string eventType)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        if (Status != RequisitionStatus.PendingApproval || AwardId != awardId)
            return false;

        AwardId = null;
        AwardedSupplierId = null;
        AwardedSupplierName = null;
        AwardAmount = null;
        Transition(RequisitionStatus.InSourcing, actorId, nowUtc, reason, reasonCode, correlationId, eventType);
        return true;
    }

    private static (string? Currency, string? Purpose, DomainError? Error) ValidateHeader(
        CostCenterSnapshot costCenter, string? currency, bool isSupportedCurrency, string? purpose, bool requireActiveCostCenter)
    {
        if (requireActiveCostCenter && !costCenter.IsActive)
            return (null, null, new DomainError("organization.inactive", DomainErrorKind.BusinessRule, "The cost center is inactive.", "ORG-003"));
        var code = CurrencyCodes.Normalize(currency);
        if (code is null || !isSupportedCurrency)
            return (null, null, RequisitionErrors.Validation("currency", "The currency must be one of the organization's supported currencies."));
        var trimmed = purpose?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return (code, null, null);
        if (trimmed.Length is < PurposeMinLength or > PurposeMaxLength)
            return (null, null, RequisitionErrors.Validation("purpose", $"The purpose must have {PurposeMinLength} to {PurposeMaxLength} characters.", "REQ-004"));
        return (code, trimmed, null);
    }

    private (string? Description, DomainError? Error) ValidateLine(RequisitionLineInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var description = input.Description?.Trim();
        if (description is null || description.Length is < RequisitionLine.DescriptionMinLength or > RequisitionLine.DescriptionMaxLength)
            return (null, RequisitionErrors.Validation("description", "The description must have 3 to 500 characters."));
        if (!Enum.IsDefined(input.Kind))
            return (null, RequisitionErrors.Validation("kind", "The kind must be Good or Service."));
        if (input.CatalogItem is { IsActive: false })
            return (null, new DomainError(MasterDataReference.InactiveCatalogCode, DomainErrorKind.BusinessRule, "The catalog item is inactive.", "CAT-002", "catalogItemId"));
        if (!input.Category.IsActive)
            return (null, new DomainError(MasterDataReference.InactiveCatalogCode, DomainErrorKind.BusinessRule, "The category is inactive.", "CAT-002", "categoryId"));
        if (!input.Unit.IsActive)
            return (null, new DomainError(MasterDataReference.InactiveCatalogCode, DomainErrorKind.BusinessRule, "The unit of measure is inactive.", "CAT-002", "unitOfMeasureId"));
        var places = DecimalPrecision.Places(input.Quantity);
        if (input.Quantity <= 0 || places > CalculationRules.MaxQuantityDecimals || places > input.Unit.AllowedDecimals || input.Quantity > CalculationRules.MaxAmount)
            return (null, RequisitionErrors.Validation("quantity", $"The quantity must be greater than zero with at most {Math.Min(input.Unit.AllowedDecimals, CalculationRules.MaxQuantityDecimals)} decimals (CAT-003)."));
        var price = input.EstimatedUnitPrice;
        if (price.Amount <= 0 || DecimalPrecision.Places(price.Amount) > CalculationRules.MaxUnitPriceDecimals || price.Amount > CalculationRules.MaxAmount)
            return (null, RequisitionErrors.Validation("estimatedUnitPrice.amount", "The estimated unit price must be greater than zero with at most 4 decimals."));
        if (!string.Equals(CurrencyCodes.Normalize(price.Currency), Currency, StringComparison.Ordinal))
            return (null, RequisitionErrors.Validation("estimatedUnitPrice.currency", "The line currency must equal the requisition currency."));
        if (input.Quantity * price.Amount > CalculationRules.MaxAmount)
            return (null, RequisitionErrors.Validation("quantity", "The line total exceeds the supported amount."));
        return (description, null);
    }

    private static bool IsActive(SubmissionContext context, Guid id) => context.ReferenceStates.TryGetValue(id, out var active) && active;

    private void ApplyCostCenter(CostCenterSnapshot costCenter)
    {
        CostCenterId = costCenter.Id;
        CostCenterCode = costCenter.Code;
        CostCenterName = costCenter.Name;
        DepartmentId = costCenter.DepartmentId;
    }

    private void Recalculate()
    {
        var total = lines.Aggregate(Money.Zero(Currency), (sum, line) => sum.Add(new Money(line.LineTotal, Currency)));
        EstimatedTotal = total.Amount;
    }

    private Guid? DetachReservation()
    {
        var allocationId = ActiveAllocationId;
        ActiveReservationId = null;
        ActiveAllocationId = null;
        return allocationId;
    }

    private void Transition(
        RequisitionStatus to, Guid? actorId, DateTime nowUtc, string? reason, string? reasonCode, string correlationId, DomainEvent domainEvent)
    {
        Transition(to, actorId, nowUtc, reason, reasonCode, correlationId, domainEvent.EventType);
        events.Raise(domainEvent);
    }

    /// <summary>A transition whose integration event is raised by another aggregate (the RFQ) in the same commit.</summary>
    private void Transition(
        RequisitionStatus to, Guid? actorId, DateTime nowUtc, string? reason, string? reasonCode, string correlationId, string eventType)
    {
        var from = Status;
        Status = to;
        AppendStateChange(from, to, actorId, nowUtc, reason, reasonCode, correlationId, eventType);
        Touch(nowUtc);
    }

    private void AppendStateChange(
        RequisitionStatus? from, RequisitionStatus to, Guid? actorId, DateTime nowUtc, string? reason, string? reasonCode, string correlationId, string? eventType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        stateChanges.Add(new RequisitionStateChange(
            Guid.NewGuid(), Id, stateChanges.Count + 1, from, to, Revision, actorId, nowUtc, reason, reasonCode,
            correlationId.Length > RequisitionStateChange.CorrelationIdMaxLength ? correlationId[..RequisitionStateChange.CorrelationIdMaxLength] : correlationId,
            eventType));
    }

    private void Touch(DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        IncrementVersion();
    }
}

/// <summary><c>procurement.requisition.submitted</c>.</summary>
public sealed record RequisitionSubmitted(DateTime OccurredAtUtc, Guid RequisitionId, string Number, int Revision, Guid CostCenterId, Money EstimatedTotal, DateOnly NeededBy)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.requisition.submitted";
}

/// <summary><c>procurement.requisition.withdrawn</c> (revision that was withdrawn).</summary>
public sealed record RequisitionWithdrawn(DateTime OccurredAtUtc, Guid RequisitionId, int Revision) : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.requisition.withdrawn";
}

/// <summary><c>procurement.requisition.cancelled</c>.</summary>
public sealed record RequisitionCancelled(DateTime OccurredAtUtc, Guid RequisitionId, int Revision, Guid? CancelledBy) : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.requisition.cancelled";
}

/// <summary><c>procurement.requisition.expired</c>.</summary>
public sealed record RequisitionExpired(DateTime OccurredAtUtc, Guid RequisitionId, int Revision) : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.requisition.expired";
}
