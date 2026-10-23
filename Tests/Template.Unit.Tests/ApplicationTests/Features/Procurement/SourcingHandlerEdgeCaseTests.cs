using System.Text.Json;
using MediatR;
using Shouldly;
using Template.Application.Common.Results;
using Template.Application.Features.Catalog;
using Template.Application.Features.Requisitions;
using Template.Application.Features.Sourcing;
using Template.Application.Messaging.Procurement;
using Template.Application.Security;
using Template.Application.Wrappers;
using Template.Domain.Requisitions;
using Template.Domain.Sourcing;

namespace Template.Unit.Tests.ApplicationTests.Features.Procurement;

/// <summary>
/// Failure and boundary paths of the sourcing use cases against port fakes: authorization (403), unknown IDs (404),
/// version preconditions (428/412), parsing and state rejections, list validation, the system close retry, the
/// requisition expiry cascade, allowed actions per state and envelope validation.
/// </summary>
public sealed class SourcingHandlerEdgeCaseTests
{
    private const string Reason = "Documented sourcing justification";
    private readonly ProcurementTestDoubles doubles = new();
    private readonly ProcurementTestDoubles.Actor buyer = new();
    private readonly ProcurementTestDoubles.Actor manager = new();

    public SourcingHandlerEdgeCaseTests()
    {
        doubles.Permissions.Grant(buyer.Id, SeededRoles.PermissionsByRole[SeededRoles.Buyer]);
        doubles.Permissions.Grant(manager.Id, SeededRoles.PermissionsByRole[SeededRoles.ProcurementManager]);
    }

