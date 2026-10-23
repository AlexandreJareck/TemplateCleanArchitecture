using System;
using System.Collections.Generic;
using System.Linq;
using Template.Domain.Common;

namespace Template.Domain.Sourcing;

/// <summary>Why a quote revision is not valid for award (SRC-014). Missing lines are never treated as zero.</summary>
public static class InvalidQuoteReasons
{
    public const string Withdrawn = "withdrawn";
    public const string Late = "late";
    public const string Expired = "expired";
    public const string SupplierNotActive = "supplier-not-active";
    public const string MissingLines = "missing-lines";
    public const string InsufficientQuantity = "insufficient-quantity";
}

/// <summary>Comparison warnings; currency and supplier IDs only, never commercial or personal text.</summary>
public static class ComparisonWarnings
{
    public const string RateUnavailable = "rate-unavailable";
    public const string InsufficientValidQuotes = "insufficient-valid-quotes";
    public const string NoValidQuotes = "no-valid-quotes";
    public const string SupplierWithoutQuote = "supplier-without-quote";
}

/// <summary>
/// Immutable comparison of one RFQ round (domain-model.md §3): base currency, the rate snapshots it used, one entry per
/// quote with raw and normalized totals, coverage, validity reasons and rank, plus warnings. A manual rate recorded
/// later (SRC-013) creates the next comparison <see cref="Sequence"/>; earlier comparisons are kept unchanged.
/// </summary>
public sealed class RfqComparison
{
    public const int WarningsMaxLength = 2000;

    private readonly List<ComparisonEntry> entries = [];

    private RfqComparison()
    {
        BaseCurrency = string.Empty;
        Warnings = string.Empty;
    }

    internal RfqComparison(Guid id, Guid rfqId, int sequence, string baseCurrency, DateOnly closeLocalDate, DateTime computedAtUtc, int requiredValidQuotes)
        : this()
    {
        Id = id;
        RfqId = rfqId;
        Sequence = sequence;
        BaseCurrency = baseCurrency;
        CloseLocalDate = closeLocalDate;
        ComputedAtUtc = computedAtUtc;
        RequiredValidQuotes = requiredValidQuotes;
    }

    public Guid Id { get; private set; }
    public Guid RfqId { get; private set; }
    public int Sequence { get; private set; }
    public ComparisonStatus Status { get; private set; }
    public string BaseCurrency { get; private set; }
    public DateOnly CloseLocalDate { get; private set; }
    public DateTime ComputedAtUtc { get; private set; }
    public int ValidQuoteCount { get; private set; }
    public int RequiredValidQuotes { get; private set; }

    /// <summary>Comma-separated warning codes such as <c>rate-unavailable:USD</c>.</summary>
    public string Warnings { get; private set; }

    /// <summary>Advisory recommendation: the rank 1 revision of a complete comparison (CALC-005); selection is human.</summary>
    public Guid? RecommendedQuoteRevisionId { get; private set; }

    public IReadOnlyCollection<ComparisonEntry> Entries => entries;

    public IReadOnlyList<string> WarningList => Warnings.Length == 0 ? [] : Warnings.Split(',');

    /// <summary>Currencies whose missing rate keeps this comparison incomplete.</summary>
    public IReadOnlyList<string> MissingRateCurrencies => WarningList
        .Where(warning => warning.StartsWith(ComparisonWarnings.RateUnavailable + ":", StringComparison.Ordinal))
        .Select(warning => warning[(ComparisonWarnings.RateUnavailable.Length + 1)..])
        .ToArray();

