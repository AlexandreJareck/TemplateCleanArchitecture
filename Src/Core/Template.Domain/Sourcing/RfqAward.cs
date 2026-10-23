using System;
using Template.Domain.Common;

namespace Template.Domain.Sourcing;

/// <summary>
/// Human selection of one quote revision for the whole RFQ (D23-15, SRC-016), submitted for approval (B23-1). Amounts
/// are frozen from the comparison: quote currency, base currency (CALC-004) and requisition currency (CALC-007).
/// </summary>
public sealed class RfqAward
{
    public const int JustificationMaxLength = 1000;

    private RfqAward()
    {
        SupplierLegalName = string.Empty;
        Currency = string.Empty;
        BaseCurrency = string.Empty;
        RequisitionCurrency = string.Empty;
    }

    internal RfqAward(
        Guid id, Guid rfqId, Guid comparisonId, ComparisonEntry entry, Money requisitionCurrencyTotal, string baseCurrency, string? justification,
        Guid? submittedBy, DateTime submittedAtUtc)
        : this()
    {
        Id = id;
        RfqId = rfqId;
        ComparisonId = comparisonId;
        QuoteId = entry.QuoteId;
        QuoteRevisionId = entry.QuoteRevisionId;
        SupplierId = entry.SupplierId;
        SupplierLegalName = entry.SupplierLegalName;
        Rank = entry.Rank ?? throw new ArgumentException("Only ranked entries are awarded.", nameof(entry));
        AwardedTotal = entry.RawTotal;
        Currency = entry.Currency;
        NormalizedTotal = entry.NormalizedTotal ?? throw new ArgumentException("Only normalized entries are awarded.", nameof(entry));
        BaseCurrency = baseCurrency;
        RequisitionCurrencyTotal = requisitionCurrencyTotal.Amount;
        RequisitionCurrency = requisitionCurrencyTotal.Currency;
        Justification = justification;
        Status = AwardStatus.PendingApproval;
        SubmittedBy = submittedBy;
        SubmittedAtUtc = submittedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid RfqId { get; private set; }
    public Guid ComparisonId { get; private set; }
    public Guid QuoteId { get; private set; }
    public Guid QuoteRevisionId { get; private set; }
    public Guid SupplierId { get; private set; }
    public string SupplierLegalName { get; private set; }
    public int Rank { get; private set; }
    public decimal AwardedTotal { get; private set; }
    public string Currency { get; private set; }
    public decimal NormalizedTotal { get; private set; }
    public string BaseCurrency { get; private set; }
    public decimal RequisitionCurrencyTotal { get; private set; }
    public string RequisitionCurrency { get; private set; }
    public string? Justification { get; private set; }
    public AwardStatus Status { get; private set; }
    public Guid? SubmittedBy { get; private set; }
    public DateTime SubmittedAtUtc { get; private set; }
    public DateTime? EndedAtUtc { get; private set; }

    internal void Cancel(DateTime nowUtc)
    {
        Status = AwardStatus.Cancelled;
        EndedAtUtc = nowUtc;
    }
}
