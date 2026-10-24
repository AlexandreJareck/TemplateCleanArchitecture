using System.Text.Json;
using Shouldly;
using Template.Application.Common.Results;
using Template.Application.Features.Approvals;
using Template.Application.Features.Purchasing;
using Template.Application.Features.Reporting;
using Template.Application.Features.Requisitions;
using Template.Application.Features.Sourcing;
using Template.Application.Interfaces.Messaging;
using Template.Application.Interfaces.Purchasing;
using Template.Application.Messaging.Procurement;
using Template.Application.Reporting;
using Template.Application.Security;
using Template.Application.Services.Reporting;
using Template.Application.Workflow;
using Template.Application.Wrappers;
using Template.Domain.Budgeting;
using Template.Domain.Common;
using Template.Domain.Files;
using Template.Domain.Purchasing;
using Template.Domain.Reporting;
using Template.Domain.Requisitions;
using Template.Domain.Sourcing;
using Template.Domain.Suppliers;
using Template.Unit.Tests.ApplicationTests.Features.Reporting;

namespace Template.Unit.Tests.ApplicationTests.Features.Procurement;

/// <summary>
/// Purchasing and receiving use cases against port fakes (PUR-001..011, RCV-001..008). The scenario is the SC-01 award
/// (18 × 900.00 BRL = 16,200.00 BRL) approved through both levels; the Worker consumer receives the committed Outbox
/// envelopes in process. All data is synthetic.
/// </summary>
public sealed class PurchasingHandlerTests
{
    private const string Reason = "Synthetic reason for the purchasing handler test";
    private const string Address = "Rua Sintética 100, São Paulo - SP";
    private readonly ReportingTestDoubles reporting = new();
    private int delivered;

    private ProcurementTestDoubles Doubles => reporting.Procurement;

    /// <summary>Organization-local date of the fake clock (the RFQ close moved it three days ahead).</summary>
    private DateOnly LocalToday => Template.Application.Services.Procurement.ProcurementWriter.LocalDate(Doubles.Clock.UtcNow, "America/Sao_Paulo");
    private ProcurementTestDoubles.Actor Manager { get; } = new();
    private ProcurementTestDoubles.Actor Head { get; } = new();
    private ProcurementTestDoubles.Actor ProcurementManager { get; } = new();
    private ProcurementTestDoubles.Actor Receiver { get; } = new();

    [Fact]
    public async Task T_PUR_001_a_ApprovalCompletion_CreatesExactlyOneDraft_ForDuplicatesAndCopies()
    {
        var (rfq, award) = await ApprovedAsync();
        var completed = Doubles.Outbox.Events.Single(envelope => envelope.EventType == "procurement.approval.completed");

        (await Doubles.PurchasingEvents().HandleAsync(completed, default)).ShouldBe(InboxApplyResult.Applied);
        (await Doubles.PurchasingEvents().HandleAsync(completed, default)).ShouldBe(InboxApplyResult.Duplicate);
        (await Doubles.PurchasingEvents().HandleAsync(completed with { EventId = Guid.NewGuid() }, default)).ShouldBe(InboxApplyResult.Applied);

        var order = Doubles.Orders.Items.ShouldHaveSingleItem();
        order.AwardId.ShouldBe(award);
        order.RfqId.ShouldBe(rfq.Id);
        order.Total.ShouldBe(16_200m);
        order.Lines.Single().Quantity.ShouldBe(18m);
        order.Lines.Single().UnitPrice.ShouldBe(900m);
        order.Lines.Single().UnitAllowedDecimals.ShouldBe(0);
        order.Lines.Single().PromisedDeliveryDate.ShouldBe(LocalToday.AddDays(7));
        order.SupplierContactEmail.ShouldBe("contact@example.invalid");
        order.SupplierAddress!.ShouldContain("São Paulo - SP");
        order.BuyerId.ShouldBe(reporting.Buyer.Id);
        order.BuyerName.ShouldBeNull();
        Doubles.Requisitions.Items.Single(item => item.Id == order.RequisitionId).PurchaseOrderId.ShouldBe(order.Id);
        Doubles.Outbox.Events.Count(envelope => envelope.EventType == "procurement.purchase-order.drafted").ShouldBe(1);
        Doubles.Audit.Records.Single(record => record.Action == PurchasingAudit.Created).Details["awardId"].ShouldBe(award.ToString());
    }

    [Fact]
    public async Task T_PUR_001_b_ADuplicateRaceOnTheAward_AndALeftRequisition_AreSettledWithoutAnOrder()
    {
        var (_, _) = await ApprovedAsync();
        var completed = Doubles.Outbox.Events.Single(envelope => envelope.EventType == "procurement.approval.completed");
        Doubles.UnitOfWork.UniqueViolation = IPurchaseOrderRepository.AwardUniqueKey;
        (await Doubles.PurchasingEvents().HandleAsync(completed, default)).ShouldBe(InboxApplyResult.Duplicate);
        Doubles.UnitOfWork.UniqueViolation = null;

        Doubles.UnitOfWork.ThrowConflict = true;
        await Should.ThrowAsync<InvalidOperationException>(() => Doubles.PurchasingEvents().HandleAsync(completed with { EventId = Guid.NewGuid() }, default));
        Doubles.UnitOfWork.ThrowConflict = false;

        var requisition = Doubles.Requisitions.Items.Single();
        Doubles.ActAs(ProcurementManager);
        Doubles.Permissions.Grant(ProcurementManager.Id, SeededRoles.PermissionsByRole[SeededRoles.ProcurementManager]);
        (await Doubles.Transitions().Handle(new CancelRequisitionCommand(requisition.Id, requisition.Version, Reason), default)).IsSuccess.ShouldBeTrue();
        Doubles.ActAsSystem();
        Doubles.Orders.Items.Clear();
        (await Doubles.PurchasingEvents().HandleAsync(completed with { EventId = Guid.NewGuid() }, default)).ShouldBe(InboxApplyResult.Applied);
        Doubles.Orders.Items.ShouldBeEmpty();
        await Should.ThrowAsync<JsonException>(() => Doubles.PurchasingEvents().HandleAsync(completed with { EventType = "procurement.rfq.closed" }, default));
    }

    [Fact]
    public async Task T_PUR_003_b_IssueGuards_AreCheckedBeforeANumberIsAllocated()
    {
        var order = await DraftAsync();
        var supplier = Doubles.SupplierEntities.Items.Single();

        var incomplete = await AsBuyer(handlers => handlers.Handle(new IssuePurchaseOrderCommand(order.Id, order.Version), default));
        incomplete.Error!.Code.ShouldBe(PurchasingErrors.NotIssuableCode);
        Doubles.OrderNumbers.Calls.ShouldBe(0);
        (await AsBuyer(handlers => handlers.Handle(new UpdatePurchaseOrderDraftCommand(order.Id, order.Version, Address, null, null), default))).IsSuccess.ShouldBeTrue();

        supplier.Suspend(Reason, null, ProcurementTestDoubles.Now).ShouldBeNull();
        var suspended = await AsBuyer(handlers => handlers.Handle(new IssuePurchaseOrderCommand(order.Id, order.Version), default));
        suspended.Error!.Code.ShouldBe("supplier.not-eligible");
        Doubles.OrderNumbers.Calls.ShouldBe(0);
        supplier.Reactivate(Reason, null, ProcurementTestDoubles.Now).ShouldBeNull();

        (await AsBuyer(handlers => handlers.Handle(new IssuePurchaseOrderCommand(order.Id, order.Version - 1), default))).Error!.Category
            .ShouldBe(ErrorCategory.PreconditionFailed);
        (await AsBuyer(handlers => handlers.Handle(new IssuePurchaseOrderCommand(order.Id, null), default))).Error!.Category
            .ShouldBe(ErrorCategory.PreconditionRequired);
        (await AsBuyer(handlers => handlers.Handle(new IssuePurchaseOrderCommand(Guid.NewGuid(), 1), default))).Error!.Code.ShouldBe(ProblemCodes.NotFound);
    }

