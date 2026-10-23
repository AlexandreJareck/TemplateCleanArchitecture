using System.Text.Json;
using Shouldly;
using Template.Application.Common.Results;
using Template.Application.Features.Catalog;
using Template.Application.Features.Requisitions;
using Template.Application.Features.Sourcing;
using Template.Application.Interfaces.Integrations;
using Template.Application.Messaging.Procurement;
using Template.Application.Security;
using Template.Application.Wrappers;
using Template.Domain.Budgeting;
using Template.Domain.Requisitions;
using Template.Domain.Sourcing;

namespace Template.Unit.Tests.ApplicationTests.Features.Procurement;

/// <summary>
/// Sourcing use cases against port fakes: the buyer journey (SRC-001..019), permissions and waivers (SRC-006/013/017/018,
/// SRC-021), the fake BCB PTAX provider (SRC-013, CALC-004), BUD-009 reservation adjustment, requisition cascades
/// (REQ-007/008), Outbox staging (INT-004) and audit (AUD-001/003). All data is synthetic.
/// </summary>
public sealed class SourcingHandlerTests
{
    private const string Reason = "Documented sourcing justification";
    private readonly ProcurementTestDoubles doubles = new();
    private readonly ProcurementTestDoubles.Actor buyer = new();
    private readonly ProcurementTestDoubles.Actor manager = new();

    public SourcingHandlerTests()
    {
        doubles.Permissions.Grant(buyer.Id, SeededRoles.PermissionsByRole[SeededRoles.Buyer]);
        doubles.Permissions.Grant(manager.Id, SeededRoles.PermissionsByRole[SeededRoles.ProcurementManager]);
        doubles.Allocation.ChangeAmount(100_000m, "Synthetic budget increase for sourcing tests", null, ProcurementTestDoubles.Now);
    }

    [Fact]
    public async Task SC_01_BuyerJourney_CreatePublishQuoteCloseAward_WithAuditableSnapshotAndEvents()
    {
        var requisition = await SubmittedAsync("18", "1000"); // 18,000.00 BRL, above the threshold
        var suppliers = Enumerable.Range(0, 3).Select(index => doubles.Suppliers.AddActive($"Supplier {index}")).ToArray();
        doubles.ActAs(buyer);

        var created = await doubles.RfqCommands().Handle(new CreateRfqCommand(requisition.Id, null), default);
        created.IsSuccess.ShouldBeTrue(created.Error?.Message);
        created.Value.Status.ShouldBe("Draft");
        created.Value.AboveThreshold.ShouldBeTrue();
        created.Value.RequiredInvitations.ShouldBe(3);
        created.Value.Lines.Single().Quantity.ShouldBe("18");
        var rfq = doubles.Rfqs.Items.Single();
        foreach (var supplier in suppliers)
            (await doubles.RfqCommands().Handle(new InviteSupplierCommand(rfq.Id, rfq.Version, supplier.SupplierId), default)).IsSuccess.ShouldBeTrue();

        var published = await doubles.RfqCommands().Handle(new PublishRfqCommand(rfq.Id, rfq.Version, ProcurementTestDoubles.Now.AddDays(3), null), default);
        published.IsSuccess.ShouldBeTrue(published.Error?.Message);
        requisition.Status.ShouldBe(RequisitionStatus.InSourcing);
        requisition.ActiveRfqId.ShouldBe(rfq.Id);
        doubles.Outbox.Events.Last().EventType.ShouldBe("procurement.rfq.published");

        decimal[] prices = [950m, 900m, 980m];
        for (var index = 0; index < suppliers.Length; index++)
        {
            var entered = await doubles.QuoteCommands().Handle(new EnterQuoteCommand(rfq.Id, rfq.Version, suppliers[index].SupplierId, Quote(prices[index])), default);
            entered.IsSuccess.ShouldBeTrue(entered.Error?.Message);
        }

        doubles.Clock.UtcNow = ProcurementTestDoubles.Now.AddDays(3);
        var closed = await doubles.RfqCommands().Handle(new CloseRfqCommand(rfq.Id, rfq.Version, null), default);
        closed.IsSuccess.ShouldBeTrue(closed.Error?.Message);
        closed.Value.Comparison!.Status.ShouldBe("Complete");
        closed.Value.Comparison.Entries.Select(entry => entry.Rank).ShouldBe([1, 2, 3]);
        doubles.Rates.Calls.ShouldBe(0); // all BRL: no provider call
        var leading = closed.Value.Comparison.RecommendedQuoteRevisionId!.Value;
        closed.Value.Comparison.Entries[0].SupplierId.ShouldBe(suppliers[1].SupplierId);

        var awarded = await doubles.RfqCommands().Handle(new SubmitAwardCommand(rfq.Id, rfq.Version, leading, "Best total", null, null), default);
        awarded.IsSuccess.ShouldBeTrue(awarded.Error?.Message);
        awarded.Value.Status.ShouldBe("AwardPending");
        awarded.Value.Awards.Single().RequisitionCurrencyTotal!.Amount.ShouldBe("16200.00"); // 18 × 900
        requisition.Status.ShouldBe(RequisitionStatus.PendingApproval);
        requisition.AwardedSupplierId.ShouldBe(suppliers[1].SupplierId);
        var reservation = doubles.Allocation.Reservations.Single(item => item.IsActive);
        reservation.Amount.ShouldBe(16_200m); // BUD-009: decreased from 18,000.00
        doubles.Allocation.AvailableAmount.ShouldBe(100_000m - 16_200m);
        reservation.Changes.Last().Operation.ShouldBe(BudgetReservationOperation.Adjusted);
        doubles.Allocations.UpdateLocks.ShouldBeGreaterThan(0);
        doubles.Outbox.Events.TakeLast(2).Select(envelope => envelope.EventType)
            .ShouldBe(["procurement.award.submitted", "procurement.budget.reservation-adjusted"], ignoreOrder: true);
        doubles.Audit.Records.Select(record => record.Action).ShouldContain(SourcingAudit.ReservationAdjusted);
        doubles.Audit.Records.Where(record => record.ResourceType == SourcingAudit.Resource)
            .SelectMany(record => record.Details.Keys).ShouldNotContain("notes");
    }

