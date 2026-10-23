#nullable enable
using System.Globalization;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Results;
using Template.Application.Features.Budgeting;
using Template.Application.Features.Catalog;
using Template.Application.Wrappers;
using Template.Domain.Sourcing;

namespace Template.Application.Features.Sourcing;

public sealed record RfqRequisitionView(
    Guid Id, string Number, int Revision, Guid RequesterId, Guid CostCenterId, Guid DepartmentId, MoneyView EstimatedTotal, DateOnly NeededBy);

public sealed record RfqLineView(
    int LineNumber, Guid? CatalogItemId, string Description, string Kind, string CategoryCode, string UnitCode, string Quantity, MoneyView EstimatedUnitPrice);

/// <summary>Invitation with the supplier identity/status snapshot taken when it was created (SRC-003).</summary>
public sealed record RfqInvitationView(
    Guid Id, Guid SupplierId, string SupplierLegalName, string SupplierCountry, string SupplierTaxId, string SupplierStatusAtInvitation,
    Guid? InvitedBy, DateTime InvitedAt, Guid? RemovedBy, DateTime? RemovedAt, bool IsActive);

public sealed record QuoteLineView(int RfqLineNumber, string QuantityOffered, MoneyView UnitPrice, MoneyView LineTotal);

public sealed record QuoteAttachmentView(Guid Id, string FileName, string MediaType, long Length, Guid? AddedBy, DateTime AddedAt);

/// <summary>Immutable revision with its commercial terms and provenance (D23-3); totals are server-computed (CALC-002).</summary>
public sealed record QuoteRevisionView(
    Guid Id,
    int RevisionNumber,
    string Status,
    string Currency,
    IReadOnlyList<QuoteLineView> Lines,
    MoneyView Subtotal,
    MoneyView Freight,
    MoneyView Discount,
    MoneyView TaxesIncluded,
    MoneyView Total,
    DateOnly ValidUntil,
    int DeliveryDays,
    string PaymentTerms,
    string? Notes,
    string Source,
    DateTime ReceivedAt,
    Guid? EnteredBy,
    DateTime EnteredAt,
    DateTime? WithdrawnAt,
    Guid? WithdrawnBy,
    string? WithdrawalReason,
    IReadOnlyList<QuoteAttachmentView> Attachments);

public sealed record QuoteView(Guid Id, Guid SupplierId, string SupplierLegalName, int LatestRevisionNumber, string LatestStatus, IReadOnlyList<QuoteRevisionView> Revisions);

/// <summary>One comparison entry: raw and normalized totals, rate, coverage, validity reasons and rank (SRC-014/015).</summary>
public sealed record ComparisonEntryView(
    Guid QuoteId,
    Guid QuoteRevisionId,
    Guid SupplierId,
    string SupplierLegalName,
    int RevisionNumber,
    MoneyView RawTotal,
    MoneyView? NormalizedTotal,
    string? Rate,
    Guid? ExchangeRateId,
    int DeliveryDays,
    DateTime ReceivedAt,
    DateOnly ValidUntil,
    int CoveredLines,
    int TotalLines,
    bool SupplierActiveAtClose,
    bool IsValid,
    IReadOnlyList<string> InvalidReasons,
    int? Rank);

/// <summary>Immutable comparison; the browser displays it and never recomputes ranks or conversions.</summary>
public sealed record ComparisonView(
    Guid Id,
    int Sequence,
    string Status,
    string BaseCurrency,
    DateOnly CloseLocalDate,
    DateTime ComputedAt,
    int ValidQuoteCount,
    int RequiredValidQuotes,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> MissingRateCurrencies,
    Guid? RecommendedQuoteRevisionId,
    IReadOnlyList<ComparisonEntryView> Entries);

/// <summary>Award; commercial fields are <c>null</c> for actors without <c>sourcing.quotes.read</c> (SRC-021).</summary>
public sealed record RfqAwardView(
    Guid Id,
    string Status,
    Guid SupplierId,
    string SupplierLegalName,
    Guid? QuoteRevisionId,
    int? Rank,
    MoneyView? AwardedTotal,
    MoneyView? NormalizedTotal,
    MoneyView? RequisitionCurrencyTotal,
    string? Justification,
    Guid? SubmittedBy,
    DateTime SubmittedAt);

public sealed record RfqWaiverView(Guid Id, string Type, string RuleId, Guid ActorId, string Permission, string Reason, DateTime DecidedAt, Guid? ScopeId);

/// <summary>Persisted rate provenance (CALC-004): base units per one <see cref="FromCurrency"/> unit.</summary>
public sealed record ExchangeRateView(
    Guid Id, string Purpose, string FromCurrency, string ToCurrency, string Rate, string Source, string Provider, DateOnly QuoteDate,
    DateTime RetrievedAt, Guid? EnteredBy, string? Reason, Guid? WaiverId);

