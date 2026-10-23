using Shouldly;
using Template.Domain.Common;
using Template.Domain.Sourcing;

namespace Template.Unit.Tests.DomainTests.Sourcing;

/// <summary>SRC-001..022 and CALC-004/005/007 rules of the request-for-quotation aggregate, without mocks.</summary>
public sealed class RequestForQuotationTests
{
    internal static readonly DateTime Now = new(2027, 3, 1, 13, 0, 0, DateTimeKind.Utc);
    internal static readonly DateTime Deadline = Now.AddDays(3);
    internal static readonly DateOnly DeadlineDate = new(2027, 3, 4);
    private const string Reason = "Documented sourcing justification";
    private const string Correlation = "trace-rfq-tests";
    private static readonly Guid Buyer = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();

    [Fact]
    public void T_SRC_001_a_And_T_SRC_002_a_CreateCopiesTheSubmissionSnapshot_AsImmutableLines_InRoundOne()
    {
        var rfq = NewRfq(estimate: 18_000m);

        rfq.Status.ShouldBe(RfqStatus.Draft);
        rfq.Number.ShouldBe("RFQ-2027-000001");
        rfq.RoundNumber.ShouldBe(1);
        rfq.PolicyVersion.ShouldBe(1);
        rfq.Lines.Select(line => (line.LineNumber, line.Quantity)).ShouldBe([(1, 10m), (2, 5m)]);
        rfq.AboveThreshold.ShouldBeTrue();
        rfq.History.Single().Action.ShouldBe(RequestForQuotation.ActionCreated);
        RequestForQuotation.Create(Guid.NewGuid(), "RFQ-2027-000002", 1, SourcingMethod.Competitive, Snapshot(1000m) with { Lines = [] }, "BRL", null,
            SourcingPolicy.V1, Buyer, Now, Correlation).Error!.Code.ShouldBe(SourcingErrors.RequisitionNotEligibleCode);
        typeof(RfqLine).GetProperties().ShouldAllBe(property => property.SetMethod == null || !property.SetMethod.IsPublic);
    }

    [Theory]
    [InlineData("5000.00", false)]
    [InlineData("5000.01", true)]
    public void T_SRC_005_a_ThresholdIsExclusive(string estimate, bool above) =>
        NewRfq(estimate: decimal.Parse(estimate, System.Globalization.CultureInfo.InvariantCulture)).AboveThreshold.ShouldBe(above);

    [Fact]
    public void T_SRC_005_a_NonBaseEstimate_UsesTheCreationRate_OrCountsAsAboveWithoutIt()
    {
        var usd = Snapshot(900m) with { Currency = "USD" };
        var withRate = RequestForQuotation.Create(Guid.NewGuid(), "RFQ-2027-000003", 1, SourcingMethod.Competitive, usd, "BRL",
            new ExchangeRateInput("USD", 5.4321m, ExchangeRateSource.BcbPtax, new DateOnly(2027, 2, 26), Now, "BcbPtax"), SourcingPolicy.V1, Buyer, Now, Correlation).Rfq!;
        withRate.EstimateInBaseCurrency.ShouldBe(4888.89m); // round(900 × 5.4321) = 4,888.89 ≤ 5,000.00
        withRate.AboveThreshold.ShouldBeFalse();
        withRate.ExchangeRates.Single().Purpose.ShouldBe(ExchangeRatePurpose.Estimate);

        var withoutRate = RequestForQuotation.Create(Guid.NewGuid(), "RFQ-2027-000004", 1, SourcingMethod.Competitive, usd, "BRL", null,
            SourcingPolicy.V1, Buyer, Now, Correlation).Rfq!;
        withoutRate.EstimateInBaseCurrency.ShouldBeNull();
        withoutRate.AboveThreshold.ShouldBeTrue();
    }

    [Fact]
    public void T_SRC_003_a_OnlyActiveSuppliersAreInvited_WithAnIdentityAndStatusSnapshot()
    {
        var rfq = NewRfq();
        var supplier = Supplier("Alpha Ltda");

        rfq.Invite(supplier with { Status = "Suspended", IsActive = false }, Buyer, Now, Correlation).Error!.Code.ShouldBe(SourcingErrors.SupplierNotEligibleCode);
        var invitation = rfq.Invite(supplier, Buyer, Now, Correlation).Invitation!;
        invitation.SupplierLegalName.ShouldBe("Alpha Ltda");
        invitation.SupplierStatusAtInvitation.ShouldBe("Active");
        invitation.SupplierTaxId.ShouldBe(supplier.TaxId);
        rfq.Invite(supplier, Buyer, Now, Correlation).Error!.Code.ShouldBe(SourcingErrors.SupplierAlreadyInvitedCode);
    }

    [Fact]
    public void T_SRC_007_a_InvitationsAreRemovedOnlyWhileDraft_AndAddedAfterPublishOnlyBeforeTheDeadline()
    {
        var (rfq, suppliers) = Published(3);
        var removable = NewRfq();
        var draftSupplier = Supplier("Delta SA");
        removable.Invite(draftSupplier, Buyer, Now, Correlation);
        removable.RemoveInvitation(draftSupplier.SupplierId, Buyer, Now, Correlation).ShouldBeNull();
        removable.Invitations.Single().RemovedAtUtc.ShouldBe(Now);
        removable.ActiveInvitations.ShouldBeEmpty();
        removable.Invite(draftSupplier, Buyer, Now, Correlation).Error.ShouldBeNull(); // a new row, history kept
        removable.Invitations.Count.ShouldBe(2);

        rfq.RemoveInvitation(suppliers[0].SupplierId, Buyer, Now, Correlation)!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        rfq.Invite(Supplier("Late addition"), Buyer, Now.AddDays(1), Correlation).Error.ShouldBeNull();
        rfq.Invite(Supplier("Too late"), Buyer, Deadline, Correlation).Error!.Code.ShouldBe(SourcingErrors.DeadlinePassedCode);
    }