    public static TheoryData<string> Commands => new(
        "invite", "remove", "publish", "extend", "remind", "close", "cancel", "rate", "award", "enter", "revise", "withdraw");

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task EveryCommand_Answers403ForANonReader_404ForAnUnknownRfq_And412ForAStaleVersion(string command)
    {
        var (rfq, supplier) = await PublishedAsync();

        doubles.ActAs(doubles.Requester);
        (await SendAsync(command, rfq.Id, rfq.Version, supplier)).Error!.Code.ShouldBe(ProblemCodes.Forbidden);
        doubles.ActAs(buyer);
        (await SendAsync(command, Guid.NewGuid(), 1, supplier)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
        (await SendAsync(command, rfq.Id, rfq.Version - 1, supplier)).Error!.Category.ShouldBe(ErrorCategory.PreconditionFailed);
    }

    [Fact]
    public async Task StateRejections_AreMappedWithTheirStableCodes()
    {
        var (rfq, supplier) = await PublishedAsync();

        (await doubles.RfqCommands().Handle(new RemoveInvitationCommand(rfq.Id, rfq.Version, supplier), default)).Error!.Code.ShouldBe(ProblemCodes.InvalidTransition);
        (await doubles.RfqCommands().Handle(new ExtendRfqDeadlineCommand(rfq.Id, rfq.Version, rfq.DeadlineUtc!.Value.AddDays(1), "short"), default))
            .Error!.Code.ShouldBe(ProblemCodes.ReasonRequired);
        (await doubles.RfqCommands().Handle(new ExtendRfqDeadlineCommand(rfq.Id, rfq.Version, DateTime.SpecifyKind(ProcurementTestDoubles.Now, DateTimeKind.Unspecified), Reason), default))
            .Error!.Code.ShouldBe(ProblemCodes.RfqDeadlineInvalid);
        (await doubles.RfqCommands().Handle(new PublishRfqCommand(rfq.Id, rfq.Version, ProcurementTestDoubles.Now.AddDays(3), null), default))
            .Error!.Code.ShouldBe(ProblemCodes.InvalidTransition);
        (await doubles.RfqCommands().Handle(new RecordManualExchangeRateCommand(rfq.Id, rfq.Version, "USD", "5.1", ProcurementTestDoubles.Today, Reason), default))
            .Error!.Code.ShouldBe(SourcingErrors.WaiverPermissionRequiredCode);
        doubles.ActAs(manager);
        (await doubles.RfqCommands().Handle(new RecordManualExchangeRateCommand(rfq.Id, rfq.Version, "USD", "5,1", ProcurementTestDoubles.Today, Reason), default))
            .Error!.FieldErrors!.Keys.ShouldContain("rate");
        (await doubles.RfqCommands().Handle(new RecordManualExchangeRateCommand(rfq.Id, rfq.Version, "USD", "5.1", ProcurementTestDoubles.Today, Reason), default))
            .Error!.Code.ShouldBe(ProblemCodes.InvalidTransition);
        (await doubles.RfqCommands().Handle(new SubmitAwardCommand(rfq.Id, rfq.Version, Guid.NewGuid(), null, null, null), default))
            .Error!.Code.ShouldBe(ProblemCodes.InvalidTransition);
        (await doubles.RfqCommands().Handle(new CancelRfqCommand(rfq.Id, rfq.Version, "short"), default)).Error!.Code.ShouldBe(ProblemCodes.ReasonRequired);
        (await doubles.QuoteCommands().Handle(new WithdrawQuoteCommand(rfq.Id, rfq.Version, Guid.NewGuid(), Reason), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
        (await doubles.QuoteCommands().Handle(new EnterQuoteCommand(rfq.Id, rfq.Version, Guid.NewGuid(), Quote("10")), default))
            .Error!.Code.ShouldBe(ProblemCodes.QuoteSupplierNotInvited);
        (await doubles.QuoteCommands().Handle(new ReviseQuoteCommand(rfq.Id, rfq.Version, Guid.NewGuid(), Quote("10")), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
    }

    [Fact]
    public async Task UnknownReferences_AndMalformedInput_AreRejectedBeforeTheAggregate()
    {
        doubles.ActAs(buyer);
        (await doubles.RfqCommands().Handle(new CreateRfqCommand(Guid.NewGuid(), null), default)).Error!.Code.ShouldBe(ProblemCodes.RfqRequisitionNotEligible);
        var (rfq, supplier) = await PublishedAsync();
        (await doubles.RfqCommands().Handle(new InviteSupplierCommand(rfq.Id, rfq.Version, Guid.NewGuid()), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
        (await doubles.RfqCommands().Handle(new PublishRfqCommand(rfq.Id, rfq.Version, null, null), default)).Error!.Code.ShouldBe(ProblemCodes.RfqDeadlineInvalid);
        foreach (var (field, quote) in new[]
                 {
                     ("lines[1].unitPrice", Quote("1,0")),
                     ("freight", Quote("10") with { Freight = "x" }),
                     ("discount", Quote("10") with { Discount = "x" }),
                     ("taxesIncluded", Quote("10") with { TaxesIncluded = "x" })
                 })
            (await doubles.QuoteCommands().Handle(new EnterQuoteCommand(rfq.Id, rfq.Version, supplier, quote), default)).Error!.FieldErrors!.Keys.ShouldContain(field);
        new QuoteRequestValidator().Validate(Quote("10") with { Attachments = Enumerable.Range(0, 11).Select(_ => new QuoteAttachmentRequest("a.pdf", "application/pdf", 1)).ToArray() })
            .IsValid.ShouldBeFalse();
        new RecordManualExchangeRateCommandValidator().Validate(new RecordManualExchangeRateCommand(rfq.Id, 1, "", "", null, Reason)).IsValid.ShouldBeFalse();
        new SubmitAwardCommandValidator().Validate(new SubmitAwardCommand(rfq.Id, 1, Guid.Empty, null, null, null)).IsValid.ShouldBeFalse();
        new CreateRfqCommandValidator().Validate(new CreateRfqCommand(Guid.Empty, null)).IsValid.ShouldBeFalse();
        new InviteSupplierCommandValidator().Validate(new InviteSupplierCommand(rfq.Id, 1, Guid.Empty)).IsValid.ShouldBeFalse();
        new PublishRfqCommandValidator().Validate(new PublishRfqCommand(rfq.Id, 1, null, null)).IsValid.ShouldBeFalse();
        new ExtendRfqDeadlineCommandValidator().Validate(new ExtendRfqDeadlineCommand(rfq.Id, 1, null, Reason)).IsValid.ShouldBeFalse();
        new EnterQuoteCommandValidator().Validate(new EnterQuoteCommand(rfq.Id, 1, Guid.Empty, Quote("10"))).IsValid.ShouldBeFalse();
        new ReviseQuoteCommandValidator().Validate(new ReviseQuoteCommand(rfq.Id, 1, Guid.NewGuid(), Quote("10") with { Currency = null })).IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task Queries_ValidatePaging_TextAndStatus_AndFilterTheList()
    {
        var (rfq, _) = await PublishedAsync();
        var queries = doubles.RfqQueries();

        (await queries.Handle(new ListRfqsQuery(PageSize: 500), default)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await queries.Handle(new ListRfqsQuery(Q: new string('q', 300)), default)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await queries.Handle(new ListRfqsQuery(Status: "Auction"), default)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        var list = await queries.Handle(new ListRfqsQuery(Status: "Published", RequisitionId: rfq.RequisitionId, Q: "rfq"), default);
        list.Value.Items.Single().Status.ShouldBe("Published");
        (await queries.Handle(new GetRfqHistoryQuery(rfq.Id), default)).Value.Entries.Select(entry => entry.Action).ShouldContain("published");
        (await queries.Handle(new GetRfqHistoryQuery(Guid.NewGuid()), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
        (await queries.Handle(new GetRfqQuery(Guid.NewGuid()), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
        doubles.ActAs(doubles.Requester);
        (await doubles.RfqQueries().Handle(new GetRfqHistoryQuery(rfq.Id), default)).Error!.Code.ShouldBe(ProblemCodes.Forbidden);
    }

    [Fact]
    public async Task SystemClose_UnknownRfq_IsNotFound_AndAnRfqBeforeItsDeadlineIsNotDue()
    {
        var (rfq, _) = await PublishedAsync();
        doubles.ActAsSystem();
        (await doubles.RfqCommands().Handle(new CloseDueRfqCommand(Guid.NewGuid()), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
        (await doubles.RfqCommands().Handle(new CloseDueRfqCommand(rfq.Id), default)).Value.ShouldBeFalse();
        rfq.Status.ShouldBe(RfqStatus.Published);
    }

    [Fact]
    public async Task T_REQ_009_ExpiringARequisitionInSourcing_CancelsItsRfqInTheSameCommit()
    {
        var (rfq, _) = await PublishedAsync();
        doubles.ActAsSystem();
        doubles.Clock.UtcNow = ProcurementTestDoubles.Now.AddDays(10 + RequisitionPolicy.V1.ExpiryGraceDays + 1);

        (await doubles.Transitions().Handle(new ExpireDueRequisitionsCommand(), default)).Value.ShouldBe(1);
        rfq.Status.ShouldBe(RfqStatus.Cancelled);
        rfq.CancellationReasonCode.ShouldBe("RequisitionExpired");
        doubles.Outbox.Events.Select(envelope => envelope.EventType).ShouldContain("procurement.rfq.cancelled");
    }

    [Fact]
    public async Task AllowedActions_FollowStatePermissionsAndTheDeadline()
    {
        var (rfq, supplier) = await PublishedAsync();
        var view = (await doubles.RfqQueries().Handle(new GetRfqQuery(rfq.Id), default)).Value;
        view.AllowedActions.ShouldBe(["invite", "extend-deadline", "remind", "enter-quote", "revise-quote", "withdraw-quote", "close-early", "cancel"]);

        doubles.Clock.UtcNow = rfq.DeadlineUtc!.Value;
        (await doubles.RfqQueries().Handle(new GetRfqQuery(rfq.Id), default)).Value.AllowedActions.ShouldContain("close");
        await doubles.QuoteCommands().Handle(new EnterQuoteCommand(rfq.Id, rfq.Version, supplier, Quote("10", "USD")), default);
        await doubles.RfqCommands().Handle(new CloseRfqCommand(rfq.Id, rfq.Version, null), default);
        doubles.ActAs(manager);
        (await doubles.RfqQueries().Handle(new GetRfqQuery(rfq.Id), default)).Value.AllowedActions.ShouldBe(["record-rate", "cancel"]);
        var manual = await doubles.RfqCommands().Handle(new RecordManualExchangeRateCommand(rfq.Id, rfq.Version, "USD", "5.25", ProcurementTestDoubles.Today, Reason), default);
        manual.Value.AllowedActions.ShouldBe(["award", "cancel"]);
        manual.Value.Awards.ShouldBeEmpty();

        var auditorView = RfqView.From(rfq, commercial: false, []);
        auditorView.Quotes.ShouldBeNull();
        auditorView.Comparison.ShouldBeNull();
    }

    [Fact]
    public void T_SRC_021_AwardView_HidesCommercialFieldsWithoutQuotesRead_ButKeepsTheSupplier()
    {
        var rfq = Template.Unit.Tests.DomainTests.Sourcing.RequestForQuotationTests.Awarded();

        var hidden = RfqView.From(rfq, commercial: false, []).Awards.Single();
        (hidden.QuoteRevisionId, hidden.Rank, hidden.AwardedTotal, hidden.NormalizedTotal, hidden.RequisitionCurrencyTotal, hidden.Justification)
            .ShouldBe((null, null, null, null, null, null));
        hidden.SupplierLegalName.ShouldNotBeNullOrWhiteSpace();
        var visible = RfqView.From(rfq, commercial: true, []).Awards.Single();
        (visible.Rank, visible.Justification).ShouldBe((1, "Best value"));
        visible.NormalizedTotal!.Currency.ShouldBe("BRL");
    }

    [Theory]
    [InlineData(WaiverType.LowValueDirectPurchase, Permissions.SourcingWaiversLowValue, "SRC-006")]
    [InlineData(WaiverType.DirectPurchase, Permissions.SourcingWaiversDirectPurchase, "SRC-006")]
    [InlineData(WaiverType.InsufficientQuotations, Permissions.SourcingWaiversInsufficientQuotations, "SRC-017")]
    [InlineData(WaiverType.NonLeadingSelection, Permissions.SourcingWaiversNonLeading, "SRC-018")]
    [InlineData(WaiverType.ManualExchangeRate, Permissions.SourcingRatesManual, "SRC-013")]
    public void EveryWaiver_HasItsDedicatedPermission_AndRuleId(WaiverType type, string permission, string ruleId)
    {
        SourcingAccess.PermissionFor(type).ShouldBe(permission);
        var error = SourcingAudit.WaiverPermissionRequired(type, permission);
        (error.Code, error.Category, error.RuleId).ShouldBe((SourcingErrors.WaiverPermissionRequiredCode, ErrorCategory.Forbidden, ruleId));
        Should.Throw<ArgumentOutOfRangeException>(() => SourcingAccess.PermissionFor((WaiverType)99));
    }

    [Fact]
    public void Contract_RejectsInvalidEnvelopes_AndThePlannerRejectsAPayloadWithoutRecipients()
    {
        var valid = ProcurementIntegrationEventContract.FromDomainEvent(
            new RfqPublished(ProcurementTestDoubles.Now, Guid.NewGuid(), "RFQ-1", Guid.NewGuid(), ProcurementTestDoubles.Now.AddDays(3), [Guid.NewGuid()],
                SourcingMethod.Direct), "trace");
        foreach (var invalid in new[]
                 {
                     valid with { EventId = Guid.Empty },
                     valid with { AggregateId = Guid.Empty },
                     valid with { EventVersion = 2 },
                     valid with { OccurredAtUtc = DateTime.SpecifyKind(valid.OccurredAtUtc, DateTimeKind.Local) },
                     valid with { EventType = " " },
                     valid with { CorrelationId = new string('c', 129) },
                     valid with { AggregateType = "" },
                     valid with { AggregateVersion = "" },
                     valid with { Payload = JsonSerializer.SerializeToElement(1) },
                     valid with { EventType = "procurement.unknown" }
                 })
            Should.Throw<JsonException>(() => ProcurementIntegrationEventContract.Validate(invalid));
        Should.Throw<JsonException>(() => ProcurementIntegrationEventContract.Deserialize("null"u8));
        Should.Throw<JsonException>(() => RfqNotificationPlanner.Plan(valid with { Payload = JsonSerializer.SerializeToElement(new { rfqId = Guid.NewGuid() }) }));
        Should.Throw<ArgumentNullException>(() => RfqNotificationPlanner.Plan(null!));
    }

    private async Task<Result<RfqView>> SendAsync(string command, Guid id, long version, Guid supplier)
    {
        IRequest<Result<RfqView>> request = command switch
        {
            "invite" => new InviteSupplierCommand(id, version, supplier),
            "remove" => new RemoveInvitationCommand(id, version, supplier),
            "publish" => new PublishRfqCommand(id, version, ProcurementTestDoubles.Now.AddDays(3), null),
            "extend" => new ExtendRfqDeadlineCommand(id, version, ProcurementTestDoubles.Now.AddDays(4), Reason),
            "remind" => new RemindRfqSuppliersCommand(id, version),
            "close" => new CloseRfqCommand(id, version, Reason),
            "cancel" => new CancelRfqCommand(id, version, Reason),
            "rate" => new RecordManualExchangeRateCommand(id, version, "USD", "5.1", ProcurementTestDoubles.Today, Reason),
            "award" => new SubmitAwardCommand(id, version, Guid.NewGuid(), null, null, null),
            "enter" => new EnterQuoteCommand(id, version, supplier, Quote("10")),
            "revise" => new ReviseQuoteCommand(id, version, Guid.NewGuid(), Quote("10")),
            _ => new WithdrawQuoteCommand(id, version, Guid.NewGuid(), Reason)
        };
        return request switch
        {
            EnterQuoteCommand enter => await doubles.QuoteCommands().Handle(enter, default),
            ReviseQuoteCommand revise => await doubles.QuoteCommands().Handle(revise, default),
            WithdrawQuoteCommand withdraw => await doubles.QuoteCommands().Handle(withdraw, default),
            InviteSupplierCommand invite => await doubles.RfqCommands().Handle(invite, default),
            RemoveInvitationCommand remove => await doubles.RfqCommands().Handle(remove, default),
            PublishRfqCommand publish => await doubles.RfqCommands().Handle(publish, default),
            ExtendRfqDeadlineCommand extend => await doubles.RfqCommands().Handle(extend, default),
            RemindRfqSuppliersCommand remind => await doubles.RfqCommands().Handle(remind, default),
            CloseRfqCommand close => await doubles.RfqCommands().Handle(close, default),
            CancelRfqCommand cancel => await doubles.RfqCommands().Handle(cancel, default),
            RecordManualExchangeRateCommand rate => await doubles.RfqCommands().Handle(rate, default),
            _ => await doubles.RfqCommands().Handle((SubmitAwardCommand)request, default)
        };
    }

    private async Task<(RequestForQuotation Rfq, Guid Supplier)> PublishedAsync()
    {
        doubles.ActAs(doubles.Requester);
        var draft = await doubles.Drafts().Handle(
            new CreateRequisitionDraftCommand(doubles.CostCenter.Id, "BRL", "Replacement laptops for the support team", ProcurementTestDoubles.Today.AddDays(10)), default);
        var requisition = doubles.Requisitions.Items.Single(item => item.Id == draft.Value.Id);
        await doubles.Drafts().Handle(new AddRequisitionLineCommand(requisition.Id, requisition.Version,
            new RequisitionLineRequest(null, "Laptop 14 inch", RequisitionLineKind.Good, doubles.Category.Id, doubles.Unit.Id, "1", new MoneyInput("100", "BRL"))), default);
        (await doubles.Transitions().Handle(new SubmitRequisitionCommand(requisition.Id, requisition.Version), default)).IsSuccess.ShouldBeTrue();
        doubles.ActAs(buyer);
        await doubles.RfqCommands().Handle(new CreateRfqCommand(requisition.Id, null), default);
        var rfq = doubles.Rfqs.Items.Single();
        var supplier = doubles.Suppliers.AddActive("Edge supplier");
        await doubles.RfqCommands().Handle(new InviteSupplierCommand(rfq.Id, rfq.Version, supplier.SupplierId), default);
        (await doubles.RfqCommands().Handle(new PublishRfqCommand(rfq.Id, rfq.Version, ProcurementTestDoubles.Now.AddDays(3), null), default)).IsSuccess.ShouldBeTrue();
        return (rfq, supplier.SupplierId);
    }

    private static QuoteRequest Quote(string unitPrice, string currency = "BRL") => new(
        currency, [new QuoteLineRequest(1, "1", unitPrice)], null, null, null, new DateOnly(2027, 4, 30), 7, "30 days", null, QuoteSource.Email,
        ProcurementTestDoubles.Now, []);
}