public sealed record PolicyReferenceView(string Type, int Version);

/// <summary>
/// RFQ representation (immutable DTO snapshot). <see cref="Quotes"/> and <see cref="Comparison"/> are present only
/// when <see cref="CommercialDetailsVisible"/> (SRC-021); <see cref="AllowedActions"/> are server-computed for the
/// current actor and state; <see cref="Version"/> equals the strong ETag value.
/// </summary>
public sealed record RfqView(
    Guid Id,
    string Number,
    RfqRequisitionView Requisition,
    int RoundNumber,
    string Method,
    PolicyReferenceView Policy,
    string BaseCurrency,
    MoneyView? EstimateInBaseCurrency,
    bool AboveThreshold,
    int RequiredInvitations,
    string? RequiredPublishWaiver,
    DateTime? Deadline,
    string Status,
    DateTime? PublishedAt,
    DateTime? ClosedAt,
    bool ClosedEarly,
    string? CloseReason,
    DateTime? CancelledAt,
    string? CancellationReason,
    string? CancellationReasonCode,
    IReadOnlyList<RfqLineView> Lines,
    IReadOnlyList<RfqInvitationView> Invitations,
    bool CommercialDetailsVisible,
    IReadOnlyList<QuoteView>? Quotes,
    ComparisonView? Comparison,
    int ComparisonCount,
    IReadOnlyList<RfqAwardView> Awards,
    IReadOnlyList<RfqWaiverView> Waivers,
    IReadOnlyList<ExchangeRateView> ExchangeRates,
    IReadOnlyList<string> AllowedActions,
    DateTime CreatedAt,
    Guid? CreatedBy,
    DateTime UpdatedAt,
    string Version)
{
    public static RfqView From(RequestForQuotation rfq, bool commercial, IReadOnlyList<string> allowedActions)
    {
        var names = rfq.Invitations.GroupBy(invitation => invitation.SupplierId)
            .ToDictionary(group => group.Key, group => group.OrderBy(invitation => invitation.Sequence).Last().SupplierLegalName);
        var comparison = rfq.CurrentComparison;
        return new RfqView(
            rfq.Id,
            rfq.Number,
            new RfqRequisitionView(rfq.RequisitionId, rfq.RequisitionNumber, rfq.RequisitionRevision, rfq.RequesterId, rfq.CostCenterId, rfq.DepartmentId,
                BudgetMoney.Of(rfq.RequisitionEstimatedTotal, rfq.RequisitionCurrency), rfq.NeededBy),
            rfq.RoundNumber,
            rfq.Method.ToString(),
            new PolicyReferenceView(SourcingPolicy.PolicyType, rfq.PolicyVersion),
            rfq.BaseCurrency,
            rfq.EstimateInBaseCurrency is { } estimate ? BudgetMoney.Of(estimate, rfq.BaseCurrency) : null,
            rfq.AboveThreshold,
            rfq.Method == SourcingMethod.Direct ? 1 : SourcingPolicy.V1.RequiredInvitations(rfq.AboveThreshold),
            rfq.RequiredPublishWaiver?.ToString(),
            rfq.DeadlineUtc,
            rfq.Status.ToString(),
            rfq.PublishedAtUtc,
            rfq.ClosedAtUtc,
            rfq.ClosedEarly,
            rfq.CloseReason,
            rfq.CancelledAtUtc,
            rfq.CancellationReason,
            rfq.CancellationReasonCode,
            rfq.Lines.OrderBy(line => line.LineNumber)
                .Select(line => new RfqLineView(line.LineNumber, line.CatalogItemId, line.Description, line.Kind, line.CategoryCode, line.UnitCode,
                    SourcingFormat.Quantity(line.Quantity), SourcingFormat.Price(line.EstimatedUnitPrice, rfq.RequisitionCurrency)))
                .ToArray(),
            rfq.Invitations.OrderBy(invitation => invitation.Sequence)
                .Select(invitation => new RfqInvitationView(invitation.Id, invitation.SupplierId, invitation.SupplierLegalName, invitation.SupplierCountry,
                    invitation.SupplierTaxId, invitation.SupplierStatusAtInvitation, invitation.InvitedBy, invitation.InvitedAtUtc, invitation.RemovedBy,
                    invitation.RemovedAtUtc, invitation.IsActive))
                .ToArray(),
            commercial,
            commercial ? rfq.Quotes.OrderBy(quote => names.GetValueOrDefault(quote.SupplierId), StringComparer.Ordinal).ThenBy(quote => quote.SupplierId)
                .Select(quote => Quote(quote, names.GetValueOrDefault(quote.SupplierId) ?? string.Empty)).ToArray() : null,
            commercial && comparison is not null ? ComparisonOf(comparison) : null,
            rfq.Comparisons.Count,
            rfq.Awards.OrderBy(award => award.SubmittedAtUtc).Select(award => Award(award, commercial)).ToArray(),
            rfq.Waivers.OrderBy(waiver => waiver.DecidedAtUtc)
                .Select(waiver => new RfqWaiverView(waiver.Id, waiver.Type.ToString(), waiver.RuleId, waiver.ActorId, waiver.Permission, waiver.Reason,
                    waiver.DecidedAtUtc, waiver.ScopeId))
                .ToArray(),
            rfq.ExchangeRates.OrderBy(rate => rate.RetrievedAtUtc).ThenBy(rate => rate.FromCurrency, StringComparer.Ordinal)
                .Select(rate => new ExchangeRateView(rate.Id, rate.Purpose.ToString(), rate.FromCurrency, rate.ToCurrency, SourcingFormat.Rate(rate.Rate),
                    rate.Source.ToString(), rate.Provider, rate.QuoteDate, rate.RetrievedAtUtc, rate.EnteredBy, rate.Reason, rate.WaiverId))
                .ToArray(),
            allowedActions,
            rfq.CreatedAtUtc,
            rfq.CreatedBy,
            rfq.UpdatedAtUtc,
            VersionPrecondition.Format(rfq.Version));
    }

    private static QuoteView Quote(SupplierQuote quote, string legalName) => new(
        quote.Id, quote.SupplierId, legalName, quote.Latest.RevisionNumber, quote.Latest.Status.ToString(),
        quote.Revisions.OrderBy(revision => revision.RevisionNumber).Select(revision => new QuoteRevisionView(
            revision.Id, revision.RevisionNumber, revision.Status.ToString(), revision.Currency,
            revision.Lines.OrderBy(line => line.RfqLineNumber)
                .Select(line => new QuoteLineView(line.RfqLineNumber, SourcingFormat.Quantity(line.QuantityOffered),
                    SourcingFormat.Price(line.UnitPrice, revision.Currency), BudgetMoney.Of(line.LineTotal, revision.Currency)))
                .ToArray(),
            BudgetMoney.Of(revision.Subtotal, revision.Currency), BudgetMoney.Of(revision.Freight, revision.Currency),
            BudgetMoney.Of(revision.Discount, revision.Currency), BudgetMoney.Of(revision.TaxesIncluded, revision.Currency),
            BudgetMoney.Of(revision.Total, revision.Currency), revision.ValidUntil, revision.DeliveryDays, revision.PaymentTerms, revision.Notes,
            revision.Source.ToString(), revision.ReceivedAtUtc, revision.EnteredBy, revision.EnteredAtUtc, revision.WithdrawnAtUtc, revision.WithdrawnBy,
            revision.WithdrawalReason,
            revision.Attachments.OrderBy(attachment => attachment.AddedAtUtc)
                .Select(attachment => new QuoteAttachmentView(attachment.Id, attachment.FileName, attachment.MediaType, attachment.Length,
                    attachment.AddedBy, attachment.AddedAtUtc))
                .ToArray()))
            .ToArray());

    internal static ComparisonView ComparisonOf(RfqComparison comparison) => new(
        comparison.Id, comparison.Sequence, comparison.Status.ToString(), comparison.BaseCurrency, comparison.CloseLocalDate, comparison.ComputedAtUtc,
        comparison.ValidQuoteCount, comparison.RequiredValidQuotes, comparison.WarningList, comparison.MissingRateCurrencies,
        comparison.RecommendedQuoteRevisionId,
        comparison.Entries.OrderBy(entry => entry.Rank ?? int.MaxValue).ThenBy(entry => entry.SupplierLegalName, StringComparer.Ordinal)
            .ThenBy(entry => entry.SupplierId)
            .Select(entry => new ComparisonEntryView(
                entry.QuoteId, entry.QuoteRevisionId, entry.SupplierId, entry.SupplierLegalName, entry.RevisionNumber,
                BudgetMoney.Of(entry.RawTotal, entry.Currency),
                entry.NormalizedTotal is { } normalized ? BudgetMoney.Of(normalized, comparison.BaseCurrency) : null,
                entry.Rate is { } rate ? SourcingFormat.Rate(rate) : null, entry.ExchangeRateId, entry.DeliveryDays, entry.ReceivedAtUtc, entry.ValidUntil,
                entry.CoveredLines, entry.TotalLines, entry.SupplierActiveAtClose, entry.IsValid, entry.InvalidReasonList, entry.Rank))
            .ToArray());

    private static RfqAwardView Award(RfqAward award, bool commercial) => new(
        award.Id, award.Status.ToString(), award.SupplierId, award.SupplierLegalName,
        commercial ? award.QuoteRevisionId : null,
        commercial ? award.Rank : null,
        commercial ? BudgetMoney.Of(award.AwardedTotal, award.Currency) : null,
        commercial ? BudgetMoney.Of(award.NormalizedTotal, award.BaseCurrency) : null,
        commercial ? BudgetMoney.Of(award.RequisitionCurrencyTotal, award.RequisitionCurrency) : null,
        commercial ? award.Justification : null,
        award.SubmittedBy,
        award.SubmittedAtUtc);
}