    [Fact]
    public void T_SRC_004_a_DeadlineMustFallInsideTheResponseWindow()
    {
        var rfq = Invited(NewRfq(), 3);
        var active = Active(rfq);

        Publish(rfq, active, Now.AddHours(24).AddSeconds(-1)).Error!.Code.ShouldBe(SourcingErrors.DeadlineInvalidCode);
        Publish(rfq, active, Now.AddDays(30).AddSeconds(1)).Error!.Code.ShouldBe(SourcingErrors.DeadlineInvalidCode);
        Publish(rfq, active, DateTime.SpecifyKind(Now.AddDays(2), DateTimeKind.Unspecified)).Error!.Code.ShouldBe(SourcingErrors.DeadlineInvalidCode);
        Publish(rfq, active, Now.AddHours(24)).Error.ShouldBeNull();
        rfq.Status.ShouldBe(RfqStatus.Published);
        rfq.DomainEvents.OfType<RfqPublished>().Single().InvitedSupplierIds.Count.ShouldBe(3);
        Publish(rfq, active, Now.AddDays(2)).Error!.Code.ShouldBe(DomainError.InvalidTransitionCode);
    }

    [Fact]
    public void T_SRC_005_b_CompetitiveMinimumInvitations_DependsOnTheThreshold()
    {
        var above = Invited(NewRfq(estimate: 5000.01m), 2);
        var error = Publish(above, Active(above), Deadline).Error!;
        error.Code.ShouldBe(SourcingErrors.InsufficientInvitationsCode);
        error.Details!["required"].ShouldBe("3");

        var below = Invited(NewRfq(estimate: 5000.00m), 1);
        Publish(below, Active(below), Deadline).Error.ShouldBeNull();
    }

    [Fact]
    public void T_SRC_003_b_SupplierSuspendedAfterInvitation_BlocksPublish()
    {
        var rfq = Invited(NewRfq(), 3);
        var states = Active(rfq).ToDictionary(pair => pair.Key, pair => pair.Value);
        states[states.Keys.First()] = false;

        Publish(rfq, states, Deadline).Error!.Code.ShouldBe(SourcingErrors.SupplierNotEligibleCode);
        rfq.Status.ShouldBe(RfqStatus.Draft);
    }

    [Fact]
    public void T_SRC_006_a_DirectRfq_HasOneInvitation_AndTheWaiverMatchingTheThreshold()
    {
        var lowValue = NewRfq(estimate: 1200m, method: SourcingMethod.Direct);
        lowValue.RequiredPublishWaiver.ShouldBe(WaiverType.LowValueDirectPurchase);
        lowValue.Invite(Supplier("Solo"), Buyer, Now, Correlation).Error.ShouldBeNull();
        lowValue.Invite(Supplier("Second"), Buyer, Now, Correlation).Error!.Code.ShouldBe(SourcingErrors.DirectSingleSupplierCode);
        Publish(lowValue, Active(lowValue), Deadline).Error!.Code.ShouldBe(SourcingErrors.WaiverRequiredCode);
        Publish(lowValue, Active(lowValue), Deadline, new WaiverRequest(WaiverType.DirectPurchase, "sourcing.waivers.direct-purchase", Reason, Manager))
            .Error!.Code.ShouldBe(SourcingErrors.WaiverRequiredCode);
        Publish(lowValue, Active(lowValue), Deadline, new WaiverRequest(WaiverType.LowValueDirectPurchase, "sourcing.waivers.low-value", "short", Buyer))
            .Error!.Code.ShouldBe(DomainError.ReasonRequiredCode);
        var (waiver, error) = Publish(lowValue, Active(lowValue), Deadline,
            new WaiverRequest(WaiverType.LowValueDirectPurchase, "sourcing.waivers.low-value", Reason, Buyer));
        error.ShouldBeNull();
        waiver!.RuleId.ShouldBe("SRC-006");
        lowValue.Waivers.Single().Permission.ShouldBe("sourcing.waivers.low-value");

        NewRfq(estimate: 5000.01m, method: SourcingMethod.Direct).RequiredPublishWaiver.ShouldBe(WaiverType.DirectPurchase);
        NewRfq().RequiredPublishWaiver.ShouldBeNull();
    }

    [Fact]
    public void T_SRC_008_a_QuoteDeadlineBoundary_AndProvenance()
    {
        var (rfq, suppliers) = Published(3);

        rfq.EnterQuote(Guid.NewGuid(), Quote(), DeadlineDate, Buyer, Now.AddHours(1), Correlation).Error!.Code.ShouldBe(SourcingErrors.QuoteSupplierNotInvitedCode);
        rfq.EnterQuote(suppliers[0].SupplierId, Quote(receivedAt: Now.AddHours(2)), DeadlineDate, Buyer, Now.AddHours(1), Correlation).Error!.Field.ShouldBe("receivedAt");
        rfq.EnterQuote(suppliers[0].SupplierId, Quote(receivedAt: Deadline.AddTicks(1)), DeadlineDate, Buyer, Deadline.AddHours(1), Correlation)
            .Error!.Code.ShouldBe(SourcingErrors.QuoteLateCode);
        var revision = rfq.EnterQuote(suppliers[0].SupplierId, Quote(receivedAt: Deadline), DeadlineDate, Buyer, Deadline.AddHours(1), Correlation).Revision!;

        revision.RevisionNumber.ShouldBe(1);
        revision.Source.ShouldBe(QuoteSource.Email);
        revision.EnteredBy.ShouldBe(Buyer);
        revision.EnteredAtUtc.ShouldBe(Deadline.AddHours(1));
        revision.ReceivedAtUtc.ShouldBe(Deadline);
        rfq.EnterQuote(suppliers[0].SupplierId, Quote(), DeadlineDate, Buyer, Now.AddHours(1), Correlation).Error!.Code.ShouldBe(SourcingErrors.QuoteExistsCode);
        rfq.DomainEvents.OfType<QuoteRecorded>().Single().Revision.ShouldBe(1);
    }