    [Fact]
    public async Task T_SRC_001_a_OnlySubmittedRequisitions_WithoutAnActiveRfq_AreSourced()
    {
        var draft = await DraftAsync();
        doubles.ActAs(buyer);
        (await doubles.RfqCommands().Handle(new CreateRfqCommand(draft.Id, null), default)).Error!.Code.ShouldBe(SourcingErrors.RequisitionNotEligibleCode);

        doubles.ActAs(doubles.Requester);
        var submitted = await SubmittedAsync();
        doubles.ActAs(buyer);
        (await doubles.RfqCommands().Handle(new CreateRfqCommand(submitted.Id, "Competitive"), default)).IsSuccess.ShouldBeTrue();
        (await doubles.RfqCommands().Handle(new CreateRfqCommand(submitted.Id, null), default)).Error!.Code.ShouldBe(SourcingErrors.AlreadyActiveCode);
        (await doubles.RfqCommands().Handle(new CreateRfqCommand(submitted.Id, "Auction"), default)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        doubles.UnitOfWork.UniqueViolation = "IX_SourcingRfqs_RequisitionId_Active";
        doubles.Rfqs.Items.Clear();
        (await doubles.RfqCommands().Handle(new CreateRfqCommand(submitted.Id, null), default)).Error!.Code.ShouldBe(SourcingErrors.AlreadyActiveCode);
    }

    [Fact]
    public async Task T_SRC_021_a_RequesterCannotReadRfqs_AndAuditorSeesNoQuoteEntryActions()
    {
        var (rfq, _) = await PublishedAsync(1);
        doubles.ActAs(doubles.Requester);
        (await doubles.RfqQueries().Handle(new GetRfqQuery(rfq.Id), default)).Error!.Code.ShouldBe(ProblemCodes.Forbidden);
        (await doubles.RfqQueries().Handle(new ListRfqsQuery(), default)).Error!.Code.ShouldBe(ProblemCodes.Forbidden);

        var auditor = new ProcurementTestDoubles.Actor();
        doubles.Permissions.Grant(auditor.Id, SeededRoles.PermissionsByRole[SeededRoles.Auditor]);
        doubles.ActAs(auditor);
        var view = (await doubles.RfqQueries().Handle(new GetRfqQuery(rfq.Id), default)).Value;
        view.CommercialDetailsVisible.ShouldBeTrue();
        view.AllowedActions.ShouldBeEmpty();

        var limited = new ProcurementTestDoubles.Actor();
        doubles.Permissions.Grant(limited.Id, [Permissions.SourcingRfqsManage]);
        doubles.ActAs(limited);
        var noCommercial = (await doubles.RfqQueries().Handle(new GetRfqQuery(rfq.Id), default)).Value;
        noCommercial.CommercialDetailsVisible.ShouldBeFalse();
        noCommercial.Quotes.ShouldBeNull();
        noCommercial.Comparison.ShouldBeNull();
    }

    [Fact]
    public async Task T_SRC_006_b_BuyerCannotUseTheDirectPurchaseWaiver_ButTheManagerCan()
    {
        var requisition = await SubmittedAsync("18", "1000");
        doubles.ActAs(buyer);
        var supplier = doubles.Suppliers.AddActive("Sole source");
        await doubles.RfqCommands().Handle(new CreateRfqCommand(requisition.Id, "Direct"), default);
        var rfq = doubles.Rfqs.Items.Single();
        await doubles.RfqCommands().Handle(new InviteSupplierCommand(rfq.Id, rfq.Version, supplier.SupplierId), default);

        var denied = await doubles.RfqCommands().Handle(new PublishRfqCommand(rfq.Id, rfq.Version, ProcurementTestDoubles.Now.AddDays(2), Reason), default);
        denied.Error!.Code.ShouldBe(SourcingErrors.WaiverPermissionRequiredCode);
        denied.Error.Category.ShouldBe(ErrorCategory.Forbidden);
        denied.Error.Extensions!["permission"].ShouldBe(Permissions.SourcingWaiversDirectPurchase);
        rfq.Status.ShouldBe(RfqStatus.Draft);

        doubles.ActAs(manager);
        var published = await doubles.RfqCommands().Handle(new PublishRfqCommand(rfq.Id, rfq.Version, ProcurementTestDoubles.Now.AddDays(2), Reason), default);
        published.IsSuccess.ShouldBeTrue(published.Error?.Message);
        published.Value.Waivers.Single().Type.ShouldBe("DirectPurchase");
        var waiverAudit = doubles.Audit.Records.Single(record => record.Action == SourcingAudit.WaiverRecorded);
        waiverAudit.WaiverId.ShouldBe(rfq.Waivers.Single().Id);
        waiverAudit.Details["permission"].ShouldBe(Permissions.SourcingWaiversDirectPurchase);
    }

    [Fact]
    public async Task T_SRC_006_c_LowValueDirectPurchase_ByTheBuyer()
    {
        var requisition = await SubmittedAsync("12", "100"); // 1,200.00 BRL
        doubles.ActAs(buyer);
        var supplier = doubles.Suppliers.AddActive("Sole source");
        await doubles.RfqCommands().Handle(new CreateRfqCommand(requisition.Id, "Direct"), default);
        var rfq = doubles.Rfqs.Items.Single();
        await doubles.RfqCommands().Handle(new InviteSupplierCommand(rfq.Id, rfq.Version, supplier.SupplierId), default);

        var published = await doubles.RfqCommands().Handle(new PublishRfqCommand(rfq.Id, rfq.Version, ProcurementTestDoubles.Now.AddDays(2), Reason), default);
        published.IsSuccess.ShouldBeTrue(published.Error?.Message);
        published.Value.Waivers.Single().Type.ShouldBe("LowValueDirectPurchase");
        published.Value.RequiredPublishWaiver.ShouldBe("LowValueDirectPurchase");
    }

    [Fact]
    public async Task T_SRC_012_EarlyClose_RequiresTheCloseEarlyPermission_AndAReason()
    {
        var (rfq, _) = await PublishedAsync(1);
        var noEarly = new ProcurementTestDoubles.Actor();
        doubles.Permissions.Grant(noEarly.Id, [Permissions.SourcingRfqsManage]);
        doubles.ActAs(noEarly);
        (await doubles.RfqCommands().Handle(new CloseRfqCommand(rfq.Id, rfq.Version, Reason), default)).Error!.Code.ShouldBe(ProblemCodes.Forbidden);

        doubles.ActAs(buyer);
        (await doubles.RfqCommands().Handle(new CloseRfqCommand(rfq.Id, rfq.Version, null), default)).Error!.Code.ShouldBe(DomainError_ReasonRequired);
        var closed = await doubles.RfqCommands().Handle(new CloseRfqCommand(rfq.Id, rfq.Version, Reason), default);
        closed.IsSuccess.ShouldBeTrue(closed.Error?.Message);
        closed.Value.ClosedEarly.ShouldBeTrue();
    }

    [Fact]
    public async Task T_SRC_012_b_SystemCloseAtTheDeadline_IsIdempotent()
    {
        var (rfq, _) = await PublishedAsync(1);
        doubles.ActAsSystem();

        (await doubles.RfqCommands().Handle(new CloseDueRfqsCommand(), default)).Value.ShouldBe(0); // not due yet
        doubles.Clock.UtcNow = rfq.DeadlineUtc!.Value;
        (await doubles.RfqCommands().Handle(new CloseDueRfqsCommand(), default)).Value.ShouldBe(1);
        rfq.Status.ShouldBe(RfqStatus.Closed);
        rfq.History.Last().ActorId.ShouldBeNull();
        (await doubles.RfqCommands().Handle(new CloseDueRfqCommand(rfq.Id), default)).Value.ShouldBeFalse();
        doubles.Outbox.Events.Count(envelope => envelope.EventType == "procurement.rfq.closed").ShouldBe(1);
    }

    [Fact]
    public async Task T_SRC_013_a_FakeBcbDown_MakesTheComparisonIncomplete_ThenAManualRateCompletesIt()
    {
        var (rfq, suppliers) = await PublishedAsync(1);
        await EnterAsync(rfq, suppliers[0], 20m, "USD");
        doubles.Rates.Failure = EnrichmentOutcome.Unavailable;
        doubles.Clock.UtcNow = rfq.DeadlineUtc!.Value;

        var closed = await doubles.RfqCommands().Handle(new CloseRfqCommand(rfq.Id, rfq.Version, null), default);
        closed.Value.Comparison!.Status.ShouldBe("Incomplete");
        closed.Value.Comparison.MissingRateCurrencies.ShouldBe(["USD"]);
        closed.Value.AllowedActions.ShouldNotContain(SourcingAudit.ActionAward);
        doubles.Rates.Calls.ShouldBe(1);
        rfq.ExchangeRates.ShouldBeEmpty(); // never guessed

        (await doubles.RfqCommands().Handle(new RecordManualExchangeRateCommand(rfq.Id, rfq.Version, "USD", "5.4321", ProcurementTestDoubles.Today, Reason), default))
            .Error!.Code.ShouldBe(SourcingErrors.WaiverPermissionRequiredCode);
        doubles.ActAs(manager);
        var manual = await doubles.RfqCommands().Handle(
            new RecordManualExchangeRateCommand(rfq.Id, rfq.Version, "USD", "5.4321", ProcurementTestDoubles.Today, Reason), default);
        manual.IsSuccess.ShouldBeTrue(manual.Error?.Message);
        manual.Value.Comparison!.Status.ShouldBe("Complete");
        manual.Value.Comparison.Sequence.ShouldBe(2);
        manual.Value.ExchangeRates.Single().Source.ShouldBe("Manual");
        doubles.Audit.Records.Single(record => record.Action == SourcingAudit.RateRecorded).Details["rate"].ShouldBe("5.4321");
    }

    [Theory]
    [InlineData(EnrichmentOutcome.NotFound)]
    [InlineData(EnrichmentOutcome.Invalid)]
    [InlineData(EnrichmentOutcome.Rejected)]
    public async Task T_SRC_013_EveryProviderFailure_IsAnExplicitIncompleteComparison(EnrichmentOutcome outcome)
    {
        var (rfq, suppliers) = await PublishedAsync(1);
        await EnterAsync(rfq, suppliers[0], 20m, "EUR");
        doubles.Rates.Failure = outcome;
        doubles.Clock.UtcNow = rfq.DeadlineUtc!.Value;

        (await doubles.RfqCommands().Handle(new CloseRfqCommand(rfq.Id, rfq.Version, null), default)).Value.Comparison!.Status.ShouldBe("Incomplete");
    }

    [Fact]
    public async Task T_CALC_004_c_FakeBcbRate_IsPersisted_WithProvenance_AndUsedForTheAward()
    {
        var (rfq, suppliers) = await PublishedAsync(1);
        await EnterAsync(rfq, suppliers[0], 20m, "USD");
        doubles.Rates.SellRates["USD"] = 5.4321m;
        doubles.Clock.UtcNow = rfq.DeadlineUtc!.Value;

        var closed = (await doubles.RfqCommands().Handle(new CloseRfqCommand(rfq.Id, rfq.Version, null), default)).Value;
        var rate = closed.ExchangeRates.Single();
        (rate.Source, rate.Provider, rate.Rate, rate.FromCurrency, rate.ToCurrency).ShouldBe(("BcbPtax", "BcbPtax", "5.4321", "USD", "BRL"));
        doubles.Rates.LastToDate.ShouldBe(new DateOnly(2027, 3, 4)); // PTAX for the close local date (SP-5)
        var entry = closed.Comparison!.Entries.Single();
        entry.RawTotal.Amount.ShouldBe("360.00"); // 18 × 20.00 USD
        entry.NormalizedTotal!.Amount.ShouldBe("1955.56"); // round(360.00 × 5.4321) = round(1955.556)

        var awarded = await doubles.RfqCommands().Handle(new SubmitAwardCommand(rfq.Id, rfq.Version, entry.QuoteRevisionId, null, null, null), default);
        awarded.IsSuccess.ShouldBeTrue(awarded.Error?.Message);
        doubles.Allocation.Reservations.Single(item => item.IsActive).Amount.ShouldBe(1955.56m); // BUD-009 increase from 100.00
        doubles.Rates.Calls.ShouldBe(1); // the award reuses the snapshot, never a live rate
    }

    [Fact]
    public async Task T_BUD_009_a_AnAwardAboveAvailability_FailsWithBudgetInsufficient_AndChangesNothing()
    {
        var (rfq, suppliers) = await PublishedAsync(1); // reservation 100.00 BRL
        await EnterAsync(rfq, suppliers[0], 200_000m);
        doubles.Clock.UtcNow = rfq.DeadlineUtc!.Value;
        await doubles.RfqCommands().Handle(new CloseRfqCommand(rfq.Id, rfq.Version, null), default);
        var version = rfq.Version;

        var result = await doubles.RfqCommands().Handle(
            new SubmitAwardCommand(rfq.Id, rfq.Version, rfq.CurrentComparison!.RecommendedQuoteRevisionId!.Value, null, null, null), default);

        result.Error!.Code.ShouldBe(ProblemCodes.BudgetInsufficient);
        result.Error.RuleId.ShouldBe("BUD-009");
        doubles.UnitOfWork.Rollbacks.ShouldBeGreaterThan(0);
        doubles.Allocation.Reservations.Single(item => item.IsActive).Amount.ShouldBe(100m);
    }

    [Fact]
    public async Task T_SRC_017_b_InsufficientQuotations_NeedsTheManagerWaiver()
    {
        var requisition = await SubmittedAsync("18", "1000");
        var (rfq, suppliers) = await PublishAsync(requisition, 3);
        await EnterAsync(rfq, suppliers[0], 900m);
        await EnterAsync(rfq, suppliers[1], 950m);
        doubles.Clock.UtcNow = rfq.DeadlineUtc!.Value;
        await doubles.RfqCommands().Handle(new CloseRfqCommand(rfq.Id, rfq.Version, null), default);
        var leading = rfq.CurrentComparison!.RecommendedQuoteRevisionId!.Value;

        (await doubles.RfqCommands().Handle(new SubmitAwardCommand(rfq.Id, rfq.Version, leading, null, null, null), default))
            .Error!.Code.ShouldBe(SourcingErrors.AwardInsufficientQuotationsCode);
        (await doubles.RfqCommands().Handle(new SubmitAwardCommand(rfq.Id, rfq.Version, leading, null, Reason, null), default))
            .Error!.Code.ShouldBe(SourcingErrors.WaiverPermissionRequiredCode);
        doubles.ActAs(manager);
        var awarded = await doubles.RfqCommands().Handle(new SubmitAwardCommand(rfq.Id, rfq.Version, leading, null, Reason, null), default);
        awarded.IsSuccess.ShouldBeTrue(awarded.Error?.Message);
        awarded.Value.Waivers.Single().Type.ShouldBe("InsufficientQuotations");
    }

    [Fact]
    public async Task T_REQ_008_b_CancellingTheRequisition_CancelsItsRfq_AndPendingAward_InTheSameCommit()
    {
        var (rfq, suppliers) = await PublishedAsync(1);
        await EnterAsync(rfq, suppliers[0], 90m);
        doubles.Clock.UtcNow = rfq.DeadlineUtc!.Value;
        await doubles.RfqCommands().Handle(new CloseRfqCommand(rfq.Id, rfq.Version, null), default);
        await doubles.RfqCommands().Handle(new SubmitAwardCommand(rfq.Id, rfq.Version, rfq.CurrentComparison!.RecommendedQuoteRevisionId!.Value, null, null, null), default);
        var requisition = doubles.Requisitions.Items.Single();
        requisition.Status.ShouldBe(RequisitionStatus.PendingApproval);

        doubles.ActAs(manager);
        var cancelled = await doubles.Transitions().Handle(new CancelRequisitionCommand(requisition.Id, requisition.Version, Reason), default);
        cancelled.IsSuccess.ShouldBeTrue(cancelled.Error?.Message);
        rfq.Status.ShouldBe(RfqStatus.Cancelled);
        rfq.Awards.Single().Status.ShouldBe(AwardStatus.Cancelled);
        doubles.Allocation.Reservations.Single().Status.ShouldBe(BudgetReservationStatus.Released);
        doubles.Outbox.Events.Select(envelope => envelope.EventType).ShouldContain("procurement.rfq.cancelled");
    }

    [Fact]
    public async Task T_REQ_007_WithdrawingTheRequisition_CancelsItsDraftRfq()
    {
        var requisition = await SubmittedAsync();
        doubles.ActAs(buyer);
        await doubles.RfqCommands().Handle(new CreateRfqCommand(requisition.Id, null), default);
        doubles.ActAs(doubles.Requester);

        (await doubles.Transitions().Handle(new WithdrawRequisitionCommand(requisition.Id, requisition.Version), default)).IsSuccess.ShouldBeTrue();
        doubles.Rfqs.Items.Single().Status.ShouldBe(RfqStatus.Cancelled);
        doubles.Rfqs.Items.Single().CancellationReasonCode.ShouldBe("RequisitionWithdrawn");
    }

    [Fact]
    public async Task T_SRC_020_b_CancellingAPublishedRfq_ReturnsTheRequisitionToSubmitted()
    {
        var (rfq, _) = await PublishedAsync(1);
        var requisition = doubles.Requisitions.Items.Single();

        var cancelled = await doubles.RfqCommands().Handle(new CancelRfqCommand(rfq.Id, rfq.Version, Reason), default);
        cancelled.IsSuccess.ShouldBeTrue(cancelled.Error?.Message);
        requisition.Status.ShouldBe(RequisitionStatus.Submitted);
        doubles.Allocation.Reservations.Single().Status.ShouldBe(BudgetReservationStatus.Reserved);
        (await doubles.RfqCommands().Handle(new CreateRfqCommand(requisition.Id, null), default)).Value.RoundNumber.ShouldBe(2);
    }

    [Fact]
    public async Task Concurrency_StaleVersion_IsPreconditionFailed_AndMissingVersionIsRequired()
    {
        var (rfq, suppliers) = await PublishedAsync(1);

        (await doubles.QuoteCommands().Handle(new EnterQuoteCommand(rfq.Id, rfq.Version - 1, suppliers[0].SupplierId, Quote(10m)), default))
            .Error!.Category.ShouldBe(ErrorCategory.PreconditionFailed);
        (await doubles.QuoteCommands().Handle(new EnterQuoteCommand(rfq.Id, null, suppliers[0].SupplierId, Quote(10m)), default))
            .Error!.Category.ShouldBe(ErrorCategory.PreconditionRequired);
        doubles.UnitOfWork.ThrowConflict = true;
        (await doubles.QuoteCommands().Handle(new EnterQuoteCommand(rfq.Id, rfq.Version, suppliers[0].SupplierId, Quote(10m)), default))
            .Error!.Code.ShouldBe(ProblemCodes.VersionMismatch);
    }

    [Fact]
    public async Task QuoteParsing_RejectsMalformedDecimals_AndNonUtcReceipt()
    {
        var (rfq, suppliers) = await PublishedAsync(1);
        var quote = Quote(10m) with { Lines = [new QuoteLineRequest(1, "1,5", "10")] };

        (await doubles.QuoteCommands().Handle(new EnterQuoteCommand(rfq.Id, rfq.Version, suppliers[0].SupplierId, quote), default))
            .Error!.FieldErrors!.Keys.ShouldContain("lines[1].quantityOffered");
        (await doubles.QuoteCommands().Handle(new EnterQuoteCommand(rfq.Id, rfq.Version, suppliers[0].SupplierId,
            Quote(10m) with { ReceivedAt = DateTime.SpecifyKind(ProcurementTestDoubles.Now, DateTimeKind.Unspecified) }), default))
            .Error!.FieldErrors!.Keys.ShouldContain("receivedAt");
        new QuoteRequestValidator().Validate(Quote(10m) with { PaymentTerms = null }).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void T_SRC_022_a_NotificationPlanner_QueuesOneRequestPerInvitedSupplier()
    {
        var suppliers = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var published = ProcurementIntegrationEventContract.FromDomainEvent(
            new RfqPublished(ProcurementTestDoubles.Now, Guid.NewGuid(), "RFQ-2027-000001", Guid.NewGuid(), ProcurementTestDoubles.Now.AddDays(3), suppliers,
                SourcingMethod.Competitive), "trace");

        var requests = RfqNotificationPlanner.Plan(published);
        requests.Select(request => request.RecipientId).ShouldBe(suppliers.OrderBy(id => id));
        requests.ShouldAllBe(request => request.Template == RfqNotificationPlanner.InvitationTemplate && request.RecipientType == "Supplier");
        published.Payload.GetRawText().ShouldNotContain("@");
        var reminder = ProcurementIntegrationEventContract.FromDomainEvent(
            new RfqReminderRequested(ProcurementTestDoubles.Now, Guid.NewGuid(), ProcurementTestDoubles.Now.AddDays(1), [suppliers[0]]), "trace");
        RfqNotificationPlanner.Plan(reminder).Single().Template.ShouldBe(RfqNotificationPlanner.ReminderTemplate);
        var closed = ProcurementIntegrationEventContract.FromDomainEvent(
            new RfqClosed(ProcurementTestDoubles.Now, Guid.NewGuid(), ProcurementTestDoubles.Now, 1, ComparisonStatus.Complete), "trace");
        Should.Throw<JsonException>(() => RfqNotificationPlanner.Plan(closed));
    }

    [Fact]
    public void T_BUD_009_a_AdjustmentUpAndDown_IsIdempotentPerAward()
    {
        var period = doubles.Period;
        var allocation = doubles.Allocation;
        var requisitionId = Guid.NewGuid();
        allocation.Reserve(Guid.NewGuid(), requisitionId, 1, new Template.Domain.Common.Money(1000m, "BRL"), period, ProcurementTestDoubles.Today, null,
            ProcurementTestDoubles.Now, "trace");
        var available = allocation.AvailableAmount;
        var award = Guid.NewGuid();

        allocation.AdjustForAward(requisitionId, new Template.Domain.Common.Money(800m, "BRL"), award, null, ProcurementTestDoubles.Now, "trace").Changed.ShouldBeTrue();
        allocation.AvailableAmount.ShouldBe(available + 200m);
        allocation.AdjustForAward(requisitionId, new Template.Domain.Common.Money(800m, "BRL"), award, null, ProcurementTestDoubles.Now, "trace").Changed.ShouldBeFalse();
        allocation.AdjustForAward(requisitionId, new Template.Domain.Common.Money(1200m, "BRL"), Guid.NewGuid(), null, ProcurementTestDoubles.Now, "trace").Changed.ShouldBeTrue();
        allocation.AvailableAmount.ShouldBe(available - 200m);
        allocation.AdjustForAward(requisitionId, new Template.Domain.Common.Money(500_000m, "BRL"), Guid.NewGuid(), null, ProcurementTestDoubles.Now, "trace")
            .Error!.Code.ShouldBe(BudgetErrors.InsufficientCode);
        allocation.Reservations.Single(item => item.RequisitionId == requisitionId).Changes.Select(change => change.Amount).ShouldBe([1000m, 800m, 1200m]);
        allocation.DomainEvents.OfType<BudgetReservationAdjusted>().Count().ShouldBe(2);
    }

    private const string DomainError_ReasonRequired = "reason-required";

    private async Task<(RequestForQuotation Rfq, Template.Domain.Sourcing.SupplierSnapshot[] Suppliers)> PublishedAsync(int invitations)
    {
        var requisition = await SubmittedAsync();
        return await PublishAsync(requisition, invitations);
    }

    private async Task<(RequestForQuotation Rfq, Template.Domain.Sourcing.SupplierSnapshot[] Suppliers)> PublishAsync(PurchaseRequisition requisition, int invitations)
    {
        doubles.ActAs(buyer);
        (await doubles.RfqCommands().Handle(new CreateRfqCommand(requisition.Id, null), default)).IsSuccess.ShouldBeTrue();
        var rfq = doubles.Rfqs.Items.Single(item => item.RequisitionId == requisition.Id && item.Status == RfqStatus.Draft);
        var suppliers = Enumerable.Range(0, invitations).Select(index => doubles.Suppliers.AddActive($"Invited {index}")).ToArray();
        foreach (var supplier in suppliers)
            (await doubles.RfqCommands().Handle(new InviteSupplierCommand(rfq.Id, rfq.Version, supplier.SupplierId), default)).IsSuccess.ShouldBeTrue();
        var published = await doubles.RfqCommands().Handle(new PublishRfqCommand(rfq.Id, rfq.Version, ProcurementTestDoubles.Now.AddDays(3), null), default);
        published.IsSuccess.ShouldBeTrue(published.Error?.Message);
        return (rfq, suppliers);
    }

    private async Task EnterAsync(RequestForQuotation rfq, Template.Domain.Sourcing.SupplierSnapshot supplier, decimal unitPrice, string currency = "BRL")
    {
        var result = await doubles.QuoteCommands().Handle(new EnterQuoteCommand(rfq.Id, rfq.Version, supplier.SupplierId, Quote(unitPrice, currency, rfq.Lines.Count)), default);
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
    }

    private static QuoteRequest Quote(decimal unitPrice, string currency = "BRL", int lines = 1) => new(
        currency,
        Enumerable.Range(1, lines).Select(number => new QuoteLineRequest(number, "18", unitPrice.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray(),
        null, null, "0", new DateOnly(2027, 4, 30), 7, "30 days", "Synthetic quote", QuoteSource.Email, ProcurementTestDoubles.Now, []);

    private async Task<PurchaseRequisition> DraftAsync(string quantity = "1", string price = "100")
    {
        doubles.ActAs(doubles.Requester);
        var result = await doubles.Drafts().Handle(
            new CreateRequisitionDraftCommand(doubles.CostCenter.Id, "BRL", "Replacement laptops for the support team", ProcurementTestDoubles.Today.AddDays(10)), default);
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        var draft = doubles.Requisitions.Items.Single(item => item.Id == result.Value.Id);
        (await doubles.Drafts().Handle(new AddRequisitionLineCommand(draft.Id, draft.Version,
            new RequisitionLineRequest(null, "Laptop 14 inch", RequisitionLineKind.Good, doubles.Category.Id, doubles.Unit.Id, quantity, new MoneyInput(price, "BRL"))), default))
            .IsSuccess.ShouldBeTrue();
        return draft;
    }

    private async Task<PurchaseRequisition> SubmittedAsync(string quantity = "1", string price = "100")
    {
        var draft = await DraftAsync(quantity, price);
        var result = await doubles.Transitions().Handle(new SubmitRequisitionCommand(draft.Id, draft.Version), default);
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        return draft;
    }
}
