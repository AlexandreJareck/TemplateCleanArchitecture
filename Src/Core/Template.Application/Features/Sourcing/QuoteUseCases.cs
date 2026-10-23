#nullable enable
using System.Globalization;
using FluentValidation;
using MediatR;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Interfaces.Sourcing;
using Template.Application.Services.MasterData;
using Template.Application.Services.Procurement;
using Template.Domain.Common;
using Template.Domain.Sourcing;

namespace Template.Application.Features.Sourcing;

public sealed record QuoteLineRequest(int RfqLineNumber, string? QuantityOffered, string? UnitPrice);

public sealed record QuoteAttachmentRequest(string? FileName, string? MediaType, long Length);

/// <summary>
/// Buyer-entered quote body (D23-3). Amounts and quantities are invariant decimal strings; freight, discount and the
/// explicit taxes-included amount default to zero; totals are never accepted from the client (CALC-002).
/// </summary>
public sealed record QuoteRequest(
    string? Currency,
    IReadOnlyList<QuoteLineRequest>? Lines,
    string? Freight,
    string? Discount,
    string? TaxesIncluded,
    DateOnly? ValidUntil,
    int? DeliveryDays,
    string? PaymentTerms,
    string? Notes,
    QuoteSource? Source,
    DateTime? ReceivedAt,
    IReadOnlyList<QuoteAttachmentRequest>? Attachments);

/// <summary>SRC-008: revision 1 for an invited supplier. Permission <c>sourcing.quotes.enter</c>.</summary>
public sealed record EnterQuoteCommand(Guid Id, long? ExpectedVersion, Guid SupplierId, QuoteRequest Quote) : IRequest<Result<RfqView>>;

/// <summary>SRC-009: revision n+1 supersedes the current revision.</summary>
public sealed record ReviseQuoteCommand(Guid Id, long? ExpectedVersion, Guid QuoteId, QuoteRequest Quote) : IRequest<Result<RfqView>>;

/// <summary>SRC-009: withdraws the current revision with a reason.</summary>
public sealed record WithdrawQuoteCommand(Guid Id, long? ExpectedVersion, Guid QuoteId, string? Reason) : IRequest<Result<RfqView>>;

public sealed class QuoteRequestValidator : AbstractValidator<QuoteRequest>
{
    public QuoteRequestValidator()
    {
        RuleFor(quote => quote.Currency).NotEmpty().Length(3);
        RuleFor(quote => quote.Lines).NotEmpty();
        RuleForEach(quote => quote.Lines).ChildRules(line =>
        {
            line.RuleFor(item => item.RfqLineNumber).GreaterThan(0);
            line.RuleFor(item => item.QuantityOffered).NotEmpty().MaximumLength(30);
            line.RuleFor(item => item.UnitPrice).NotEmpty().MaximumLength(30);
        });
        RuleFor(quote => quote.Freight).MaximumLength(30);
        RuleFor(quote => quote.Discount).MaximumLength(30);
        RuleFor(quote => quote.TaxesIncluded).MaximumLength(30);
        RuleFor(quote => quote.ValidUntil).NotNull();
        RuleFor(quote => quote.DeliveryDays).NotNull();
        RuleFor(quote => quote.PaymentTerms).NotEmpty().MaximumLength(QuoteRevision.PaymentTermsMaxLength);
        RuleFor(quote => quote.Notes).MaximumLength(QuoteRevision.NotesMaxLength);
        RuleFor(quote => quote.Source).NotNull().IsInEnum();
        RuleFor(quote => quote.ReceivedAt).NotNull();
        RuleFor(quote => quote.Attachments).Must(list => list is null || list.Count <= RequestForQuotation.MaxQuoteAttachments);
    }
}

public sealed class EnterQuoteCommandValidator : AbstractValidator<EnterQuoteCommand>
{
    public EnterQuoteCommandValidator()
    {
        RuleFor(command => command.SupplierId).NotEmpty();
        RuleFor(command => command.Quote).NotNull().SetValidator(new QuoteRequestValidator());
    }
}

public sealed class ReviseQuoteCommandValidator : AbstractValidator<ReviseQuoteCommand>
{
    public ReviseQuoteCommandValidator() => RuleFor(command => command.Quote).NotNull().SetValidator(new QuoteRequestValidator());
}

