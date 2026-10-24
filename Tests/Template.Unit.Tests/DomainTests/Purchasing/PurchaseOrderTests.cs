using Shouldly;
using Template.Domain.Budgeting;
using Template.Domain.Common;
using Template.Domain.Organization;
using Template.Domain.Purchasing;

namespace Template.Unit.Tests.DomainTests.Purchasing;

/// <summary>Mock-free rules of the purchase-order aggregate (PUR-001..010, RCV-001/003/007, CALC-006/008/009). Synthetic data.</summary>
public sealed class PurchaseOrderTests
{
    private static readonly DateTime Now = new(2027, 5, 3, 13, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2027, 5, 3);
    private static readonly Guid Buyer = Guid.NewGuid();
    private const string Correlation = "unit-po";
    private const string Reason = "Synthetic purchasing reason";

    internal static PurchaseOrderDraftInput Input(decimal quantity = 10m, decimal unitPrice = 100m, decimal freight = 0m, decimal discount = 0m,
        int unitDecimals = 0, string currency = "BRL", decimal quoteRate = 1m, string requisitionCurrency = "BRL", decimal requisitionRate = 1m,
        params (decimal Quantity, decimal Price)[] extraLines)
    {
        var lines = new List<PurchaseOrderLineInput>
        {
            new(1, null, "Synthetic laptop", "Good", Guid.NewGuid(), Guid.NewGuid(), "UN", unitDecimals, quantity, unitPrice, Today.AddDays(7))
        };
        var number = 2;
        foreach (var (extraQuantity, price) in extraLines)
            lines.Add(new PurchaseOrderLineInput(number++, null, "Synthetic service", "Service", Guid.NewGuid(), Guid.NewGuid(), "H", 2, extraQuantity, price, Today.AddDays(7)));
        var subtotal = lines.Sum(line => CalculationRules.LineTotal(line.Quantity, new Money(line.UnitPrice, currency)).Amount);
        var total = subtotal + freight - discount;
        return new PurchaseOrderDraftInput(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "RFQ-2027-000001", Guid.NewGuid(), Guid.NewGuid(), "REQ-2027-000001", Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(),
            new PurchaseOrderSupplierSnapshot(Guid.NewGuid(), "Synthetic Supplier Ltda", "Synthetic", "BR", "11222333000181", "Avenida Sintética 1",
                "Synthetic Contact", "contact@example.invalid", "+55 11 0000-0000"),
            Buyer, "Synthetic Buyer", currency, freight, discount, 0m, total, "30 days", Today.AddDays(30), 7, "BRL", quoteRate, requisitionCurrency,
            requisitionRate, CalculationRules.ToRequisitionCurrency(new Money(total, currency), quoteRate, requisitionCurrency, requisitionRate, "BRL").Amount,
            lines);
    }

    internal static PurchaseOrder Draft(PurchaseOrderDraftInput? input = null) =>
        PurchaseOrder.CreateDraft(Guid.NewGuid(), input ?? Input(), Now, Correlation);

    internal static PurchaseOrder Issued(PurchaseOrderDraftInput? input = null)
    {
        var order = Draft(input);
        order.UpdateDraft("Rua Sintética 100, São Paulo", null, null, Today, Buyer, Now, Correlation).ShouldBeNull();
        order.Issue("PO-2027-000001", Today, Guid.NewGuid(), true, true, true, Buyer, Now, Correlation).Error.ShouldBeNull();
        order.ClearDomainEvents();
        return order;
    }

