using System;
using System.Collections.Generic;
using System.Linq;
using Template.Domain.Common;

namespace Template.Domain.Sourcing;

/// <summary>Requisition line as frozen in the submission snapshot the RFQ was created from (SRC-002).</summary>
public sealed record RfqLineSnapshot(
    int LineNumber,
    Guid? CatalogItemId,
    string Description,
    string Kind,
    Guid CategoryId,
    string CategoryCode,
    Guid UnitOfMeasureId,
    string UnitCode,
    decimal Quantity,
    decimal EstimatedUnitPrice,
    decimal LineTotal);

/// <summary>
/// What an RFQ copies from the requisition's latest submission (domain-model.md §4): identity, revision, scope for
/// visibility, currency, estimated total, needed-by and lines. Requisitions are referenced by ID only (ADR-0001).
/// </summary>
public sealed record RequisitionSourcingSnapshot(
    Guid RequisitionId,
    string Number,
    int Revision,
    Guid RequesterId,
    Guid CostCenterId,
    Guid DepartmentId,
    string Currency,
    decimal EstimatedTotal,
    DateOnly NeededBy,
    IReadOnlyList<RfqLineSnapshot> Lines);

/// <summary>Supplier identity and status evidence at the instant of an invitation (SRC-003, SUP-004).</summary>
public sealed record SupplierSnapshot(Guid SupplierId, string LegalName, string Country, string TaxId, string Status, bool IsActive);

/// <summary>Resolved exchange rate (base units per foreign unit) offered by the use case for a snapshot (CALC-004).</summary>
public sealed record ExchangeRateInput(
    string Currency,
    decimal Rate,
    ExchangeRateSource Source,
    DateOnly QuoteDate,
    DateTime RetrievedAtUtc,
    string Provider);

/// <summary>One offered line; totals are server-computed (CALC-002).</summary>
public sealed record QuoteLineInput(int RfqLineNumber, decimal QuantityOffered, decimal UnitPrice);

public sealed record QuoteAttachmentInput(string? FileName, string? MediaType, long Length);

/// <summary>Buyer-entered quote content (D23-3); taxes are an explicit quoted amount only (no tax engine).</summary>
public sealed record QuoteInput(
    string? Currency,
    bool IsSupportedCurrency,
    IReadOnlyList<QuoteLineInput> Lines,
    decimal Freight,
    decimal Discount,
    decimal TaxesIncluded,
    DateOnly ValidUntil,
    int DeliveryDays,
    string? PaymentTerms,
    string? Notes,
    QuoteSource Source,
    DateTime ReceivedAtUtc,
    IReadOnlyList<QuoteAttachmentInput> Attachments);

/// <summary>RFQ line copied from the requisition snapshot; immutable (SRC-002).</summary>
public sealed class RfqLine
{
    private RfqLine()
    {
        Description = string.Empty;
        Kind = string.Empty;
        CategoryCode = string.Empty;
        UnitCode = string.Empty;
    }

    internal RfqLine(Guid id, Guid rfqId, RfqLineSnapshot line)
        : this()
    {
        Id = id;
        RfqId = rfqId;
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
        EstimatedLineTotal = line.LineTotal;
    }

    public Guid Id { get; private set; }
    public Guid RfqId { get; private set; }
    public int LineNumber { get; private set; }
    public Guid? CatalogItemId { get; private set; }
    public string Description { get; private set; }
    public string Kind { get; private set; }
    public Guid CategoryId { get; private set; }
    public string CategoryCode { get; private set; }
    public Guid UnitOfMeasureId { get; private set; }
    public string UnitCode { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal EstimatedUnitPrice { get; private set; }
    public decimal EstimatedLineTotal { get; private set; }
}

/// <summary>
/// Invitation with the supplier identity/status snapshot of that instant (SRC-003). Removal (Draft only, SRC-007)
/// keeps the row with its removal instant; a later re-invitation is a new row.
/// </summary>
public sealed class RfqInvitation
{
    public const int LegalNameMaxLength = 200;
    public const int TaxIdMaxLength = 20;

    private RfqInvitation()
    {
        SupplierLegalName = string.Empty;
        SupplierCountry = string.Empty;
        SupplierTaxId = string.Empty;
        SupplierStatusAtInvitation = string.Empty;
    }

