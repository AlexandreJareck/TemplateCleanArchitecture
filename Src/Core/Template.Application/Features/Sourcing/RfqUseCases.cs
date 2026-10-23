#nullable enable
using System.Globalization;
using FluentValidation;
using MediatR;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Results;
using Template.Application.Features.Budgeting;
using Template.Application.Interfaces.Budgeting;
using Template.Application.Interfaces.Requisitions;
using Template.Application.Interfaces.Sourcing;
using Template.Application.Security;
using Template.Application.Services.Procurement;
using Template.Domain.Budgeting;
using Template.Domain.Common;
using Template.Domain.Requisitions;
using Template.Domain.Sourcing;

namespace Template.Application.Features.Sourcing;

/// <summary>SRC-001/002/005: a Draft round for a Submitted requisition. Permission <c>sourcing.rfqs.manage</c>.</summary>
public sealed record CreateRfqCommand(Guid RequisitionId, string? Method) : IRequest<Result<RfqView>>;

public sealed record InviteSupplierCommand(Guid Id, long? ExpectedVersion, Guid SupplierId) : IRequest<Result<RfqView>>;

public sealed record RemoveInvitationCommand(Guid Id, long? ExpectedVersion, Guid SupplierId) : IRequest<Result<RfqView>>;

/// <summary>SRC-004/005/006: publish with a deadline; a Direct RFQ also needs the waiver reason.</summary>
public sealed record PublishRfqCommand(Guid Id, long? ExpectedVersion, DateTime? Deadline, string? WaiverReason) : IRequest<Result<RfqView>>;

public sealed record ExtendRfqDeadlineCommand(Guid Id, long? ExpectedVersion, DateTime? Deadline, string? Reason) : IRequest<Result<RfqView>>;

/// <summary>SRC-022: queues a reminder to invited suppliers without a current quote.</summary>
public sealed record RemindRfqSuppliersCommand(Guid Id, long? ExpectedVersion) : IRequest<Result<RfqView>>;

/// <summary>SRC-012: normal close after the deadline, early close with reason and <c>sourcing.rfqs.close-early</c>.</summary>
public sealed record CloseRfqCommand(Guid Id, long? ExpectedVersion, string? Reason) : IRequest<Result<RfqView>>;

/// <summary>SRC-012 system close at the deadline (scheduler in Phase 33). Idempotent; retries an optimistic conflict.</summary>
public sealed record CloseDueRfqCommand(Guid Id) : IRequest<Result<bool>>;

/// <summary>SRC-012 sweep: closes up to <see cref="Max"/> Published RFQs whose deadline passed; returns how many closed.</summary>
public sealed record CloseDueRfqsCommand(int Max = 100) : IRequest<Result<int>>;

/// <summary>SRC-020: cancellation with reason; the requisition returns to Submitted.</summary>
public sealed record CancelRfqCommand(Guid Id, long? ExpectedVersion, string? Reason) : IRequest<Result<RfqView>>;

/// <summary>SRC-013: manual rate with the <c>ManualExchangeRate</c> waiver; recomputes the comparison.</summary>
public sealed record RecordManualExchangeRateCommand(Guid Id, long? ExpectedVersion, string? Currency, string? Rate, DateOnly? QuoteDate, string? Reason)
    : IRequest<Result<RfqView>>;

/// <summary>
/// SRC-016..019 and BUD-009: the buyer's selection of one quote revision for the whole RFQ, submitted for approval.
/// Waiver reasons are needed only when SRC-017 or SRC-018 applies.
/// </summary>
public sealed record SubmitAwardCommand(
    Guid Id, long? ExpectedVersion, Guid QuoteRevisionId, string? Justification, string? InsufficientQuotationsReason, string? NonLeadingReason)
    : IRequest<Result<RfqView>>;

public sealed class CreateRfqCommandValidator : AbstractValidator<CreateRfqCommand>
{
    public CreateRfqCommandValidator() => RuleFor(command => command.RequisitionId).NotEmpty();
}

public sealed class InviteSupplierCommandValidator : AbstractValidator<InviteSupplierCommand>
{
    public InviteSupplierCommandValidator() => RuleFor(command => command.SupplierId).NotEmpty();
}

public sealed class PublishRfqCommandValidator : AbstractValidator<PublishRfqCommand>
{
    public PublishRfqCommandValidator() => RuleFor(command => command.Deadline).NotNull();
}