/// <summary>List item without lines, quotes or comparison.</summary>
public sealed record RfqSummaryView(
    Guid Id,
    string Number,
    Guid RequisitionId,
    string RequisitionNumber,
    int RoundNumber,
    string Method,
    string Status,
    DateTime? Deadline,
    int InvitationCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string Version)
{
    public static RfqSummaryView From(RequestForQuotation rfq) => new(
        rfq.Id, rfq.Number, rfq.RequisitionId, rfq.RequisitionNumber, rfq.RoundNumber, rfq.Method.ToString(), rfq.Status.ToString(), rfq.DeadlineUtc,
        rfq.Invitations.Count(invitation => invitation.IsActive), rfq.CreatedAtUtc, rfq.UpdatedAtUtc, VersionPrecondition.Format(rfq.Version));
}

public sealed record RfqHistoryEntryView(
    int Sequence, string Action, string? From, string To, Guid? ActorId, DateTime At, string? Reason, string? EventType, Guid? SubjectId, string CorrelationId);

public sealed record RfqHistoryView(Guid RfqId, IReadOnlyList<RfqHistoryEntryView> Entries)
{
    public static RfqHistoryView From(RequestForQuotation rfq) => new(
        rfq.Id,
        rfq.History.OrderBy(entry => entry.Sequence)
            .Select(entry => new RfqHistoryEntryView(entry.Sequence, entry.Action, entry.From?.ToString(), entry.To.ToString(), entry.ActorId, entry.AtUtc,
                entry.Reason, entry.EventType, entry.SubjectId, entry.CorrelationId))
            .ToArray());
}

