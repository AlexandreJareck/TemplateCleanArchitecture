using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Template.Application.Features.Purchasing;
using Template.Application.Interfaces.Messaging;
using Template.Application.Interfaces.Purchasing;
using Template.Application.Interfaces.Reporting;
using Template.Application.Messaging.Procurement;
using Template.Application.Reporting;
using Template.Application.Security;
using Template.Application.Services.Reporting;
using Template.Infrastructure.Persistence.Contexts;
using Template.Infrastructure.Persistence.Messaging;

namespace Template.Functional.Tests.ApiEndpoints.v2;

/// <summary>
/// Phase 32 HTTP contract on the self-contained Test host. The Worker's purchasing consumer and report attempt run in
/// process through the same Application use cases, fed with the Outbox rows the API committed: approval completion →
/// exactly one draft (PUR-001) → issue with a gapless number (PUR-005/006) → document and supplier notification
/// (PUR-007) → partial/final receipts with the over-receipt guard and a compensating correction (RCV-001..007, SC-07)
/// → completion (PUR-009); plus cancellation, short close, PUR-011 visibility and failure/retry paths.
/// </summary>
public sealed class PurchasingFunctionalTests(ProcurementWebApplicationFactory factory) : IClassFixture<ProcurementWebApplicationFactory>
{
    private const string Orders = "/api/v2/purchase-orders";
    private const string Receipts = "/api/v2/receipts";
    private const string Reason = "Synthetic purchasing decision reason";

