using Shouldly;
using Template.Domain.Budgeting;
using Template.Domain.Common;
using Template.Domain.Requisitions;
using Template.Domain.Sourcing;
using static Template.Unit.Tests.DomainTests.Sourcing.RequestForQuotationTests;

namespace Template.Unit.Tests.DomainTests.Sourcing;

/// <summary>Guard and boundary paths of the Sourcing aggregate, CALC-004/007 and the Phase 29 requisition/budget transitions.</summary>
public sealed class SourcingEdgeCaseTests
{
    private const string Reason = "Documented sourcing justification";
    private const string Correlation = "trace-sourcing-edges";
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public void Create_RejectsAnUndefinedMethod_AndAnEstimateRateOfAnotherCurrency()
    {
        RequestForQuotation.Create(Guid.NewGuid(), "RFQ-1", 1, (SourcingMethod)9, Snapshot(100m), "BRL", null, SourcingPolicy.V1, Actor, Now, Correlation)
            .Error!.Field.ShouldBe("method");
        Should.Throw<ArgumentException>(() => RequestForQuotation.Create(Guid.NewGuid(), "RFQ-1", 1, SourcingMethod.Competitive, Snapshot(100m) with { Currency = "USD" },
            "BRL", new ExchangeRateInput("EUR", 5m, ExchangeRateSource.BcbPtax, DeadlineDate, Now, "BcbPtax"), SourcingPolicy.V1, Actor, Now, Correlation));
        var rfq = NewRfq();
        (rfq.IsActive, rfq.IsTerminal).ShouldBe((true, false));
        rfq.Cancel(Reason, Actor, Now, Correlation);
        (rfq.IsActive, rfq.IsTerminal).ShouldBe((false, true));
    }

    [Fact]
    public void Invitations_AreBoundedByState_Count_AndExistence()
    {
        var rfq = NewRfq(estimate: 100m);
        rfq.RemoveInvitation(Guid.NewGuid(), Actor, Now, Correlation)!.Code.ShouldBe("not-found");
        for (var index = 0; index < RequestForQuotation.MaxInvitations; index++)
            rfq.Invite(Supplier($"Supplier {index:D2}"), Actor, Now, Correlation).Error.ShouldBeNull();
        rfq.Invite(Supplier("One too many"), Actor, Now, Correlation).Error!.Field.ShouldBe("supplierId");

        rfq.Publish(Deadline, rfq.ActiveInvitations.ToDictionary(invitation => invitation.SupplierId, _ => true), null, SourcingPolicy.V1, Actor, Now, Correlation);
        var closed = rfq.Close(Reason, rfq.ActiveInvitations.ToDictionary(invitation => invitation.SupplierId, _ => true), [], DeadlineDate, SourcingPolicy.V1, Actor,
            Now.AddHours(1), Correlation).Comparison!;
        closed.Warnings.Length.ShouldBe(RfqComparison.WarningsMaxLength); // 50 supplier-without-quote warnings are truncated
        rfq.Invite(Supplier("After close"), Actor, Now.AddHours(2), Correlation).Error!.Code.ShouldBe(DomainError.InvalidTransitionCode);
    }

    [Fact]
    public void DirectPublish_WithoutInvitation_IsRejected()
    {
        var rfq = NewRfq(estimate: 100m, method: SourcingMethod.Direct);

        rfq.Publish(Deadline, new Dictionary<Guid, bool>(), new WaiverRequest(WaiverType.LowValueDirectPurchase, "sourcing.waivers.low-value", Reason, Actor),
            SourcingPolicy.V1, Actor, Now, Correlation).Error!.Code.ShouldBe(SourcingErrors.DirectSingleSupplierCode);
    }

    [Fact]
    public void DraftRfq_RejectsExtensionReminderQuotesRatesAndAwards()
    {
        var rfq = NewRfq();

        rfq.ExtendDeadline(Deadline, Reason, SourcingPolicy.V1, Actor, Now, Correlation)!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        rfq.RequestReminder(Actor, Now, Correlation).Error!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        rfq.EnterQuote(Guid.NewGuid(), Quote(), DeadlineDate, Actor, Now, Correlation).Error!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        rfq.WithdrawQuote(Guid.NewGuid(), Reason, Actor, Now, Correlation)!.Code.ShouldBe(SourcingErrors.QuoteImmutableCode);
        rfq.RecordManualRate("USD", 5m, DeadlineDate, null, SourcingPolicy.V1, Now, Correlation).Error!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        rfq.EvaluateAward(Guid.NewGuid(), new Dictionary<Guid, bool>(), DeadlineDate, SourcingPolicy.V1).Error!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        rfq.Close(null, new Dictionary<Guid, bool>(), [], DeadlineDate, SourcingPolicy.V1, Actor, Now, Correlation).Error!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        rfq.CancelForRequisition("RequisitionWithdrawn", null, Now, Correlation).ShouldBeTrue();
        rfq.CancelForRequisition("RequisitionWithdrawn", null, Now, Correlation).ShouldBeFalse();
    }