    [Fact]
    public void T_PUR_001_a_T_CALC_006_a_DraftCopiesTheAwardedRevision_AndItsTotalExactly()
    {
        var input = Input(quantity: 3m, unitPrice: 33.3333m, freight: 10m, discount: 5m);
        var order = Draft(input);

        order.Status.ShouldBe(PurchaseOrderStatus.Draft);
        order.Number.ShouldBeNull();
        order.Lines.Single().LineTotal.ShouldBe(100.00m);
        order.Subtotal.ShouldBe(100.00m);
        order.Total.ShouldBe(105.00m);
        order.Total.ShouldBe(input.AwardedTotal);
        order.SupplierLegalName.ShouldBe("Synthetic Supplier Ltda");
        order.DocumentStatus.ShouldBe(PurchaseOrderDocumentStatus.NotRequested);
        order.DomainEvents.Single().ShouldBeOfType<PurchaseOrderDrafted>().AwardId.ShouldBe(input.AwardId);
        order.History.Single().Action.ShouldBe(PurchaseOrder.ActionCreated);
        Should.Throw<InvalidOperationException>(() => PurchaseOrder.CreateDraft(Guid.NewGuid(), input with { AwardedTotal = 104.99m }, Now, Correlation));
        Should.Throw<ArgumentException>(() => PurchaseOrder.CreateDraft(Guid.NewGuid(), input with { Lines = [] }, Now, Correlation));
    }

    [Fact]
    public void T_PUR_002_a_OnlyTheDeliveryFieldsChangeInDraft_ThenEverythingIsImmutable()
    {
        var order = Draft();
        var line = order.Lines.Single().LineNumber;

        order.UpdateDraft("abc", null, null, Today, Buyer, Now, Correlation)!.Field.ShouldBe("deliveryAddress");
        order.UpdateDraft(null, null, new Dictionary<int, DateOnly> { [line] = Today.AddDays(-1) }, Today, Buyer, Now, Correlation)!.Field
            .ShouldBe("lines[1].promisedDeliveryDate");
        order.UpdateDraft(null, null, new Dictionary<int, DateOnly> { [99] = Today }, Today, Buyer, Now, Correlation)!.Code.ShouldBe("validation");
        order.UpdateDraft(null, new string('n', PurchaseOrder.NotesMaxLength + 1), null, Today, Buyer, Now, Correlation)!.Field.ShouldBe("notes");
        var version = order.Version;
        order.UpdateDraft("  Rua Sintética 100  ", "Dock 2", new Dictionary<int, DateOnly> { [line] = Today.AddDays(20) }, Today, Buyer, Now, Correlation)
            .ShouldBeNull();
        order.DeliveryAddress.ShouldBe("Rua Sintética 100");
        order.Notes.ShouldBe("Dock 2");
        order.Lines.Single().PromisedDeliveryDate.ShouldBe(Today.AddDays(20));
        order.Version.ShouldBe(version + 1);

        order.Issue("PO-2027-000001", Today, Guid.NewGuid(), true, true, true, Buyer, Now, Correlation).Error.ShouldBeNull();
        order.UpdateDraft(null, "Too late", null, Today, Buyer, Now, Correlation).ShouldBe(PurchasingErrors.Immutable);
    }

    [Fact]
    public void T_PUR_003_a_EachIssueGuard_IsReported_AndNothingChanges()
    {
        var order = Draft();
        order.CheckIssuable(false, true, true)!.Code.ShouldBe("supplier.not-eligible");
        var guard = order.CheckIssuable(true, false, false)!;
        guard.Code.ShouldBe(PurchasingErrors.NotIssuableCode);
        guard.Details!["fields"].ShouldBe("approval,reservation,deliveryAddress");
        var (changed, error) = order.Issue("PO-2027-000001", Today, Guid.NewGuid(), true, true, true, Buyer, Now, Correlation);
        changed.ShouldBeFalse();
        error!.Code.ShouldBe(PurchasingErrors.NotIssuableCode);
        order.Number.ShouldBeNull();
        order.Status.ShouldBe(PurchaseOrderStatus.Draft);
    }

    [Fact]
    public void T_PUR_005_a_T_PUR_006_a_IssueAllocatesTheNumberOnce_AndQueuesTheDocument()
    {
        var order = Draft();
        order.UpdateDraft("Rua Sintética 100", null, null, Today, Buyer, Now, Correlation).ShouldBeNull();
        var job = Guid.NewGuid();

        order.Issue("PO-2027-000042", Today, job, true, true, true, Buyer, Now, Correlation).ShouldBe((true, null));
        order.Number.ShouldBe("PO-2027-000042");
        order.Status.ShouldBe(PurchaseOrderStatus.Issued);
        order.IssueDate.ShouldBe(Today);
        order.DocumentStatus.ShouldBe(PurchaseOrderDocumentStatus.Pending);
        order.DocumentJobId.ShouldBe(job);
        order.DocumentAttempts.ShouldBe(1);
        order.DomainEvents.OfType<PurchaseOrderIssued>().Single().Number.ShouldBe("PO-2027-000042");

        order.Issue("PO-2027-000043", Today, Guid.NewGuid(), true, true, true, Buyer, Now, Correlation).ShouldBe((false, null));
        order.Number.ShouldBe("PO-2027-000042");
        PurchaseOrder.FormatNumber(2027, 7).ShouldBe("PO-2027-000007");
    }