    [Fact]
    public void T_CALC_002_QuoteTotals_AreComputedWithFreightAndDiscount_AndValidated()
    {
        var (rfq, suppliers) = Published(3);
        var supplier = suppliers[0].SupplierId;

        var revision = rfq.EnterQuote(supplier, Quote(unitPrices: [33.3333m, 10m], freight: 15m, discount: 5.5m, taxes: 120m), DeadlineDate, Buyer,
            Now.AddHours(1), Correlation).Revision!;
        revision.Lines.Select(line => line.LineTotal).ShouldBe([333.33m, 50m]); // round(10 × 33.3333) and 5 × 10
        revision.Subtotal.ShouldBe(383.33m);
        revision.Total.ShouldBe(392.83m); // subtotal + freight − discount; taxes are informational
        revision.TaxesIncluded.ShouldBe(120m);

        var (other, _) = Published(3);
        var id = other.ActiveInvitations.First().SupplierId;
        other.EnterQuote(id, Quote(unitPrices: [1.23456m, 1m]), DeadlineDate, Buyer, Now.AddHours(1), Correlation).Error!.Field.ShouldBe("lines[1].unitPrice");
        other.EnterQuote(id, Quote(quantities: [1.2345m, 5m]), DeadlineDate, Buyer, Now.AddHours(1), Correlation).Error!.Field.ShouldBe("lines[1].quantityOffered");
        other.EnterQuote(id, Quote(freight: 1.005m), DeadlineDate, Buyer, Now.AddHours(1), Correlation).Error!.Field.ShouldBe("freight");
        other.EnterQuote(id, Quote(discount: 100_000m), DeadlineDate, Buyer, Now.AddHours(1), Correlation).Error!.Field.ShouldBe("discount");
        other.EnterQuote(id, Quote(lineNumbers: [1, 9]), DeadlineDate, Buyer, Now.AddHours(1), Correlation).Error!.Field.ShouldBe("lines[9].rfqLineNumber");
        other.EnterQuote(id, Quote(lineNumbers: [1, 1]), DeadlineDate, Buyer, Now.AddHours(1), Correlation).Error!.Field.ShouldBe("lines");
        other.EnterQuote(id, Quote(currency: "JPY", supported: false), DeadlineDate, Buyer, Now.AddHours(1), Correlation).Error!.Field.ShouldBe("currency");
        other.EnterQuote(id, Quote(paymentTerms: " "), DeadlineDate, Buyer, Now.AddHours(1), Correlation).Error!.Field.ShouldBe("paymentTerms");
        other.EnterQuote(id, Quote(deliveryDays: -1), DeadlineDate, Buyer, Now.AddHours(1), Correlation).Error!.Field.ShouldBe("deliveryDays");
        other.EnterQuote(id, Quote(attachments: [new QuoteAttachmentInput("x.exe", "application/x-msdownload", 10)]), DeadlineDate, Buyer, Now.AddHours(1), Correlation)
            .Error!.Kind.ShouldBe(DomainErrorKind.UnsupportedMediaType);
        other.Quotes.ShouldBeEmpty();
    }

    [Fact]
    public void T_SRC_010_a_ValidUntil_MustReachTheDeadlineDate()
    {
        var (rfq, suppliers) = Published(3);

        rfq.EnterQuote(suppliers[0].SupplierId, Quote(validUntil: DeadlineDate.AddDays(-1)), DeadlineDate, Buyer, Now.AddHours(1), Correlation)
            .Error!.RuleId.ShouldBe("SRC-010");
        rfq.EnterQuote(suppliers[0].SupplierId, Quote(validUntil: DeadlineDate), DeadlineDate, Buyer, Now.AddHours(1), Correlation).Error.ShouldBeNull();
    }

    [Fact]
    public void T_SRC_009_a_RevisionsAreImmutable_SupersededAndWithdrawnStayInHistory()
    {
        var (rfq, suppliers) = Published(3);
        var first = rfq.EnterQuote(suppliers[0].SupplierId, Quote(unitPrices: [10m, 10m]), DeadlineDate, Buyer, Now.AddHours(1), Correlation).Revision!;
        var quoteId = first.QuoteId;

        rfq.ReviseQuote(quoteId, Quote(receivedAt: Now), DeadlineDate, Buyer, Now.AddHours(2), Correlation).Error!.Field.ShouldBe("receivedAt");
        var second = rfq.ReviseQuote(quoteId, Quote(unitPrices: [9m, 9m], receivedAt: Now.AddHours(2)), DeadlineDate, Buyer, Now.AddHours(2), Correlation).Revision!;
        first.Status.ShouldBe(QuoteRevisionStatus.Superseded);
        first.Total.ShouldBe(150m);
        second.RevisionNumber.ShouldBe(2);
        second.Status.ShouldBe(QuoteRevisionStatus.Current);

        rfq.WithdrawQuote(quoteId, "short", Buyer, Now.AddHours(3), Correlation)!.Code.ShouldBe(DomainError.ReasonRequiredCode);
        rfq.WithdrawQuote(quoteId, Reason, Buyer, Now.AddHours(3), Correlation).ShouldBeNull();
        second.Status.ShouldBe(QuoteRevisionStatus.Withdrawn);
        second.WithdrawalReason.ShouldBe(Reason);
        rfq.WithdrawQuote(quoteId, Reason, Buyer, Now.AddHours(3), Correlation)!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        var third = rfq.ReviseQuote(quoteId, Quote(receivedAt: Now.AddHours(4)), DeadlineDate, Buyer, Now.AddHours(4), Correlation).Revision!;
        third.RevisionNumber.ShouldBe(3);
        second.Status.ShouldBe(QuoteRevisionStatus.Withdrawn);
        rfq.Quotes.Single().Revisions.Count.ShouldBe(3);
        typeof(QuoteRevision).GetProperties().ShouldAllBe(property => property.SetMethod == null || !property.SetMethod.IsPublic);
    }