    /// <summary>
    /// T-SRC-008-c (D33-11): a received instant slightly ahead of the server clock (client clock skew) is recorded as
    /// the server's now; beyond the tolerance it stays a validation error. Found by the Phase 33 real-stack journey.
    /// </summary>
    [Fact]
    public void T_SRC_008_c_ReceivedInstant_WithinClockSkew_IsRecordedAsNow_AndBeyondItIsRejected()
    {
        var (rfq, suppliers) = Published(3);
        var at = Now.AddHours(1);

        var skewed = rfq.EnterQuote(suppliers[0].SupplierId, Quote(receivedAt: at.AddSeconds(30)), DeadlineDate, Actor, at, Correlation).Revision!;
        skewed.ReceivedAtUtc.ShouldBe(at);

        var revised = rfq.ReviseQuote(skewed.QuoteId, Quote(receivedAt: at.Add(RequestForQuotation.ClockSkewTolerance)), DeadlineDate, Actor, at, Correlation).Revision!;
        revised.ReceivedAtUtc.ShouldBe(at);

        rfq.EnterQuote(suppliers[1].SupplierId, Quote(receivedAt: at.Add(RequestForQuotation.ClockSkewTolerance).AddTicks(1)), DeadlineDate, Actor, at, Correlation)
            .Error!.Field.ShouldBe("receivedAt");
    }

    [Fact]
    public void QuoteValidation_CoversEveryInputGuard_AndAttachmentsAreStoredAsMetadata()
    {
        var (rfq, suppliers) = Published(3);
        var supplier = suppliers[0].SupplierId;
        var at = Now.AddHours(1);

        rfq.EnterQuote(supplier, Quote() with { Lines = [] }, DeadlineDate, Actor, at, Correlation).Error!.Field.ShouldBe("lines");
        rfq.EnterQuote(supplier, Quote() with { Notes = new string('n', QuoteRevision.NotesMaxLength + 1) }, DeadlineDate, Actor, at, Correlation).Error!.Field.ShouldBe("notes");
        rfq.EnterQuote(supplier, Quote() with { Source = (QuoteSource)9 }, DeadlineDate, Actor, at, Correlation).Error!.Field.ShouldBe("source");
        rfq.EnterQuote(supplier, Quote(attachments: Enumerable.Range(0, 11).Select(_ => new QuoteAttachmentInput("a.pdf", "application/pdf", 10)).ToArray()),
            DeadlineDate, Actor, at, Correlation).Error!.Code.ShouldBe("file.limit-reached");
        rfq.EnterQuote(supplier, Quote(attachments: [new QuoteAttachmentInput("  ", "application/pdf", 10)]), DeadlineDate, Actor, at, Correlation)
            .Error!.Field.ShouldBe("attachments.fileName");
        rfq.EnterQuote(supplier, Quote(attachments: [new QuoteAttachmentInput("a.pdf", "application/pdf", 0)]), DeadlineDate, Actor, at, Correlation)
            .Error!.Field.ShouldBe("attachments.length");
        rfq.EnterQuote(supplier, Quote(attachments: [new QuoteAttachmentInput("a.pdf", "application/pdf", 11L * 1024 * 1024)]), DeadlineDate, Actor, at, Correlation)
            .Error!.Kind.ShouldBe(DomainErrorKind.TooLarge);
        rfq.EnterQuote(supplier, Quote(freight: 0m, discount: 1600m), DeadlineDate, Actor, at, Correlation).Error!.Field.ShouldBe("discount");

        var revision = rfq.EnterQuote(supplier, Quote(attachments: [new QuoteAttachmentInput(@"C:\quotes\proposal<1>.pdf", "Application/PDF", 2048)]),
            DeadlineDate, Actor, at, Correlation).Revision!;
        var attachment = revision.Attachments.Single();
        (attachment.FileName, attachment.MediaType, attachment.Length, attachment.AddedBy, attachment.RevisionId)
            .ShouldBe(("proposal_1_.pdf", "application/pdf", 2048L, (Guid?)Actor, revision.Id));
        attachment.Id.ShouldNotBe(Guid.Empty);
        attachment.AddedAtUtc.ShouldBe(at);

        rfq.ReviseQuote(Guid.NewGuid(), Quote(), DeadlineDate, Actor, at, Correlation).Error!.Code.ShouldBe("not-found");
        rfq.ReviseQuote(revision.QuoteId, Quote(currency: "XX"), DeadlineDate, Actor, at, Correlation).Error!.Field.ShouldBe("currency");
        rfq.ReviseQuote(revision.QuoteId, Quote(discount: 1600m), DeadlineDate, Actor, at, Correlation).Error!.Field.ShouldBe("discount");
        rfq.WithdrawQuote(Guid.NewGuid(), Reason, Actor, at, Correlation)!.Code.ShouldBe("not-found");
    }

