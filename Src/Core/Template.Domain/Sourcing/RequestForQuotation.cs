using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Template.Domain.Common;

namespace Template.Domain.Sourcing;

/// <summary>
/// Request-for-quotation aggregate (docs/product/domain-model.md §3, workflows.md §2). It owns invitations, quotes and
/// their immutable revisions, waivers, exchange-rate snapshots, comparisons, awards and the append-only history.
/// Lines are the requisition submission snapshot (SRC-002); supplier data are snapshots taken at invitation (SRC-003),
/// so later master-data changes never rewrite history. Closing freezes the current revisions and computes the
/// immutable comparison (SRC-012). Every change increments the version (SP-1); every transition raises a
/// <see cref="DomainEvent"/> mapped to the approved integration events.
/// </summary>
public sealed class RequestForQuotation : VersionedAggregateRoot
{
    public const int NumberMaxLength = 20;
    public const int MaxInvitations = 50;
    public const int MaxQuoteAttachments = 10;
    public const int ReasonCodeMaxLength = 40;

    public const string ActionCreated = "created";
    public const string ActionInvited = "supplier-invited";
    public const string ActionRemoved = "supplier-removed";
    public const string ActionPublished = "published";
    public const string ActionDeadlineExtended = "deadline-extended";
    public const string ActionReminderRequested = "reminder-requested";
    public const string ActionQuoteRecorded = "quote-recorded";
    public const string ActionQuoteRevised = "quote-revised";
    public const string ActionQuoteWithdrawn = "quote-withdrawn";
    public const string ActionClosed = "closed";
    public const string ActionRateRecorded = "rate-recorded";
    public const string ActionAwardSubmitted = "award-submitted";
    public const string ActionCancelled = "cancelled";
    public const string ActionAwardApproved = "award-approved";
    public const string ActionAwardEnded = "award-ended";

    private readonly List<RfqLine> lines = [];
    private readonly List<RfqInvitation> invitations = [];
    private readonly List<SupplierQuote> quotes = [];
    private readonly List<RfqWaiver> waivers = [];
    private readonly List<RfqExchangeRate> exchangeRates = [];
    private readonly List<RfqComparison> comparisons = [];
    private readonly List<RfqAward> awards = [];
    private readonly List<RfqHistoryEntry> history = [];
    private readonly DomainEventBuffer events = new();

    private RequestForQuotation()
    {
        Number = string.Empty;
        RequisitionNumber = string.Empty;
        RequisitionCurrency = string.Empty;
        BaseCurrency = string.Empty;
    }

    /// <summary><c>RFQ-{yyyy}-{nnnnnn}</c>.</summary>
    public string Number { get; private set; }

    public Guid RequisitionId { get; private set; }
    public string RequisitionNumber { get; private set; }
    public int RequisitionRevision { get; private set; }
    public Guid RequesterId { get; private set; }
    public Guid CostCenterId { get; private set; }
    public Guid DepartmentId { get; private set; }
    public string RequisitionCurrency { get; private set; }
    public decimal RequisitionEstimatedTotal { get; private set; }
    public DateOnly NeededBy { get; private set; }

    /// <summary>Previous rounds of the requisition + 1 (SRC-001).</summary>
    public int RoundNumber { get; private set; }

    public SourcingMethod Method { get; private set; }

    /// <summary>Pinned <see cref="SourcingPolicy"/> version (B23-10).</summary>
    public int PolicyVersion { get; private set; }

    public string BaseCurrency { get; private set; }

    /// <summary>SRC-005 estimate in base currency at creation; <c>null</c> when no rate was available.</summary>
    public decimal? EstimateInBaseCurrency { get; private set; }

    /// <summary>SRC-005: above the competition threshold (or unknown, conservative).</summary>
    public bool AboveThreshold { get; private set; }