    [Fact]
    public void T_PUR_007_a_DocumentAndNotificationFailures_AreRetryable_WithoutRenumbering()
    {
        var order = Issued();
        var job = order.DocumentJobId!.Value;
        order.RecordDocumentFailed(Guid.NewGuid(), "report.timeout", Now, Correlation).ShouldBeFalse();
        order.RetryNotification(Buyer, Now, Correlation)!.Code.ShouldBe(PurchasingErrors.NotRetryableCode);
        order.RetryDocument(Guid.NewGuid(), Buyer, Now, Correlation)!.Code.ShouldBe(PurchasingErrors.NotRetryableCode);
        order.RecordDocumentFailed(job, "report.output-invalid", Now, Correlation).ShouldBeTrue();
        order.DocumentStatus.ShouldBe(PurchaseOrderDocumentStatus.Failed);
        order.Status.ShouldBe(PurchaseOrderStatus.Issued);

        var retryJob = Guid.NewGuid();
        order.RetryDocument(retryJob, Buyer, Now, Correlation).ShouldBeNull();
        order.DocumentAttempts.ShouldBe(2);
        order.RecordDocumentReady(job, Guid.NewGuid(), "purchase-order-pdf.v1", Now, Correlation).ShouldBeFalse();
        var file = Guid.NewGuid();
        order.RecordDocumentReady(retryJob, file, "purchase-order-pdf.v1", Now, Correlation).ShouldBeTrue();
        order.RecordDocumentReady(retryJob, file, "purchase-order-pdf.v1", Now, Correlation).ShouldBeFalse();
        order.DocumentFileId.ShouldBe(file);
        order.NotificationStatus.ShouldBe(PurchaseOrderNotificationStatus.Pending);
        order.DomainEvents.OfType<PurchaseOrderNotificationRequested>().Single().Attempt.ShouldBe(1);

        order.RecordNotificationFailed(2, "x", Now, Correlation).ShouldBeFalse();
        order.RecordNotificationFailed(1, "notification.channel-refused", Now, Correlation).ShouldBeTrue();
        order.RetryNotification(Buyer, Now, Correlation).ShouldBeNull();
        order.NotificationAttempts.ShouldBe(2);
        order.RecordNotificationSent(1, Now, Correlation).ShouldBeFalse();
        order.RecordNotificationSent(2, Now, Correlation).ShouldBeTrue();
        order.RecordNotificationSent(2, Now, Correlation).ShouldBeFalse();
        order.NotificationStatus.ShouldBe(PurchaseOrderNotificationStatus.Sent);
        order.Number.ShouldBe("PO-2027-000001");
    }

    [Fact]
    public void T_PUR_008_a_DraftAndIssuedCancel_NeedAReason_AndStopOnceAccepted()
    {
        var draft = Draft();
        draft.Cancel("short", Buyer, Now, Correlation).Error!.Code.ShouldBe(DomainError.ReasonRequiredCode);
        draft.Cancel(Reason, Buyer, Now, Correlation).ShouldBe((PurchaseOrderStatus.Draft, true, null));
        draft.DomainEvents.OfType<PurchaseOrderCancelled>().Single().ReasonCode.ShouldBe("DraftCancelled");
        draft.Cancel(Reason, Buyer, Now, Correlation).Changed.ShouldBeFalse();

        var rejectedOnly = Issued();
        rejectedOnly.ApplyReceipt(Guid.NewGuid(), [new OrderReceiptLine(1, 2m, 0m, 2m)], 0m, Buyer, Now, Correlation).Error.ShouldBeNull();
        rejectedOnly.Status.ShouldBe(PurchaseOrderStatus.Issued);
        rejectedOnly.Cancel(Reason, Buyer, Now, Correlation).ShouldBe((PurchaseOrderStatus.Issued, true, null));

        var received = Issued();
        received.ApplyReceipt(Guid.NewGuid(), [new OrderReceiptLine(1, 1m, 1m, 0m)], 0m, Buyer, Now, Correlation).Error.ShouldBeNull();
        received.Cancel(Reason, Buyer, Now, Correlation).Error.ShouldBe(PurchasingErrors.HasReceipts);
    }