    [Fact]
    public void Close_RejectsAnInvalidProvidedRate_AndAMissingRequisitionCurrencyRateMakesTheComparisonIncomplete()
    {
        var (rfq, suppliers) = Published(3);
        foreach (var supplier in suppliers)
            rfq.EnterQuote(supplier.SupplierId, Quote(), DeadlineDate, Actor, Now.AddHours(1), Correlation);
        Should.Throw<ArgumentException>(() => rfq.Close(null, AllActive(rfq), [new ExchangeRateInput("USD", 0m, ExchangeRateSource.BcbPtax, DeadlineDate, Now, "BcbPtax")],
            DeadlineDate, SourcingPolicy.V1, Actor, Deadline, Correlation));

        var usdRequisition = RequestForQuotation.Create(Guid.NewGuid(), "RFQ-USD", 1, SourcingMethod.Competitive, Snapshot(100m) with { Currency = "USD" }, "BRL",
            new ExchangeRateInput("USD", 5m, ExchangeRateSource.BcbPtax, DeadlineDate, Now, "BcbPtax"), SourcingPolicy.V1, Actor, Now, Correlation).Rfq!;
        var only = Supplier("Only supplier");
        usdRequisition.Invite(only, Actor, Now, Correlation);
        usdRequisition.Publish(Deadline, new Dictionary<Guid, bool> { [only.SupplierId] = true }, null, SourcingPolicy.V1, Actor, Now, Correlation).Error.ShouldBeNull();
        usdRequisition.EnterQuote(only.SupplierId, Quote(), DeadlineDate, Actor, Now.AddHours(1), Correlation);
        var comparison = usdRequisition.Close(null, AllActive(usdRequisition), [], DeadlineDate, SourcingPolicy.V1, Actor, Deadline, Correlation).Comparison!;
        comparison.Status.ShouldBe(ComparisonStatus.Incomplete);
        comparison.MissingRateCurrencies.ShouldBe(["USD"]); // CALC-007 needs the requisition-currency rate
    }

    [Fact]
    public void Award_RejectsALongJustification_AndAShortWaiverReasonLeavesNoWaiver()
    {
        var rfq = ClosedWith((1000m, 5, Coverage.Full), (1100m, 5, Coverage.Full), (1200m, 5, Coverage.Full));
        var second = rfq.CurrentComparison!.Entries.Single(entry => entry.Rank == 2).QuoteRevisionId;
        var leading = rfq.CurrentComparison.RecommendedQuoteRevisionId!.Value;

        rfq.SubmitAward(Guid.NewGuid(), leading, AllActive(rfq), DeadlineDate, [], new string('j', RfqAward.JustificationMaxLength + 1), SourcingPolicy.V1, Actor,
            Deadline, Correlation).Error!.Field.ShouldBe("justification");
        rfq.SubmitAward(Guid.NewGuid(), second, AllActive(rfq), DeadlineDate,
            [new WaiverRequest(WaiverType.NonLeadingSelection, "sourcing.waivers.non-leading", "short", Actor)], null, SourcingPolicy.V1, Actor, Deadline, Correlation)
            .Error!.Code.ShouldBe(DomainError.ReasonRequiredCode);
        rfq.Waivers.ShouldBeEmpty();
        rfq.Status.ShouldBe(RfqStatus.Closed);
    }

    [Fact]
    public void T_CALC_007_b_EurRequisition_UsdAward_UsesBothSnapshotsWithTwoRoundings()
    {
        var awarded = new Money(100m, "USD");

        CalculationRules.ToRequisitionCurrency(awarded, 5.4321m, "EUR", 5.9m, "BRL").ShouldBe(new Money(92.07m, "EUR")); // round(543.21 ÷ 5.9)
        CalculationRules.ToRequisitionCurrency(awarded, 5.4321m, "USD", 5.4321m, "BRL").ShouldBe(awarded);
        CalculationRules.ToRequisitionCurrency(new Money(10m, "BRL"), 1m, "EUR", 5.9m, "BRL").ShouldBe(new Money(1.69m, "EUR"));
        CalculationRules.Normalize(new Money(10m, "BRL"), 0m, "BRL").ShouldBe(new Money(10m, "BRL")); // same currency: rate 1, no snapshot
        Should.Throw<ArgumentOutOfRangeException>(() => CalculationRules.Normalize(awarded, 1.1234567m, "BRL"));
        Should.Throw<ArgumentOutOfRangeException>(() => CalculationRules.ToRequisitionCurrency(awarded, 5m, "EUR", 0m, "BRL"));
        CalculationRules.IsValidExchangeRate(5.123456m).ShouldBeTrue();
        CalculationRules.IsValidExchangeRate(-1m).ShouldBeFalse();
    }