    internal RfqInvitation(Guid id, Guid rfqId, int sequence, SupplierSnapshot supplier, Guid? invitedBy, DateTime invitedAtUtc)
        : this()
    {
        Id = id;
        RfqId = rfqId;
        Sequence = sequence;
        SupplierId = supplier.SupplierId;
        SupplierLegalName = supplier.LegalName;
        SupplierCountry = supplier.Country;
        SupplierTaxId = supplier.TaxId;
        SupplierStatusAtInvitation = supplier.Status;
        InvitedBy = invitedBy;
        InvitedAtUtc = invitedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid RfqId { get; private set; }
    public int Sequence { get; private set; }
    public Guid SupplierId { get; private set; }
    public string SupplierLegalName { get; private set; }
    public string SupplierCountry { get; private set; }
    public string SupplierTaxId { get; private set; }
    public string SupplierStatusAtInvitation { get; private set; }
    public Guid? InvitedBy { get; private set; }
    public DateTime InvitedAtUtc { get; private set; }
    public Guid? RemovedBy { get; private set; }
    public DateTime? RemovedAtUtc { get; private set; }

    public bool IsActive => RemovedAtUtc is null;

    internal void Remove(Guid? actorId, DateTime nowUtc)
    {
        RemovedBy = actorId;
        RemovedAtUtc = nowUtc;
    }
}

/// <summary>A supplier's quote on one RFQ; its revisions are immutable (SRC-009).</summary>
public sealed class SupplierQuote
{
    private readonly List<QuoteRevision> revisions = [];

    private SupplierQuote()
    {
    }

    internal SupplierQuote(Guid id, Guid rfqId, Guid supplierId, Guid invitationId)
    {
        Id = id;
        RfqId = rfqId;
        SupplierId = supplierId;
        InvitationId = invitationId;
    }

    public Guid Id { get; private set; }
    public Guid RfqId { get; private set; }
    public Guid SupplierId { get; private set; }
    public Guid InvitationId { get; private set; }
    public IReadOnlyCollection<QuoteRevision> Revisions => revisions;

    /// <summary>The highest revision (Current or Withdrawn); superseded revisions stay in history.</summary>
    public QuoteRevision Latest => revisions.OrderByDescending(revision => revision.RevisionNumber).First();

    internal void AddRevision(QuoteRevision revision)
    {
        if (revisions.Count > 0 && Latest.Status == QuoteRevisionStatus.Current)
            Latest.Supersede();
        revisions.Add(revision);
    }
}

/// <summary>
/// Immutable quote revision with commercial terms (domain-model.md §3): currency, offered lines, freight, discount,
/// explicit taxes-included amount, validity, delivery estimate, payment terms, notes, attachment metadata and the
/// provenance required by D23-3 (source, received instant, entered-by user and entry instant). Totals follow CALC-002.
/// </summary>
public sealed class QuoteRevision
{
    public const int PaymentTermsMaxLength = 200;
    public const int NotesMaxLength = 2000;
    public const int MaxDeliveryDays = 3650;

    private readonly List<QuoteLineOffer> lines = [];
    private readonly List<QuoteAttachment> attachments = [];

    private QuoteRevision()
    {
        Currency = string.Empty;
        PaymentTerms = string.Empty;
    }