    [Fact]
    public async Task T_PUR_005_b_T_PUR_006_b_IssueNumbersOnce_QueuesTheDocument_AndOrdersTheRequisition()
    {
        var order = await IssuedAsync();

        order.Number.ShouldBe("PO-2027-000001");
        (order.BuyerId, order.BuyerName).ShouldBe((reporting.Buyer.Id, $"Synthetic {reporting.Buyer.Id.ToString()[..4]}"));
        Doubles.OrderNumbers.Calls.ShouldBe(1);
        var job = Doubles.ReportJobs.Items.ShouldHaveSingleItem();
        job.ReportType.ShouldBe(ReportType.PurchaseOrderPdf);
        job.Id.ShouldBe(order.DocumentJobId!.Value);
        job.RequestedBy.ShouldBe(reporting.Buyer.Id);
        Doubles.Requisitions.Items.Single().Status.ShouldBe(RequisitionStatus.Ordered);

        var repeat = await AsBuyer(handlers => handlers.Handle(new IssuePurchaseOrderCommand(order.Id, 1), default));
        repeat.Value.Number.ShouldBe("PO-2027-000001");
        Doubles.OrderNumbers.Calls.ShouldBe(1);
        Doubles.Outbox.Events.Count(envelope => envelope.EventType == "procurement.purchase-order.issued").ShouldBe(1);
        Doubles.Audit.Records.Single(record => record.Action == PurchasingAudit.Issued).Details["number"].ShouldBe("PO-2027-000001");
    }

    [Fact]
    public async Task T_PUR_005_c_ALostCommitRace_Answers412_AndRollsBack()
    {
        var order = await DraftAsync();
        (await AsBuyer(handlers => handlers.Handle(new UpdatePurchaseOrderDraftCommand(order.Id, order.Version, Address, null, null), default))).IsSuccess.ShouldBeTrue();
        Doubles.UnitOfWork.ThrowConflict = true;
        var result = await AsBuyer(handlers => handlers.Handle(new IssuePurchaseOrderCommand(order.Id, order.Version), default));
        result.Error!.Category.ShouldBe(ErrorCategory.PreconditionFailed);
        Doubles.UnitOfWork.Rollbacks.ShouldBeGreaterThan(0);
        Doubles.UnitOfWork.ThrowConflict = false;
    }

    [Fact]
    public async Task T_PUR_007_b_TheWorkerRecordsTheDocument_ThenNotifiesTheSupplierOnce_AndFailuresAreRetryable()
    {
        var order = await IssuedAsync();
        var job = Doubles.ReportJobs.Items.Single();

        var file = Guid.NewGuid();
        Complete(job, file);
        var ready = ProcurementIntegrationEventContract.FromDomainEvent(new ReportReady(ProcurementTestDoubles.Now, job.Id, job.ReportType, job.RequestedBy, file), "t");
        (await Doubles.PurchasingEvents().HandleAsync(ready, default)).ShouldBe(InboxApplyResult.Applied);
        order.DocumentStatus.ShouldBe(PurchaseOrderDocumentStatus.Ready);
        order.DocumentFileId.ShouldBe(file);
        order.DocumentTemplateVersion.ShouldBe("purchase-order-pdf.v1");
        var requested = Doubles.Outbox.Events.Single(envelope => envelope.EventType == "procurement.purchase-order.notification-requested");
        Doubles.Notifier.FailureCode = "notification.channel-refused";
        (await Doubles.PurchasingEvents().HandleAsync(requested, default)).ShouldBe(InboxApplyResult.Applied);
        order.NotificationStatus.ShouldBe(PurchaseOrderNotificationStatus.Failed);
        Doubles.Notifier.Sent.ShouldBeEmpty();

        Doubles.Notifier.FailureCode = null;
        (await AsBuyer(handlers => handlers.Handle(new RetryPurchaseOrderNotificationCommand(order.Id, order.Version), default))).IsSuccess.ShouldBeTrue();
        var retry = Doubles.Outbox.Events.Last(envelope => envelope.EventType == "procurement.purchase-order.notification-requested");
        (await Doubles.PurchasingEvents().HandleAsync(requested with { EventId = Guid.NewGuid() }, default)).ShouldBe(InboxApplyResult.Applied);
        Doubles.Notifier.Sent.ShouldBeEmpty();
        (await Doubles.PurchasingEvents().HandleAsync(retry, default)).ShouldBe(InboxApplyResult.Applied);
        (await Doubles.PurchasingEvents().HandleAsync(retry, default)).ShouldBe(InboxApplyResult.Duplicate);
        var sent = Doubles.Notifier.Sent.ShouldHaveSingleItem();
        (sent.SupplierId, sent.Attempt, sent.DocumentFileId, sent.Template).ShouldBe((order.SupplierId, 2, file, PurchasingAudit.SupplierNotificationTemplate));
        order.NotificationStatus.ShouldBe(PurchaseOrderNotificationStatus.Sent);
        Doubles.Audit.Records.Select(record => record.Action).ShouldContain(PurchasingAudit.NotificationFailed);

        var other = ProcurementIntegrationEventContract.FromDomainEvent(
            new ReportReady(ProcurementTestDoubles.Now, Guid.NewGuid(), ReportType.RequisitionPdf, job.RequestedBy, Guid.NewGuid()), "t");
        (await Doubles.PurchasingEvents().HandleAsync(other, default)).ShouldBe(InboxApplyResult.Applied);
        RfqNotificationPlanner.Plan(ready).ShouldBeEmpty();
    }