    [Fact]
    public void T_RCV_003_a_T_PUR_009_a_ReceiptsCompleteAtTheCap_AndOverReceiptIsNeverSilent()
    {
        var order = Issued();
        order.ApplyReceipt(Guid.NewGuid(), [new OrderReceiptLine(1, 4m, 4m, 0m)], 0m, Buyer, Now, Correlation).Change.ShouldBe(OrderCompletionChange.None);
        order.Status.ShouldBe(PurchaseOrderStatus.PartiallyReceived);
        order.Lines.Single().RemainingQuantity.ShouldBe(6m);

        var (_, error) = order.ApplyReceipt(Guid.NewGuid(), [new OrderReceiptLine(1, 7m, 7m, 0m)], 0m, Buyer, Now, Correlation);
        error!.Code.ShouldBe(PurchasingErrors.OverReceiptCode);
        error.Details!["cap"].ShouldBe("10");
        error.Details["resulting"].ShouldBe("11");
        order.Lines.Single().AcceptedQuantity.ShouldBe(4m);

        order.ApplyReceipt(Guid.NewGuid(), [new OrderReceiptLine(1, 6m, 6m, 0m)], 0m, Buyer, Now, Correlation).Change.ShouldBe(OrderCompletionChange.Completed);
        order.Status.ShouldBe(PurchaseOrderStatus.Completed);
        order.CompletionSequence.ShouldBe(1);
        order.DomainEvents.OfType<PurchaseOrderCompleted>().Single().ShortClosed.ShouldBeFalse();
        order.ApplyReceipt(Guid.NewGuid(), [new OrderReceiptLine(1, 1m, 1m, 0m)], 0m, Buyer, Now, Correlation).Error.ShouldBe(PurchasingErrors.OrderNotReceivable);
        order.ApplyReceipt(Guid.NewGuid(), [new OrderReceiptLine(9, 1m, 1m, 0m)], 0m, Buyer, Now, Correlation).Error.ShouldBe(PurchasingErrors.OrderNotReceivable);
    }

    [Fact]
    public void T_RCV_003_b_ATolerance_RaisesTheCap_AndAnUnknownLineIsRejected()
    {
        var order = Issued();
        order.ApplyReceipt(Guid.NewGuid(), [new OrderReceiptLine(1, 10.5m, 10.5m, 0m)], 5m, Buyer, Now, Correlation).Error!.Code.ShouldBe(PurchasingErrors.OverReceiptCode);
        order.ApplyReceipt(Guid.NewGuid(), [new OrderReceiptLine(1, 10m, 10m, 0m)], 5m, Buyer, Now, Correlation).Change.ShouldBe(OrderCompletionChange.Completed);
        Issued().ApplyReceipt(Guid.NewGuid(), [new OrderReceiptLine(2, 1m, 1m, 0m)], 0m, Buyer, Now, Correlation).Error!.Field.ShouldBe("lines[2].lineNumber");
        Draft().ApplyReceipt(Guid.NewGuid(), [new OrderReceiptLine(1, 1m, 1m, 0m)], 0m, Buyer, Now, Correlation).Error.ShouldBe(PurchasingErrors.OrderNotReceivable);
    }