    [Fact]
    public void T_SRC_011_a_DeadlineExtension_BeforeTheDeadline_AtMostThirtyDays_WithReason()
    {
        var (rfq, _) = Published(3);

        rfq.ExtendDeadline(Deadline.AddDays(1), "short", SourcingPolicy.V1, Buyer, Now, Correlation)!.Code.ShouldBe(DomainError.ReasonRequiredCode);
        rfq.ExtendDeadline(Deadline, Reason, SourcingPolicy.V1, Buyer, Now, Correlation)!.Code.ShouldBe(SourcingErrors.DeadlineInvalidCode);
        rfq.ExtendDeadline(Deadline.AddDays(30).AddSeconds(1), Reason, SourcingPolicy.V1, Buyer, Now, Correlation)!.Code.ShouldBe(SourcingErrors.DeadlineInvalidCode);
        rfq.ExtendDeadline(Deadline.AddDays(30), Reason, SourcingPolicy.V1, Buyer, Now, Correlation).ShouldBeNull();
        rfq.DeadlineUtc.ShouldBe(Deadline.AddDays(30));
        var extended = rfq.DomainEvents.OfType<RfqDeadlineExtended>().Single();
        extended.PreviousDeadlineUtc.ShouldBe(Deadline);
        extended.InvitedSupplierIds.Count.ShouldBe(3);
        rfq.ExtendDeadline(Deadline.AddDays(40), Reason, SourcingPolicy.V1, Buyer, Deadline.AddDays(30), Correlation)!.Code.ShouldBe(SourcingErrors.DeadlinePassedCode);
    }

    [Fact]
    public void T_SRC_022_a_ReminderTargetsOnlySuppliersWithoutACurrentQuote()
    {
        var (rfq, suppliers) = Published(3);
        rfq.EnterQuote(suppliers[0].SupplierId, Quote(), DeadlineDate, Buyer, Now.AddHours(1), Correlation);

        var (pending, error) = rfq.RequestReminder(Buyer, Now.AddHours(2), Correlation);
        error.ShouldBeNull();
        pending.ShouldBe(suppliers.Skip(1).Select(supplier => supplier.SupplierId).OrderBy(id => id));
        rfq.DomainEvents.OfType<RfqReminderRequested>().Single().SupplierIds.ShouldBe(pending);
        foreach (var supplier in suppliers.Skip(1))
            rfq.EnterQuote(supplier.SupplierId, Quote(), DeadlineDate, Buyer, Now.AddHours(3), Correlation);
        rfq.RequestReminder(Buyer, Now.AddHours(4), Correlation).Error!.Code.ShouldBe(SourcingErrors.NoPendingSuppliersCode);
        rfq.RequestReminder(Buyer, Deadline, Correlation).Error!.Code.ShouldBe(SourcingErrors.DeadlinePassedCode);
    }

    [Fact]
    public void T_SRC_012_a_EarlyCloseNeedsAReason_NormalCloseFreezesRevisions()
    {
        var (rfq, suppliers) = Published(3);
        rfq.EnterQuote(suppliers[0].SupplierId, Quote(), DeadlineDate, Buyer, Now.AddHours(1), Correlation);
        var states = Active(rfq);

        rfq.Close(null, states, [], DeadlineDate, SourcingPolicy.V1, Buyer, Now.AddDays(1), Correlation).Error!.Code.ShouldBe(DomainError.ReasonRequiredCode);
        var (comparison, error) = rfq.Close(null, states, [], DeadlineDate, SourcingPolicy.V1, Buyer, Deadline, Correlation);
        error.ShouldBeNull();
        rfq.Status.ShouldBe(RfqStatus.Closed);
        rfq.ClosedEarly.ShouldBeFalse();
        comparison!.Sequence.ShouldBe(1);
        rfq.DomainEvents.OfType<RfqClosed>().Single().ComparisonStatus.ShouldBe(ComparisonStatus.Complete);
        rfq.EnterQuote(suppliers[1].SupplierId, Quote(), DeadlineDate, Buyer, Deadline, Correlation).Error!.Code.ShouldBe(SourcingErrors.QuoteImmutableCode);
        rfq.ReviseQuote(rfq.Quotes.Single().Id, Quote(), DeadlineDate, Buyer, Deadline, Correlation).Error!.Code.ShouldBe(SourcingErrors.QuoteImmutableCode);
        rfq.WithdrawQuote(rfq.Quotes.Single().Id, Reason, Buyer, Deadline, Correlation)!.Code.ShouldBe(SourcingErrors.QuoteImmutableCode);
        rfq.Close(null, states, [], DeadlineDate, SourcingPolicy.V1, Buyer, Deadline, Correlation).Error!.Code.ShouldBe(DomainError.InvalidTransitionCode);

        var (early, _) = Published(3);
        early.Close(Reason, Active(early), [], new DateOnly(2027, 3, 2), SourcingPolicy.V1, Buyer, Now.AddDays(1), Correlation).Error.ShouldBeNull();
        early.ClosedEarly.ShouldBeTrue();
        early.CloseReason.ShouldBe(Reason);
    }

    [Fact]
    public void T_SRC_020_a_CancellationReturnsWhetherTheRequisitionWasInSourcing()
    {
        var draft = NewRfq();
        draft.Cancel("short", Buyer, Now, Correlation).Error!.Code.ShouldBe(DomainError.ReasonRequiredCode);
        draft.Cancel(Reason, Buyer, Now, Correlation).RequisitionWasInSourcing.ShouldBeFalse();
        draft.Status.ShouldBe(RfqStatus.Cancelled);
        draft.DomainEvents.OfType<RfqCancelled>().Single().ReasonCode.ShouldBe("Cancelled");
        draft.Cancel(Reason, Buyer, Now, Correlation).Error!.Code.ShouldBe(DomainError.InvalidTransitionCode);

        var (published, _) = Published(3);
        published.Cancel(Reason, Buyer, Now, Correlation).RequisitionWasInSourcing.ShouldBeTrue();
        published.CancelForRequisition("RequisitionCancelled", Buyer, Now, Correlation).ShouldBeFalse(); // idempotent cascade
    }