    [Fact]
    public async Task T_PUR_007_c_AFailedDocument_IsVisible_AndARetryQueuesANewJob()
    {
        var order = await IssuedAsync();
        var job = Doubles.ReportJobs.Items.Single();
        var failed = ProcurementIntegrationEventContract.FromDomainEvent(
            new ReportFailed(ProcurementTestDoubles.Now, job.Id, job.ReportType, job.RequestedBy, ReportFailureCodes.RendererUnavailable), "t");
        (await Doubles.PurchasingEvents().HandleAsync(failed, default)).ShouldBe(InboxApplyResult.Applied);
        order.DocumentStatus.ShouldBe(PurchaseOrderDocumentStatus.Failed);
        order.DocumentFailureCode.ShouldBe(ReportFailureCodes.RendererUnavailable);
        var view = await AsBuyer(handlers => handlers.Handle(new GetPurchaseOrderQuery(order.Id), default));
        view.Value.AllowedActions.ShouldContain(PurchasingAudit.ActionRetryDocument);
        (await AsBuyer(handlers => handlers.Handle(new DownloadPurchaseOrderDocumentQuery(order.Id), default))).Error!.Code
            .ShouldBe(PurchasingErrors.DocumentNotReadyCode);

        (await AsBuyer(handlers => handlers.Handle(new RetryPurchaseOrderDocumentCommand(order.Id, order.Version), default))).IsSuccess.ShouldBeTrue();
        Doubles.ReportJobs.Items.Count.ShouldBe(2);
        order.DocumentJobId.ShouldBe(Doubles.ReportJobs.Items[1].Id);
        (await AsBuyer(handlers => handlers.Handle(new RetryPurchaseOrderDocumentCommand(order.Id, order.Version), default))).Error!.Code
            .ShouldBe(PurchasingErrors.NotRetryableCode);
        order.Number.ShouldBe("PO-2027-000001");
    }

    [Fact]
    public async Task T_PUR_007_d_TheIssuedDocument_IsStreamedOnlyToPriceReaders_AndAnOutageAnswers503()
    {
        var order = await IssuedAsync();
        var job = Doubles.ReportJobs.Items.Single();
        var (file, _) = StoredFile.CreatePending(Guid.NewGuid(), "ReportJob", job.Id, "pedido-PO-2027-000001.pdf", ReportCatalog.PdfMediaType,
            FileClassification.Confidential, null, ProcurementTestDoubles.Now, null);
        Doubles.Storage.Objects[file!.Id] = "%PDF-1.7 unit"u8.ToArray();
        file.MarkAvailable(13, new string('a', 64), long.MaxValue, ProcurementTestDoubles.Now).ShouldBeNull();
        Doubles.Files.Add(file);
        order.RecordDocumentReady(job.Id, file.Id, job.TemplateVersion, ProcurementTestDoubles.Now, "t").ShouldBeTrue();

        var download = await AsBuyer(handlers => handlers.Handle(new DownloadPurchaseOrderDocumentQuery(order.Id), default));
        download.IsSuccess.ShouldBeTrue();
        (download.Value.MediaType, download.Value.TemplateVersion).ShouldBe((ReportCatalog.PdfMediaType, "purchase-order-pdf.v1"));
        Doubles.Audit.Records.Select(record => record.Action).ShouldContain(PurchasingAudit.DocumentDownloaded);
        Doubles.Storage.UnavailableOnRead = true;
        (await AsBuyer(handlers => handlers.Handle(new DownloadPurchaseOrderDocumentQuery(order.Id), default))).Error!.Category.ShouldBe(ErrorCategory.Unavailable);
        Doubles.Storage.UnavailableOnRead = false;
        GrantReceiver();
        Doubles.ActAs(Receiver);
        (await Doubles.PurchaseOrders().Handle(new DownloadPurchaseOrderDocumentQuery(order.Id), default)).Error!.Code.ShouldBe(ProblemCodes.NotFound);
        Doubles.ActAsSystem();
    }

    [Fact]
    public async Task T_PUR_008_c_DraftCancel_WithdrawsTheAward_AndReturnsTheCommitmentToReserved()
    {
        var order = await DraftAsync();
        var rfq = Doubles.Rfqs.Items.Single();

        var cancelled = await AsBuyer(handlers => handlers.Handle(new CancelPurchaseOrderCommand(order.Id, order.Version, Reason), default));
        cancelled.IsSuccess.ShouldBeTrue(cancelled.Error?.Message);
        order.Status.ShouldBe(PurchaseOrderStatus.Cancelled);
        rfq.Status.ShouldBe(RfqStatus.Closed);
        rfq.Awards.Single().Status.ShouldBe(AwardStatus.Withdrawn);
        Doubles.Requisitions.Items.Single().Status.ShouldBe(RequisitionStatus.InSourcing);
        var reservation = Doubles.Allocation.Reservations.Single();
        reservation.Status.ShouldBe(BudgetReservationStatus.Reserved);
        Doubles.Allocation.CommittedAmount.ShouldBe(0m);
        Doubles.Outbox.Events.Count(envelope => envelope.EventType == "procurement.budget.reservation-uncommitted").ShouldBe(1);
        (await AsBuyer(handlers => handlers.Handle(new CancelPurchaseOrderCommand(order.Id, order.Version, Reason), default))).IsSuccess.ShouldBeTrue();
        Doubles.Outbox.Events.Count(envelope => envelope.EventType == "procurement.purchase-order.cancelled").ShouldBe(1);
    }

    [Fact]
    public async Task T_PUR_008_d_IssuedCancel_IsLimitedToCancelAny_AndReleasesTheCommitmentOnce()
    {
        var order = await IssuedAsync();
        (await AsBuyer(handlers => handlers.Handle(new CancelPurchaseOrderCommand(order.Id, order.Version, Reason), default))).Error!.Category
            .ShouldBe(ErrorCategory.Forbidden);

        var cancelled = await AsManager(handlers => handlers.Handle(new CancelPurchaseOrderCommand(order.Id, order.Version, Reason), default));
        cancelled.IsSuccess.ShouldBeTrue(cancelled.Error?.Message);
        Doubles.Requisitions.Items.Single().Status.ShouldBe(RequisitionStatus.Cancelled);
        var reservation = Doubles.Allocation.Reservations.Single();
        (reservation.Status, reservation.ReleaseReason).ShouldBe((BudgetReservationStatus.Released, BudgetReleaseReason.OrderCancelled));
        Doubles.Allocation.CommittedAmount.ShouldBe(0m);
        Doubles.Outbox.Events.Count(envelope => envelope.EventType == "procurement.budget.reservation-released").ShouldBe(1);
    }