    internal QuoteRevision(Guid id, Guid quoteId, int revisionNumber, string currency, Guid? enteredBy, DateTime enteredAtUtc)
        : this()
    {
        Id = id;
        QuoteId = quoteId;
        RevisionNumber = revisionNumber;
        Currency = currency;
        Status = QuoteRevisionStatus.Current;
        EnteredBy = enteredBy;
        EnteredAtUtc = enteredAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid QuoteId { get; private set; }
    public int RevisionNumber { get; private set; }
    public QuoteRevisionStatus Status { get; private set; }
    public string Currency { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal Freight { get; private set; }
    public decimal Discount { get; private set; }
    public decimal TaxesIncluded { get; private set; }

    /// <summary>CALC-002: Σ line totals + freight − discount, always computed by the server.</summary>
    public decimal Total { get; private set; }

    public DateOnly ValidUntil { get; private set; }
    public int DeliveryDays { get; private set; }
    public string PaymentTerms { get; private set; }
    public string? Notes { get; private set; }
    public QuoteSource Source { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }
    public Guid? EnteredBy { get; private set; }
    public DateTime EnteredAtUtc { get; private set; }
    public DateTime? WithdrawnAtUtc { get; private set; }
    public Guid? WithdrawnBy { get; private set; }
    public string? WithdrawalReason { get; private set; }
    public IReadOnlyCollection<QuoteLineOffer> Lines => lines;
    public IReadOnlyCollection<QuoteAttachment> Attachments => attachments;

    public Money TotalMoney => new(Total, Currency);

    internal void Apply(QuoteInput input, string paymentTerms, string? notes, IReadOnlyList<(string FileName, string MediaType, long Length)> files)
    {
        foreach (var line in input.Lines.OrderBy(line => line.RfqLineNumber))
            lines.Add(new QuoteLineOffer(Guid.NewGuid(), Id, line.RfqLineNumber, line.QuantityOffered, line.UnitPrice,
                CalculationRules.LineTotal(line.QuantityOffered, new Money(line.UnitPrice, Currency)).Amount));
        foreach (var file in files)
            attachments.Add(new QuoteAttachment(Guid.NewGuid(), Id, file.FileName, file.MediaType, file.Length, EnteredBy, EnteredAtUtc));
        Subtotal = lines.Aggregate(Money.Zero(Currency), (sum, line) => sum.Add(new Money(line.LineTotal, Currency))).Amount;
        Freight = input.Freight;
        Discount = input.Discount;
        TaxesIncluded = input.TaxesIncluded;
        Total = Subtotal + Freight - Discount;
        ValidUntil = input.ValidUntil;
        DeliveryDays = input.DeliveryDays;
        PaymentTerms = paymentTerms;
        Notes = notes;
        Source = input.Source;
        ReceivedAtUtc = input.ReceivedAtUtc;
    }

    internal void Supersede() => Status = QuoteRevisionStatus.Superseded;

    internal void Withdraw(string reason, Guid? actorId, DateTime nowUtc)
    {
        Status = QuoteRevisionStatus.Withdrawn;
        WithdrawalReason = reason;
        WithdrawnBy = actorId;
        WithdrawnAtUtc = nowUtc;
    }
}

/// <summary>Offered quantity and unit price for one RFQ line; the line total is CALC-002.</summary>
public sealed class QuoteLineOffer
{
    private QuoteLineOffer()
    {
    }

    internal QuoteLineOffer(Guid id, Guid revisionId, int rfqLineNumber, decimal quantityOffered, decimal unitPrice, decimal lineTotal)
    {
        Id = id;
        RevisionId = revisionId;
        RfqLineNumber = rfqLineNumber;
        QuantityOffered = quantityOffered;
        UnitPrice = unitPrice;
        LineTotal = lineTotal;
    }

    public Guid Id { get; private set; }
    public Guid RevisionId { get; private set; }
    public int RfqLineNumber { get; private set; }
    public decimal QuantityOffered { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal LineTotal { get; private set; }
}

/// <summary>Attachment metadata of a quote revision (FIL-002 placeholder until Phase 30; no bytes).</summary>
public sealed class QuoteAttachment
{
    private QuoteAttachment()
    {
        FileName = string.Empty;
        MediaType = string.Empty;
    }

    internal QuoteAttachment(Guid id, Guid revisionId, string fileName, string mediaType, long length, Guid? addedBy, DateTime addedAtUtc)
        : this()
    {
        Id = id;
        RevisionId = revisionId;
        FileName = fileName;
        MediaType = mediaType;
        Length = length;
        AddedBy = addedBy;
        AddedAtUtc = addedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid RevisionId { get; private set; }
    public string FileName { get; private set; }
    public string MediaType { get; private set; }
    public long Length { get; private set; }
    public Guid? AddedBy { get; private set; }
    public DateTime AddedAtUtc { get; private set; }
}

/// <summary>Recorded waiver decision (domain-model.md §2 <c>WaiverDecision</c>); never edited.</summary>
public sealed class RfqWaiver
{
    public const int PermissionMaxLength = 100;

    private RfqWaiver()
    {
        RuleId = string.Empty;
        Permission = string.Empty;
        Reason = string.Empty;
    }

    internal RfqWaiver(Guid id, Guid rfqId, WaiverType type, string ruleId, Guid actorId, string permission, string reason, DateTime decidedAtUtc, Guid? scopeId)
        : this()
    {
        Id = id;
        RfqId = rfqId;
        Type = type;
        RuleId = ruleId;
        ActorId = actorId;
        Permission = permission;
        Reason = reason;
        DecidedAtUtc = decidedAtUtc;
        ScopeId = scopeId;
    }

    public Guid Id { get; private set; }
    public Guid RfqId { get; private set; }
    public WaiverType Type { get; private set; }
    public string RuleId { get; private set; }
    public Guid ActorId { get; private set; }
    public string Permission { get; private set; }
    public string Reason { get; private set; }
    public DateTime DecidedAtUtc { get; private set; }

    /// <summary>The award or exchange rate the waiver covers, when narrower than the whole RFQ.</summary>
    public Guid? ScopeId { get; private set; }
}

/// <summary>
/// Persisted exchange-rate snapshot (CALC-004, SRC-013): base units per one unit of <see cref="FromCurrency"/>, with
/// source, quote date, retrieval instant and, for <c>Manual</c>, the entering user, reason and waiver. Never read live.
/// </summary>
public sealed class RfqExchangeRate
{
    public const int ProviderMaxLength = 30;

    private RfqExchangeRate()
    {
        FromCurrency = string.Empty;
        ToCurrency = string.Empty;
        Provider = string.Empty;
    }

    internal RfqExchangeRate(Guid id, Guid rfqId, ExchangeRatePurpose purpose, string baseCurrency, ExchangeRateInput rate, Guid? enteredBy, string? reason, Guid? waiverId)
        : this()
    {
        Id = id;
        RfqId = rfqId;
        Purpose = purpose;
        FromCurrency = rate.Currency;
        ToCurrency = baseCurrency;
        Rate = rate.Rate;
        Source = rate.Source;
        Provider = rate.Provider;
        QuoteDate = rate.QuoteDate;
        RetrievedAtUtc = rate.RetrievedAtUtc;
        EnteredBy = enteredBy;
        Reason = reason;
        WaiverId = waiverId;
    }

    public Guid Id { get; private set; }
    public Guid RfqId { get; private set; }
    public ExchangeRatePurpose Purpose { get; private set; }
    public string FromCurrency { get; private set; }
    public string ToCurrency { get; private set; }
    public decimal Rate { get; private set; }
    public ExchangeRateSource Source { get; private set; }
    public string Provider { get; private set; }
    public DateOnly QuoteDate { get; private set; }
    public DateTime RetrievedAtUtc { get; private set; }
    public Guid? EnteredBy { get; private set; }
    public string? Reason { get; private set; }
    public Guid? WaiverId { get; private set; }
}

/// <summary>
/// Append-only RFQ timeline entry: transitions and in-state actions (invite, remove, quote, extend, remind, rate,
/// award) with actor, UTC instant, reason, correlation and the emitted event type.
/// </summary>
public sealed class RfqHistoryEntry
{
    public const int ActionMaxLength = 40;
    public const int CorrelationIdMaxLength = 128;
    public const int EventTypeMaxLength = 100;

    private RfqHistoryEntry()
    {
        Action = string.Empty;
        CorrelationId = string.Empty;
    }

    internal RfqHistoryEntry(
        Guid id, Guid rfqId, int sequence, string action, RfqStatus? from, RfqStatus to, Guid? actorId, DateTime atUtc, string? reason,
        string correlationId, string? eventType, Guid? subjectId)
        : this()
    {
        Id = id;
        RfqId = rfqId;
        Sequence = sequence;
        Action = action;
        From = from;
        To = to;
        ActorId = actorId;
        AtUtc = atUtc;
        Reason = reason;
        CorrelationId = correlationId;
        EventType = eventType;
        SubjectId = subjectId;
    }

    public Guid Id { get; private set; }
    public Guid RfqId { get; private set; }
    public int Sequence { get; private set; }
    public string Action { get; private set; }
    public RfqStatus? From { get; private set; }
    public RfqStatus To { get; private set; }
    public Guid? ActorId { get; private set; }
    public DateTime AtUtc { get; private set; }
    public string? Reason { get; private set; }
    public string CorrelationId { get; private set; }
    public string? EventType { get; private set; }

    /// <summary>Supplier, quote, rate or award the action concerns.</summary>
    public Guid? SubjectId { get; private set; }
}