internal static class SourcingFormat
{
    public static string Quantity(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    public static MoneyView Price(decimal value, string currency) => new(value.ToString("0.00##", CultureInfo.InvariantCulture), currency);

    public static string Rate(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}

/// <summary>Audit actions, allowed actions and failures of the Sourcing module (SRC-*).</summary>
public static class SourcingAudit
{
    public const string Resource = "request-for-quotation";
    public const string WaiverResource = "waiver";

    public const string Created = "rfq.created";
    public const string SupplierInvited = "rfq.supplier-invited";
    public const string SupplierRemoved = "rfq.supplier-removed";
    public const string Published = "rfq.published";
    public const string DeadlineExtended = "rfq.deadline-extended";
    public const string ReminderRequested = "rfq.reminder-requested";
    public const string Closed = "rfq.closed";
    public const string Cancelled = "rfq.cancelled";
    public const string RateRecorded = "rfq.exchange-rate.recorded";
    public const string QuoteRecorded = "quote.recorded";
    public const string QuoteRevised = "quote.revised";
    public const string QuoteWithdrawn = "quote.withdrawn";
    public const string AwardSubmitted = "award.submitted";
    public const string WaiverRecorded = "waiver.recorded";
    public const string RequisitionSourcingStarted = "requisition.sourcing-started";
    public const string RequisitionSourcingReturned = "requisition.sourcing-returned";
    public const string ReservationAdjusted = "budget.reservation.adjusted";

    public const string ActionInvite = "invite";
    public const string ActionRemoveInvitation = "remove-invitation";
    public const string ActionPublish = "publish";
    public const string ActionExtendDeadline = "extend-deadline";
    public const string ActionRemind = "remind";
    public const string ActionEnterQuote = "enter-quote";
    public const string ActionReviseQuote = "revise-quote";
    public const string ActionWithdrawQuote = "withdraw-quote";
    public const string ActionClose = "close";
    public const string ActionCloseEarly = "close-early";
    public const string ActionCancel = "cancel";
    public const string ActionRecordRate = "record-rate";
    public const string ActionAward = "award";

    public static ApplicationError NotFound() => new(ProblemCodes.NotFound, ErrorCategory.NotFound, "The request for quotation was not found.");

    public static ApplicationError WaiverPermissionRequired(WaiverType type, string permission) => new(
        SourcingErrors.WaiverPermissionRequiredCode, ErrorCategory.Forbidden, $"The {type} waiver requires the {permission} permission.",
        type switch
        {
            WaiverType.InsufficientQuotations => "SRC-017",
            WaiverType.NonLeadingSelection => "SRC-018",
            WaiverType.ManualExchangeRate => "SRC-013",
            _ => "SRC-006"
        },
        new Dictionary<string, object?> { ["waiverType"] = type.ToString(), ["permission"] = permission });

    internal static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    internal static string Amount(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