    [Fact]
    public async Task SC_01_FullJourney_ApprovedAwardToCompletedOrder_ExactlyOnceWithGaplessNumbers()
    {
        var scenario = await PurchasingScenario.CreateApprovedAsync(factory);

        // PUR-001: the approval.completed event creates one draft; a redelivery and a second event copy change nothing.
        (await scenario.DeliverAsync("procurement.approval.completed")).ShouldBe([InboxApplyResult.Applied]);
        (await scenario.RedeliverLastAsync()).ShouldBe(InboxApplyResult.Duplicate);
        (await scenario.DeliverCopyOfLastAsync()).ShouldBe(InboxApplyResult.Applied);
        var orderId = await scenario.SingleOrderIdAsync();
        var (draft, draftTag) = await scenario.OrderAsync(scenario.Buyer, orderId);
        draft.GetProperty("status").GetString().ShouldBe("Draft");
        draft.GetProperty("number").ValueKind.ShouldBe(JsonValueKind.Null);
        draft.GetProperty("total").GetProperty("amount").GetString().ShouldBe("16200.00");
        draft.GetProperty("lines")[0].GetProperty("quantity").GetString().ShouldBe("18");
        draft.GetProperty("supplier").GetProperty("contact").GetProperty("email").GetString().ShouldBe("contact@example.invalid");
        Strings(draft.GetProperty("allowedActions")).ShouldBe(["update", "issue", "cancel"]);
        (await scenario.Approval.Sourcing.RequisitionStatusAsync(scenario.Approval.RequisitionId)).ShouldBe("Approved");

        // PUR-003: no delivery address yet → not issuable; PUR-002: only the editable fields change while Draft.
        using var incomplete = await scenario.Buyer.PostAsync($"{Orders}/{orderId}/issue", null, draftTag);
        incomplete.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await incomplete.Content.ReadAsStringAsync());
        (await ApiUser.CodeAsync(incomplete)).ShouldBe("purchase-order.not-issuable");
        var updatedTag = await scenario.UpdateDraftAsync(orderId, draftTag);
        using var stale = await scenario.Buyer.PostAsync($"{Orders}/{orderId}/issue", null, draftTag);
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        using var requesterIssue = await scenario.Requester.PostAsync($"{Orders}/{orderId}/issue", null, updatedTag);
        requesterIssue.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // PUR-005/006: issue allocates the number once; a repeat (even with an older ETag) returns the same order and number.
        var issued = await scenario.IssueAsync(orderId, updatedTag);
        var number = issued.GetProperty("number").GetString()!;
        number.ShouldMatch(@"^PO-\d{4}-\d{6}$");
        issued.GetProperty("status").GetString().ShouldBe("Issued");
        issued.GetProperty("document").GetProperty("status").GetString().ShouldBe("Pending");
        using var repeat = await scenario.Buyer.PostAsync($"{Orders}/{orderId}/issue", null, updatedTag);
        repeat.StatusCode.ShouldBe(HttpStatusCode.OK, await repeat.Content.ReadAsStringAsync());
        (await ApiUser.ReadAsync(repeat)).GetProperty("number").GetString().ShouldBe(number);
        (await scenario.Approval.Sourcing.RequisitionStatusAsync(scenario.Approval.RequisitionId)).ShouldBe("Ordered");
        using var immutable = await scenario.Buyer.PutAsync($"{Orders}/{orderId}", new { notes = "Too late" }, (await scenario.OrderAsync(scenario.Buyer, orderId)).ETag);
        immutable.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiUser.CodeAsync(immutable)).ShouldBe("purchase-order.immutable");

        // PUR-007: Worker renders the document from the immutable snapshot, then the supplier notification is delivered once.
        (await scenario.RunReportAttemptAsync()).ShouldBe(ReportProcessingOutcome.Ready);
        (await scenario.DeliverAsync("procurement.report.ready")).ShouldBe([InboxApplyResult.Applied]);
        (await scenario.RedeliverLastAsync()).ShouldBe(InboxApplyResult.Duplicate);
        (await scenario.DeliverAsync("procurement.purchase-order.notification-requested", orderId)).ShouldBe([InboxApplyResult.Applied]);
        (await scenario.RedeliverLastAsync()).ShouldBe(InboxApplyResult.Duplicate);
        var (notified, _) = await scenario.OrderAsync(scenario.Buyer, orderId);
        notified.GetProperty("document").GetProperty("status").GetString().ShouldBe("Ready");
        notified.GetProperty("document").GetProperty("templateVersion").GetString().ShouldBe("purchase-order-pdf.v1");
        notified.GetProperty("notification").GetProperty("status").GetString().ShouldBe("Sent");
        notified.GetProperty("notification").GetProperty("attempts").GetInt32().ShouldBe(1);
        (await scenario.SupplierDeliveriesAsync(orderId)).ShouldBe(1);
        using var document = await scenario.Buyer.GetAsync($"{Orders}/{orderId}/document");
        document.StatusCode.ShouldBe(HttpStatusCode.OK);
        document.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        (await document.Content.ReadAsStringAsync()).ShouldStartWith("%PDF-");
        using var receiverDocument = await scenario.Receiver.GetAsync($"{Orders}/{orderId}/document");
        receiverDocument.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // SC-07 / RCV-001..007: 10 then 9 (denied, over-receipt), 8 (completes), correction −1 (reopens), 1 (completes).
        var (asReceiver, _) = await scenario.OrderAsync(scenario.Receiver, orderId);
        asReceiver.GetProperty("total").ValueKind.ShouldBe(JsonValueKind.Null);
        asReceiver.GetProperty("lines")[0].GetProperty("unitPrice").ValueKind.ShouldBe(JsonValueKind.Null);
        asReceiver.GetProperty("pricesVisible").GetBoolean().ShouldBeFalse();
        Strings(asReceiver.GetProperty("allowedActions")).ShouldContain("receive");
        var first = await scenario.ReceiveAsync(orderId, "12", "10", "2", "Damaged", "Two boxes crushed in transit");
        first.GetProperty("purchaseOrder").GetProperty("status").GetString().ShouldBe("PartiallyReceived");
        first.GetProperty("purchaseOrder").GetProperty("lines")[0].GetProperty("remainingQuantity").GetString().ShouldBe("8");
        first.GetProperty("receipt").GetProperty("hasDiscrepancy").GetBoolean().ShouldBeTrue();
        first.GetProperty("receipt").GetProperty("number").GetString()!.ShouldMatch(@"^RCV-\d{4}-\d{6}$");
        using var over = await scenario.ReceiveResponseAsync(orderId, "9", "9", "0");
        over.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await over.Content.ReadAsStringAsync());
        (await ApiUser.CodeAsync(over)).ShouldBe("receipt.over-receipt");
        var second = await scenario.ReceiveAsync(orderId, "8", "8", "0");
        second.GetProperty("purchaseOrder").GetProperty("status").GetString().ShouldBe("Completed");
        (await scenario.Approval.Sourcing.RequisitionStatusAsync(scenario.Approval.RequisitionId)).ShouldBe("Closed");

        var secondReceipt = second.GetProperty("receipt");
        using var outOfRange = await scenario.Receiver.PostAsync($"{Receipts}/{secondReceipt.GetProperty("id").GetGuid()}/corrections",
            new { poLineNumber = 1, acceptedDelta = "1", reason = Reason }, $"\"{secondReceipt.GetProperty("version").GetString()}\"");
        outOfRange.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ApiUser.CodeAsync(outOfRange)).ShouldBe("receipt.correction-out-of-range");
        using var corrected = await scenario.Receiver.PostAsync($"{Receipts}/{secondReceipt.GetProperty("id").GetGuid()}/corrections",
            new { poLineNumber = 1, acceptedDelta = "-1", reason = Reason }, $"\"{secondReceipt.GetProperty("version").GetString()}\"");
        corrected.StatusCode.ShouldBe(HttpStatusCode.OK, await corrected.Content.ReadAsStringAsync());
        var correction = await ApiUser.ReadAsync(corrected);
        correction.GetProperty("purchaseOrder").GetProperty("status").GetString().ShouldBe("PartiallyReceived");
        correction.GetProperty("receipt").GetProperty("lines")[0].GetProperty("correctedAcceptedQuantity").GetString().ShouldBe("7");
        (await scenario.Approval.Sourcing.RequisitionStatusAsync(scenario.Approval.RequisitionId)).ShouldBe("Ordered");
        var third = await scenario.ReceiveAsync(orderId, "1", "1", "0");
        third.GetProperty("purchaseOrder").GetProperty("status").GetString().ShouldBe("Completed");
        third.GetProperty("purchaseOrder").GetProperty("lines")[0].GetProperty("acceptedQuantity").GetString().ShouldBe("18");
        (await scenario.Approval.Sourcing.RequisitionStatusAsync(scenario.Approval.RequisitionId)).ShouldBe("Closed");

        using var receiptList = await scenario.Receiver.GetAsync($"{Receipts}?purchaseOrderId={orderId}&sort=postedAt");
        (await ApiUser.ReadAsync(receiptList)).GetProperty("totalItems").GetInt64().ShouldBe(3);
        using var history = await scenario.Buyer.GetAsync($"{Orders}/{orderId}/history");
        Strings((await ApiUser.ReadAsync(history)).GetProperty("entries"), "action").ShouldBe([
            "created", "updated", "issued", "document-ready", "notification-sent", "receipt-posted", "partially-received", "receipt-posted", "completed",
            "receipt-corrected", "reopened", "receipt-posted", "completed"]);
        (await scenario.OutboxEventsAsync(orderId)).ShouldBe([
            "procurement.purchase-order.drafted", "procurement.purchase-order.issued", "procurement.purchase-order.notification-requested",
            "procurement.purchase-order.completed", "procurement.purchase-order.reopened", "procurement.purchase-order.completed"], ignoreOrder: true);
        (await scenario.AuditActionsAsync(orderId)).ShouldContain("purchase-order.issued");
        var allocation = await scenario.Approval.Sourcing.Scenario.AllocationAsync();
        allocation.GetProperty("committed").GetProperty("amount").GetString().ShouldBe("16200.00");
    }

    [Fact]
    public async Task T_PUR_008_DraftCancel_ReturnsTheRequisitionToSourcing_AndTheCommitmentToReserved()
    {
        var scenario = await PurchasingScenario.CreateApprovedAsync(factory);
        await scenario.DeliverAsync("procurement.approval.completed");
        var orderId = await scenario.SingleOrderIdAsync();
        var (_, tag) = await scenario.OrderAsync(scenario.Buyer, orderId);

        using var noReason = await scenario.Buyer.PostAsync($"{Orders}/{orderId}/cancel", new { reason = "short" }, tag);
        noReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var cancelled = await scenario.Buyer.PostAsync($"{Orders}/{orderId}/cancel", new { reason = Reason }, tag);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
        (await ApiUser.ReadAsync(cancelled)).GetProperty("status").GetString().ShouldBe("Cancelled");
        (await scenario.Approval.Sourcing.RequisitionStatusAsync(scenario.Approval.RequisitionId)).ShouldBe("InSourcing");
        var allocation = await scenario.Approval.Sourcing.Scenario.AllocationAsync();
        allocation.GetProperty("reserved").GetProperty("amount").GetString().ShouldBe("16200.00");
        allocation.GetProperty("committed").GetProperty("amount").GetString().ShouldBe("0.00");
        using var again = await scenario.Buyer.PostAsync($"{Orders}/{orderId}/cancel", new { reason = Reason }, cancelled.Headers.ETag!.Tag);
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await scenario.OutboxEventsAsync(orderId)).Count(type => type == "procurement.purchase-order.cancelled").ShouldBe(1);
    }

    [Fact]
    public async Task T_PUR_008_T_PUR_010_IssuedCancelScope_HasReceiptsGuard_AndShortCloseRelease()
    {
        var scenario = await PurchasingScenario.CreateApprovedAsync(factory);
        await scenario.DeliverAsync("procurement.approval.completed");
        var orderId = await scenario.SingleOrderIdAsync();
        var (_, tag) = await scenario.OrderAsync(scenario.Buyer, orderId);
        var issued = await scenario.IssueAsync(orderId, await scenario.UpdateDraftAsync(orderId, tag));

        // PUR-008 (D32-7): a buyer may cancel drafts only; an issued order needs requisitions.cancel-any.
        using var buyerCancel = await scenario.Buyer.PostAsync($"{Orders}/{orderId}/cancel", new { reason = Reason }, $"\"{issued.GetProperty("version").GetString()}\"");
        buyerCancel.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        Strings(issued.GetProperty("allowedActions")).ShouldNotContain("cancel");

        var received = await scenario.ReceiveAsync(orderId, "6", "6", "0");
        using var hasReceipts = await scenario.Manager.PostAsync($"{Orders}/{orderId}/cancel", new { reason = Reason },
            $"\"{received.GetProperty("purchaseOrder").GetProperty("version").GetString()}\"");
        hasReceipts.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiUser.CodeAsync(hasReceipts)).ShouldBe("purchase-order.has-receipts");

        // PUR-010 / CALC-008: 12 unreceived × 900.00 = 10,800.00 released; the order completes and the requisition closes.
        using var buyerShort = await scenario.Buyer.PostAsync($"{Orders}/{orderId}/short-close", new { reason = Reason },
            $"\"{received.GetProperty("purchaseOrder").GetProperty("version").GetString()}\"");
        buyerShort.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var shortClosed = await scenario.Manager.PostAsync($"{Orders}/{orderId}/short-close", new { reason = Reason },
            $"\"{received.GetProperty("purchaseOrder").GetProperty("version").GetString()}\"");
        shortClosed.StatusCode.ShouldBe(HttpStatusCode.OK, await shortClosed.Content.ReadAsStringAsync());
        var body = await ApiUser.ReadAsync(shortClosed);
        body.GetProperty("status").GetString().ShouldBe("Completed");
        body.GetProperty("shortClosed").GetBoolean().ShouldBeTrue();
        body.GetProperty("lines")[0].GetProperty("isShortClosed").GetBoolean().ShouldBeTrue();
        (await scenario.Approval.Sourcing.RequisitionStatusAsync(scenario.Approval.RequisitionId)).ShouldBe("Closed");
        var allocation = await scenario.Approval.Sourcing.Scenario.AllocationAsync();
        allocation.GetProperty("committed").GetProperty("amount").GetString().ShouldBe("5400.00");
        using var notReceivable = await scenario.ReceiveResponseAsync(orderId, "1", "1", "0");
        notReceivable.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ApiUser.CodeAsync(notReceivable)).ShouldBe("receipt.order-not-receivable");
    }

    [Fact]
    public async Task T_PUR_008_IssuedCancel_WithoutAcceptedQuantity_ReleasesTheCommitmentOnce()
    {
        var scenario = await PurchasingScenario.CreateApprovedAsync(factory);
        await scenario.DeliverAsync("procurement.approval.completed");
        var orderId = await scenario.SingleOrderIdAsync();
        var (_, tag) = await scenario.OrderAsync(scenario.Buyer, orderId);
        await scenario.IssueAsync(orderId, await scenario.UpdateDraftAsync(orderId, tag));
        var rejectedOnly = await scenario.ReceiveAsync(orderId, "2", "0", "2", "WrongItem", "Different model delivered");
        rejectedOnly.GetProperty("purchaseOrder").GetProperty("status").GetString().ShouldBe("Issued");

        using var cancelled = await scenario.Manager.PostAsync($"{Orders}/{orderId}/cancel", new { reason = Reason },
            $"\"{rejectedOnly.GetProperty("purchaseOrder").GetProperty("version").GetString()}\"");
        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
        (await scenario.Approval.Sourcing.RequisitionStatusAsync(scenario.Approval.RequisitionId)).ShouldBe("Cancelled");
        var allocation = await scenario.Approval.Sourcing.Scenario.AllocationAsync();
        allocation.GetProperty("committed").GetProperty("amount").GetString().ShouldBe("0.00");
        allocation.GetProperty("available").GetProperty("amount").GetString().ShouldBe("50000.00");

        // The pending document is still rendered, but a cancelled order never notifies the supplier.
        (await scenario.RunReportAttemptAsync()).ShouldBe(ReportProcessingOutcome.Ready);
        await scenario.DeliverAsync("procurement.report.ready");
        await scenario.DeliverAsync("procurement.purchase-order.notification-requested", orderId);
        (await scenario.SupplierDeliveriesAsync(orderId)).ShouldBe(0);
    }

    [Fact]
    public async Task T_PUR_007_DocumentAndNotificationFailures_AreVisibleAndRetryable_WithoutRenumbering()
    {
        var scenario = await PurchasingScenario.CreateApprovedAsync(factory);
        await scenario.DeliverAsync("procurement.approval.completed");
        var orderId = await scenario.SingleOrderIdAsync();
        var (_, tag) = await scenario.OrderAsync(scenario.Buyer, orderId);
        var number = (await scenario.IssueAsync(orderId, await scenario.UpdateDraftAsync(orderId, tag))).GetProperty("number").GetString();

        (await scenario.RunReportAttemptAsync(validOutput: false)).ShouldBe(ReportProcessingOutcome.Failed);
        await scenario.DeliverAsync("procurement.report.failed");
        var (failed, failedTag) = await scenario.OrderAsync(scenario.Buyer, orderId);
        failed.GetProperty("document").GetProperty("status").GetString().ShouldBe("Failed");
        failed.GetProperty("document").GetProperty("failureCode").GetString().ShouldBe("report.output-invalid");
        failed.GetProperty("status").GetString().ShouldBe("Issued");
        Strings(failed.GetProperty("allowedActions")).ShouldContain("retry-document");
        using var notReady = await scenario.Buyer.GetAsync($"{Orders}/{orderId}/document");
        notReady.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiUser.CodeAsync(notReady)).ShouldBe("purchase-order.document-not-ready");
        using var notificationNotRetryable = await scenario.Buyer.PostAsync($"{Orders}/{orderId}/notification/retry", null, failedTag);
        notificationNotRetryable.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiUser.CodeAsync(notificationNotRetryable)).ShouldBe("purchase-order.not-retryable");

        using var retried = await scenario.Buyer.PostAsync($"{Orders}/{orderId}/document/retry", null, failedTag);
        retried.StatusCode.ShouldBe(HttpStatusCode.OK, await retried.Content.ReadAsStringAsync());
        (await ApiUser.ReadAsync(retried)).GetProperty("document").GetProperty("attempts").GetInt32().ShouldBe(2);
        (await scenario.RunReportAttemptAsync()).ShouldBe(ReportProcessingOutcome.Ready);
        await scenario.DeliverAsync("procurement.report.ready");

        (await scenario.DeliverAsync("procurement.purchase-order.notification-requested", orderId, failingNotifier: true)).ShouldBe([InboxApplyResult.Applied]);
        var (notifyFailed, notifyTag) = await scenario.OrderAsync(scenario.Buyer, orderId);
        notifyFailed.GetProperty("notification").GetProperty("status").GetString().ShouldBe("Failed");
        notifyFailed.GetProperty("notification").GetProperty("failureCode").GetString().ShouldBe("notification.channel-refused");
        using var retryNotification = await scenario.Buyer.PostAsync($"{Orders}/{orderId}/notification/retry", null, notifyTag);
        retryNotification.StatusCode.ShouldBe(HttpStatusCode.OK, await retryNotification.Content.ReadAsStringAsync());
        await scenario.DeliverAsync("procurement.purchase-order.notification-requested", orderId);
        var (sent, _) = await scenario.OrderAsync(scenario.Buyer, orderId);
        sent.GetProperty("notification").GetProperty("status").GetString().ShouldBe("Sent");
        sent.GetProperty("notification").GetProperty("attempts").GetInt32().ShouldBe(2);
        sent.GetProperty("number").GetString().ShouldBe(number);
        (await scenario.SupplierDeliveriesAsync(orderId)).ShouldBe(1);
    }

    [Fact]
    public async Task T_PUR_011_Visibility_And_RCV_002_004_005_Validation()
    {
        var scenario = await PurchasingScenario.CreateApprovedAsync(factory);
        await scenario.DeliverAsync("procurement.approval.completed");
        var orderId = await scenario.SingleOrderIdAsync();

        using var receiverDraft = await scenario.Receiver.GetAsync($"{Orders}/{orderId}");
        receiverDraft.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var (own, tag) = await scenario.OrderAsync(scenario.Requester, orderId);
        own.GetProperty("supplier").GetProperty("contact").ValueKind.ShouldBe(JsonValueKind.Null);
        own.GetProperty("supplier").GetProperty("address").ValueKind.ShouldBe(JsonValueKind.Null);
        own.GetProperty("total").ValueKind.ShouldBe(JsonValueKind.Null);
        Strings(own.GetProperty("allowedActions")).ShouldBeEmpty();
        var outsider = await ApiUser.CreateAsync(factory, SeededRoles.Requester);
        using var hidden = await outsider.GetAsync($"{Orders}/{orderId}");
        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var outsiderList = await outsider.GetAsync($"{Orders}?requisitionId={scenario.Approval.RequisitionId}");
        (await ApiUser.ReadAsync(outsiderList)).GetProperty("totalItems").GetInt64().ShouldBe(0);
        using var notIssued = await scenario.ReceiveResponseAsync(orderId, "1", "1", "0");
        notIssued.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await scenario.IssueAsync(orderId, await scenario.UpdateDraftAsync(orderId, tag));
        using var queue = await scenario.Receiver.GetAsync($"{Orders}?status=Issued,PartiallyReceived");
        (await ApiUser.ReadAsync(queue)).GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ShouldContain(orderId);
        using var unbalanced = await scenario.ReceiveResponseAsync(orderId, "3", "1", "1");
        unbalanced.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var noDiscrepancy = await scenario.ReceiveResponseAsync(orderId, "2", "1", "1");
        noDiscrepancy.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var fractional = await scenario.ReceiveResponseAsync(orderId, "1.5", "1.5", "0");
        fractional.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var future = await scenario.ReceiveResponseAsync(orderId, "1", "1", "0", receivedOn: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)));
        future.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var noKey = await scenario.Receiver.SendAsync(HttpMethod.Post, Receipts, new { purchaseOrderId = orderId });
        noKey.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiUser.CodeAsync(noKey)).ShouldBe("idempotency.key-required");
        using var requesterReceipt = await scenario.Requester.PostAsync(Receipts, new { purchaseOrderId = orderId });
        requesterReceipt.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var receiptsForRequester = await scenario.Requester.GetAsync(Receipts);
        receiptsForRequester.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OpenApi_DocumentsThePurchasingAndReceivingOperations()
    {
        using var response = await factory.CreateClient().GetAsync("/swagger/v2/swagger.json");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var paths = (await ApiUser.ReadAsync(response)).GetProperty("paths");
        foreach (var path in new[]
                 {
                     "/api/v2/purchase-orders", "/api/v2/purchase-orders/{id}", "/api/v2/purchase-orders/{id}/history", "/api/v2/purchase-orders/{id}/issue",
                     "/api/v2/purchase-orders/{id}/cancel", "/api/v2/purchase-orders/{id}/short-close", "/api/v2/purchase-orders/{id}/document",
                     "/api/v2/purchase-orders/{id}/document/retry", "/api/v2/purchase-orders/{id}/notification/retry", "/api/v2/receipts",
                     "/api/v2/receipts/{id}", "/api/v2/receipts/{id}/corrections"
                 })
            paths.TryGetProperty(path, out _).ShouldBeTrue(path);
        paths.GetProperty("/api/v2/receipts/{id}").TryGetProperty("delete", out _).ShouldBeFalse();
        paths.GetProperty("/api/v2/receipts/{id}").TryGetProperty("put", out _).ShouldBeFalse();
    }

    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(item => item.GetString()!).ToArray();

    private static string[] Strings(JsonElement array, string property) =>
        array.EnumerateArray().Select(item => item.GetProperty(property).GetString()!).ToArray();
}