    [Fact]
    public void T_RCV_007_a_T_PUR_009_b_CorrectionsReopenAndCompleteAgain_WithinZeroAndTheCap()
    {
        var order = Issued();
        var receipt = Guid.NewGuid();
        order.ApplyReceipt(receipt, [new OrderReceiptLine(1, 10m, 10m, 0m)], 0m, Buyer, Now, Correlation).Change.ShouldBe(OrderCompletionChange.Completed);
        order.ClearDomainEvents();

        order.ApplyCorrection(receipt, 1, 1m, 0m, Buyer, Now, Correlation).Error!.Code.ShouldBe(PurchasingErrors.CorrectionOutOfRangeCode);
        order.ApplyCorrection(receipt, 1, -11m, 0m, Buyer, Now, Correlation).Error!.Code.ShouldBe(PurchasingErrors.CorrectionOutOfRangeCode);
        order.ApplyCorrection(receipt, 1, 0m, 0m, Buyer, Now, Correlation).Error!.Code.ShouldBe(PurchasingErrors.CorrectionOutOfRangeCode);
        order.ApplyCorrection(receipt, 7, -1m, 0m, Buyer, Now, Correlation).Error!.Field.ShouldBe("poLineNumber");
        order.ApplyCorrection(receipt, 1, -1m, 0m, Buyer, Now, Correlation).Change.ShouldBe(OrderCompletionChange.Reopened);
        order.Status.ShouldBe(PurchaseOrderStatus.PartiallyReceived);
        order.DomainEvents.OfType<PurchaseOrderReopened>().Single().CompletionSequence.ShouldBe(1);
        order.ApplyCorrection(receipt, 1, -9m, 0m, Buyer, Now, Correlation).Change.ShouldBe(OrderCompletionChange.None);
        order.Status.ShouldBe(PurchaseOrderStatus.Issued);
        order.ApplyCorrection(receipt, 1, 10m, 0m, Buyer, Now, Correlation).Change.ShouldBe(OrderCompletionChange.Completed);
        order.CompletionSequence.ShouldBe(2);
        Draft().ApplyCorrection(receipt, 1, 1m, 0m, Buyer, Now, Correlation).Error!.Code.ShouldBe(DomainError.InvalidTransitionCode);
    }

    [Fact]
    public void T_PUR_010_a_T_CALC_008_a_ShortCloseReleasesTheUnreceivedCommitment_AndCompletes()
    {
        var order = Issued(Input(quantity: 10m, unitPrice: 100m, extraLines: [(4m, 50m)]));
        order.ShortClose(Reason, 1200m, Buyer, Now, Correlation).Error!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        order.ApplyReceipt(Guid.NewGuid(), [new OrderReceiptLine(1, 6m, 6m, 0m), new OrderReceiptLine(2, 4m, 4m, 0m)], 0m, Buyer, Now, Correlation)
            .Error.ShouldBeNull();
        order.ShortClose("no", 1200m, Buyer, Now, Correlation).Error!.Code.ShouldBe(DomainError.ReasonRequiredCode);

        var (release, error) = order.ShortClose(Reason, 1200m, Buyer, Now, Correlation);
        error.ShouldBeNull();
        release.ShouldBe(new Money(400m, "BRL"));
        order.Status.ShouldBe(PurchaseOrderStatus.Completed);
        order.ShortClosed.ShouldBeTrue();
        order.Lines.Single(line => line.LineNumber == 1).IsShortClosed.ShouldBeTrue();
        order.Lines.Single(line => line.LineNumber == 2).IsShortClosed.ShouldBeFalse();
        order.Lines.Sum(line => line.RemainingQuantity).ShouldBe(0m);
        order.DomainEvents.OfType<PurchaseOrderCompleted>().Single().ShortClosed.ShouldBeTrue();
        order.ApplyCorrection(Guid.NewGuid(), 1, -1m, 0m, Buyer, Now, Correlation).Error!.Details!["currentState"].ShouldBe("ShortClosed");

        var capped = Issued();
        capped.ApplyReceipt(Guid.NewGuid(), [new OrderReceiptLine(1, 1m, 1m, 0m)], 0m, Buyer, Now, Correlation).Error.ShouldBeNull();
        capped.ShortClose(Reason, 500m, Buyer, Now, Correlation).Release.ShouldBe(new Money(500m, "BRL"));
    }