public sealed class ExtendRfqDeadlineCommandValidator : AbstractValidator<ExtendRfqDeadlineCommand>
{
    public ExtendRfqDeadlineCommandValidator() => RuleFor(command => command.Deadline).NotNull();
}

public sealed class RecordManualExchangeRateCommandValidator : AbstractValidator<RecordManualExchangeRateCommand>
{
    public RecordManualExchangeRateCommandValidator()
    {
        RuleFor(command => command.Currency).NotEmpty().Length(3);
        RuleFor(command => command.Rate).NotEmpty().MaximumLength(30);
        RuleFor(command => command.QuoteDate).NotNull();
    }
}

public sealed class SubmitAwardCommandValidator : AbstractValidator<SubmitAwardCommand>
{
    public SubmitAwardCommandValidator()
    {
        RuleFor(command => command.QuoteRevisionId).NotEmpty();
        RuleFor(command => command.Justification).MaximumLength(RfqAward.JustificationMaxLength);
    }
}

public sealed class RfqCommandHandlers(
    IRequestForQuotationRepository rfqs,
    IRfqNumberAllocator numbers,
    IPurchaseRequisitionRepository requisitions,
    IBudgetAllocationRepository allocations,
    ISupplierSnapshotReader suppliers,
    SourcingExchangeRates exchangeRates,
    SourcingAccess access,
    ProcurementWriter writer) :
    IRequestHandler<CreateRfqCommand, Result<RfqView>>,
    IRequestHandler<InviteSupplierCommand, Result<RfqView>>,
    IRequestHandler<RemoveInvitationCommand, Result<RfqView>>,
    IRequestHandler<PublishRfqCommand, Result<RfqView>>,
    IRequestHandler<ExtendRfqDeadlineCommand, Result<RfqView>>,
    IRequestHandler<RemindRfqSuppliersCommand, Result<RfqView>>,
    IRequestHandler<CloseRfqCommand, Result<RfqView>>,
    IRequestHandler<CloseDueRfqCommand, Result<bool>>,
    IRequestHandler<CloseDueRfqsCommand, Result<int>>,
    IRequestHandler<CancelRfqCommand, Result<RfqView>>,
    IRequestHandler<RecordManualExchangeRateCommand, Result<RfqView>>,
    IRequestHandler<SubmitAwardCommand, Result<RfqView>>
{
    private const int SystemRetries = 3;
    private static readonly SourcingPolicy Policy = SourcingPolicy.V1;

    public async Task<Result<RfqView>> Handle(CreateRfqCommand request, CancellationToken cancellationToken)
    {
        var method = SourcingMethod.Competitive;
        if (request.Method is { Length: > 0 } text && (!Enum.TryParse(text, true, out method) || int.TryParse(text, out _)))
            return ApplicationError.Validation("method", "method accepts Competitive or Direct.");
        var requisition = await requisitions.FindAsync(request.RequisitionId, cancellationToken);
        if (requisition is null)
            return SourcingErrors.RequisitionNotEligible.ToApplicationError();
        if (requisition.Status != RequisitionStatus.Submitted)
            return SourcingErrors.RequisitionNotEligible.ToApplicationError() with
            {
                Extensions = new Dictionary<string, object?> { ["currentState"] = requisition.Status.ToString() }
            };
        if (await rfqs.FindActiveByRequisitionAsync(requisition.Id, cancellationToken) is not null)
            return SourcingErrors.AlreadyActive.ToApplicationError();

        var settings = await writer.SettingsAsync(cancellationToken);
        var now = writer.UtcNow;
        var today = ProcurementWriter.LocalDate(now, settings.TimeZone);
        var submission = requisition.Submissions.Single(candidate => candidate.Revision == requisition.Revision);
        var snapshot = new RequisitionSourcingSnapshot(
            requisition.Id, requisition.Number, requisition.Revision, requisition.RequesterId, submission.CostCenterId, submission.DepartmentId,
            submission.Currency, submission.EstimatedTotal, submission.NeededBy,
            submission.Lines.OrderBy(line => line.LineNumber).Select(line => new RfqLineSnapshot(
                line.LineNumber, line.CatalogItemId, line.Description, line.Kind.ToString(), line.CategoryId, line.CategoryCode, line.UnitOfMeasureId,
                line.UnitCode, line.Quantity, line.EstimatedUnitPrice, line.LineTotal)).ToArray());
        // SRC-005: a non-base estimate uses the PTAX rate of the creation date; without it the threshold counts as exceeded.
        var estimateRate = snapshot.Currency == settings.BaseCurrency ? null : await exchangeRates.ResolveAsync(snapshot.Currency, today, cancellationToken);
        var round = await rfqs.CountRoundsAsync(requisition.Id, cancellationToken) + 1;
        var number = RequestForQuotation.FormatNumber(today.Year, await numbers.NextAsync(today.Year, cancellationToken));
        var (rfq, error) = RequestForQuotation.Create(
            Guid.NewGuid(), number, round, method, snapshot, settings.BaseCurrency, estimateRate, Policy, writer.ActorId, now, writer.CorrelationId);
        if (error is not null)
            return error.ToApplicationError();

        rfqs.Add(rfq!);
        writer.Audit(SourcingAudit.Created, SourcingAudit.Resource, rfq!.Id, toState: "Draft", ruleIds: ["SRC-001", "SRC-002", "SRC-005"],
            details: new Dictionary<string, string>
            {
                ["number"] = rfq.Number,
                ["requisitionId"] = requisition.Id.ToString(),
                ["revision"] = SourcingAudit.Text(rfq.RequisitionRevision),
                ["roundNumber"] = SourcingAudit.Text(rfq.RoundNumber),
                ["method"] = rfq.Method.ToString(),
                ["policy"] = $"{SourcingPolicy.PolicyType}:{SourcingAudit.Text(rfq.PolicyVersion)}"
            });
        var commit = await writer.CommitAsync(_ => Task.FromResult<long?>(null), UniqueViolation, cancellationToken);
        return commit is null ? await access.ViewAsync(rfq, cancellationToken) : commit;
    }

    public async Task<Result<RfqView>> Handle(InviteSupplierCommand request, CancellationToken cancellationToken)
    {
        var (rfq, failure) = await LoadAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        var supplier = (await suppliers.GetAsync([request.SupplierId], cancellationToken)).GetValueOrDefault(request.SupplierId);
        if (supplier is null)
            return ProcurementWriter.NotFound("supplier");
        var (_, error) = rfq!.Invite(supplier, writer.ActorId, writer.UtcNow, writer.CorrelationId);
        if (error is not null)
            return error.ToApplicationError();

        writer.Audit(SourcingAudit.SupplierInvited, SourcingAudit.Resource, rfq.Id, ruleIds: ["SRC-003", "SRC-007"],
            details: new Dictionary<string, string> { ["supplierId"] = supplier.SupplierId.ToString() });
        return await CommitAsync(rfq, null, null, cancellationToken);
    }

    public async Task<Result<RfqView>> Handle(RemoveInvitationCommand request, CancellationToken cancellationToken)
    {
        var (rfq, failure) = await LoadAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        if (rfq!.RemoveInvitation(request.SupplierId, writer.ActorId, writer.UtcNow, writer.CorrelationId) is { } error)
            return error.ToApplicationError();

        writer.Audit(SourcingAudit.SupplierRemoved, SourcingAudit.Resource, rfq.Id, ruleIds: ["SRC-007"],
            details: new Dictionary<string, string> { ["supplierId"] = request.SupplierId.ToString() });
        return await CommitAsync(rfq, null, null, cancellationToken);
    }

    public async Task<Result<RfqView>> Handle(PublishRfqCommand request, CancellationToken cancellationToken)
    {
        var (rfq, failure) = await LoadAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        if (request.Deadline is not { Kind: DateTimeKind.Utc } deadline)
            return SourcingErrors.DeadlineInvalid("The deadline must be a UTC instant (ISO 8601 with Z).").ToApplicationError();
        WaiverRequest? waiver = null;
        if (rfq!.Status == RfqStatus.Draft && rfq.RequiredPublishWaiver is { } waiverType)
        {
            waiver = await access.WaiverAsync(waiverType, request.WaiverReason, cancellationToken);
            if (waiver is null)
                return SourcingAudit.WaiverPermissionRequired(waiverType, SourcingAccess.PermissionFor(waiverType));
        }

        var requisition = await requisitions.FindAsync(rfq.RequisitionId, cancellationToken)
            ?? throw new InvalidOperationException("An RFQ references a missing requisition.");
        var invited = rfq.ActiveInvitations.Select(invitation => invitation.SupplierId).ToArray();
        var states = await SupplierStatesAsync(invited, cancellationToken);
        var now = writer.UtcNow;
        var (recorded, error) = rfq.Publish(deadline, states, waiver, Policy, writer.ActorId, now, writer.CorrelationId);
        if (error is not null)
            return error.ToApplicationError();
        if (requisition.StartSourcing(rfq.Id, writer.ActorId, now, writer.CorrelationId) is { } requisitionError)
            return requisitionError.ToApplicationError();

        writer.Audit(SourcingAudit.Published, SourcingAudit.Resource, rfq.Id, "Draft", "Published", recorded?.Reason,
            recorded is null ? ["SRC-004", "SRC-005"] : ["SRC-004", "SRC-006"],
            new Dictionary<string, string>
            {
                ["deadline"] = Instant(deadline),
                ["invitationCount"] = SourcingAudit.Text(invited.Length),
                ["method"] = rfq.Method.ToString()
            }, recorded?.Id);
        AuditWaiver(recorded, rfq.Id);
        writer.Audit(SourcingAudit.RequisitionSourcingStarted, "purchase-requisition", requisition.Id, "Submitted", "InSourcing", ruleIds: ["SRC-001"],
            details: new Dictionary<string, string> { ["rfqId"] = rfq.Id.ToString(), ["number"] = requisition.Number });
        return await CommitAsync(rfq, requisition, null, cancellationToken);
    }

    public async Task<Result<RfqView>> Handle(ExtendRfqDeadlineCommand request, CancellationToken cancellationToken)
    {
        var (rfq, failure) = await LoadAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        if (request.Deadline is not { Kind: DateTimeKind.Utc } deadline)
            return SourcingErrors.DeadlineInvalid("The deadline must be a UTC instant (ISO 8601 with Z).", "SRC-011").ToApplicationError();
        var previous = rfq!.DeadlineUtc;
        if (rfq.ExtendDeadline(deadline, request.Reason, Policy, writer.ActorId, writer.UtcNow, writer.CorrelationId) is { } error)
            return error.ToApplicationError();

        writer.Audit(SourcingAudit.DeadlineExtended, SourcingAudit.Resource, rfq.Id, reason: MasterDataRules.NormalizeReason(request.Reason),
            ruleIds: ["SRC-011"],
            details: new Dictionary<string, string> { ["previousDeadline"] = Instant(previous!.Value), ["deadline"] = Instant(deadline) });
        return await CommitAsync(rfq, null, null, cancellationToken);
    }

    public async Task<Result<RfqView>> Handle(RemindRfqSuppliersCommand request, CancellationToken cancellationToken)
    {
        var (rfq, failure) = await LoadAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        var (suppliersToRemind, error) = rfq!.RequestReminder(writer.ActorId, writer.UtcNow, writer.CorrelationId);
        if (error is not null)
            return error.ToApplicationError();

        writer.Audit(SourcingAudit.ReminderRequested, SourcingAudit.Resource, rfq.Id, ruleIds: ["SRC-022"],
            details: new Dictionary<string, string> { ["invitationCount"] = SourcingAudit.Text(suppliersToRemind.Count) });
        return await CommitAsync(rfq, null, null, cancellationToken);
    }

    public async Task<Result<RfqView>> Handle(CloseRfqCommand request, CancellationToken cancellationToken)
    {
        var (rfq, failure) = await LoadAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        var early = rfq!.Status == RfqStatus.Published && writer.UtcNow < rfq.DeadlineUtc;
        if (early && !await writer.HasPermissionAsync(Permissions.SourcingRfqsCloseEarly, cancellationToken))
            return ProcurementWriter.Forbidden("Closing before the deadline requires sourcing.rfqs.close-early (SRC-012).");

        var error = await CloseCoreAsync(rfq, request.Reason, cancellationToken);
        return error is not null ? error : await CommitAsync(rfq, null, null, cancellationToken);
    }

    public async Task<Result<bool>> Handle(CloseDueRfqCommand request, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var rfq = await rfqs.FindAsync(request.Id, cancellationToken);
            if (rfq is null)
                return SourcingAudit.NotFound();
            if (rfq.Status != RfqStatus.Published || writer.UtcNow < rfq.DeadlineUtc)
                return false;

            if (await CloseCoreAsync(rfq, null, cancellationToken) is { } error)
                return error;
            var events = writer.Stage(rfq.DomainEvents);
            var commit = await writer.CommitAsync(token => rfqs.GetVersionAsync(rfq.Id, token), null, cancellationToken);
            if (commit is null)
            {
                writer.Record(events);
                rfq.ClearDomainEvents();
                return true;
            }

            if (attempt >= SystemRetries || commit.Category != Wrappers.ErrorCategory.PreconditionFailed)
                return commit;
        }
    }

    public async Task<Result<int>> Handle(CloseDueRfqsCommand request, CancellationToken cancellationToken)
    {
        var due = await rfqs.ListDueForCloseAsync(writer.UtcNow, Math.Clamp(request.Max, 1, 500), cancellationToken);
        var closed = 0;
        foreach (var id in due)
        {
            var result = await Handle(new CloseDueRfqCommand(id), cancellationToken);
            if (result.IsSuccess && result.Value)
                closed++;
        }

        return closed;
    }

    public async Task<Result<RfqView>> Handle(CancelRfqCommand request, CancellationToken cancellationToken)
    {
        var (rfq, failure) = await LoadAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        var from = rfq!.Status.ToString();
        var now = writer.UtcNow;
        var (wasInSourcing, error) = rfq.Cancel(request.Reason, writer.ActorId, now, writer.CorrelationId);
        if (error is not null)
            return error.ToApplicationError();
        var reason = MasterDataRules.NormalizeReason(request.Reason);
        PurchaseRequisition? requisition = null;
        if (wasInSourcing)
        {
            requisition = await requisitions.FindAsync(rfq.RequisitionId, cancellationToken)
                ?? throw new InvalidOperationException("An RFQ references a missing requisition.");
            if (requisition.ReturnFromSourcing(reason, writer.ActorId, now, writer.CorrelationId) is { } requisitionError)
                return requisitionError.ToApplicationError();
            writer.Audit(SourcingAudit.RequisitionSourcingReturned, "purchase-requisition", requisition.Id, "InSourcing", "Submitted", reason, ["SRC-020"],
                new Dictionary<string, string> { ["rfqId"] = rfq.Id.ToString(), ["number"] = requisition.Number });
        }

        writer.Audit(SourcingAudit.Cancelled, SourcingAudit.Resource, rfq.Id, from, "Cancelled", reason, ["SRC-020"],
            new Dictionary<string, string> { ["requisitionId"] = rfq.RequisitionId.ToString() });
        return await CommitAsync(rfq, requisition, null, cancellationToken);
    }

    public async Task<Result<RfqView>> Handle(RecordManualExchangeRateCommand request, CancellationToken cancellationToken)
    {
        var (rfq, failure) = await LoadAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        if (!decimal.TryParse(request.Rate, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rate))
            return ApplicationError.Validation("rate", "The rate must be an invariant decimal string.");
        var waiver = await access.WaiverAsync(WaiverType.ManualExchangeRate, request.Reason, cancellationToken);
        if (waiver is null)
            return SourcingAudit.WaiverPermissionRequired(WaiverType.ManualExchangeRate, Permissions.SourcingRatesManual);
        var (comparison, error) = rfq!.RecordManualRate(request.Currency, rate, request.QuoteDate!.Value, waiver, Policy, writer.UtcNow, writer.CorrelationId);
        if (error is not null)
            return error.ToApplicationError();

        var recorded = rfq.Waivers.OrderBy(candidate => candidate.DecidedAtUtc).Last();
        var snapshot = rfq.ExchangeRates.Single(candidate => candidate.WaiverId == recorded.Id);
        AuditWaiver(recorded, rfq.Id);
        writer.Audit(SourcingAudit.RateRecorded, SourcingAudit.Resource, rfq.Id, reason: recorded.Reason, ruleIds: ["SRC-013", "CALC-004"],
            details: new Dictionary<string, string>
            {
                ["currency"] = snapshot.FromCurrency,
                ["rate"] = SourcingFormat.Rate(snapshot.Rate),
                ["rateSource"] = snapshot.Source.ToString(),
                ["quoteDate"] = snapshot.QuoteDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["comparisonId"] = comparison!.Id.ToString(),
                ["comparisonStatus"] = comparison.Status.ToString()
            }, waiverId: recorded.Id);
        return await CommitAsync(rfq, null, null, cancellationToken);
    }

    public async Task<Result<RfqView>> Handle(SubmitAwardCommand request, CancellationToken cancellationToken)
    {
        var (rfq, failure) = await LoadAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        var settings = await writer.SettingsAsync(cancellationToken);
        var now = writer.UtcNow;
        var today = ProcurementWriter.LocalDate(now, settings.TimeZone);
        var states = await SupplierStatesAsync(rfq!.CurrentComparison?.Entries.Select(entry => entry.SupplierId).Distinct().ToArray() ?? [], cancellationToken);
        var (needed, blocking) = rfq.EvaluateAward(request.QuoteRevisionId, states, today, Policy);
        if (blocking is not null)
            return blocking.ToApplicationError();
        var waivers = new List<WaiverRequest>();
        foreach (var type in needed)
        {
            var reason = type == WaiverType.InsufficientQuotations ? request.InsufficientQuotationsReason : request.NonLeadingReason;
            if (reason is null)
                continue;
            var waiver = await access.WaiverAsync(type, reason, cancellationToken);
            if (waiver is null)
                return SourcingAudit.WaiverPermissionRequired(type, SourcingAccess.PermissionFor(type));
            waivers.Add(waiver);
        }

        var requisition = await requisitions.FindAsync(rfq.RequisitionId, cancellationToken)
            ?? throw new InvalidOperationException("An RFQ references a missing requisition.");
        // BUD-006/009: the allocation row is locked so a competing increase observes the reduced availability.
        await writer.BeginAsync(cancellationToken);
        var allocation = requisition.ActiveAllocationId is { } allocationId
            ? await allocations.FindForUpdateAsync(allocationId, requisition.Id, cancellationToken)
            : null;
        if (allocation is null)
            return await writer.FailAsync(DomainError.InvalidTransition(requisition.Status.ToString(), "The requisition has no active reservation.", "BUD-009")
                .ToApplicationError());

        var awardId = Guid.NewGuid();
        var (award, awardError) = rfq.SubmitAward(awardId, request.QuoteRevisionId, states, today, waivers, request.Justification, Policy,
            writer.ActorId, now, writer.CorrelationId);
        if (awardError is not null)
            return await writer.FailAsync(awardError.ToApplicationError());
        var requisitionAmount = new Money(award!.RequisitionCurrencyTotal, award.RequisitionCurrency);
        var previousAmount = allocation.Reservations.Single(reservation => reservation.RequisitionId == requisition.Id && reservation.IsActive).Amount;
        var (reservation, adjusted, adjustError) = allocation.AdjustForAward(requisition.Id, requisitionAmount, awardId, writer.ActorId, now, writer.CorrelationId);
        if (adjustError is not null)
        {
            writer.LogBudgetRejection(adjustError.Code, requisitionAmount.Currency);
            return await writer.FailAsync(adjustError.ToApplicationError());
        }

        if (requisition.SubmitAward(awardId, award.SupplierId, award.SupplierLegalName, requisitionAmount, writer.ActorId, now, writer.CorrelationId) is { } requisitionError)
            return await writer.FailAsync(requisitionError.ToApplicationError());

        var awardWaivers = rfq.Waivers.Where(waiver => waiver.ScopeId == awardId).ToArray();
        writer.Audit(SourcingAudit.AwardSubmitted, SourcingAudit.Resource, rfq.Id, "Closed", "AwardPending", ruleIds: ["SRC-016", "SRC-019", "CALC-007"],
            details: new Dictionary<string, string>
            {
                ["awardId"] = awardId.ToString(),
                ["quoteRevisionId"] = award.QuoteRevisionId.ToString(),
                ["supplierId"] = award.SupplierId.ToString(),
                ["rank"] = SourcingAudit.Text(award.Rank),
                ["amount"] = SourcingAudit.Amount(award.NormalizedTotal),
                ["currency"] = award.BaseCurrency
            }, waiverId: awardWaivers.FirstOrDefault()?.Id);
        foreach (var waiver in awardWaivers)
            AuditWaiver(waiver, rfq.Id);
        writer.Audit(SourcingAudit.AwardSubmitted, "purchase-requisition", requisition.Id, "InSourcing", "PendingApproval", ruleIds: ["SRC-019"],
            details: new Dictionary<string, string> { ["awardId"] = awardId.ToString(), ["rfqId"] = rfq.Id.ToString(), ["number"] = requisition.Number });
        if (adjusted)
            writer.Audit(SourcingAudit.ReservationAdjusted, BudgetMoney.ReservationResource, reservation!.Id, "Reserved", "Reserved", ruleIds: ["BUD-009"],
                details: new Dictionary<string, string>
                {
                    ["requisitionId"] = requisition.Id.ToString(),
                    ["allocationId"] = allocation.Id.ToString(),
                    ["awardId"] = awardId.ToString(),
                    ["previousAmount"] = SourcingAudit.Amount(previousAmount),
                    ["amount"] = SourcingAudit.Amount(reservation.Amount),
                    ["currency"] = reservation.Currency
                });
        return await CommitAsync(rfq, requisition, allocation, cancellationToken);
    }

    /// <summary>Close with rates resolved for the close local date (SP-5); a missing rate leaves the comparison Incomplete.</summary>
    private async Task<ApplicationError?> CloseCoreAsync(RequestForQuotation rfq, string? reason, CancellationToken cancellationToken)
    {
        var settings = await writer.SettingsAsync(cancellationToken);
        var now = writer.UtcNow;
        var closeDate = ProcurementWriter.LocalDate(now, settings.TimeZone);
        var states = await SupplierStatesAsync(rfq.Quotes.Select(quote => quote.SupplierId).ToArray(), cancellationToken);
        var rates = rfq.Status == RfqStatus.Published && (now >= rfq.DeadlineUtc || MasterDataRules.NormalizeReason(reason) is not null)
            ? await exchangeRates.ResolveManyAsync(rfq.CurrenciesNeedingRates(), closeDate, cancellationToken)
            : [];
        var from = rfq.Status.ToString();
        var (comparison, error) = rfq.Close(reason, states, rates, closeDate, Policy, writer.ActorId, now, writer.CorrelationId);
        if (error is not null)
            return error.ToApplicationError();

        writer.Audit(SourcingAudit.Closed, SourcingAudit.Resource, rfq.Id, from, "Closed", rfq.CloseReason, ["SRC-012", "SRC-013", "SRC-014", "SRC-015"],
            new Dictionary<string, string>
            {
                ["comparisonId"] = comparison!.Id.ToString(),
                ["comparisonStatus"] = comparison.Status.ToString(),
                ["validQuoteCount"] = SourcingAudit.Text(comparison.ValidQuoteCount)
            });
        return null;
    }

    private void AuditWaiver(RfqWaiver? waiver, Guid rfqId)
    {
        if (waiver is null)
            return;
        writer.Audit(SourcingAudit.WaiverRecorded, SourcingAudit.WaiverResource, waiver.Id, reason: waiver.Reason, ruleIds: [waiver.RuleId],
            details: new Dictionary<string, string>
            {
                ["rfqId"] = rfqId.ToString(),
                ["waiverType"] = waiver.Type.ToString(),
                ["permission"] = waiver.Permission
            }, waiverId: waiver.Id);
    }

    private async Task<IReadOnlyDictionary<Guid, bool>> SupplierStatesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        (await suppliers.GetAsync(ids, cancellationToken)).ToDictionary(pair => pair.Key, pair => pair.Value.IsActive);

    /// <summary>404 when missing or the actor may not read RFQs, then 428/412.</summary>
    private async Task<(RequestForQuotation? Rfq, ApplicationError? Error)> LoadAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken)
    {
        if (!await access.MayReadAsync(cancellationToken))
            return (null, ProcurementWriter.Forbidden());
        var rfq = await rfqs.FindAsync(id, cancellationToken);
        if (rfq is null)
            return (null, SourcingAudit.NotFound());
        return VersionPrecondition.Check(expectedVersion, rfq.Version) is { } precondition ? (null, precondition) : (rfq, null);
    }

    private async Task<Result<RfqView>> CommitAsync(
        RequestForQuotation rfq, PurchaseRequisition? requisition, BudgetAllocation? allocation, CancellationToken cancellationToken)
    {
        var events = writer.Stage(rfq.DomainEvents.Concat(requisition?.DomainEvents ?? []).Concat(allocation?.DomainEvents ?? []).ToArray());
        var commit = await writer.CommitAsync(token => rfqs.GetVersionAsync(rfq.Id, token), UniqueViolation, cancellationToken);
        if (commit is not null)
            return commit;

        writer.Record(events);
        rfq.ClearDomainEvents();
        requisition?.ClearDomainEvents();
        allocation?.ClearDomainEvents();
        return await access.ViewAsync(rfq, cancellationToken);
    }

    private static ApplicationError? UniqueViolation(string constraint) =>
        constraint == IRequestForQuotationRepository.ActiveRfqUniqueKey ? SourcingErrors.AlreadyActive.ToApplicationError() : null;

    private static string Instant(DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);
}