    public DateTime? DeadlineUtc { get; private set; }
    public RfqStatus Status { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public bool ClosedEarly { get; private set; }
    public string? CloseReason { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? CancellationReasonCode { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<RfqLine> Lines => lines;
    public IReadOnlyCollection<RfqInvitation> Invitations => invitations;
    public IReadOnlyCollection<SupplierQuote> Quotes => quotes;
    public IReadOnlyCollection<RfqWaiver> Waivers => waivers;
    public IReadOnlyCollection<RfqExchangeRate> ExchangeRates => exchangeRates;
    public IReadOnlyCollection<RfqComparison> Comparisons => comparisons;
    public IReadOnlyCollection<RfqAward> Awards => awards;
    public IReadOnlyCollection<RfqHistoryEntry> History => history;
    public IReadOnlyList<DomainEvent> DomainEvents => events.Events;

    /// <summary>The latest comparison, or <c>null</c> before close.</summary>
    public RfqComparison? CurrentComparison => comparisons.OrderByDescending(comparison => comparison.Sequence).FirstOrDefault();

    public RfqAward? PendingAward => awards.FirstOrDefault(award => award.Status == AwardStatus.PendingApproval);

    public bool IsTerminal => Status is RfqStatus.Cancelled;

    /// <summary>Non-terminal RFQs block a second active RFQ of the same requisition (SRC-001).</summary>
    public bool IsActive => Status is not RfqStatus.Cancelled;

    public IEnumerable<RfqInvitation> ActiveInvitations => invitations.Where(invitation => invitation.IsActive);

    public void ClearDomainEvents() => events.Clear();

    public static string FormatNumber(int year, long sequence) =>
        $"RFQ-{year.ToString("D4", NumberFormatInfo.InvariantInfo)}-{sequence.ToString("D6", NumberFormatInfo.InvariantInfo)}";

    /// <summary>
    /// SRC-001/002/005: a <c>Draft</c> round for the requisition's latest submission (eligibility is checked by the
    /// use case), with the pinned policy and the threshold estimate in base currency. A non-base estimate uses
    /// <paramref name="estimateRate"/> (rate of the creation date); without it the threshold counts as exceeded.
    /// </summary>
    public static (RequestForQuotation? Rfq, DomainError? Error) Create(
        Guid id, string number, int roundNumber, SourcingMethod method, RequisitionSourcingSnapshot requisition, string baseCurrency,
        ExchangeRateInput? estimateRate, SourcingPolicy policy, Guid? actorId, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureId(id, nameof(id));
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseCurrency);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentNullException.ThrowIfNull(requisition);
        ArgumentNullException.ThrowIfNull(policy);
        if (!Enum.IsDefined(method))
            return (null, SourcingErrors.Validation("method", "The sourcing method must be Competitive or Direct.", "SRC-006"));
        if (requisition.Lines.Count == 0 || roundNumber < 1)
            return (null, SourcingErrors.RequisitionNotEligible);
        if (estimateRate is not null && (estimateRate.Currency != requisition.Currency || !CalculationRules.IsValidExchangeRate(estimateRate.Rate)))
            throw new ArgumentException("The estimate rate must convert the requisition currency.", nameof(estimateRate));

        var rfq = new RequestForQuotation
        {
            Id = id,
            Number = number,
            RequisitionId = requisition.RequisitionId,
            RequisitionNumber = requisition.Number,
            RequisitionRevision = requisition.Revision,
            RequesterId = requisition.RequesterId,
            CostCenterId = requisition.CostCenterId,
            DepartmentId = requisition.DepartmentId,
            RequisitionCurrency = requisition.Currency,
            RequisitionEstimatedTotal = requisition.EstimatedTotal,
            NeededBy = requisition.NeededBy,
            RoundNumber = roundNumber,
            Method = method,
            PolicyVersion = policy.Version,
            BaseCurrency = baseCurrency,
            Status = RfqStatus.Draft,
            CreatedAtUtc = nowUtc,
            CreatedBy = actorId,
            UpdatedAtUtc = nowUtc
        };
        var estimate = new Money(requisition.EstimatedTotal, requisition.Currency);
        if (requisition.Currency == baseCurrency)
            rfq.EstimateInBaseCurrency = estimate.Amount;
        else if (estimateRate is not null)
        {
            rfq.EstimateInBaseCurrency = CalculationRules.Normalize(estimate, estimateRate.Rate, baseCurrency).Amount;
            rfq.exchangeRates.Add(new RfqExchangeRate(Guid.NewGuid(), id, ExchangeRatePurpose.Estimate, baseCurrency, estimateRate, null, null, null));
        }

        rfq.AboveThreshold = policy.IsAboveThreshold(rfq.EstimateInBaseCurrency);
        foreach (var line in requisition.Lines.OrderBy(line => line.LineNumber))
            rfq.lines.Add(new RfqLine(Guid.NewGuid(), id, line));
        rfq.Append(ActionCreated, null, RfqStatus.Draft, actorId, nowUtc, null, correlationId, null, null);
        return (rfq, null);
    }

    /// <summary>The waiver SRC-006 needs at publish: <c>Direct</c> RFQs only, by estimate versus threshold.</summary>
    public WaiverType? RequiredPublishWaiver =>
        Method == SourcingMethod.Direct ? AboveThreshold ? WaiverType.DirectPurchase : WaiverType.LowValueDirectPurchase : null;

    /// <summary>SRC-003/007: Draft, or Published before the deadline; Active suppliers only; one active invitation each.</summary>
    public (RfqInvitation? Invitation, DomainError? Error) Invite(SupplierSnapshot supplier, Guid? actorId, DateTime nowUtc, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(supplier);
        MasterDataRules.EnsureId(supplier.SupplierId, nameof(supplier));
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (Status == RfqStatus.Published && nowUtc >= DeadlineUtc)
            return (null, SourcingErrors.DeadlinePassed with { RuleId = "SRC-007" });
        if (Status is not (RfqStatus.Draft or RfqStatus.Published))
            return (null, DomainError.InvalidTransition(Status.ToString(), "Suppliers are invited while the RFQ is Draft or Published.", "SRC-007"));
        if (!supplier.IsActive)
            return (null, SourcingErrors.SupplierNotEligibleFor(supplier.SupplierId) with { RuleId = "SRC-003" });
        if (ActiveInvitations.Any(invitation => invitation.SupplierId == supplier.SupplierId))
            return (null, SourcingErrors.SupplierAlreadyInvited);
        if (Method == SourcingMethod.Direct && ActiveInvitations.Any())
            return (null, SourcingErrors.DirectSingleSupplier);
        if (ActiveInvitations.Count() >= MaxInvitations)
            return (null, SourcingErrors.Validation("supplierId", $"An RFQ invites at most {MaxInvitations} suppliers.", "SRC-007"));

        var invitation = new RfqInvitation(Guid.NewGuid(), Id, invitations.Count + 1, supplier, actorId, nowUtc);
        invitations.Add(invitation);
        Append(ActionInvited, Status, Status, actorId, nowUtc, null, correlationId, null, supplier.SupplierId);
        return (invitation, null);
    }

    /// <summary>SRC-007: invitations are removed only while Draft; the row and its snapshot stay in history.</summary>
    public DomainError? RemoveInvitation(Guid supplierId, Guid? actorId, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (Status != RfqStatus.Draft)
            return DomainError.InvalidTransition(Status.ToString(), "Invitations are removed only while the RFQ is Draft.", "SRC-007");
        var invitation = ActiveInvitations.FirstOrDefault(candidate => candidate.SupplierId == supplierId);
        if (invitation is null)
            return SourcingErrors.NotFound("invitation");

        invitation.Remove(actorId, nowUtc);
        Append(ActionRemoved, Status, Status, actorId, nowUtc, null, correlationId, null, supplierId);
        return null;
    }

    /// <summary>
    /// <c>Draft → Published</c> (SRC-004/005/006): deadline inside the response window, every invited supplier still
    /// Active, the competitive minimum of invitations or, for a Direct RFQ, exactly one invitation and the waiver
    /// <see cref="RequiredPublishWaiver"/>. The use case moves the requisition to <c>InSourcing</c> in the same commit.
    /// </summary>
    public (RfqWaiver? Waiver, DomainError? Error) Publish(
        DateTime deadlineUtc, IReadOnlyDictionary<Guid, bool> supplierActive, WaiverRequest? waiver, SourcingPolicy policy, Guid? actorId,
        DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentNullException.ThrowIfNull(supplierActive);
        ArgumentNullException.ThrowIfNull(policy);
        if (Status != RfqStatus.Draft)
            return (null, DomainError.InvalidTransition(Status.ToString(), "Only a Draft RFQ can be published.", "SRC-004"));
        if (deadlineUtc.Kind != DateTimeKind.Utc)
            return (null, SourcingErrors.DeadlineInvalid("The deadline must be a UTC instant."));
        var window = deadlineUtc - nowUtc;
        if (window < policy.MinResponseWindow || window > policy.MaxResponseWindow)
            return (null, SourcingErrors.DeadlineInvalid(
                $"The deadline must be {policy.MinResponseWindow.TotalHours:0} hours to {policy.MaxResponseWindow.TotalDays:0} days from now."));
        var invited = ActiveInvitations.ToArray();
        if (invited.FirstOrDefault(invitation => !(supplierActive.TryGetValue(invitation.SupplierId, out var active) && active)) is { } ineligible)
            return (null, SourcingErrors.SupplierNotEligibleFor(ineligible.SupplierId) with { RuleId = "SRC-003" });

        RfqWaiver? recorded = null;
        if (Method == SourcingMethod.Competitive)
        {
            var required = policy.RequiredInvitations(AboveThreshold);
            if (invited.Length < required)
                return (null, SourcingErrors.InsufficientInvitations(required, invited.Length));
        }
        else
        {
            if (invited.Length != 1)
                return (null, SourcingErrors.DirectSingleSupplier);
            var (waiverRow, waiverError) = RecordWaiver(waiver, RequiredPublishWaiver!.Value, "SRC-006", null, nowUtc);
            if (waiverError is not null)
                return (null, waiverError);
            recorded = waiverRow;
        }

        DeadlineUtc = deadlineUtc;
        PublishedAtUtc = nowUtc;
        Transition(ActionPublished, RfqStatus.Published, actorId, nowUtc, recorded?.Reason, correlationId, recorded?.Id,
            new RfqPublished(nowUtc, Id, Number, RequisitionId, deadlineUtc, invited.Select(invitation => invitation.SupplierId).OrderBy(id => id).ToArray(), Method));
        return (recorded, null);
    }

    /// <summary>SRC-011: before the deadline passes, later than now's deadline, at most +30 days, with reason.</summary>
    public DomainError? ExtendDeadline(DateTime newDeadlineUtc, string? reason, SourcingPolicy policy, Guid? actorId, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentNullException.ThrowIfNull(policy);
        if (Status != RfqStatus.Published)
            return DomainError.InvalidTransition(Status.ToString(), "Only a Published RFQ deadline can be extended.", "SRC-011");
        if (nowUtc >= DeadlineUtc)
            return SourcingErrors.DeadlinePassed;
        var normalizedReason = MasterDataRules.NormalizeReason(reason);
        if (normalizedReason is null)
            return DomainError.ReasonRequired("SRC-011", MasterDataRules.ReasonMinLength, MasterDataRules.ReasonMaxLength);
        if (newDeadlineUtc.Kind != DateTimeKind.Utc || newDeadlineUtc <= DeadlineUtc || newDeadlineUtc - DeadlineUtc!.Value > policy.MaxExtension)
            return SourcingErrors.DeadlineInvalid(
                $"The new deadline must be later than the current one by at most {policy.MaxExtension.TotalDays:0} days.", "SRC-011");

        var previous = DeadlineUtc!.Value;
        DeadlineUtc = newDeadlineUtc;
        Act(ActionDeadlineExtended, actorId, nowUtc, normalizedReason, correlationId, null,
            new RfqDeadlineExtended(nowUtc, Id, previous, newDeadlineUtc, ActiveSupplierIds()));
        return null;
    }

    /// <summary>SRC-022: queues a reminder to invited suppliers without a current quote, before the deadline.</summary>
    public (IReadOnlyList<Guid> SupplierIds, DomainError? Error) RequestReminder(Guid? actorId, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (Status != RfqStatus.Published)
            return ([], DomainError.InvalidTransition(Status.ToString(), "Reminders are sent while the RFQ is Published.", "SRC-022"));
        if (nowUtc >= DeadlineUtc)
            return ([], SourcingErrors.DeadlinePassed with { RuleId = "SRC-022" });
        var pending = ActiveInvitations
            .Where(invitation => quotes.All(quote => quote.SupplierId != invitation.SupplierId || quote.Latest.Status != QuoteRevisionStatus.Current))
            .Select(invitation => invitation.SupplierId).OrderBy(id => id).ToArray();
        if (pending.Length == 0)
            return ([], SourcingErrors.NoPendingSuppliers);

        Act(ActionReminderRequested, actorId, nowUtc, null, correlationId, null, new RfqReminderRequested(nowUtc, Id, DeadlineUtc!.Value, pending));
        return (pending, null);
    }

    /// <summary>
    /// SRC-008/010: revision 1 of an invited supplier's quote, entered by the buyer for a <c>Published</c> RFQ, received
    /// no later than now and the deadline; valid until at least the deadline local date.
    /// </summary>
    public (QuoteRevision? Revision, DomainError? Error) EnterQuote(
        Guid supplierId, QuoteInput input, DateOnly deadlineLocalDate, Guid? actorId, DateTime nowUtc, string correlationId)
    {
        if (CheckQuoteWindow(input, nowUtc) is { } windowError)
            return (null, windowError);
        var invitation = ActiveInvitations.FirstOrDefault(candidate => candidate.SupplierId == supplierId);
        if (invitation is null)
            return (null, SourcingErrors.QuoteSupplierNotInvited);
        if (quotes.Any(quote => quote.SupplierId == supplierId))
            return (null, SourcingErrors.QuoteExists);
        var (currency, terms, notes, files, error) = ValidateQuote(input, deadlineLocalDate);
        if (error is not null)
            return (null, error);

        var quote = new SupplierQuote(Guid.NewGuid(), Id, supplierId, invitation.Id);
        var revision = new QuoteRevision(Guid.NewGuid(), quote.Id, 1, currency!, actorId, nowUtc);
        revision.Apply(input, terms!, notes, files!);
        if (revision.Total < 0)
            return (null, SourcingErrors.Validation("discount", "The discount cannot exceed the subtotal plus freight (CALC-002).", "SRC-008"));
        quote.AddRevision(revision);
        quotes.Add(quote);
        Act(ActionQuoteRecorded, actorId, nowUtc, null, correlationId, quote.Id, new QuoteRecorded(nowUtc, Id, quote.Id, 1, supplierId));
        return (revision, null);
    }

    /// <summary>SRC-009: revision n+1 supersedes the current revision (or follows a withdrawn one); history is kept.</summary>
    public (QuoteRevision? Revision, DomainError? Error) ReviseQuote(
        Guid quoteId, QuoteInput input, DateOnly deadlineLocalDate, Guid? actorId, DateTime nowUtc, string correlationId)
    {
        if (CheckQuoteWindow(input, nowUtc) is { } windowError)
            return (null, windowError);
        var quote = quotes.FirstOrDefault(candidate => candidate.Id == quoteId);
        if (quote is null)
            return (null, SourcingErrors.NotFound("quote"));
        var previous = quote.Latest;
        if (input.ReceivedAtUtc < previous.ReceivedAtUtc)
            return (null, SourcingErrors.Validation("receivedAt", "A revision cannot be received before the revision it replaces.", "SRC-009"));
        var (currency, terms, notes, files, error) = ValidateQuote(input, deadlineLocalDate);
        if (error is not null)
            return (null, error);

        var revision = new QuoteRevision(Guid.NewGuid(), quote.Id, previous.RevisionNumber + 1, currency!, actorId, nowUtc);
        revision.Apply(input, terms!, notes, files!);
        if (revision.Total < 0)
            return (null, SourcingErrors.Validation("discount", "The discount cannot exceed the subtotal plus freight (CALC-002).", "SRC-008"));
        quote.AddRevision(revision);
        Act(ActionQuoteRevised, actorId, nowUtc, null, correlationId, quote.Id,
            new QuoteRecorded(nowUtc, Id, quote.Id, revision.RevisionNumber, quote.SupplierId));
        return (revision, null);
    }

    /// <summary>SRC-009: marks the current revision <c>Withdrawn</c> (reason required) while the RFQ is Published.</summary>
    public DomainError? WithdrawQuote(Guid quoteId, string? reason, Guid? actorId, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (Status != RfqStatus.Published)
            return SourcingErrors.QuoteImmutable;
        var quote = quotes.FirstOrDefault(candidate => candidate.Id == quoteId);
        if (quote is null)
            return SourcingErrors.NotFound("quote");
        var normalizedReason = MasterDataRules.NormalizeReason(reason);
        if (normalizedReason is null)
            return DomainError.ReasonRequired("SRC-009", MasterDataRules.ReasonMinLength, MasterDataRules.ReasonMaxLength);
        if (quote.Latest.Status != QuoteRevisionStatus.Current)
            return DomainError.InvalidTransition(quote.Latest.Status.ToString(), "Only the current revision can be withdrawn.", "SRC-009");

        quote.Latest.Withdraw(normalizedReason, actorId, nowUtc);
        Append(ActionQuoteWithdrawn, Status, Status, actorId, nowUtc, normalizedReason, correlationId, null, quote.Id);
        return null;
    }

    /// <summary>Currencies that need a rate snapshot at close: quote currencies and the requisition currency, base excluded.</summary>
    public IReadOnlyList<string> CurrenciesNeedingRates() =>
        quotes.Select(quote => quote.Latest).Where(revision => revision.Status == QuoteRevisionStatus.Current).Select(revision => revision.Currency)
            .Append(RequisitionCurrency).Where(currency => currency != BaseCurrency).Distinct().OrderBy(currency => currency, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// <c>Published → Closed</c> (SRC-012): normal after the deadline, early (before it) only with a reason; the caller
    /// checks <c>sourcing.rfqs.close-early</c>. Freezes the revisions and computes comparison 1 with the persisted rate
    /// snapshots of the close local date (SRC-013/014/015). A system close before the deadline is not due.
    /// </summary>
    public (RfqComparison? Comparison, DomainError? Error) Close(
        string? reason, IReadOnlyDictionary<Guid, bool> supplierActiveAtClose, IReadOnlyList<ExchangeRateInput> rates, DateOnly closeLocalDate,
        SourcingPolicy policy, Guid? actorId, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentNullException.ThrowIfNull(supplierActiveAtClose);
        ArgumentNullException.ThrowIfNull(rates);
        if (Status != RfqStatus.Published)
            return (null, DomainError.InvalidTransition(Status.ToString(), "Only a Published RFQ can be closed.", "SRC-012"));
        var early = nowUtc < DeadlineUtc;
        string? normalizedReason = null;
        if (early)
        {
            normalizedReason = MasterDataRules.NormalizeReason(reason);
            if (normalizedReason is null)
                return (null, DomainError.ReasonRequired("SRC-012", MasterDataRules.ReasonMinLength, MasterDataRules.ReasonMaxLength));
        }

        foreach (var rate in rates.Where(rate => rate.Currency != BaseCurrency).DistinctBy(rate => rate.Currency))
        {
            if (!CalculationRules.IsValidExchangeRate(rate.Rate))
                throw new ArgumentException("Exchange rates are positive with at most 6 decimals.", nameof(rates));
            exchangeRates.Add(new RfqExchangeRate(Guid.NewGuid(), Id, ExchangeRatePurpose.Comparison, BaseCurrency, rate, null, null, null));
        }

        var comparison = RfqComparison.Compute(Guid.NewGuid(), this, 1, supplierActiveAtClose, ComparisonRates(), closeLocalDate, nowUtc,
            policy.RequiredValidQuotes(Method, AboveThreshold));
        comparisons.Add(comparison);
        ClosedAtUtc = nowUtc;
        ClosedEarly = early;
        CloseReason = normalizedReason;
        Transition(ActionClosed, RfqStatus.Closed, actorId, nowUtc, normalizedReason, correlationId, comparison.Id,
            new RfqClosed(nowUtc, Id, nowUtc, comparison.ValidQuoteCount, comparison.Status));
        return (comparison, null);
    }

    /// <summary>
    /// SRC-013: records a <c>Manual</c> rate (with the <c>ManualExchangeRate</c> waiver) for a currency the current
    /// comparison lacks, then computes the next comparison from the same frozen revisions and close-time evidence.
    /// </summary>
    public (RfqComparison? Comparison, DomainError? Error) RecordManualRate(
        string? currency, decimal rate, DateOnly quoteDate, WaiverRequest? waiver, SourcingPolicy policy, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentNullException.ThrowIfNull(policy);
        if (Status != RfqStatus.Closed || CurrentComparison is not { } current)
            return (null, DomainError.InvalidTransition(Status.ToString(), "Manual rates are recorded on a Closed RFQ.", "SRC-013"));
        var code = CurrencyCodes.Normalize(currency);
        if (code is null || !current.MissingRateCurrencies.Contains(code))
            return (null, new DomainError(SourcingErrors.RateNotNeededCode, DomainErrorKind.BusinessRule,
                "The current comparison does not miss a rate for this currency.", "SRC-013", "currency"));
        if (!CalculationRules.IsValidExchangeRate(rate))
            return (null, SourcingErrors.Validation("rate", "The rate must be positive with at most 6 decimals (CALC-001).", "CALC-004"));
        if (quoteDate > current.CloseLocalDate)
            return (null, SourcingErrors.Validation("quoteDate", "The rate date cannot be later than the close date.", "CALC-004"));
        var (waiverRow, waiverError) = RecordWaiver(waiver, WaiverType.ManualExchangeRate, "SRC-013", null, nowUtc);
        if (waiverError is not null)
            return (null, waiverError);

        var snapshot = new RfqExchangeRate(Guid.NewGuid(), Id, ExchangeRatePurpose.Comparison, BaseCurrency,
            new ExchangeRateInput(code, rate, ExchangeRateSource.Manual, quoteDate, nowUtc, "Manual"), waiverRow!.ActorId, waiverRow.Reason, waiverRow.Id);
        exchangeRates.Add(snapshot);
        var evidence = current.Entries.ToDictionary(entry => entry.SupplierId, entry => entry.SupplierActiveAtClose);
        var comparison = RfqComparison.Compute(Guid.NewGuid(), this, current.Sequence + 1, evidence, ComparisonRates(), current.CloseLocalDate, nowUtc,
            policy.RequiredValidQuotes(Method, AboveThreshold));
        comparisons.Add(comparison);
        Append(ActionRateRecorded, Status, Status, waiverRow.ActorId, nowUtc, waiverRow.Reason, correlationId, null, snapshot.Id);
        return (comparison, null);
    }

    /// <summary>
    /// Waivers an award of <paramref name="quoteRevisionId"/> needs now (SRC-017/018), or the blocking rejection
    /// (SRC-013/016, SUP-003, SRC-010). The use case checks the dedicated permissions before calling
    /// <see cref="SubmitAward"/>.
    /// </summary>
    public (IReadOnlyList<WaiverType> Waivers, DomainError? Error) EvaluateAward(
        Guid quoteRevisionId, IReadOnlyDictionary<Guid, bool> supplierActiveNow, DateOnly today, SourcingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(supplierActiveNow);
        ArgumentNullException.ThrowIfNull(policy);
        if (Status == RfqStatus.AwardPending)
            return ([], SourcingErrors.AwardPendingExists);
        if (Status != RfqStatus.Closed || CurrentComparison is not { } comparison)
            return ([], DomainError.InvalidTransition(Status.ToString(), "Only a Closed RFQ can be awarded.", "SRC-016"));
        if (comparison.Status != ComparisonStatus.Complete)
            return ([], SourcingErrors.AwardComparisonIncomplete);
        var entry = comparison.Entries.FirstOrDefault(candidate => candidate.QuoteRevisionId == quoteRevisionId);
        if (entry is not { IsValid: true, Rank: not null })
            return ([], SourcingErrors.AwardNotEligible);
        if (!IsSupplierActive(supplierActiveNow, entry.SupplierId))
            return ([], SourcingErrors.SupplierNotEligibleFor(entry.SupplierId) with { RuleId = "SRC-016" });
        if (entry.ValidUntil < today)
            return ([], SourcingErrors.QuoteExpired);

        var needed = new List<WaiverType>();
        var required = policy.RequiredValidQuotes(Method, AboveThreshold);
        if (comparison.ValidQuoteCount < required)
            needed.Add(WaiverType.InsufficientQuotations);
        var leading = comparison.Entries
            .Where(candidate => candidate is { IsValid: true, Rank: not null } && candidate.ValidUntil >= today && IsSupplierActive(supplierActiveNow, candidate.SupplierId))
            .OrderBy(candidate => candidate.Rank)
            .First();
        if (leading.QuoteRevisionId != quoteRevisionId)
            needed.Add(WaiverType.NonLeadingSelection);
        return (needed, null);
    }

    /// <summary>
    /// <c>Closed → AwardPending</c> (SRC-016..019): records the award for the whole RFQ with its frozen amounts and the
    /// waivers <see cref="EvaluateAward"/> asked for. The use case adjusts the reservation (BUD-009) and moves the
    /// requisition to <c>PendingApproval</c> in the same commit.
    /// </summary>
    public (RfqAward? Award, DomainError? Error) SubmitAward(
        Guid awardId, Guid quoteRevisionId, IReadOnlyDictionary<Guid, bool> supplierActiveNow, DateOnly today, IReadOnlyList<WaiverRequest> waiverRequests,
        string? justification, SourcingPolicy policy, Guid? actorId, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureId(awardId, nameof(awardId));
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentNullException.ThrowIfNull(waiverRequests);
        var (needed, error) = EvaluateAward(quoteRevisionId, supplierActiveNow, today, policy);
        if (error is not null)
            return (null, error);
        var comparison = CurrentComparison!;
        var entry = comparison.Entries.Single(candidate => candidate.QuoteRevisionId == quoteRevisionId);
        var trimmedJustification = justification?.Trim();
        if (trimmedJustification is { Length: > RfqAward.JustificationMaxLength })
            return (null, SourcingErrors.Validation("justification", $"The justification has at most {RfqAward.JustificationMaxLength} characters.", "SRC-016"));

        foreach (var type in needed)
        {
            if (waiverRequests.All(request => request.Type != type))
                return (null, type == WaiverType.InsufficientQuotations
                    ? SourcingErrors.InsufficientQuotations(comparison.RequiredValidQuotes, comparison.ValidQuoteCount)
                    : SourcingErrors.NonLeadingRequiresWaiver(comparison.Entries.Where(candidate => candidate.Rank is not null)
                        .Where(candidate => candidate.ValidUntil >= today && IsSupplierActive(supplierActiveNow, candidate.SupplierId))
                        .OrderBy(candidate => candidate.Rank).First().QuoteRevisionId));
        }

        var recorded = new List<RfqWaiver>();
        foreach (var type in needed)
        {
            var (waiverRow, waiverError) = RecordWaiver(
                waiverRequests.First(request => request.Type == type), type, type == WaiverType.InsufficientQuotations ? "SRC-017" : "SRC-018", awardId, nowUtc);
            if (waiverError is not null)
            {
                waivers.RemoveAll(recorded.Contains);
                return (null, waiverError);
            }

            recorded.Add(waiverRow!);
        }

        var rates = ComparisonRates();
        var quoteRate = entry.Currency == BaseCurrency ? 1m : rates[entry.Currency].Rate;
        var requisitionRate = RequisitionCurrency == BaseCurrency ? 1m : rates[RequisitionCurrency].Rate;
        var requisitionTotal = CalculationRules.ToRequisitionCurrency(
            new Money(entry.RawTotal, entry.Currency), quoteRate, RequisitionCurrency, requisitionRate, BaseCurrency);
        var award = new RfqAward(awardId, Id, comparison.Id, entry, requisitionTotal, BaseCurrency,
            string.IsNullOrEmpty(trimmedJustification) ? null : trimmedJustification, actorId, nowUtc);
        awards.Add(award);
        Transition(ActionAwardSubmitted, RfqStatus.AwardPending, actorId, nowUtc, null, correlationId, awardId,
            new AwardSubmitted(nowUtc, awardId, Id, RequisitionId, entry.SupplierId, new Money(award.NormalizedTotal, BaseCurrency),
                requisitionTotal, $"{SourcingPolicy.PolicyType}:{PolicyVersion.ToString(NumberFormatInfo.InvariantInfo)}"));
        return (award, null);
    }

    /// <summary>
    /// APR-013 (Phase 31): <c>AwardPending → Awarded</c> with the award <c>Approved</c>, in the commit that approves the
    /// instance. Idempotent: an RFQ already awarded with this award returns <c>false</c>.
    /// </summary>
    public (bool Changed, DomainError? Error) ApproveAward(Guid awardId, DateTime nowUtc, string correlationId, string eventType)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        var award = awards.FirstOrDefault(candidate => candidate.Id == awardId);
        if (award is null)
            return (false, SourcingErrors.NotFound("award"));
        if (award.Status == AwardStatus.Approved && Status == RfqStatus.Awarded)
            return (false, null);
        if (Status != RfqStatus.AwardPending || award.Status != AwardStatus.PendingApproval)
            return (false, DomainError.InvalidTransition(Status.ToString(), "Only a pending award is approved.", "APR-013"));

        award.End(AwardStatus.Approved, nowUtc);
        var from = Status;
        Status = RfqStatus.Awarded;
        Append(ActionAwardApproved, from, Status, null, nowUtc, null, correlationId, eventType, awardId);
        Touch(nowUtc);
        return (true, null);
    }

    /// <summary>
    /// Phase 31 ends of a pending award other than approval. <paramref name="reopen"/> <c>true</c> (APR-012 invalidation,
    /// APR-015 administrative cancel) returns the RFQ to <c>Closed</c> so the buyer can award again from the frozen
    /// comparison; <c>false</c> (APR-009 rejection, APR-011 expiry) cancels the RFQ. Idempotent per award.
    /// </summary>
    public bool EndPendingAward(Guid awardId, AwardStatus outcome, bool reopen, string reasonCode, DateTime nowUtc, string correlationId, string eventType)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        if (outcome is AwardStatus.PendingApproval or AwardStatus.Approved)
            throw new ArgumentOutOfRangeException(nameof(outcome));
        var award = awards.FirstOrDefault(candidate => candidate.Id == awardId);
        if (award is null || award.Status != AwardStatus.PendingApproval || Status != RfqStatus.AwardPending)
            return false;

        award.End(outcome, nowUtc);
        if (!reopen)
        {
            CancelCore(null, reasonCode, null, nowUtc, correlationId);
            return true;
        }

        var from = Status;
        Status = RfqStatus.Closed;
        Append(ActionAwardEnded, from, Status, null, nowUtc, reasonCode, correlationId, eventType, awardId);
        Touch(nowUtc);
        return true;
    }

    /// <summary>
    /// SRC-020: buyer cancellation with reason from Draft, Published or Closed. Returns whether the requisition was in
    /// sourcing (Published/Closed), so the use case returns it to <c>Submitted</c> in the same commit.
    /// </summary>
    public (bool RequisitionWasInSourcing, DomainError? Error) Cancel(string? reason, Guid? actorId, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        var normalizedReason = MasterDataRules.NormalizeReason(reason);
        if (normalizedReason is null)
            return (false, DomainError.ReasonRequired("SRC-020", MasterDataRules.ReasonMinLength, MasterDataRules.ReasonMaxLength));
        if (Status is not (RfqStatus.Draft or RfqStatus.Published or RfqStatus.Closed))
            return (false, DomainError.InvalidTransition(Status.ToString(), "The RFQ cannot be cancelled in its current state.", "SRC-020"));

        var wasInSourcing = Status != RfqStatus.Draft;
        CancelCore(normalizedReason, "Cancelled", actorId, nowUtc, correlationId);
        return (wasInSourcing, null);
    }

    /// <summary>
    /// Cascade from the requisition (REQ-007 draft RFQ on withdrawal, REQ-008 cancellation, REQ-009 expiry): any
    /// non-terminal RFQ before an approved award is cancelled and a pending award ends <c>Cancelled</c>. Idempotent.
    /// </summary>
    public bool CancelForRequisition(string reasonCode, Guid? actorId, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        if (Status is RfqStatus.Cancelled or RfqStatus.Awarded)
            return false;
        PendingAward?.Cancel(nowUtc);
        CancelCore(null, reasonCode, actorId, nowUtc, correlationId);
        return true;
    }

    private void CancelCore(string? reason, string reasonCode, Guid? actorId, DateTime nowUtc, string correlationId)
    {
        CancelledAtUtc = nowUtc;
        CancellationReason = reason;
        CancellationReasonCode = reasonCode.Length > ReasonCodeMaxLength ? reasonCode[..ReasonCodeMaxLength] : reasonCode;
        Transition(ActionCancelled, RfqStatus.Cancelled, actorId, nowUtc, reason ?? reasonCode, correlationId, null,
            new RfqCancelled(nowUtc, Id, RequisitionId, CancellationReasonCode));
    }

    private Dictionary<string, RfqExchangeRate> ComparisonRates() =>
        exchangeRates.Where(rate => rate.Purpose == ExchangeRatePurpose.Comparison)
            .GroupBy(rate => rate.FromCurrency)
            .ToDictionary(group => group.Key, group => group.OrderBy(rate => rate.Source == ExchangeRateSource.Manual ? 1 : 0).Last(), StringComparer.Ordinal);

    private DomainError? CheckQuoteWindow(QuoteInput input, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(input);
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (Status != RfqStatus.Published)
            return Status is RfqStatus.Closed or RfqStatus.AwardPending or RfqStatus.Awarded
                ? SourcingErrors.QuoteImmutable
                : DomainError.InvalidTransition(Status.ToString(), "Quotes are recorded while the RFQ is Published.", "SRC-008");
        if (input.ReceivedAtUtc.Kind != DateTimeKind.Utc || input.ReceivedAtUtc > nowUtc)
            return SourcingErrors.Validation("receivedAt", "The received instant must be a UTC instant no later than now.");
        return input.ReceivedAtUtc > DeadlineUtc ? SourcingErrors.QuoteLate(DeadlineUtc!.Value) : null;
    }

    private (string? Currency, string? Terms, string? Notes, IReadOnlyList<(string, string, long)>? Files, DomainError? Error) ValidateQuote(
        QuoteInput input, DateOnly deadlineLocalDate)
    {
        var currency = CurrencyCodes.Normalize(input.Currency);
        if (currency is null || !input.IsSupportedCurrency)
            return Fail(SourcingErrors.Validation("currency", "The currency must be one of the organization's supported currencies."));
        if (input.Lines is not { Count: > 0 })
            return Fail(SourcingErrors.Validation("lines", "A quote offers at least one RFQ line."));
        var known = lines.ToDictionary(line => line.LineNumber);
        if (input.Lines.Select(line => line.RfqLineNumber).Distinct().Count() != input.Lines.Count)
            return Fail(SourcingErrors.Validation("lines", "Each RFQ line is offered at most once."));
        foreach (var line in input.Lines)
        {
            var field = $"lines[{line.RfqLineNumber.ToString(NumberFormatInfo.InvariantInfo)}]";
            if (!known.ContainsKey(line.RfqLineNumber))
                return Fail(SourcingErrors.Validation($"{field}.rfqLineNumber", "The line is not part of the RFQ."));
            if (line.QuantityOffered <= 0 || DecimalPrecision.Places(line.QuantityOffered) > CalculationRules.MaxQuantityDecimals || line.QuantityOffered > CalculationRules.MaxAmount)
                return Fail(SourcingErrors.Validation($"{field}.quantityOffered", "The offered quantity must be greater than zero with at most 3 decimals."));
            if (line.UnitPrice <= 0 || DecimalPrecision.Places(line.UnitPrice) > CalculationRules.MaxUnitPriceDecimals || line.UnitPrice > CalculationRules.MaxAmount
                || line.QuantityOffered * line.UnitPrice > CalculationRules.MaxAmount)
                return Fail(SourcingErrors.Validation($"{field}.unitPrice", "The unit price must be greater than zero with at most 4 decimals."));
        }

        foreach (var (name, value) in new[] { ("freight", input.Freight), ("discount", input.Discount), ("taxesIncluded", input.TaxesIncluded) })
        {
            if (value < 0 || value > CalculationRules.MaxAmount || !CalculationRules.HasMinorUnitPrecision(value, currency))
                return Fail(SourcingErrors.Validation(name, $"The {name} must be zero or positive with at most the currency minor units."));
        }

        if (input.ValidUntil < deadlineLocalDate)
            return Fail(SourcingErrors.Validation("validUntil", "The quote must be valid at least until the RFQ deadline date.", "SRC-010"));
        if (input.DeliveryDays is < 0 or > QuoteRevision.MaxDeliveryDays)
            return Fail(SourcingErrors.Validation("deliveryDays", $"The delivery estimate is 0 to {QuoteRevision.MaxDeliveryDays} days."));
        var terms = input.PaymentTerms?.Trim();
        if (string.IsNullOrEmpty(terms) || terms.Length > QuoteRevision.PaymentTermsMaxLength)
            return Fail(SourcingErrors.Validation("paymentTerms", $"The payment terms have 1 to {QuoteRevision.PaymentTermsMaxLength} characters."));
        var notes = input.Notes?.Trim();
        if (notes is { Length: > QuoteRevision.NotesMaxLength })
            return Fail(SourcingErrors.Validation("notes", $"The notes have at most {QuoteRevision.NotesMaxLength} characters."));
        if (!Enum.IsDefined(input.Source))
            return Fail(SourcingErrors.Validation("source", "The source must be Email, Phone, Document or Other."));
        var attachments = input.Attachments ?? [];
        if (attachments.Count > MaxQuoteAttachments)
            return Fail(new DomainError("file.limit-reached", DomainErrorKind.BusinessRule, $"A quote revision has at most {MaxQuoteAttachments} attachments.", "FIL-002"));
        var files = new List<(string, string, long)>();
        foreach (var attachment in attachments)
        {
            var name = SanitizeFileName(attachment.FileName);
            if (name is null)
                return Fail(SourcingErrors.Validation("attachments.fileName", "The file name must have 1 to 255 usable characters.", "FIL-002"));
            var type = attachment.MediaType?.Trim().ToLowerInvariant();
            if (type is null || !AllowedMediaTypes.Contains(type))
                return Fail(new DomainError("file.type-not-allowed", DomainErrorKind.UnsupportedMediaType, "The media type is not allowed by the file policy.", "FIL-002", "mediaType"));
            if (attachment.Length <= 0)
                return Fail(SourcingErrors.Validation("attachments.length", "The length must be greater than zero.", "FIL-002"));
            if (attachment.Length > MaxAttachmentBytes)
                return Fail(new DomainError("file.too-large", DomainErrorKind.TooLarge, $"An attachment has at most {MaxAttachmentBytes} bytes.", "FIL-002", "length"));
            files.Add((name, type, attachment.Length));
        }

        return (currency, terms, string.IsNullOrEmpty(notes) ? null : notes, files, null);

        static (string?, string?, string?, IReadOnlyList<(string, string, long)>?, DomainError?) Fail(DomainError error) => (null, null, null, null, error);
    }

    /// <summary><c>FilePolicy</c> v1 values (FIL-002); the Requisitions module holds the same policy, not referenced here (ADR-0001).</summary>
    private const long MaxAttachmentBytes = 10L * 1024 * 1024;

    private static readonly HashSet<string> AllowedMediaTypes = new(StringComparer.Ordinal)
    {
        "application/pdf",
        "image/png",
        "image/jpeg",
        "text/plain",
        "text/csv",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    };

    private static string? SanitizeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var name = value.Replace('\\', '/').Split('/').Last();
        var cleaned = new string(name.Select(character => char.IsControl(character) || "<>:\"|?*".Contains(character) ? '_' : character).ToArray())
            .Trim().Trim('.');
        return cleaned.Length is 0 or > 255 ? null : cleaned;
    }