    /// <summary>
    /// SRC-013/014/015 and CALC-004/005: evaluates every quote's latest revision, normalizes valid totals with the rate
    /// snapshots, ranks valid normalized entries and records warnings. Pure and deterministic: the same inputs in any
    /// order give the same entries, ranks and warnings.
    /// </summary>
    internal static RfqComparison Compute(
        Guid id,
        RequestForQuotation rfq,
        int sequence,
        IReadOnlyDictionary<Guid, bool> supplierActiveAtClose,
        IReadOnlyDictionary<string, RfqExchangeRate> rates,
        DateOnly closeLocalDate,
        DateTime nowUtc,
        int requiredValidQuotes)
    {
        var comparison = new RfqComparison(id, rfq.Id, sequence, rfq.BaseCurrency, closeLocalDate, nowUtc, requiredValidQuotes);
        var warnings = new SortedSet<string>(StringComparer.Ordinal);
        var lines = rfq.Lines.ToDictionary(line => line.LineNumber);
        var invitations = rfq.Invitations.Where(invitation => invitation.IsActive).ToDictionary(invitation => invitation.SupplierId);

        foreach (var quote in rfq.Quotes.OrderBy(quote => quote.SupplierId))
        {
            var revision = quote.Latest;
            var reasons = new List<string>();
            if (revision.Status == QuoteRevisionStatus.Withdrawn)
                reasons.Add(InvalidQuoteReasons.Withdrawn);
            if (rfq.DeadlineUtc is { } deadline && revision.ReceivedAtUtc > deadline)
                reasons.Add(InvalidQuoteReasons.Late);
            if (revision.ValidUntil < closeLocalDate)
                reasons.Add(InvalidQuoteReasons.Expired);
            var supplierActive = supplierActiveAtClose.TryGetValue(quote.SupplierId, out var active) && active;
            if (!supplierActive)
                reasons.Add(InvalidQuoteReasons.SupplierNotActive);
            var offers = revision.Lines.ToDictionary(line => line.RfqLineNumber);
            var covered = lines.Keys.Count(offers.ContainsKey);
            if (covered < lines.Count)
                reasons.Add(InvalidQuoteReasons.MissingLines);
            if (offers.Any(offer => lines.TryGetValue(offer.Key, out var line) && offer.Value.QuantityOffered < line.Quantity))
                reasons.Add(InvalidQuoteReasons.InsufficientQuantity);

            var isValid = reasons.Count == 0;
            RfqExchangeRate? rate = null;
            decimal? normalized = null;
            if (string.Equals(revision.Currency, rfq.BaseCurrency, StringComparison.Ordinal))
                normalized = revision.Total;
            else if (rates.TryGetValue(revision.Currency, out rate))
                normalized = CalculationRules.Normalize(revision.TotalMoney, rate.Rate, rfq.BaseCurrency).Amount;
            else if (isValid)
                warnings.Add($"{ComparisonWarnings.RateUnavailable}:{revision.Currency}");

            comparison.entries.Add(new ComparisonEntry(
                Guid.NewGuid(), comparison.Id, quote.Id, revision.Id, quote.SupplierId,
                invitations.TryGetValue(quote.SupplierId, out var invitation) ? invitation.SupplierLegalName : string.Empty,
                revision.RevisionNumber, revision.Currency, revision.Total, normalized, rate?.Id, rate?.Rate, revision.DeliveryDays,
                revision.ReceivedAtUtc, revision.ValidUntil, covered, lines.Count, supplierActive, isValid, string.Join(',', reasons)));
        }

        foreach (var supplierId in invitations.Keys.Where(supplierId => rfq.Quotes.All(quote => quote.SupplierId != supplierId)).OrderBy(id => id))
            warnings.Add($"{ComparisonWarnings.SupplierWithoutQuote}:{supplierId}");
        if (!string.Equals(rfq.RequisitionCurrency, rfq.BaseCurrency, StringComparison.Ordinal) && !rates.ContainsKey(rfq.RequisitionCurrency))
            warnings.Add($"{ComparisonWarnings.RateUnavailable}:{rfq.RequisitionCurrency}");

        // CALC-005: ascending normalized total, then fewer delivery days, earlier receipt, supplier legal name (ordinal)
        // and, as the last deterministic key, the supplier ID (D29-5).
        var ranked = comparison.entries
            .Where(entry => entry.IsValid && entry.NormalizedTotal is not null)
            .OrderBy(entry => entry.NormalizedTotal)
            .ThenBy(entry => entry.DeliveryDays)
            .ThenBy(entry => entry.ReceivedAtUtc)
            .ThenBy(entry => entry.SupplierLegalName, StringComparer.Ordinal)
            .ThenBy(entry => entry.SupplierId)
            .ToArray();
        for (var index = 0; index < ranked.Length; index++)
            ranked[index].AssignRank(index + 1);

        comparison.ValidQuoteCount = comparison.entries.Count(entry => entry.IsValid);
        if (comparison.ValidQuoteCount == 0)
            warnings.Add(ComparisonWarnings.NoValidQuotes);
        else if (comparison.ValidQuoteCount < requiredValidQuotes)
            warnings.Add(ComparisonWarnings.InsufficientValidQuotes);
        comparison.Status = warnings.Any(warning => warning.StartsWith(ComparisonWarnings.RateUnavailable, StringComparison.Ordinal))
            ? ComparisonStatus.Incomplete
            : ComparisonStatus.Complete;
        comparison.Warnings = string.Join(',', warnings);
        if (comparison.Warnings.Length > WarningsMaxLength)
            comparison.Warnings = comparison.Warnings[..WarningsMaxLength];
        comparison.RecommendedQuoteRevisionId = comparison.Status == ComparisonStatus.Complete ? ranked.FirstOrDefault()?.QuoteRevisionId : null;
        return comparison;
    }
}