    [Fact]
    public void CancelForRequisition_EndsAPendingAward_AsCancelled()
    {
        var rfq = Awarded();

        rfq.CancelForRequisition("RequisitionCancelled", Manager, Deadline.AddDays(1), Correlation).ShouldBeTrue();
        rfq.Status.ShouldBe(RfqStatus.Cancelled);
        rfq.Awards.Single().Status.ShouldBe(AwardStatus.Cancelled);
        rfq.CancellationReasonCode.ShouldBe("RequisitionCancelled");
    }

    [Fact]
    public void T_SRC_016_a_AwardRecordsFrozenAmounts_AndOnlyOnePendingAward()
    {
        var rfq = Awarded();

        var award = rfq.Awards.Single();
        award.Status.ShouldBe(AwardStatus.PendingApproval);
        award.Rank.ShouldBe(1);
        award.RequisitionCurrencyTotal.ShouldBe(award.AwardedTotal);
        rfq.Status.ShouldBe(RfqStatus.AwardPending);
        rfq.DomainEvents.OfType<AwardSubmitted>().Single().PolicyRef.ShouldBe("SourcingPolicy:1");
        rfq.EvaluateAward(award.QuoteRevisionId, AllActive(rfq), DeadlineDate, SourcingPolicy.V1).Error!.Code.ShouldBe(SourcingErrors.AwardPendingExistsCode);
    }

    [Fact]
    public void T_SRC_016_a_InvalidExpiredOrIneligibleQuotes_CannotBeAwarded()
    {
        var rfq = ClosedWith((1000m, 5, Coverage.Full), (900m, 5, Coverage.MissingLine), (950m, 5, Coverage.Full));
        var comparison = rfq.CurrentComparison!;
        var invalid = comparison.Entries.Single(entry => !entry.IsValid);
        var leading = comparison.Entries.Single(entry => entry.Rank == 1);

        rfq.EvaluateAward(invalid.QuoteRevisionId, AllActive(rfq), DeadlineDate, SourcingPolicy.V1).Error!.Code.ShouldBe(SourcingErrors.AwardNotEligibleCode);
        rfq.EvaluateAward(Guid.NewGuid(), AllActive(rfq), DeadlineDate, SourcingPolicy.V1).Error!.Code.ShouldBe(SourcingErrors.AwardNotEligibleCode);
        var suspended = AllActive(rfq).ToDictionary(pair => pair.Key, pair => pair.Value);
        suspended[leading.SupplierId] = false;
        rfq.EvaluateAward(leading.QuoteRevisionId, suspended, DeadlineDate, SourcingPolicy.V1).Error!.Code.ShouldBe(SourcingErrors.SupplierNotEligibleCode);
        rfq.EvaluateAward(leading.QuoteRevisionId, AllActive(rfq), leading.ValidUntil.AddDays(1), SourcingPolicy.V1).Error!.Code.ShouldBe(SourcingErrors.QuoteExpiredCode);
    }

    [Fact]
    public void T_SRC_017_a_TwoValidQuotesAboveThreshold_NeedTheInsufficientQuotationsWaiver()
    {
        var rfq = ClosedWith((1000m, 5, Coverage.Full), (1100m, 5, Coverage.Full), (900m, 5, Coverage.MissingLine));
        var leading = rfq.CurrentComparison!.Entries.Single(entry => entry.Rank == 1);
        rfq.CurrentComparison.ValidQuoteCount.ShouldBe(2);
        rfq.CurrentComparison.WarningList.ShouldContain(ComparisonWarnings.InsufficientValidQuotes);

        rfq.EvaluateAward(leading.QuoteRevisionId, AllActive(rfq), DeadlineDate, SourcingPolicy.V1).Waivers.ShouldBe([WaiverType.InsufficientQuotations]);
        Award(rfq, leading.QuoteRevisionId).Error!.Code.ShouldBe(SourcingErrors.AwardInsufficientQuotationsCode);
        var (award, error) = Award(rfq, leading.QuoteRevisionId,
            new WaiverRequest(WaiverType.InsufficientQuotations, "sourcing.waivers.insufficient-quotations", Reason, Manager));
        error.ShouldBeNull();
        rfq.Waivers.Single().ScopeId.ShouldBe(award!.Id);
        rfq.Waivers.Single().RuleId.ShouldBe("SRC-017");
    }

    [Fact]
    public void T_SRC_017_ZeroValidQuotes_MakeAnAwardImpossible()
    {
        var rfq = ClosedWith((900m, 5, Coverage.MissingLine), (950m, 5, Coverage.MissingLine), (990m, 5, Coverage.MissingLine));

        rfq.CurrentComparison!.ValidQuoteCount.ShouldBe(0);
        rfq.CurrentComparison.WarningList.ShouldContain(ComparisonWarnings.NoValidQuotes);
        rfq.CurrentComparison.RecommendedQuoteRevisionId.ShouldBeNull();
        rfq.EvaluateAward(rfq.CurrentComparison.Entries.First().QuoteRevisionId, AllActive(rfq), DeadlineDate, SourcingPolicy.V1)
            .Error!.Code.ShouldBe(SourcingErrors.AwardNotEligibleCode);
    }