    private (RfqWaiver? Waiver, DomainError? Error) RecordWaiver(WaiverRequest? request, WaiverType expected, string ruleId, Guid? scopeId, DateTime nowUtc)
    {
        if (request is null || request.Type != expected)
            return (null, SourcingErrors.WaiverRequired(expected, ruleId));
        MasterDataRules.EnsureId(request.ActorId, nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Permission);
        var reason = MasterDataRules.NormalizeReason(request.Reason);
        if (reason is null)
            return (null, DomainError.ReasonRequired(ruleId, MasterDataRules.ReasonMinLength, MasterDataRules.ReasonMaxLength));
        var waiver = new RfqWaiver(Guid.NewGuid(), Id, expected, ruleId, request.ActorId, request.Permission, reason, nowUtc, scopeId);
        waivers.Add(waiver);
        return (waiver, null);
    }

    private IReadOnlyList<Guid> ActiveSupplierIds() => ActiveInvitations.Select(invitation => invitation.SupplierId).OrderBy(id => id).ToArray();

    private static bool IsSupplierActive(IReadOnlyDictionary<Guid, bool> states, Guid supplierId) => states.TryGetValue(supplierId, out var active) && active;

    private void Transition(string action, RfqStatus to, Guid? actorId, DateTime nowUtc, string? reason, string correlationId, Guid? subjectId, DomainEvent domainEvent)
    {
        var from = Status;
        Status = to;
        Append(action, from, to, actorId, nowUtc, reason, correlationId, domainEvent.EventType, subjectId);
        events.Raise(domainEvent);
        Touch(nowUtc);
    }