    [Fact]
    public void RequisitionSourcingTransitions_AreGuardedByState_AndTheAwardCurrency()
    {
        var requisition = SubmittedRequisition();
        requisition.ReturnFromSourcing(Reason, Actor, Now, Correlation)!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        requisition.SubmitAward(Guid.NewGuid(), Guid.NewGuid(), "Supplier", new Money(10m, "BRL"), Actor, Now, Correlation)!.Code.ShouldBe(DomainError.InvalidTransitionCode);

        var rfqId = Guid.NewGuid();
        requisition.StartSourcing(rfqId, Actor, Now, Correlation).ShouldBeNull();
        requisition.StartSourcing(rfqId, Actor, Now, Correlation)!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        Should.Throw<ArgumentException>(() => requisition.SubmitAward(Guid.NewGuid(), Guid.NewGuid(), "Supplier", new Money(10m, "USD"), Actor, Now, Correlation));
        requisition.ReturnFromSourcing(Reason, Actor, Now, Correlation).ShouldBeNull();
        requisition.Status.ShouldBe(RequisitionStatus.Submitted);
        requisition.StateChanges.Last().EventType.ShouldBe("procurement.rfq.cancelled");
        requisition.DomainEvents.OfType<RequisitionSubmitted>().Count().ShouldBe(1); // sourcing transitions raise no requisition event
    }

    [Fact]
    public void T_BUD_009_AdjustmentGuards_ReservationStateCurrencyAndAmount()
    {
        var period = FiscalPeriod.Create(Guid.NewGuid(), "FY27", "Fiscal year", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31), null, Now).Period!;
        period.Open(Now);
        var allocation = BudgetAllocation.Create(Guid.NewGuid(), period, new MasterDataReference(Guid.NewGuid(), true), "BRL", true, 1000m, null, Now).Allocation!;
        var requisitionId = Guid.NewGuid();

        allocation.AdjustForAward(requisitionId, new Money(10m, "BRL"), Guid.NewGuid(), null, Now, Correlation).Error!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        allocation.Reserve(Guid.NewGuid(), requisitionId, 1, new Money(100m, "BRL"), period, new DateOnly(2027, 3, 1), null, Now, Correlation);
        allocation.AdjustForAward(requisitionId, new Money(0m, "BRL"), Guid.NewGuid(), null, Now, Correlation).Error!.Code.ShouldBe(BudgetErrors.InvalidAmount("amount").Code);
        Should.Throw<ArgumentException>(() => allocation.AdjustForAward(requisitionId, new Money(10m, "USD"), Guid.NewGuid(), null, Now, Correlation));
        allocation.Commit(requisitionId, null, Now, Correlation);
        allocation.AdjustForAward(requisitionId, new Money(90m, "BRL"), Guid.NewGuid(), null, Now, Correlation).Error!.Details!["currentState"].ShouldBe("Committed");
    }

    private static PurchaseRequisition SubmittedRequisition()
    {
        var costCenter = new CostCenterSnapshot(Guid.NewGuid(), "CC-OPS", "Operations", Guid.NewGuid(), true);
        var category = new CategorySnapshot(Guid.NewGuid(), "IT", "IT supplies", true);
        var unit = new UnitSnapshot(Guid.NewGuid(), "UN", "Unit", 0, true);
        var requisition = PurchaseRequisition.CreateDraft(Guid.NewGuid(), "REQ-2027-000001", Actor, costCenter, "BRL", true,
            "Replacement laptops for the support team", new DateOnly(2027, 4, 1), Actor, Now, Correlation).Requisition!;
        requisition.AddLine(new RequisitionLineInput(null, "Laptop", RequisitionLineKind.Good, category, unit, 1m, new Money(100m, "BRL")), RequisitionPolicy.V1, Now)
            .Error.ShouldBeNull();
        requisition.Submit(new SubmissionContext(Actor, Now, new DateOnly(2027, 3, 1), true, costCenter,
                new Dictionary<Guid, bool> { [category.Id] = true, [unit.Id] = true }, Correlation, RequisitionPolicy.V1),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).ShouldBeNull();
        return requisition;
    }
}