    [Fact]
    public void T_CALC_008_b_T_CALC_009_a_ConversionAndCapRules()
    {
        CalculationRules.ShortCloseRelease([(10m, 4m, 100m)], "USD", 5.4321m, "BRL", 1m, "BRL", 100_000m).ShouldBe(new Money(3259.26m, "BRL"));
        CalculationRules.ShortCloseRelease([(10m, 12m, 100m)], "BRL", 1m, "BRL", 1m, "BRL", 1000m).ShouldBe(new Money(0m, "BRL"));
        CalculationRules.ReceiptCap(10m, 0m, 0).ShouldBe(10m);
        CalculationRules.ReceiptCap(10m, 5m, 0).ShouldBe(10m);
        CalculationRules.ReceiptCap(10m, 5m, 2).ShouldBe(10.5m);
        CalculationRules.ReceiptCap(3m, 33.3333m, 3).ShouldBe(3.999m);
        Should.Throw<ArgumentOutOfRangeException>(() => CalculationRules.ReceiptCap(0m, 0m, 0));
        Should.Throw<ArgumentOutOfRangeException>(() => CalculationRules.ReceiptCap(1m, -1m, 0));
    }

    [Fact]
    public void T_PUR_008_b_T_PUR_010_b_BudgetCommitmentMovesBackOrPartially_AndNeverTwice()
    {
        var period = FiscalPeriod.Create(Guid.NewGuid(), "FY27", "Fiscal year 2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31), null, Now).Period!;
        period.Open(Now);
        var allocation = BudgetAllocation.Create(Guid.NewGuid(), period, new MasterDataReference(Guid.NewGuid(), true), "BRL", true, 2000m, null, Now).Allocation!;
        var requisition = Guid.NewGuid();
        allocation.Reserve(Guid.NewGuid(), requisition, 1, new Money(1000m, "BRL"), period, Today, null, Now, Correlation).Error.ShouldBeNull();
        allocation.ReleasePartially(requisition, new Money(1m, "BRL"), null, Now, Correlation).Error!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        allocation.Uncommit(requisition, null, Now, Correlation).Changed.ShouldBeFalse();
        allocation.Commit(requisition, null, Now, Correlation).Error.ShouldBeNull();

        allocation.Uncommit(requisition, null, Now, Correlation).Changed.ShouldBeTrue();
        (allocation.ReservedAmount, allocation.CommittedAmount).ShouldBe((1000m, 0m));
        allocation.DomainEvents.OfType<BudgetReservationUncommitted>().Single().Amount.ShouldBe(new Money(1000m, "BRL"));
        allocation.Commit(requisition, null, Now, Correlation).Error.ShouldBeNull();

        allocation.ReleasePartially(requisition, new Money(0m, "BRL"), null, Now, Correlation).Changed.ShouldBeFalse();
        Should.Throw<ArgumentOutOfRangeException>(() => allocation.ReleasePartially(requisition, new Money(1000.01m, "BRL"), null, Now, Correlation));
        Should.Throw<ArgumentException>(() => allocation.ReleasePartially(requisition, new Money(1m, "USD"), null, Now, Correlation));
        var (reservation, changed, error) = allocation.ReleasePartially(requisition, new Money(400m, "BRL"), null, Now, Correlation);
        (changed, error).ShouldBe((true, null));
        reservation!.Amount.ShouldBe(600m);
        reservation.Status.ShouldBe(BudgetReservationStatus.Committed);
        allocation.CommittedAmount.ShouldBe(600m);
        allocation.AvailableAmount.ShouldBe(1400m);
        reservation.Changes.Last().Operation.ShouldBe(BudgetReservationOperation.PartiallyReleased);
        allocation.DomainEvents.OfType<BudgetReservationReleased>().Single().Reason.ShouldBe(BudgetReleaseReason.ShortClosed);
        Should.Throw<ArgumentException>(() => allocation.ReleasePartially(Guid.NewGuid(), new Money(1m, "BRL"), null, Now, ""));
        allocation.Uncommit(Guid.NewGuid(), null, Now, Correlation).Error!.Code.ShouldBe(DomainError.InvalidTransitionCode);
    }
}