/// <summary>
/// An approved award (the SC-01 two-level approval of 18 × 900.00 BRL) plus a receiver; delivers the committed Outbox
/// rows to the purchasing consumer in process, like the Worker would after RabbitMQ.
/// </summary>
internal sealed class PurchasingScenario
{
    private readonly ProcurementWebApplicationFactory factory;
    private readonly HashSet<Guid> delivered = [];
    private IntegrationEventEnvelope? last;

    private PurchasingScenario(ProcurementWebApplicationFactory factory, ApprovalScenario approval, ApiUser receiver)
    {
        this.factory = factory;
        Approval = approval;
        Receiver = receiver;
    }

    public ApprovalScenario Approval { get; }
    public ApiUser Receiver { get; }
    public ApiUser Buyer => Approval.Sourcing.Buyer;
    public ApiUser Manager => Approval.Sourcing.Manager;
    public ApiUser Requester => Approval.Sourcing.Scenario.Requester;

    public static async Task<PurchasingScenario> CreateApprovedAsync(ProcurementWebApplicationFactory factory)
    {
        var approval = await ApprovalScenario.CreateAsync(factory);
        var instance = await approval.StartedInstanceAsync();
        var (_, firstTag) = await approval.GetAsync(approval.Manager, instance);
        using var first = await approval.Manager.PostAsync($"/api/v2/approval-instances/{instance}/steps/1/decision", new { decision = "Approved" }, firstTag);
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        await approval.RunActivitiesAsync();
        var (_, secondTag) = await approval.GetAsync(approval.Head, instance);
        using var second = await approval.Head.PostAsync($"/api/v2/approval-instances/{instance}/steps/2/decision", new { decision = "Approved" }, secondTag);
        second.StatusCode.ShouldBe(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        await approval.RunActivitiesAsync();
        (await approval.Sourcing.RequisitionStatusAsync(approval.RequisitionId)).ShouldBe("Approved");
        return new PurchasingScenario(factory, approval, await ApiUser.CreateAsync(factory, SeededRoles.Receiver));
    }

    /// <summary>Delivers every undelivered Outbox row of <paramref name="eventType"/> (optionally of one aggregate) to the purchasing consumer.</summary>
    public async Task<IReadOnlyList<InboxApplyResult>> DeliverAsync(string eventType, Guid? aggregateId = null, bool failingNotifier = false)
    {
        List<OutboxMessage> rows;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            rows = await context.Set<OutboxMessage>().AsNoTracking()
                .Where(row => row.EventType == eventType && (aggregateId == null || row.AggregateId == aggregateId))
                .OrderBy(row => row.CreatedAtUtc).ToListAsync();
        }

        var results = new List<InboxApplyResult>();
        foreach (var row in rows.Where(row => !delivered.Contains(row.EventId) && Belongs(row)))
        {
            delivered.Add(row.EventId);
            using var document = JsonDocument.Parse(row.PayloadJson);
            var envelope = new IntegrationEventEnvelope(row.EventId, row.EventType, row.EventVersion, DateTime.SpecifyKind(row.OccurredAtUtc, DateTimeKind.Utc),
                row.CorrelationId, row.CausationId, row.AggregateType, row.AggregateId, row.AggregateVersion, document.RootElement.Clone());
            results.Add(await HandleAsync(envelope, failingNotifier));
        }

        return results;
    }