    [Fact]
    public void T_SRC_018_a_NonLeadingSelection_NeedsAWaiver_ButTheBestAwardableQuoteDoesNot()
    {
        var rfq = ClosedWith((1000m, 5, Coverage.Full), (1100m, 5, Coverage.Full), (1200m, 5, Coverage.Full));
        var entries = rfq.CurrentComparison!.Entries.OrderBy(entry => entry.Rank).ToArray();

        rfq.EvaluateAward(entries[1].QuoteRevisionId, AllActive(rfq), DeadlineDate, SourcingPolicy.V1).Waivers.ShouldBe([WaiverType.NonLeadingSelection]);
        Award(rfq, entries[1].QuoteRevisionId).Error!.Code.ShouldBe(SourcingErrors.AwardNonLeadingCode);

        // SC-06: rank 1 supplier suspended → rank 2 is now the best awardable quote and needs no waiver.
        var suspended = AllActive(rfq).ToDictionary(pair => pair.Key, pair => pair.Value);
        suspended[entries[0].SupplierId] = false;
        rfq.EvaluateAward(entries[1].QuoteRevisionId, suspended, DeadlineDate, SourcingPolicy.V1).Waivers.ShouldBeEmpty();
    }

    [Fact]
    public void T_SRC_014_a_EachInvalidReason_IsListed_AndMissingLinesAreNeverZero()
    {
        var (rfq, suppliers) = Published(4);
        var states = Active(rfq).ToDictionary(pair => pair.Key, pair => pair.Value);
        rfq.EnterQuote(suppliers[0].SupplierId, Quote(lineNumbers: [1], unitPrices: [1m]), DeadlineDate, Buyer, Now.AddHours(1), Correlation);
        rfq.EnterQuote(suppliers[1].SupplierId, Quote(quantities: [9m, 5m]), DeadlineDate, Buyer, Now.AddHours(1), Correlation);
        rfq.EnterQuote(suppliers[2].SupplierId, Quote(validUntil: DeadlineDate), DeadlineDate, Buyer, Now.AddHours(1), Correlation);
        var withdrawn = rfq.EnterQuote(suppliers[3].SupplierId, Quote(), DeadlineDate, Buyer, Now.AddHours(1), Correlation).Revision!;
        rfq.WithdrawQuote(withdrawn.QuoteId, Reason, Buyer, Now.AddHours(2), Correlation);
        states[suppliers[1].SupplierId] = false;

        var comparison = rfq.Close(null, states, [], DeadlineDate.AddDays(1), SourcingPolicy.V1, Buyer, Deadline, Correlation).Comparison!;
        Reasons(comparison, suppliers[0]).ShouldBe([InvalidQuoteReasons.MissingLines]);
        Reasons(comparison, suppliers[1]).ShouldBe([InvalidQuoteReasons.SupplierNotActive, InvalidQuoteReasons.InsufficientQuantity]);
        Reasons(comparison, suppliers[2]).ShouldBe([InvalidQuoteReasons.Expired]);
        Reasons(comparison, suppliers[3]).ShouldBe([InvalidQuoteReasons.Withdrawn]);
        comparison.Entries.Single(entry => entry.SupplierId == suppliers[0].SupplierId).CoveredLines.ShouldBe(1);
        comparison.Entries.Single(entry => entry.SupplierId == suppliers[0].SupplierId).Rank.ShouldBeNull();
        comparison.ValidQuoteCount.ShouldBe(0);
    }

    [Fact]
    public void T_CALC_005_a_EachTieBreakerLevel_AndT_CALC_005_b_DeterminismAcrossInputOrder()
    {
        // equal totals → fewer delivery days wins; then earlier receipt; then legal name (ordinal).
        var rfq = ClosedWith((1000m, 7, Coverage.Full), (1000m, 3, Coverage.Full), (900m, 9, Coverage.Full));
        var ranked = rfq.CurrentComparison!.Entries.OrderBy(entry => entry.Rank).ToArray();
        ranked.Select(entry => (entry.NormalizedTotal!.Value, entry.DeliveryDays)).ShouldBe([(1350m, 9), (1500m, 3), (1500m, 7)]);
        rfq.CurrentComparison.RecommendedQuoteRevisionId.ShouldBe(ranked[0].QuoteRevisionId);

        var names = new[] { "Charlie", "alpha", "Bravo" };
        string[] first = RankedNames(names, sameReceipt: true);
        RankedNames(names.Reverse().ToArray(), sameReceipt: true).ShouldBe(first);
        first.ShouldBe(["Bravo", "Charlie", "alpha"]); // ordinal: upper case before lower case

        RankedNames(["Zulu", "Alpha"], sameReceipt: false).ShouldBe(["Zulu", "Alpha"]); // Zulu received first
    }