    private void Act(string action, Guid? actorId, DateTime nowUtc, string? reason, string correlationId, Guid? subjectId, DomainEvent domainEvent)
    {
        Append(action, Status, Status, actorId, nowUtc, reason, correlationId, domainEvent.EventType, subjectId);
        events.Raise(domainEvent);
        Touch(nowUtc);
    }

    private void Append(
        string action, RfqStatus? from, RfqStatus to, Guid? actorId, DateTime nowUtc, string? reason, string correlationId, string? eventType, Guid? subjectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        history.Add(new RfqHistoryEntry(
            Guid.NewGuid(), Id, history.Count + 1, action, from, to, actorId == Guid.Empty ? null : actorId, nowUtc, reason,
            correlationId.Length > RfqHistoryEntry.CorrelationIdMaxLength ? correlationId[..RfqHistoryEntry.CorrelationIdMaxLength] : correlationId,
            eventType, subjectId));
        if (eventType is null)
            Touch(nowUtc);
    }

    private void Touch(DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        IncrementVersion();
    }
}

/// <summary><c>procurement.rfq.published</c>.</summary>
public sealed record RfqPublished(DateTime OccurredAtUtc, Guid RfqId, string Number, Guid RequisitionId, DateTime DeadlineUtc, IReadOnlyList<Guid> InvitedSupplierIds, SourcingMethod Method)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.rfq.published";
}