    /// <summary>Broker redelivery of the same event (same ID): the Inbox makes it a no-op.</summary>
    public Task<InboxApplyResult> RedeliverLastAsync() => HandleAsync(last!, failingNotifier: false);

    /// <summary>A second Outbox copy of the last event with a new ID: the domain idempotency (unique award, stale attempt) makes it a no-op.</summary>
    public Task<InboxApplyResult> DeliverCopyOfLastAsync() => HandleAsync(last! with { EventId = Guid.NewGuid() }, failingNotifier: false);

    public async Task<Guid> SingleOrderIdAsync()
    {
        using var response = await Buyer.GetAsync($"/api/v2/purchase-orders?requisitionId={Approval.RequisitionId}");
        var items = (await ApiUser.ReadAsync(response)).GetProperty("items");
        items.GetArrayLength().ShouldBe(1);
        return items[0].GetProperty("id").GetGuid();
    }

    public async Task<(JsonElement Body, string ETag)> OrderAsync(ApiUser user, Guid id)
    {
        using var response = await user.GetAsync($"/api/v2/purchase-orders/{id}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await ApiUser.ReadAsync(response), response.Headers.ETag!.Tag);
    }

    public async Task<string> UpdateDraftAsync(Guid id, string etag)
    {
        using var response = await Buyer.PutAsync($"/api/v2/purchase-orders/{id}", new
        {
            deliveryAddress = "Rua Sintética 100, Bela Vista, São Paulo - SP, 01310-100",
            notes = "Deliver to the support team storeroom",
            promisedDates = new[] { new { lineNumber = 1, promisedDeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)) } }
        }, etag);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return response.Headers.ETag!.Tag;
    }

    public async Task<JsonElement> IssueAsync(Guid id, string etag)
    {
        using var response = await Buyer.PostAsync($"/api/v2/purchase-orders/{id}/issue", null, etag);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await ApiUser.ReadAsync(response);
    }

    public async Task<JsonElement> ReceiveAsync(Guid orderId, string received, string accepted, string rejected, string? discrepancyType = null, string? notes = null)
    {
        using var response = await ReceiveResponseAsync(orderId, received, accepted, rejected, discrepancyType, notes);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await ApiUser.ReadAsync(response);
    }

    public Task<HttpResponseMessage> ReceiveResponseAsync(
        Guid orderId, string received, string accepted, string rejected, string? discrepancyType = null, string? notes = null, DateOnly? receivedOn = null) =>
        Receiver.PostAsync("/api/v2/receipts", new
        {
            purchaseOrderId = orderId,
            receivedOn = receivedOn ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"))),
            location = "Dock 2",
            deliveryReference = "NF-SYNTHETIC-1",
            lines = new[] { new { poLineNumber = 1, received, accepted, rejected, discrepancyType, discrepancyNotes = notes } },
            attachments = new[] { new { fileName = "delivery-note.pdf", mediaType = "application/pdf", length = 2048 } }
        });

    /// <summary>
    /// Worker report attempts in process until this scenario's current document job ends; jobs of other scenarios in the
    /// host are rendered on the way like a shared Worker would. <paramref name="validOutput"/> false renders bytes without a
    /// PDF signature for this scenario's job.
    /// </summary>
    public async Task<ReportProcessingOutcome> RunReportAttemptAsync(bool validOutput = true)
    {
        var jobId = await CurrentDocumentJobAsync();
        for (var attempt = 0; attempt < 40; attempt++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var renderer = new Renderer(validOutput);
            var processor = ActivatorUtilities.CreateInstance<ReportJobProcessor>(scope.ServiceProvider, (IPdfRenderer)renderer, (ISpreadsheetWriter)renderer);
            var (outcome, processed) = await processor.ProcessNextAsync("functional-worker", CancellationToken.None);
            if (processed == jobId && outcome is not ReportProcessingOutcome.Idle)
                return outcome;
            if (outcome == ReportProcessingOutcome.Idle)
                await Task.Delay(50);
        }

        return ReportProcessingOutcome.Idle;
    }

    private async Task<Guid?> CurrentDocumentJobAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Set<Template.Domain.Purchasing.PurchaseOrder>().AsNoTracking()
            .Where(order => order.RequisitionId == Approval.RequisitionId).Select(order => order.DocumentJobId).SingleAsync();
    }

    public async Task<int> SupplierDeliveriesAsync(Guid orderId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Set<NotificationDelivery>().CountAsync(delivery => delivery.AggregateId == orderId && delivery.RecipientType == "Supplier");
    }

    public async Task<IReadOnlyList<string>> OutboxEventsAsync(Guid aggregateId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Set<OutboxMessage>().Where(message => message.AggregateId == aggregateId).Select(message => message.EventType).ToListAsync();
    }

    public async Task<IReadOnlyList<string>> AuditActionsAsync(Guid resourceId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var id = resourceId.ToString();
        return await context.Set<Template.Domain.Auditing.AuditRecord>().Where(record => record.ResourceId == id).Select(record => record.Action).ToListAsync();
    }

    /// <summary>Approval and report events of other scenarios in this host are left for their own scenario.</summary>
    private bool Belongs(OutboxMessage row) => row.EventType switch
    {
        "procurement.approval.completed" => row.PayloadJson.Contains(Approval.RequisitionId.ToString(), StringComparison.OrdinalIgnoreCase),
        "procurement.report.ready" or "procurement.report.failed" => row.PayloadJson.Contains("PurchaseOrderPdf", StringComparison.Ordinal) && OwnsJob(row.AggregateId),
        _ => true
    };

    private bool OwnsJob(Guid reportJobId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return context.Set<Template.Domain.Purchasing.PurchaseOrder>().AsNoTracking()
            .Any(order => order.RequisitionId == Approval.RequisitionId && order.DocumentJobId == reportJobId);
    }

    private async Task<InboxApplyResult> HandleAsync(IntegrationEventEnvelope envelope, bool failingNotifier)
    {
        last = envelope;
        await using var scope = factory.Services.CreateAsyncScope();
        if (!failingNotifier)
            return await scope.ServiceProvider.GetRequiredService<IProcurementIntegrationEventHandler>()
                .HandleAsync(ProcurementConsumers.Purchasing, envelope, CancellationToken.None);
        var handler = ActivatorUtilities.CreateInstance<PurchasingEventHandler>(scope.ServiceProvider, (ISupplierOrderNotifier)new RefusingNotifier());
        return await handler.HandleAsync(envelope, CancellationToken.None);
    }

    private sealed class RefusingNotifier : ISupplierOrderNotifier
    {
        public Task NotifyAsync(SupplierOrderNotification notification, CancellationToken cancellationToken) =>
            throw new SupplierNotificationException("notification.channel-refused", "synthetic refusal");
    }

    private sealed class Renderer(bool validOutput) : IPdfRenderer, ISpreadsheetWriter
    {
        public Task RenderAsync(ReportDocument document, Stream output, CancellationToken cancellationToken) =>
            output.WriteAsync(validOutput ? "%PDF-1.7 functional purchase order"u8.ToArray() : "not a pdf"u8.ToArray(), cancellationToken).AsTask();

        public Task WriteAsync(WorkbookDocument workbook, Stream output, CancellationToken cancellationToken) =>
            output.WriteAsync(new byte[] { 0x50, 0x4B, 0x03, 0x04, 7, 7 }, cancellationToken).AsTask();
    }
}