/// <summary>One quote revision in a comparison: raw and normalized totals, rate used, coverage, validity and rank.</summary>
public sealed class ComparisonEntry
{
    public const int InvalidReasonsMaxLength = 200;

    private ComparisonEntry()
    {
        SupplierLegalName = string.Empty;
        Currency = string.Empty;
        InvalidReasons = string.Empty;
    }

    internal ComparisonEntry(
        Guid id, Guid comparisonId, Guid quoteId, Guid quoteRevisionId, Guid supplierId, string supplierLegalName, int revisionNumber,
        string currency, decimal rawTotal, decimal? normalizedTotal, Guid? exchangeRateId, decimal? rate, int deliveryDays,
        DateTime receivedAtUtc, DateOnly validUntil, int coveredLines, int totalLines, bool supplierActiveAtClose, bool isValid,
        string invalidReasons)
        : this()
    {
        Id = id;
        ComparisonId = comparisonId;
        QuoteId = quoteId;
        QuoteRevisionId = quoteRevisionId;
        SupplierId = supplierId;
        SupplierLegalName = supplierLegalName;
        RevisionNumber = revisionNumber;
        Currency = currency;
        RawTotal = rawTotal;
        NormalizedTotal = normalizedTotal;
        ExchangeRateId = exchangeRateId;
        Rate = rate;
        DeliveryDays = deliveryDays;
        ReceivedAtUtc = receivedAtUtc;
        ValidUntil = validUntil;
        CoveredLines = coveredLines;
        TotalLines = totalLines;
        SupplierActiveAtClose = supplierActiveAtClose;
        IsValid = isValid;
        InvalidReasons = invalidReasons;
    }

    public Guid Id { get; private set; }
    public Guid ComparisonId { get; private set; }
    public Guid QuoteId { get; private set; }
    public Guid QuoteRevisionId { get; private set; }
    public Guid SupplierId { get; private set; }
    public string SupplierLegalName { get; private set; }
    public int RevisionNumber { get; private set; }
    public string Currency { get; private set; }
    public decimal RawTotal { get; private set; }

    /// <summary>Base-currency total (CALC-004), or <c>null</c> when the rate is missing.</summary>
    public decimal? NormalizedTotal { get; private set; }

    public Guid? ExchangeRateId { get; private set; }
    public decimal? Rate { get; private set; }
    public int DeliveryDays { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }
    public DateOnly ValidUntil { get; private set; }
    public int CoveredLines { get; private set; }
    public int TotalLines { get; private set; }

    /// <summary>Supplier status evidence at close, reused unchanged when a manual rate recomputes the comparison.</summary>
    public bool SupplierActiveAtClose { get; private set; }

    public bool IsValid { get; private set; }

    /// <summary>Comma-separated <see cref="InvalidQuoteReasons"/> codes.</summary>
    public string InvalidReasons { get; private set; }

    public int? Rank { get; private set; }

    public IReadOnlyList<string> InvalidReasonList => InvalidReasons.Length == 0 ? [] : InvalidReasons.Split(',');

    internal void AssignRank(int rank) => Rank = rank;
}