    [Fact]
    public void T_CALC_004_a_And_T_SRC_013_CrossCurrencyQuotes_AreNormalized_OrTheComparisonIsIncomplete()
    {
        var (rfq, suppliers) = Published(3);
        rfq.EnterQuote(suppliers[0].SupplierId, Quote(currency: "USD", unitPrices: [10m, 0m + 1m], quantities: [10m, 5m]), DeadlineDate, Buyer, Now.AddHours(1), Correlation);
        rfq.EnterQuote(suppliers[1].SupplierId, Quote(unitPrices: [60m, 10m]), DeadlineDate, Buyer, Now.AddHours(1), Correlation);
        rfq.EnterQuote(suppliers[2].SupplierId, Quote(unitPrices: [70m, 10m]), DeadlineDate, Buyer, Now.AddHours(1), Correlation);
        rfq.CurrenciesNeedingRates().ShouldBe(["USD"]);

        var incomplete = rfq.Close(null, Active(rfq), [], DeadlineDate, SourcingPolicy.V1, Buyer, Deadline, Correlation).Comparison!;
        incomplete.Status.ShouldBe(ComparisonStatus.Incomplete);
        incomplete.MissingRateCurrencies.ShouldBe(["USD"]);
        incomplete.RecommendedQuoteRevisionId.ShouldBeNull();
        incomplete.Entries.Single(entry => entry.Currency == "USD").NormalizedTotal.ShouldBeNull();
        incomplete.Entries.Single(entry => entry.Currency == "USD").Rank.ShouldBeNull();
        rfq.EvaluateAward(incomplete.Entries.First(entry => entry.Rank == 1).QuoteRevisionId, AllActive(rfq), DeadlineDate, SourcingPolicy.V1)
            .Error!.Code.ShouldBe(SourcingErrors.AwardComparisonIncompleteCode);

        var waiver = new WaiverRequest(WaiverType.ManualExchangeRate, "sourcing.rates.manual", Reason, Manager);
        rfq.RecordManualRate("EUR", 5.9m, DeadlineDate, waiver, SourcingPolicy.V1, Deadline.AddHours(1), Correlation).Error!.Code.ShouldBe(SourcingErrors.RateNotNeededCode);
        rfq.RecordManualRate("USD", 5.12345678m, DeadlineDate, waiver, SourcingPolicy.V1, Deadline.AddHours(1), Correlation).Error!.Field.ShouldBe("rate");
        rfq.RecordManualRate("USD", 5.4321m, DeadlineDate.AddDays(1), waiver, SourcingPolicy.V1, Deadline.AddHours(1), Correlation).Error!.Field.ShouldBe("quoteDate");
        rfq.RecordManualRate("USD", 5.4321m, DeadlineDate, null, SourcingPolicy.V1, Deadline.AddHours(1), Correlation).Error!.Code.ShouldBe(SourcingErrors.WaiverRequiredCode);
        var complete = rfq.RecordManualRate("USD", 5.4321m, DeadlineDate, waiver, SourcingPolicy.V1, Deadline.AddHours(1), Correlation).Comparison!;

        complete.Sequence.ShouldBe(2);
        complete.Status.ShouldBe(ComparisonStatus.Complete);
        var usd = complete.Entries.Single(entry => entry.Currency == "USD");
        usd.RawTotal.ShouldBe(105m);
        usd.NormalizedTotal.ShouldBe(570.37m); // round(105.00 × 5.4321) = 570.3705 → 570.37
        usd.Rank.ShouldBe(1);
        rfq.ExchangeRates.Single(rate => rate.Source == ExchangeRateSource.Manual).EnteredBy.ShouldBe(Manager);
        rfq.Waivers.Single().Type.ShouldBe(WaiverType.ManualExchangeRate);
        incomplete.Status.ShouldBe(ComparisonStatus.Incomplete); // the first comparison is kept unchanged
        rfq.Comparisons.Count.ShouldBe(2);
    }

    [Fact]
    public void T_CALC_004_c_CloseSnapshotsTheProvidedRate_AndReusesItForTheAward()
    {
        var (rfq, suppliers) = Published(3);
        rfq.EnterQuote(suppliers[0].SupplierId, Quote(currency: "USD", unitPrices: [10m, 1m]), DeadlineDate, Buyer, Now.AddHours(1), Correlation);
        rfq.EnterQuote(suppliers[1].SupplierId, Quote(unitPrices: [60m, 10m]), DeadlineDate, Buyer, Now.AddHours(1), Correlation);
        rfq.EnterQuote(suppliers[2].SupplierId, Quote(unitPrices: [70m, 10m]), DeadlineDate, Buyer, Now.AddHours(1), Correlation);
        var rate = new ExchangeRateInput("USD", 5.4321m, ExchangeRateSource.BcbPtax, DeadlineDate.AddDays(-1), Deadline, "BcbPtax");

        var comparison = rfq.Close(null, Active(rfq), [rate], DeadlineDate, SourcingPolicy.V1, Buyer, Deadline, Correlation).Comparison!;
        comparison.Status.ShouldBe(ComparisonStatus.Complete);
        var snapshot = rfq.ExchangeRates.Single();
        snapshot.Source.ShouldBe(ExchangeRateSource.BcbPtax);
        snapshot.QuoteDate.ShouldBe(DeadlineDate.AddDays(-1));
        var usd = comparison.Entries.Single(entry => entry.Currency == "USD");
        usd.ExchangeRateId.ShouldBe(snapshot.Id);
        var award = Award(rfq, usd.QuoteRevisionId).Award!;
        award.AwardedTotal.ShouldBe(105m);
        award.NormalizedTotal.ShouldBe(570.37m);
        award.RequisitionCurrencyTotal.ShouldBe(570.37m); // CALC-007: BRL requisition, USD award
    }

    [Fact]
    public void SupplierChangesAfterInvitation_NeverRewriteTheSnapshot()
    {
        var rfq = NewRfq();
        var supplier = Supplier("Original Name Ltda");
        rfq.Invite(supplier, Buyer, Now, Correlation);

        _ = supplier with { LegalName = "Renamed Ltda", Status = "Suspended", IsActive = false };
        rfq.Invitations.Single().SupplierLegalName.ShouldBe("Original Name Ltda");
        rfq.Invitations.Single().SupplierStatusAtInvitation.ShouldBe("Active");
    }

    [Fact]
    public void EveryChange_IncrementsTheVersion_AndAppendsHistory()
    {
        var rfq = NewRfq();
        var version = rfq.Version;
        rfq.Invite(Supplier("A"), Buyer, Now, Correlation);
        rfq.Version.ShouldBe(version + 1);
        rfq.History.Select(entry => entry.Sequence).ShouldBe([1, 2]);
    }

    private static IReadOnlyList<string> Reasons(RfqComparison comparison, SupplierSnapshot supplier) =>
        comparison.Entries.Single(entry => entry.SupplierId == supplier.SupplierId).InvalidReasonList;

    private static string[] RankedNames(string[] names, bool sameReceipt)
    {
        var rfq = NewRfq(estimate: 1000m);
        var suppliers = names.Select(Supplier).ToArray();
        foreach (var supplier in suppliers)
            rfq.Invite(supplier, Buyer, Now, Correlation);
        Publish(rfq, Active(rfq), Deadline);
        for (var index = 0; index < suppliers.Length; index++)
            rfq.EnterQuote(suppliers[index].SupplierId, Quote(receivedAt: sameReceipt ? Now.AddHours(1) : Now.AddHours(1 + index)), DeadlineDate, Buyer, Now.AddHours(5), Correlation);
        var comparison = rfq.Close(null, Active(rfq), [], DeadlineDate, SourcingPolicy.V1, Buyer, Deadline, Correlation).Comparison!;
        return comparison.Entries.OrderBy(entry => entry.Rank).Select(entry => entry.SupplierLegalName).ToArray();
    }