public sealed class QuoteCommandHandlers(
    IRequestForQuotationRepository rfqs,
    SourcingAccess access,
    ProcurementWriter writer) :
    IRequestHandler<EnterQuoteCommand, Result<RfqView>>,
    IRequestHandler<ReviseQuoteCommand, Result<RfqView>>,
    IRequestHandler<WithdrawQuoteCommand, Result<RfqView>>
{
    public async Task<Result<RfqView>> Handle(EnterQuoteCommand request, CancellationToken cancellationToken)
    {
        var (rfq, failure) = await LoadAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        var (input, deadlineDate, parseError) = await ParseAsync(rfq!, request.Quote, cancellationToken);
        if (parseError is not null)
            return parseError;
        var (revision, error) = rfq!.EnterQuote(request.SupplierId, input!, deadlineDate, writer.ActorId, writer.UtcNow, writer.CorrelationId);
        if (error is not null)
            return error.ToApplicationError();

        Audit(SourcingAudit.QuoteRecorded, rfq, revision!, request.SupplierId, ["SRC-008", "SRC-010"]);
        return await CommitAsync(rfq, cancellationToken);
    }

    public async Task<Result<RfqView>> Handle(ReviseQuoteCommand request, CancellationToken cancellationToken)
    {
        var (rfq, failure) = await LoadAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        var (input, deadlineDate, parseError) = await ParseAsync(rfq!, request.Quote, cancellationToken);
        if (parseError is not null)
            return parseError;
        var (revision, error) = rfq!.ReviseQuote(request.QuoteId, input!, deadlineDate, writer.ActorId, writer.UtcNow, writer.CorrelationId);
        if (error is not null)
            return error.ToApplicationError();

        Audit(SourcingAudit.QuoteRevised, rfq, revision!, rfq.Quotes.Single(quote => quote.Id == request.QuoteId).SupplierId, ["SRC-009", "SRC-010"]);
        return await CommitAsync(rfq, cancellationToken);
    }

    public async Task<Result<RfqView>> Handle(WithdrawQuoteCommand request, CancellationToken cancellationToken)
    {
        var (rfq, failure) = await LoadAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        if (rfq!.WithdrawQuote(request.QuoteId, request.Reason, writer.ActorId, writer.UtcNow, writer.CorrelationId) is { } error)
            return error.ToApplicationError();

        var quote = rfq.Quotes.Single(candidate => candidate.Id == request.QuoteId);
        writer.Audit(SourcingAudit.QuoteWithdrawn, SourcingAudit.Resource, rfq.Id, "Current", "Withdrawn", MasterDataRules.NormalizeReason(request.Reason),
            ["SRC-009"],
            new Dictionary<string, string>
            {
                ["quoteId"] = quote.Id.ToString(),
                ["quoteRevisionId"] = quote.Latest.Id.ToString(),
                ["revision"] = SourcingAudit.Text(quote.Latest.RevisionNumber),
                ["supplierId"] = quote.SupplierId.ToString()
            });
        return await CommitAsync(rfq, cancellationToken);
    }

    private void Audit(string action, RequestForQuotation rfq, QuoteRevision revision, Guid supplierId, string[] ruleIds) =>
        writer.Audit(action, SourcingAudit.Resource, rfq.Id, ruleIds: ruleIds,
            details: new Dictionary<string, string>
            {
                ["quoteId"] = revision.QuoteId.ToString(),
                ["quoteRevisionId"] = revision.Id.ToString(),
                ["revision"] = SourcingAudit.Text(revision.RevisionNumber),
                ["supplierId"] = supplierId.ToString(),
                ["amount"] = SourcingAudit.Amount(revision.Total),
                ["currency"] = revision.Currency
            });

    private async Task<(QuoteInput? Input, DateOnly DeadlineDate, ApplicationError? Error)> ParseAsync(
        RequestForQuotation rfq, QuoteRequest quote, CancellationToken cancellationToken)
    {
        var settings = await writer.SettingsAsync(cancellationToken);
        var lines = new List<QuoteLineInput>();
        foreach (var line in quote.Lines ?? [])
        {
            if (!TryParse(line.QuantityOffered, out var quantity))
                return Fail($"lines[{line.RfqLineNumber}].quantityOffered");
            if (!TryParse(line.UnitPrice, out var price))
                return Fail($"lines[{line.RfqLineNumber}].unitPrice");
            lines.Add(new QuoteLineInput(line.RfqLineNumber, quantity, price));
        }

        if (!TryParseOptional(quote.Freight, out var freight))
            return Fail("freight");
        if (!TryParseOptional(quote.Discount, out var discount))
            return Fail("discount");
        if (!TryParseOptional(quote.TaxesIncluded, out var taxes))
            return Fail("taxesIncluded");
        if (quote.ReceivedAt is not { Kind: DateTimeKind.Utc } receivedAt)
            return (null, default, ApplicationError.Validation("receivedAt", "receivedAt must be a UTC instant (ISO 8601 with Z)."));

        var currency = CurrencyCodes.Normalize(quote.Currency);
        var deadlineDate = rfq.DeadlineUtc is { } deadline ? ProcurementWriter.LocalDate(deadline, settings.TimeZone) : DateOnly.MinValue;
        var input = new QuoteInput(
            quote.Currency, currency is not null && settings.SupportedCurrencies.Contains(currency), lines, freight, discount, taxes,
            quote.ValidUntil!.Value, quote.DeliveryDays!.Value, quote.PaymentTerms, quote.Notes, quote.Source!.Value, receivedAt,
            (quote.Attachments ?? []).Select(attachment => new QuoteAttachmentInput(attachment.FileName, attachment.MediaType, attachment.Length)).ToArray());
        return (input, deadlineDate, null);

        static (QuoteInput?, DateOnly, ApplicationError?) Fail(string field) =>
            (null, default, ApplicationError.Validation(field, $"{field} must be an invariant decimal string."));
    }

    private static bool TryParse(string? value, out decimal amount) =>
        decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);

    private static bool TryParseOptional(string? value, out decimal amount)
    {
        amount = 0m;
        return string.IsNullOrWhiteSpace(value) || TryParse(value, out amount);
    }

    private async Task<(RequestForQuotation? Rfq, ApplicationError? Error)> LoadAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken)
    {
        if (!await access.MayReadAsync(cancellationToken))
            return (null, ProcurementWriter.Forbidden());
        var rfq = await rfqs.FindAsync(id, cancellationToken);
        if (rfq is null)
            return (null, SourcingAudit.NotFound());
        return VersionPrecondition.Check(expectedVersion, rfq.Version) is { } precondition ? (null, precondition) : (rfq, null);
    }

    private async Task<Result<RfqView>> CommitAsync(RequestForQuotation rfq, CancellationToken cancellationToken)
    {
        var events = writer.Stage(rfq.DomainEvents);
        var commit = await writer.CommitAsync(token => rfqs.GetVersionAsync(rfq.Id, token), null, cancellationToken);
        if (commit is not null)
            return commit;

        writer.Record(events);
        rfq.ClearDomainEvents();
        return await access.ViewAsync(rfq, cancellationToken);
    }
}