/// <summary><c>procurement.rfq.deadline-extended</c>.</summary>
public sealed record RfqDeadlineExtended(DateTime OccurredAtUtc, Guid RfqId, DateTime PreviousDeadlineUtc, DateTime NewDeadlineUtc, IReadOnlyList<Guid> InvitedSupplierIds)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.rfq.deadline-extended";
}

/// <summary><c>procurement.rfq.reminder-requested</c> (D29-6).</summary>
public sealed record RfqReminderRequested(DateTime OccurredAtUtc, Guid RfqId, DateTime DeadlineUtc, IReadOnlyList<Guid> SupplierIds)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.rfq.reminder-requested";
}

/// <summary><c>procurement.rfq.closed</c> (carries the comparison status).</summary>
public sealed record RfqClosed(DateTime OccurredAtUtc, Guid RfqId, DateTime ClosedAtUtc, int ValidQuoteCount, ComparisonStatus ComparisonStatus)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.rfq.closed";
}

/// <summary><c>procurement.rfq.cancelled</c>.</summary>
public sealed record RfqCancelled(DateTime OccurredAtUtc, Guid RfqId, Guid RequisitionId, string ReasonCode) : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.rfq.cancelled";
}

/// <summary><c>procurement.quote.recorded</c> (entered or revised).</summary>
public sealed record QuoteRecorded(DateTime OccurredAtUtc, Guid RfqId, Guid QuoteId, int Revision, Guid SupplierId) : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.quote.recorded";
}

/// <summary><c>procurement.award.submitted</c> (starts approval in Phase 31, APR-001).</summary>
public sealed record AwardSubmitted(
    DateTime OccurredAtUtc, Guid AwardId, Guid RfqId, Guid RequisitionId, Guid SupplierId, Money NormalizedTotal, Money RequisitionCurrencyTotal, string PolicyRef)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.award.submitted";
}