    internal enum Coverage
    {
        Full,
        MissingLine
    }

    /// <summary>Closed RFQ (estimate 18,000.00 BRL, above threshold) with one quote per tuple: unit price of both lines, delivery days, coverage.</summary>
    internal static RequestForQuotation ClosedWith(params (decimal UnitPrice, int DeliveryDays, Coverage Coverage)[] quotes)
    {
        var (rfq, suppliers) = Published(quotes.Length);
        for (var index = 0; index < quotes.Length; index++)
        {
            var (price, days, coverage) = quotes[index];
            var input = coverage == Coverage.Full
                ? Quote(unitPrices: [price / 10m, price / 10m], deliveryDays: days)
                : Quote(lineNumbers: [1], unitPrices: [price / 10m], deliveryDays: days);
            rfq.EnterQuote(suppliers[index].SupplierId, input, DeadlineDate, Buyer, Now.AddHours(1), Correlation).Error.ShouldBeNull();
        }

        rfq.Close(null, Active(rfq), [], DeadlineDate, SourcingPolicy.V1, Buyer, Deadline, Correlation).Error.ShouldBeNull();
        return rfq;
    }

    internal static RequestForQuotation Awarded()
    {
        var rfq = ClosedWith((1000m, 5, Coverage.Full), (1100m, 5, Coverage.Full), (1200m, 5, Coverage.Full));
        Award(rfq, rfq.CurrentComparison!.RecommendedQuoteRevisionId!.Value).Error.ShouldBeNull();
        return rfq;
    }

    internal static (RfqAward? Award, DomainError? Error) Award(RequestForQuotation rfq, Guid revisionId, params WaiverRequest[] waivers) =>
        rfq.SubmitAward(Guid.NewGuid(), revisionId, AllActive(rfq), DeadlineDate, waivers, "Best value", SourcingPolicy.V1, Buyer, Deadline.AddHours(1), Correlation);

    internal static IReadOnlyDictionary<Guid, bool> AllActive(RequestForQuotation rfq) =>
        rfq.Invitations.Select(invitation => invitation.SupplierId).Distinct().ToDictionary(id => id, _ => true);

    internal static (RequestForQuotation Rfq, SupplierSnapshot[] Suppliers) Published(int invitations, decimal estimate = 18_000m)
    {
        var rfq = NewRfq(estimate: estimate);
        var suppliers = Enumerable.Range(0, invitations).Select(index => Supplier($"Supplier {(char)('A' + index)}")).ToArray();
        foreach (var supplier in suppliers)
            rfq.Invite(supplier, Buyer, Now, Correlation).Error.ShouldBeNull();
        Publish(rfq, Active(rfq), Deadline).Error.ShouldBeNull();
        return (rfq, suppliers);
    }

    private static RequestForQuotation Invited(RequestForQuotation rfq, int count)
    {
        for (var index = 0; index < count; index++)
            rfq.Invite(Supplier($"Invited {index}"), Buyer, Now, Correlation);
        return rfq;
    }

    private static IReadOnlyDictionary<Guid, bool> Active(RequestForQuotation rfq) =>
        rfq.ActiveInvitations.ToDictionary(invitation => invitation.SupplierId, _ => true);

    private static (RfqWaiver? Waiver, DomainError? Error) Publish(
        RequestForQuotation rfq, IReadOnlyDictionary<Guid, bool> states, DateTime deadline, WaiverRequest? waiver = null) =>
        rfq.Publish(deadline, states, waiver, SourcingPolicy.V1, Buyer, Now, Correlation);

    internal static SupplierSnapshot Supplier(string legalName) => new(Guid.NewGuid(), legalName, "BR", "11222333000181", "Active", true);

    internal static RequestForQuotation NewRfq(decimal estimate = 18_000m, SourcingMethod method = SourcingMethod.Competitive) =>
        RequestForQuotation.Create(Guid.NewGuid(), "RFQ-2027-000001", 1, method, Snapshot(estimate), "BRL", null, SourcingPolicy.V1, Buyer, Now, Correlation).Rfq!;

    internal static RequisitionSourcingSnapshot Snapshot(decimal estimate) => new(
        Guid.NewGuid(), "REQ-2027-000001", 1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "BRL", estimate, new DateOnly(2027, 4, 1),
        [
            new RfqLineSnapshot(1, null, "Laptop", "Good", Guid.NewGuid(), "IT", Guid.NewGuid(), "UN", 10m, 1200m, 12000m),
            new RfqLineSnapshot(2, null, "Docking station", "Good", Guid.NewGuid(), "IT", Guid.NewGuid(), "UN", 5m, 1200m, 6000m)
        ]);

    internal static QuoteInput Quote(
        string currency = "BRL",
        bool supported = true,
        int[]? lineNumbers = null,
        decimal[]? quantities = null,
        decimal[]? unitPrices = null,
        decimal freight = 0m,
        decimal discount = 0m,
        decimal taxes = 0m,
        DateOnly? validUntil = null,
        int deliveryDays = 5,
        string paymentTerms = "30 days",
        DateTime? receivedAt = null,
        IReadOnlyList<QuoteAttachmentInput>? attachments = null)
    {
        var numbers = lineNumbers ?? [1, 2];
        var lines = numbers.Select((number, index) => new QuoteLineInput(
            number,
            quantities?[index] ?? (number == 1 ? 10m : 5m),
            unitPrices?[index] ?? 100m)).ToArray();
        return new QuoteInput(currency, supported, lines, freight, discount, taxes, validUntil ?? DeadlineDate.AddDays(30), deliveryDays, paymentTerms,
            "Synthetic note", QuoteSource.Email, receivedAt ?? Now.AddHours(1), attachments ?? []);
    }
}