public sealed record GetRfqQuery(Guid Id) : IRequest<Result<RfqView>>;

public sealed record GetRfqHistoryQuery(Guid Id) : IRequest<Result<RfqHistoryView>>;

/// <summary>RFQ list; <see cref="Status"/> is comma-separated; <see cref="Q"/> matches the RFQ or requisition number.</summary>
public sealed record ListRfqsQuery(
    int? Page = null,
    int? PageSize = null,
    string? Sort = null,
    string? Status = null,
    Guid? RequisitionId = null,
    Guid? SupplierId = null,
    string? Q = null) : IRequest<Result<PagedResult<RfqSummaryView>>>;

/// <summary>Reads require <c>sourcing.rfqs.manage</c> or <c>sourcing.quotes.read</c>; commercial details follow SRC-021.</summary>
public sealed class RfqQueryHandlers(IRequestForQuotationRepository rfqs, SourcingAccess access) :
    IRequestHandler<GetRfqQuery, Result<RfqView>>,
    IRequestHandler<GetRfqHistoryQuery, Result<RfqHistoryView>>,
    IRequestHandler<ListRfqsQuery, Result<PagedResult<RfqSummaryView>>>
{
    public const string DefaultSort = "-createdAt";

    public async Task<Result<RfqView>> Handle(GetRfqQuery request, CancellationToken cancellationToken)
    {
        if (!await access.MayReadAsync(cancellationToken))
            return ProcurementWriter.Forbidden();
        var rfq = await rfqs.FindAsync(request.Id, cancellationToken);
        return rfq is null ? SourcingAudit.NotFound() : await access.ViewAsync(rfq, cancellationToken);
    }

    public async Task<Result<RfqHistoryView>> Handle(GetRfqHistoryQuery request, CancellationToken cancellationToken)
    {
        if (!await access.MayReadAsync(cancellationToken))
            return ProcurementWriter.Forbidden();
        var rfq = await rfqs.FindAsync(request.Id, cancellationToken);
        return rfq is null ? SourcingAudit.NotFound() : RfqHistoryView.From(rfq);
    }

    public async Task<Result<PagedResult<RfqSummaryView>>> Handle(ListRfqsQuery request, CancellationToken cancellationToken)
    {
        if (!await access.MayReadAsync(cancellationToken))
            return ProcurementWriter.Forbidden();
        var page = PageRequest.Create(request.Page, request.PageSize, request.Sort, IRequestForQuotationRepository.SortFields, DefaultSort);
        if (!page.IsSuccess)
            return page.Error!;
        if (MasterDataQueries.InvalidQ(request.Q) is { } invalidQ)
            return invalidQ;
        var statuses = ProcurementQueries.ParseEnumList<RfqStatus>(request.Status, "status");
        if (!statuses.IsSuccess)
            return statuses.Error!;

        var filter = new RfqFilter(statuses.Value, request.RequisitionId, MasterDataQueries.Trim(request.Q)?.ToUpperInvariant(), request.SupplierId);
        return MasterDataQueries.Map(await rfqs.ListAsync(filter, page.Value, cancellationToken), RfqSummaryView.From);
    }
}