    [Fact]
    public async Task T_SC_07_PartialReceipts_Correction_AndCompletion_FollowTheRequisition()
    {
        var order = await IssuedAsync();
        GrantReceiver();

        (await ReceiveAsync(order, "4", "4", "0")).Value.PurchaseOrder.Status.ShouldBe("PartiallyReceived");
        var over = await ReceiveAsync(order, "15", "15", "0");
        over.Error!.Code.ShouldBe(PurchasingErrors.OverReceiptCode);
        Doubles.Receipts.Items.Count.ShouldBe(1);
        var final = await ReceiveAsync(order, "14", "14", "0");
        final.Value.PurchaseOrder.Status.ShouldBe("Completed");
        Doubles.Requisitions.Items.Single().Status.ShouldBe(RequisitionStatus.Closed);

        var receipt = Doubles.Receipts.Items.Last();
        Doubles.ActAs(Receiver);
        var corrected = await Doubles.ReceiptHandlers().Handle(new CorrectReceiptCommand(receipt.Id, receipt.Version, 1, "-1", Reason), default);
        corrected.IsSuccess.ShouldBeTrue(corrected.Error?.Message);
        corrected.Value.PurchaseOrder.Status.ShouldBe("PartiallyReceived");
        (await Doubles.ReceiptHandlers().Handle(new CorrectReceiptCommand(receipt.Id, receipt.Version, 1, "abc", Reason), default)).Error!.Category
            .ShouldBe(ErrorCategory.Validation);
        (await Doubles.ReceiptHandlers().Handle(new CorrectReceiptCommand(receipt.Id, receipt.Version - 1, 1, "1", Reason), default)).Error!.Category
            .ShouldBe(ErrorCategory.PreconditionFailed);
        (await Doubles.ReceiptHandlers().Handle(new CorrectReceiptCommand(Guid.NewGuid(), 1, 1, "1", Reason), default)).Error!.Code.ShouldBe(ProblemCodes.NotFound);
        Doubles.ActAsSystem();
        Doubles.Requisitions.Items.Single().Status.ShouldBe(RequisitionStatus.Ordered);
        (await ReceiveAsync(order, "1", "1", "0")).Value.PurchaseOrder.Status.ShouldBe("Completed");
        Doubles.Requisitions.Items.Single().Status.ShouldBe(RequisitionStatus.Closed);

        Doubles.Outbox.Events.Count(envelope => envelope.EventType == "procurement.purchase-order.completed").ShouldBe(2);
        Doubles.Outbox.Events.Count(envelope => envelope.EventType == "procurement.purchase-order.reopened").ShouldBe(1);
        Doubles.Outbox.Events.Count(envelope => envelope.EventType == "procurement.receipt.posted").ShouldBe(3);
        Doubles.Outbox.Events.Count(envelope => envelope.EventType == "procurement.receipt.corrected").ShouldBe(1);
        Doubles.Orders.UpdateLocks.ShouldBeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public async Task T_RCV_004_b_Discrepancies_AreAuditedWithoutNotes_AndQueriesFilterThem()
    {
        var order = await IssuedAsync();
        GrantReceiver();
        var posted = await ReceiveAsync(order, "3", "1", "2", "Damaged", "Boxes crushed in transit");
        posted.IsSuccess.ShouldBeTrue(posted.Error?.Message);
        posted.Value.Receipt.HasDiscrepancy.ShouldBeTrue();
        Doubles.Outbox.Events.Count(envelope => envelope.EventType == "procurement.receipt.discrepancy-recorded").ShouldBe(1);
        var audit = Doubles.Audit.Records.Single(record => record.Action == PurchasingAudit.ReceiptDiscrepancyRecorded);
        audit.Details["lines"].ShouldBe("1:Damaged");
        audit.Details.Values.ShouldNotContain(value => value.Contains("crushed"));

        (await ReceiveAsync(order, "1", "1", "0", "Unknown", null)).Error!.Field().ShouldBe("lines[1].discrepancyType");
        (await ReceiveAsync(order, "x", "1", "0")).Error!.Category.ShouldBe(ErrorCategory.Validation);
        Doubles.ActAs(Receiver);
        var list = await Doubles.ReceiptHandlers().Handle(new ListReceiptsQuery(HasDiscrepancy: true), default);
        list.Value.Items.ShouldHaveSingleItem().Lines.Single().DiscrepancyNotes.ShouldBe("Boxes crushed in transit");
        list.Value.Items.Single().AllowedActions.ShouldBe([PurchasingAudit.ActionCorrect]);
        (await Doubles.ReceiptHandlers().Handle(new GetReceiptQuery(posted.Value.Receipt.Id), default)).IsSuccess.ShouldBeTrue();
        (await Doubles.ReceiptHandlers().Handle(new ListReceiptsQuery(ReceivedFrom: LocalToday, ReceivedTo: LocalToday.AddDays(-1)), default))
            .Error!.Category.ShouldBe(ErrorCategory.Validation);
        Doubles.ActAs(Doubles.Requester);
        (await Doubles.ReceiptHandlers().Handle(new ListReceiptsQuery(), default)).Error!.Category.ShouldBe(ErrorCategory.Forbidden);
        (await Doubles.ReceiptHandlers().Handle(new GetReceiptQuery(posted.Value.Receipt.Id), default)).Error!.Code.ShouldBe(ProblemCodes.NotFound);
        Doubles.ActAsSystem();
        (await Doubles.ReceiptHandlers().Handle(new PostReceiptCommand(order.Id, LocalToday, null, null, null, [], null), default)).Error!.Category
            .ShouldBe(ErrorCategory.Forbidden);
    }

    [Fact]
    public async Task T_PUR_010_c_ShortClose_ReleasesTheUnreceivedCommitment_WithTheWaiverAudit()
    {
        var order = await IssuedAsync();
        GrantReceiver();
        (await ReceiveAsync(order, "6", "6", "0")).IsSuccess.ShouldBeTrue();

        var closed = await AsManager(handlers => handlers.Handle(new ShortClosePurchaseOrderCommand(order.Id, order.Version, Reason), default));
        closed.IsSuccess.ShouldBeTrue(closed.Error?.Message);
        closed.Value.Status.ShouldBe("Completed");
        Doubles.Allocation.CommittedAmount.ShouldBe(5_400m);
        Doubles.Requisitions.Items.Single().Status.ShouldBe(RequisitionStatus.Closed);
        var waiver = Doubles.Audit.Records.Single(record => record.Action == PurchasingAudit.WaiverRecorded);
        (waiver.Details["waiverType"], waiver.Details["permission"]).ShouldBe(("ShortClose", Permissions.PurchasingOrdersShortClose));
        Doubles.Audit.Records.Single(record => record.Action == PurchasingAudit.ShortClosed).Details["releasedAmount"].ShouldBe("10800.00");
        (await AsManager(handlers => handlers.Handle(new ShortClosePurchaseOrderCommand(order.Id, order.Version, Reason), default))).Error!.Code
            .ShouldBe(DomainError.InvalidTransitionCode);
    }

    [Fact]
    public async Task T_PUR_011_a_VisibilityAndPriceHiding_AndListFilters()
    {
        var order = await DraftAsync();
        GrantReceiver();
        Doubles.ActAs(Receiver);
        (await Doubles.PurchaseOrders().Handle(new GetPurchaseOrderQuery(order.Id), default)).Error!.Code.ShouldBe(ProblemCodes.NotFound);
        (await Doubles.PurchaseOrders().Handle(new ListPurchaseOrdersQuery(), default)).Value.Items.ShouldBeEmpty();
        Doubles.ActAs(Doubles.Requester);
        var own = (await Doubles.PurchaseOrders().Handle(new GetPurchaseOrderQuery(order.Id), default)).Value;
        (own.PricesVisible, own.Total, own.Supplier.Contact, own.Supplier.Address).ShouldBe((false, null, null, null));
        own.AllowedActions.ShouldBeEmpty();
        (await Doubles.PurchaseOrders().Handle(new GetPurchaseOrderHistoryQuery(order.Id), default)).Value.Entries.ShouldHaveSingleItem();
        Doubles.ActAs(new ProcurementTestDoubles.Actor());
        (await Doubles.PurchaseOrders().Handle(new GetPurchaseOrderHistoryQuery(order.Id), default)).Error!.Code.ShouldBe(ProblemCodes.NotFound);
        Doubles.ActAsSystem();

        var buyerView = (await AsBuyer(handlers => handlers.Handle(new GetPurchaseOrderQuery(order.Id), default))).Value;
        (buyerView.PricesVisible, buyerView.Total!.Amount, buyerView.Supplier.Contact!.Email).ShouldBe((true, "16200.00", "contact@example.invalid"));
        buyerView.AllowedActions.ShouldBe([PurchasingAudit.ActionUpdate, PurchasingAudit.ActionIssue, PurchasingAudit.ActionCancel]);
        (await AsBuyer(handlers => handlers.Handle(new ListPurchaseOrdersQuery(Status: "Draft", Q: "REQ"), default))).Value.TotalItems.ShouldBe(1);
        (await AsBuyer(handlers => handlers.Handle(new ListPurchaseOrdersQuery(Status: "Shipped"), default))).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await AsBuyer(handlers => handlers.Handle(new ListPurchaseOrdersQuery(DocumentStatus: "Nope"), default))).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await AsBuyer(handlers => handlers.Handle(new ListPurchaseOrdersQuery(NotificationStatus: "Nope"), default))).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await AsBuyer(handlers => handlers.Handle(new ListPurchaseOrdersQuery(Sort: "supplier"), default))).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await AsBuyer(handlers => handlers.Handle(new UpdatePurchaseOrderDraftCommand(order.Id, order.Version, null, null,
            [new PromisedDateChange(1, LocalToday), new PromisedDateChange(1, LocalToday)]), default))).Error!.Category
            .ShouldBe(ErrorCategory.Validation);
        new UpdatePurchaseOrderDraftCommandValidator().Validate(new UpdatePurchaseOrderDraftCommand(order.Id, 1, new string('a', 501), null, null)).IsValid.ShouldBeFalse();
        new PostReceiptCommandValidator().Validate(new PostReceiptCommand(Guid.Empty, null, null, null, null, null, null)).IsValid.ShouldBeFalse();
        new CorrectReceiptCommandValidator().Validate(new CorrectReceiptCommand(Guid.NewGuid(), 1, 0, null, null)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task T_PUR_003_c_IssueRechecksApprovalSupplierAndCommitment_AndAConcurrentNumberAnswers412()
    {
        var order = await DraftAsync();
        (await AsBuyer(handlers => handlers.Handle(new UpdatePurchaseOrderDraftCommand(order.Id, order.Version, Address, null, null), default))).IsSuccess.ShouldBeTrue();
        var approval = Doubles.Approvals.Items.Single();
        var supplier = Doubles.SupplierEntities.Items.Single();

        Doubles.Approvals.Items.Clear();
        (await AsBuyer(handlers => handlers.Handle(new IssuePurchaseOrderCommand(order.Id, order.Version), default))).Error.ShouldNotBeNull();
        Doubles.Approvals.Items.Add(approval);
        Doubles.SupplierEntities.Items.Clear();
        (await AsBuyer(handlers => handlers.Handle(new IssuePurchaseOrderCommand(order.Id, order.Version), default))).Error!.Code.ShouldBe("supplier.not-eligible");
        Doubles.SupplierEntities.Items.Add(supplier);
        Doubles.Allocations.Items.Clear();
        (await AsBuyer(handlers => handlers.Handle(new IssuePurchaseOrderCommand(order.Id, order.Version), default))).Error.ShouldNotBeNull();
        Doubles.Allocations.Items.Add(Doubles.Allocation);
        Doubles.OrderNumbers.Calls.ShouldBe(0);
        order.Number.ShouldBeNull();

        Doubles.UnitOfWork.UniqueViolation = IPurchaseOrderRepository.NumberUniqueKey;
        var lost = await AsBuyer(handlers => handlers.Handle(new IssuePurchaseOrderCommand(order.Id, order.Version), default));
        Doubles.UnitOfWork.UniqueViolation = null;
        (lost.Error!.Code, lost.Error.Category).ShouldBe((ProblemCodes.VersionMismatch, ErrorCategory.PreconditionFailed));
    }

    [Fact]
    public async Task T_PUR_007_f_AMissingOrPendingFile_IsNotDownloadable_AndStaleDocumentEventsAreNoOps()
    {
        var order = await IssuedAsync();
        var job = Doubles.ReportJobs.Items.Single();
        var fileId = Guid.NewGuid();
        var ready = ProcurementIntegrationEventContract.FromDomainEvent(new ReportReady(ProcurementTestDoubles.Now, job.Id, job.ReportType, job.RequestedBy, fileId), "t");
        (await Doubles.PurchasingEvents().HandleAsync(ready, default)).ShouldBe(InboxApplyResult.Applied);
        order.DocumentStatus.ShouldBe(PurchaseOrderDocumentStatus.Pending); // the job has no output yet

        Complete(job, fileId);
        (await Doubles.PurchasingEvents().HandleAsync(ready with { EventId = Guid.NewGuid() }, default)).ShouldBe(InboxApplyResult.Applied);
        order.DocumentStatus.ShouldBe(PurchaseOrderDocumentStatus.Ready);
        (await Doubles.PurchasingEvents().HandleAsync(ready with { EventId = Guid.NewGuid() }, default)).ShouldBe(InboxApplyResult.Applied);
        var failed = ProcurementIntegrationEventContract.FromDomainEvent(
            new ReportFailed(ProcurementTestDoubles.Now, job.Id, job.ReportType, job.RequestedBy, ReportFailureCodes.RendererUnavailable), "t");
        (await Doubles.PurchasingEvents().HandleAsync(failed, default)).ShouldBe(InboxApplyResult.Applied);
        order.DocumentStatus.ShouldBe(PurchaseOrderDocumentStatus.Ready);
        foreach (var unknown in new[]
        {
            ProcurementIntegrationEventContract.FromDomainEvent(new ReportReady(ProcurementTestDoubles.Now, Guid.NewGuid(), ReportType.PurchaseOrderPdf, job.RequestedBy, fileId), "t"),
            ProcurementIntegrationEventContract.FromDomainEvent(
                new ReportFailed(ProcurementTestDoubles.Now, Guid.NewGuid(), ReportType.PurchaseOrderPdf, job.RequestedBy, ReportFailureCodes.Unexpected), "t")
        })
            (await Doubles.PurchasingEvents().HandleAsync(unknown, default)).ShouldBe(InboxApplyResult.Applied);
        Doubles.Audit.Records.Count(record => record.Action == PurchasingAudit.DocumentReady).ShouldBe(1);
        Doubles.Audit.Records.ShouldNotContain(record => record.Action == PurchasingAudit.DocumentFailed);

        (await AsBuyer(handlers => handlers.Handle(new DownloadPurchaseOrderDocumentQuery(order.Id), default))).Error!.Code
            .ShouldBe(PurchasingErrors.DocumentNotReadyCode);
        var (file, _) = StoredFile.CreatePending(fileId, "ReportJob", job.Id, "pedido.pdf", ReportCatalog.PdfMediaType, FileClassification.Confidential, null,
            ProcurementTestDoubles.Now, null);
        Doubles.Files.Add(file!);
        (await AsBuyer(handlers => handlers.Handle(new DownloadPurchaseOrderDocumentQuery(order.Id), default))).Error!.Code
            .ShouldBe(PurchasingErrors.DocumentNotReadyCode);
        (await AsBuyer(handlers => handlers.Handle(new DownloadPurchaseOrderDocumentQuery(Guid.NewGuid()), default))).Error!.Code.ShouldBe(ProblemCodes.NotFound);
    }

    [Fact]
    public async Task T_PUR_008_e_DraftCancel_WithoutTheRfqOrAllocation_StillReturnsTheRequisitionToSourcing()
    {
        var order = await DraftAsync();
        Doubles.Rfqs.Items.Clear();
        Doubles.Allocations.Items.Clear();

        var cancelled = await AsBuyer(handlers => handlers.Handle(new CancelPurchaseOrderCommand(order.Id, order.Version, Reason), default));
        cancelled.IsSuccess.ShouldBeTrue(cancelled.Error?.Message);
        Doubles.Requisitions.Items.Single().Status.ShouldBe(RequisitionStatus.InSourcing);
        Doubles.Audit.Records.ShouldNotContain(record => record.Action == "award.withdrawn");
        Doubles.Outbox.Events.ShouldNotContain(envelope => envelope.EventType == "procurement.budget.reservation-uncommitted");
        (await AsBuyer(handlers => handlers.Handle(new CancelPurchaseOrderCommand(Guid.NewGuid(), 1, Reason), default))).Error!.Code.ShouldBe(ProblemCodes.NotFound);
        (await AsBuyer(handlers => handlers.Handle(new RetryPurchaseOrderNotificationCommand(order.Id, order.Version - 1), default))).Error!.Category
            .ShouldBe(ErrorCategory.PreconditionFailed);
    }

    [Fact]
    public async Task T_PUR_008_f_IssuedCancel_WithoutAnAllocation_CancelsTheRequisitionWithoutARelease()
    {
        var order = await IssuedAsync();
        Doubles.Allocations.Items.Clear();

        var cancelled = await AsManager(handlers => handlers.Handle(new CancelPurchaseOrderCommand(order.Id, order.Version, Reason), default));
        cancelled.IsSuccess.ShouldBeTrue(cancelled.Error?.Message);
        Doubles.Requisitions.Items.Single().Status.ShouldBe(RequisitionStatus.Cancelled);
        Doubles.Outbox.Events.ShouldNotContain(envelope => envelope.EventType == "procurement.budget.reservation-released");
    }

    [Fact]
    public async Task T_PUR_010_d_ShortClose_WithoutACommitment_ReleasesNothing()
    {
        var order = await IssuedAsync();
        GrantReceiver();
        (await ReceiveAsync(order, "6", "6", "0")).IsSuccess.ShouldBeTrue();
        Doubles.Allocations.Items.Clear();

        var closed = await AsManager(handlers => handlers.Handle(new ShortClosePurchaseOrderCommand(order.Id, order.Version, Reason), default));
        closed.IsSuccess.ShouldBeTrue(closed.Error?.Message);
        closed.Value.Status.ShouldBe("Completed");
        Doubles.Audit.Records.Single(record => record.Action == PurchasingAudit.ShortClosed).Details["releasedAmount"].ShouldBe("0.00");
        Doubles.Outbox.Events.ShouldNotContain(envelope => envelope.EventType == "procurement.budget.reservation-released");
        (await AsManager(handlers => handlers.Handle(new ShortClosePurchaseOrderCommand(order.Id, order.Version - 1, Reason), default))).Error!.Category
            .ShouldBe(ErrorCategory.PreconditionFailed);
    }

    [Fact]
    public async Task T_RCV_002_d_ReceiptInputs_Lookups_AndALostCommit_AreAnsweredWithoutAPosting()
    {
        var order = await IssuedAsync();
        GrantReceiver();
        Doubles.ActAs(Receiver);
        var handlers = Doubles.ReceiptHandlers();
        (await handlers.Handle(new PostReceiptCommand(order.Id, LocalToday, null, null, null, null, null), default)).Error.ShouldNotBeNull();
        (await handlers.Handle(new PostReceiptCommand(order.Id, LocalToday, null, null, null,
            [new ReceiptLineRequest(1, "2", "x", "0", null, null)], null), default)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await handlers.Handle(new PostReceiptCommand(Guid.NewGuid(), LocalToday, null, null, null,
            [new ReceiptLineRequest(1, "2", "2", null, null, null)], null), default)).Error.ShouldNotBeNull();
        (await handlers.Handle(new GetReceiptQuery(Guid.NewGuid()), default)).Error!.Code.ShouldBe(ProblemCodes.NotFound);
        (await handlers.Handle(new ListReceiptsQuery(Sort: "supplier"), default)).Error!.Category.ShouldBe(ErrorCategory.Validation);

        Doubles.UnitOfWork.ThrowConflict = true;
        var lost = await handlers.Handle(new PostReceiptCommand(order.Id, LocalToday, null, null, null,
            [new ReceiptLineRequest(1, "2", "2", null, null, null)], null), default);
        Doubles.UnitOfWork.ThrowConflict = false;
        lost.Error!.Category.ShouldBe(ErrorCategory.PreconditionFailed);
        Doubles.Receipts.Items.Clear();

        var posted = await handlers.Handle(new PostReceiptCommand(order.Id, LocalToday, null, null, null,
            [new ReceiptLineRequest(1, "3", "3", null, null, null)], null), default);
        posted.IsSuccess.ShouldBeTrue(posted.Error?.Message);
        var auditor = new ProcurementTestDoubles.Actor();
        Doubles.Permissions.Grant(auditor.Id, SeededRoles.PermissionsByRole[SeededRoles.Auditor]);
        Doubles.ActAs(auditor);
        var listed = await Doubles.ReceiptHandlers().Handle(new ListReceiptsQuery(PurchaseOrderId: order.Id), default);
        listed.Value.Items.ShouldHaveSingleItem().AllowedActions.ShouldBeEmpty();
        Doubles.ActAsSystem();
    }

    [Fact]
    public async Task T_PUR_007_g_TheOrderPdf_ShowsDashesForEmptySnapshotFields_AndOnlyRendersIssuedOrders()
    {
        await ApprovedAsync();
        var supplier = Doubles.SupplierEntities.Items.Single();
        supplier.UpdateDetails(supplier.LegalName, null, supplier.Country, supplier.TaxId, ProcurementTestDoubles.Now).ShouldBeNull();
        var order = await DraftAsync(reuseApproval: true);
        (order.SupplierTradeName, order.SupplierContactPhone).ShouldBe((null, null));

        var builder = new PurchaseOrderPdfBuilder(Doubles.Orders);
        var parameters = new ReportParameters(PurchaseOrderId: order.Id).ToCanonicalJson();
        var (draftJob, _) = ReportJob.Request(Guid.NewGuid(), ReportType.PurchaseOrderPdf, reporting.Buyer.Id, parameters,
            ReportParameters.Fingerprint(ReportType.PurchaseOrderPdf, parameters), ProcurementTestDoubles.Now, "America/Sao_Paulo", ReportPolicy.V1,
            ProcurementTestDoubles.Now);
        (await Should.ThrowAsync<ReportSourceException>(() => builder.BuildAsync(Context(draftJob!, order.Id), default))).FailureCode
            .ShouldBe(ReportFailureCodes.SourceNotFound);
        (await Should.ThrowAsync<ReportSourceException>(() => builder.BuildAsync(Context(draftJob!, Guid.NewGuid()), default))).FailureCode
            .ShouldBe(ReportFailureCodes.SourceNotFound);

        (await AsBuyer(handlers => handlers.Handle(new UpdatePurchaseOrderDraftCommand(order.Id, order.Version, Address, null, null), default))).IsSuccess.ShouldBeTrue();
        (await AsBuyer(handlers => handlers.Handle(new IssuePurchaseOrderCommand(order.Id, order.Version), default))).IsSuccess.ShouldBeTrue();
        var content = await builder.BuildAsync(Context(Doubles.ReportJobs.Items.Single(), order.Id), default);
        content.FileName.ShouldContain(order.Number!);
        var fields = content.Pdf!.Sections.Where(section => section.Fields is not null).SelectMany(section => section.Fields!)
            .ToDictionary(field => field.Label, field => field.Value);
        (fields["Nome fantasia"], fields["Contato"], fields["Observações"]).ShouldBe(("—", "Synthetic Contact · contact@example.invalid", "—"));
        fields["Endereço de entrega"].ShouldBe(Address);
    }

    [Fact]
    public async Task T_PUR_005_d_AChangeCommittedWhileWaitingForTheOrderLock_Answers412()
    {
        var order = await DraftAsync();
        (await AsBuyer(handlers => handlers.Handle(new UpdatePurchaseOrderDraftCommand(order.Id, order.Version, null, "Notes only", null), default))).IsSuccess.ShouldBeTrue();
        (await AsBuyer(handlers => handlers.Handle(new UpdatePurchaseOrderDraftCommand(order.Id, order.Version, Address, null, null), default))).IsSuccess.ShouldBeTrue();
        Action<PurchaseOrder> concurrentEdit = locked =>
            locked.UpdateDraft(null, "Concurrent note", new Dictionary<int, DateOnly>(), LocalToday, null, ProcurementTestDoubles.Now, "t").ShouldBeNull();

        Doubles.Orders.OnLock = concurrentEdit;
        (await AsBuyer(handlers => handlers.Handle(new IssuePurchaseOrderCommand(order.Id, order.Version), default))).Error!.Category
            .ShouldBe(ErrorCategory.PreconditionFailed);
        Doubles.OrderNumbers.Calls.ShouldBe(0);
        Doubles.Orders.OnLock = concurrentEdit;
        (await AsBuyer(handlers => handlers.Handle(new CancelPurchaseOrderCommand(order.Id, order.Version, Reason), default))).Error!.Category
            .ShouldBe(ErrorCategory.PreconditionFailed);
        order.Status.ShouldBe(PurchaseOrderStatus.Draft);

        (await AsBuyer(handlers => handlers.Handle(new IssuePurchaseOrderCommand(order.Id, order.Version), default))).IsSuccess.ShouldBeTrue();
        GrantReceiver();
        var posted = await ReceiveAsync(order, "6", "6", "0");
        Doubles.Orders.OnLock = locked => locked.RecordDocumentFailed(locked.DocumentJobId!.Value, ReportFailureCodes.Unexpected, ProcurementTestDoubles.Now, "t")
            .ShouldBeTrue();
        (await AsManager(handlers => handlers.Handle(new ShortClosePurchaseOrderCommand(order.Id, order.Version, Reason), default))).Error!.Category
            .ShouldBe(ErrorCategory.PreconditionFailed);

        var receipt = Doubles.Receipts.Items.Single(item => item.Id == posted.Value.Receipt.Id);
        Doubles.ActAs(Receiver);
        var handlers = Doubles.ReceiptHandlers();
        Doubles.Orders.OnLock = _ => receipt.Correct(Guid.NewGuid(), 1, -1m, Reason, Receiver.Id, ProcurementTestDoubles.Now, "t").Error.ShouldBeNull();
        (await handlers.Handle(new CorrectReceiptCommand(receipt.Id, receipt.Version, 1, "-1", Reason), default)).Error!.Category
            .ShouldBe(ErrorCategory.PreconditionFailed);
        (await handlers.Handle(new PostReceiptCommand(order.Id, LocalToday, null, null, null,
            [new ReceiptLineRequest(1, "2", "1", "x", "Damaged", "Box")], null), default)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await handlers.Handle(new ListReceiptsQuery(Q: " RCV "), default)).Value.Items.ShouldNotBeEmpty();
        Doubles.ActAsSystem();
        (await Doubles.ReceiptHandlers().Handle(new CorrectReceiptCommand(receipt.Id, receipt.Version, 1, "1", Reason), default)).Error!.Category
            .ShouldBe(ErrorCategory.Forbidden);

        (await AsManager(handlers => handlers.Handle(new ShortClosePurchaseOrderCommand(order.Id, order.Version, Reason), default))).IsSuccess.ShouldBeTrue();
        Doubles.ActAs(Receiver);
        (await Doubles.ReceiptHandlers().Handle(new CorrectReceiptCommand(receipt.Id, receipt.Version, 1, "1", Reason), default)).Error.ShouldNotBeNull();
        Doubles.ActAsSystem();
    }

    [Fact]
    public async Task T_PUR_001_c_AWithdrawnAwardOrAMissingApproval_AndMalformedReportEvents_CreateNothing()
    {
        var (rfq, award) = await ApprovedAsync();
        var completed = Doubles.Outbox.Events.Single(envelope => envelope.EventType == "procurement.approval.completed");

        Doubles.UnitOfWork.UniqueViolation = "IX_Unrelated";
        try
        {
            await Doubles.PurchasingEvents().HandleAsync(completed with { EventId = Guid.NewGuid() }, default);
        }
        catch (Exception exception) when (exception is not ShouldAssertException)
        {
            // An unrelated unique violation is not a duplicate draft: the delivery fails and is retried.
        }

        Doubles.UnitOfWork.UniqueViolation = null;
        Doubles.Orders.Items.Clear();
        var approval = Doubles.Approvals.Items.Single();
        Doubles.Approvals.Items.Clear();
        (await Doubles.PurchasingEvents().HandleAsync(completed with { EventId = Guid.NewGuid() }, default)).ShouldBe(InboxApplyResult.Applied);
        Doubles.Approvals.Items.Add(approval);
        rfq.WithdrawApprovedAward(award, Reason, null, ProcurementTestDoubles.Now, "t").ShouldBeTrue();
        (await Doubles.PurchasingEvents().HandleAsync(completed with { EventId = Guid.NewGuid() }, default)).ShouldBe(InboxApplyResult.Applied);
        Doubles.Orders.Items.ShouldBeEmpty();

        var failed = ProcurementIntegrationEventContract.FromDomainEvent(
            new ReportFailed(ProcurementTestDoubles.Now, Guid.NewGuid(), ReportType.PurchaseOrderPdf, reporting.Buyer.Id, ReportFailureCodes.Unexpected), "t");
        var withoutCode = System.Text.Json.Nodes.JsonNode.Parse(failed.Payload.GetRawText())!.AsObject();
        withoutCode.Remove("failureCode");
        (await Doubles.PurchasingEvents().HandleAsync(failed with { EventId = Guid.NewGuid(), Payload = JsonSerializer.SerializeToElement(withoutCode) }, default))
            .ShouldBe(InboxApplyResult.Applied);
        var unknownType = System.Text.Json.Nodes.JsonNode.Parse(failed.Payload.GetRawText())!.AsObject();
        unknownType["reportType"] = "Unknown";
        (await Doubles.PurchasingEvents().HandleAsync(failed with { EventId = Guid.NewGuid(), Payload = JsonSerializer.SerializeToElement(unknownType) }, default))
            .ShouldBe(InboxApplyResult.Applied);
    }

    private static ReportBuildContext Context(ReportJob job, Guid purchaseOrderId) =>
        new(job, new ReportParameters(PurchaseOrderId: purchaseOrderId), ProcurementTestDoubles.Now,
            TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"), ReportPolicy.V1);

    private async Task<(RequestForQuotation Rfq, Guid AwardId)> ApprovedAsync()
    {
        var (rfq, _) = await reporting.ClosedRfqAsync();
        var leading = rfq.CurrentComparison!.Entries.Single(entry => entry.Rank == 1);
        Doubles.ActAs(reporting.Buyer);
        (await Doubles.RfqCommands().Handle(new SubmitAwardCommand(rfq.Id, rfq.Version, leading.QuoteRevisionId, null, null, null), default)).IsSuccess.ShouldBeTrue();
        var award = rfq.Awards.Single().Id;
        Doubles.CostCenter.Update("Operations", Manager.Id, ProcurementTestDoubles.Now).ShouldBeNull();
        Doubles.Department.Update("Operations", Head.Id, ProcurementTestDoubles.Now).ShouldBeNull();
        Doubles.Approvers.AddApprover(Manager.Id, SeededRoles.CostCenterManager);
        Doubles.Approvers.AddApprover(Head.Id, SeededRoles.DepartmentHead);
        Doubles.Approvers.AddUser(reporting.Buyer.Id, SeededRoles.Buyer);
        Doubles.Permissions.Grant(Manager.Id, SeededRoles.PermissionsByRole[SeededRoles.CostCenterManager]);
        Doubles.Permissions.Grant(Head.Id, SeededRoles.PermissionsByRole[SeededRoles.DepartmentHead]);
        Doubles.Permissions.Grant(ProcurementManager.Id, SeededRoles.PermissionsByRole[SeededRoles.ProcurementManager]);
        Doubles.ActAsSystem();

        var activities = Doubles.ApprovalActivities();
        var input = new ApprovalStartRequest(new ApprovalWorkflowInput(ApprovalWorkflowInput.Version1, award, rfq.RequisitionId, ProcurementTestDoubles.Now, "t"), "1.0");
        var request = new ApprovalActivityRequest(award, "t");
        await activities.StartAsync(input, default);
        await activities.AssignStepAsync(request, default);
        var instance = Doubles.Approvals.Items.Single();
        foreach (var (level, actor) in new[] { (1, Manager), (2, Head) })
        {
            Doubles.ActAs(actor);
            (await Doubles.ApprovalDecisions().Handle(new DecideApprovalStepCommand(instance.Id, level, instance.Version, "Approved", null), default))
                .IsSuccess.ShouldBeTrue();
            Doubles.ActAsSystem();
            if (level == 1)
                await activities.AssignStepAsync(request, default);
        }

        (await activities.FinalizeAsync(request, default)).Status.ShouldBe("Approved");
        AddSupplierEntity(leading.SupplierId);
        return (rfq, award);
    }

    private async Task<PurchaseOrder> DraftAsync(bool reuseApproval = false)
    {
        if (!reuseApproval)
            await ApprovedAsync();
        var completed = Doubles.Outbox.Events.Single(envelope => envelope.EventType == "procurement.approval.completed");
        (await Doubles.PurchasingEvents().HandleAsync(completed with { EventId = Guid.NewGuid() }, default)).ShouldBe(InboxApplyResult.Applied);
        return Doubles.Orders.Items.Single();
    }

    private async Task<PurchaseOrder> IssuedAsync()
    {
        var order = await DraftAsync();
        (await AsBuyer(handlers => handlers.Handle(new UpdatePurchaseOrderDraftCommand(order.Id, order.Version, Address, "Dock 2",
            [new PromisedDateChange(1, LocalToday.AddDays(10))]), default))).IsSuccess.ShouldBeTrue();
        var issued = await AsBuyer(handlers => handlers.Handle(new IssuePurchaseOrderCommand(order.Id, order.Version), default));
        issued.IsSuccess.ShouldBeTrue(issued.Error?.Message);
        return order;
    }

    private void AddSupplierEntity(Guid supplierId)
    {
        var now = ProcurementTestDoubles.Now;
        var supplier = Supplier.CreateDraft(supplierId, "Synthetic Supplier Ltda", "Synthetic", "BR", "11222333000181", null, now).Supplier!;
        supplier.ReplaceContacts([new ContactInput(null, "Synthetic Contact", "contact@example.invalid", null, "Sales")], now).ShouldBeNull();
        supplier.AddAddress(Guid.NewGuid(), new AddressValues(SupplierAddressType.Headquarters, "01310-100", "Avenida Paulista", "1000", null, "Bela Vista",
            "São Paulo", "SP", "BR"), now).Error.ShouldBeNull();
        supplier.SetCategories([new MasterDataReference(Doubles.Category.Id, true)], now).ShouldBeNull();
        supplier.Activate(null, now).ShouldBeNull();
        Doubles.SupplierEntities.Add(supplier);
    }

    private void GrantReceiver() => Doubles.Permissions.Grant(Receiver.Id, SeededRoles.PermissionsByRole[SeededRoles.Receiver]);

    private async Task<Result<ReceiptPostingView>> ReceiveAsync(
        PurchaseOrder order, string received, string accepted, string rejected, string? discrepancy = null, string? notes = null)
    {
        Doubles.ActAs(Receiver);
        try
        {
            return await Doubles.ReceiptHandlers().Handle(new PostReceiptCommand(order.Id, LocalToday, "Dock 2", $"NF-{++delivered}", null,
                [new ReceiptLineRequest(1, received, accepted, rejected, discrepancy, notes)], [new ReceiptAttachmentRequest("note.pdf", "application/pdf", 10)]), default);
        }
        finally
        {
            Doubles.ActAsSystem();
        }
    }

    private async Task<Result<T>> AsBuyer<T>(Func<PurchaseOrderHandlers, Task<Result<T>>> action)
    {
        Doubles.ActAs(reporting.Buyer);
        try
        {
            return await action(Doubles.PurchaseOrders());
        }
        finally
        {
            Doubles.ActAsSystem();
        }
    }

    private async Task<Result<T>> AsManager<T>(Func<PurchaseOrderHandlers, Task<Result<T>>> action)
    {
        Doubles.ActAs(ProcurementManager);
        try
        {
            return await action(Doubles.PurchaseOrders());
        }
        finally
        {
            Doubles.ActAsSystem();
        }
    }

    private void Complete(ReportJob job, Guid fileId)
    {
        job.Claim("unit-worker", ReportPolicy.V1, Doubles.Clock.UtcNow).Claimed.ShouldBeTrue();
        job.Complete("unit-worker", fileId, "pedido.pdf", 10, ReportPolicy.V1, Doubles.Clock.UtcNow).ShouldBeNull();
    }
}

internal static class ApplicationErrorTestExtensions
{
    public static string? Field(this ApplicationError error) => error.FieldErrors?.Keys.SingleOrDefault();
}
