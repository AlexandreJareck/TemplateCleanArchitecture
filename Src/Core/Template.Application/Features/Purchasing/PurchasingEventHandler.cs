#nullable enable
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Template.Application.Interfaces.Approvals;
using Template.Application.Interfaces.Messaging;
using Template.Application.Interfaces.Purchasing;
using Template.Application.Interfaces.Reporting;
using Template.Application.Interfaces.Requisitions;
using Template.Application.Interfaces.Sourcing;
using Template.Application.Interfaces.Suppliers;
using Template.Application.Logging;
using Template.Application.Messaging.Procurement;
using Template.Application.Services.Procurement;
using Template.Domain.Common;
using Template.Domain.Purchasing;
using Template.Domain.Reporting;
using Template.Domain.Requisitions;
using Template.Domain.Sourcing;
using Template.Domain.Suppliers;

namespace Template.Application.Features.Purchasing;

/// <summary>
/// Worker use case of the purchasing consumer (PUR-001, PUR-007). Every effect commits atomically with its Inbox entry
/// (INT-003), so a redelivered event changes nothing:
/// <list type="bullet">
/// <item><c>procurement.approval.completed</c> creates the one draft order of the approved award (unique award ID);</item>
/// <item><c>procurement.report.ready</c>/<c>failed</c> of a purchase-order document record the stored file or the
/// visible failure, never unissuing or renumbering the order;</item>
/// <item><c>procurement.purchase-order.notification-requested</c> delivers the supplier notification through the
/// <see cref="ISupplierOrderNotifier"/> port and records Sent or Failed.</item>
/// </list>
/// Transient database failures propagate so the broker retries with the bounded policy; business no-ops complete the
/// Inbox entry.
/// </summary>
public sealed class PurchasingEventHandler(
    ITransactionalInbox inbox,
    IPurchaseOrderRepository orders,
    IPurchaseRequisitionRepository requisitions,
    IRequestForQuotationRepository rfqs,
    IApprovalInstanceRepository approvals,
    ISupplierRepository suppliers,
    IRequisitionReferenceReader references,
    IReportJobRepository jobs,
    ISupplierOrderNotifier notifier,
    ProcurementWriter writer,
    ILogger<PurchasingEventHandler> logger)
{
    private static readonly Common.Results.ApplicationError DuplicateDraft = new(
        "purchase-order.duplicate-draft", Wrappers.ErrorCategory.Conflict, "The draft of this award already exists.");

    public async Task<InboxApplyResult> HandleAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (!ProcurementConsumers.PurchasingEventTypes.Contains(envelope.EventType))
            throw new JsonException("The purchasing consumer does not receive this event type.");

        await writer.BeginAsync(cancellationToken);
        if (!await inbox.TryAcquireAsync(ProcurementConsumers.Purchasing, envelope, writer.UtcNow, cancellationToken))
        {
            await writer.RollbackAsync();
            return InboxApplyResult.Duplicate;
        }

        var staged = envelope.EventType switch
        {
            "procurement.approval.completed" => await CreateDraftAsync(envelope.Payload, cancellationToken),
            "procurement.report.ready" or "procurement.report.failed" => await RecordDocumentAsync(envelope, cancellationToken),
            _ => await NotifySupplierAsync(envelope, cancellationToken)
        };
        var commit = await writer.CommitAsync(_ => Task.FromResult<long?>(null),
            constraint => constraint == IPurchaseOrderRepository.AwardUniqueKey ? DuplicateDraft : null, cancellationToken);
        if (commit == DuplicateDraft)
        {
            // Another consumer instance created the draft of this award first (PUR-001 unique award ID); this delivery
            // is a duplicate effect and the event is settled.
            logger.LogInformation(LogEvents.PurchasingEventDuplicate, "Purchasing event {EventType} lost a duplicate race", envelope.EventType);
            return InboxApplyResult.Duplicate;
        }

        if (commit is not null)
            throw new InvalidOperationException("The purchasing effect lost an optimistic-concurrency race; the delivery is retried.");

        writer.Record(staged);
        return InboxApplyResult.Applied;
    }

    /// <summary>PUR-001/002, CALC-006: the draft copies the approved award's quote revision and current supplier identity.</summary>
    private async Task<IReadOnlyList<DomainEvent>> CreateDraftAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var awardId = payload.GetProperty("awardId").GetGuid();
        var approvalInstanceId = payload.GetProperty("approvalInstanceId").GetGuid();
        var requisitionId = payload.GetProperty("requisitionId").GetGuid();
        if (await orders.FindByAwardAsync(awardId, cancellationToken) is not null)
            return [];
        var requisition = await requisitions.FindAsync(requisitionId, cancellationToken);
        var approval = await approvals.FindByAwardAsync(awardId, cancellationToken);
        if (requisition is not { Status: RequisitionStatus.Approved } || requisition.AwardId != awardId || approval is null)
        {
            logger.LogInformation(LogEvents.PurchasingEventSkipped, "Draft order for award {AwardId} skipped: the requisition left Approved", awardId);
            return [];
        }

        var rfq = await rfqs.FindAsync(approval.RfqId, cancellationToken)
            ?? throw new InvalidOperationException("An approval references a missing RFQ.");
        var award = rfq.Awards.Single(candidate => candidate.Id == awardId);
        if (award.Status != AwardStatus.Approved)
            return [];
        var revision = rfq.Quotes.SelectMany(quote => quote.Revisions).Single(candidate => candidate.Id == award.QuoteRevisionId);
        var supplier = await suppliers.FindAsync(award.SupplierId, cancellationToken)
            ?? throw new InvalidOperationException("An award references a missing supplier.");
        var settings = await writer.SettingsAsync(cancellationToken);
        var approvedOn = ProcurementWriter.LocalDate(writer.UtcNow, settings.TimeZone);
        var rfqLines = rfq.Lines.ToDictionary(line => line.LineNumber);
        var lines = new List<PurchaseOrderLineInput>();
        foreach (var offer in revision.Lines.OrderBy(offer => offer.RfqLineNumber))
        {
            var line = rfqLines[offer.RfqLineNumber];
            var unit = await references.FindUnitAsync(line.UnitOfMeasureId, cancellationToken);
            lines.Add(new PurchaseOrderLineInput(
                line.LineNumber, line.CatalogItemId, line.Description, line.Kind, line.CategoryId, line.UnitOfMeasureId, line.UnitCode,
                unit?.AllowedDecimals ?? CalculationRules.MaxQuantityDecimals, offer.QuantityOffered, offer.UnitPrice,
                approvedOn.AddDays(Math.Max(revision.DeliveryDays, 0))));
        }

        var (quoteRate, requisitionRate) = rfq.AwardRates(awardId);
        var address = supplier.Addresses.OrderBy(candidate => candidate.Type).FirstOrDefault();
        var contact = supplier.Contacts.FirstOrDefault();
        var input = new PurchaseOrderDraftInput(
            awardId, approval.Id, rfq.Id, rfq.Number, revision.Id, requisition.Id, requisition.Number, requisition.RequesterId,
            requisition.CostCenterId, requisition.DepartmentId,
            new PurchaseOrderSupplierSnapshot(supplier.Id, supplier.LegalName, supplier.TradeName, supplier.Country, supplier.TaxId,
                address is null ? null : FormatAddress(address), contact?.Name, contact?.Email, contact?.Phone),
            award.SubmittedBy, null, revision.Currency, revision.Freight, revision.Discount, revision.TaxesIncluded, award.AwardedTotal,
            revision.PaymentTerms, revision.ValidUntil, revision.DeliveryDays, rfq.BaseCurrency, quoteRate, award.RequisitionCurrency, requisitionRate,
            award.RequisitionCurrencyTotal, lines);
        var order = PurchaseOrder.CreateDraft(Guid.NewGuid(), input, writer.UtcNow, writer.CorrelationId);
        orders.Add(order);
        requisition.LinkPurchaseOrder(order.Id, writer.UtcNow);
        writer.Audit(PurchasingAudit.Created, PurchasingAudit.Resource, order.Id, toState: "Draft", ruleIds: ["PUR-001", "PUR-002", "CALC-006"],
            details: new Dictionary<string, string>
            {
                ["awardId"] = awardId.ToString(),
                ["approvalInstanceId"] = approval.Id.ToString(),
                ["requisitionId"] = requisition.Id.ToString(),
                ["supplierId"] = supplier.Id.ToString(),
                ["total"] = PurchasingAudit.Amount(order.Total),
                ["currency"] = order.Currency
            });
        var staged = writer.Stage(order.DomainEvents);
        order.ClearDomainEvents();
        logger.LogInformation(LogEvents.PurchaseOrderDrafted, "Draft purchase order {PurchaseOrderId} created for award {AwardId}", order.Id, awardId);
        return staged;
    }

    /// <summary>PUR-007: the document outcome of the order's current job; other report types and stale jobs are no-ops.</summary>
    private async Task<IReadOnlyList<DomainEvent>> RecordDocumentAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        var payload = envelope.Payload;
        if (!Enum.TryParse<ReportType>(payload.GetProperty("reportType").GetString(), out var type) || type != ReportType.PurchaseOrderPdf)
            return [];
        var jobId = payload.GetProperty("reportJobId").GetGuid();
        var order = await orders.FindByDocumentJobAsync(jobId, cancellationToken);
        if (order is null)
            return [];
        var now = writer.UtcNow;
        if (envelope.EventType == "procurement.report.ready")
        {
            var job = await jobs.FindAsync(jobId, cancellationToken) ?? throw new InvalidOperationException("The ready report job disappeared.");
            if (job.OutputFileId is not { } fileId || !order.RecordDocumentReady(jobId, fileId, job.TemplateVersion, now, writer.CorrelationId))
                return [];
            writer.Audit(PurchasingAudit.DocumentReady, PurchasingAudit.Resource, order.Id, ruleIds: ["PUR-007"],
                details: new Dictionary<string, string>
                {
                    ["reportJobId"] = jobId.ToString(),
                    ["fileId"] = fileId.ToString(),
                    ["templateVersion"] = job.TemplateVersion,
                    ["attempt"] = PurchasingAudit.Text(order.DocumentAttempts)
                });
        }
        else
        {
            var failureCode = payload.TryGetProperty("failureCode", out var code) ? code.GetString() ?? ReportFailureCodes.Unexpected : ReportFailureCodes.Unexpected;
            if (!order.RecordDocumentFailed(jobId, failureCode, now, writer.CorrelationId))
                return [];
            writer.Audit(PurchasingAudit.DocumentFailed, PurchasingAudit.Resource, order.Id, ruleIds: ["PUR-007"],
                details: new Dictionary<string, string> { ["reportJobId"] = jobId.ToString(), ["failureCode"] = order.DocumentFailureCode! });
            logger.LogWarning(LogEvents.PurchaseOrderDocumentFailed, "Purchase order {PurchaseOrderId} document failed with {FailureCode}", order.Id,
                order.DocumentFailureCode);
        }

        var staged = writer.Stage(order.DomainEvents);
        order.ClearDomainEvents();
        return staged;
    }

    /// <summary>PUR-007: one supplier delivery per requested attempt; a refused channel is a visible, retryable failure.</summary>
    private async Task<IReadOnlyList<DomainEvent>> NotifySupplierAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        var payload = envelope.Payload;
        var order = await orders.FindAsync(payload.GetProperty("purchaseOrderId").GetGuid(), cancellationToken);
        var attempt = payload.GetProperty("attempt").GetInt32();
        // Only the current pending attempt is delivered: a stale or repeated request never reaches the supplier twice.
        if (order is null || order.Status == PurchaseOrderStatus.Cancelled || order.Number is null || order.DocumentFileId is not { } fileId
            || attempt != order.NotificationAttempts || order.NotificationStatus != PurchaseOrderNotificationStatus.Pending)
            return [];
        var now = writer.UtcNow;
        try
        {
            await notifier.NotifyAsync(
                new SupplierOrderNotification(envelope.EventId, order.Id, order.Number, order.SupplierId, fileId, attempt, PurchasingAudit.SupplierNotificationTemplate,
                    now),
                cancellationToken);
            if (!order.RecordNotificationSent(attempt, now, writer.CorrelationId))
                return [];
            writer.Audit(PurchasingAudit.NotificationSent, PurchasingAudit.Resource, order.Id, ruleIds: ["PUR-007"],
                details: new Dictionary<string, string> { ["supplierId"] = order.SupplierId.ToString(), ["attempt"] = PurchasingAudit.Text(attempt) });
        }
        catch (SupplierNotificationException exception)
        {
            if (!order.RecordNotificationFailed(attempt, exception.FailureCode, now, writer.CorrelationId))
                return [];
            writer.Audit(PurchasingAudit.NotificationFailed, PurchasingAudit.Resource, order.Id, ruleIds: ["PUR-007"],
                details: new Dictionary<string, string> { ["failureCode"] = order.NotificationFailureCode!, ["attempt"] = PurchasingAudit.Text(attempt) });
            logger.LogWarning(LogEvents.PurchaseOrderNotificationFailed, "Purchase order {PurchaseOrderId} notification attempt {Attempt} failed with {FailureCode}",
                order.Id, attempt, order.NotificationFailureCode);
        }

        var staged = writer.Stage(order.DomainEvents);
        order.ClearDomainEvents();
        return staged;
    }

    private static string FormatAddress(SupplierAddress address) =>
        string.Join(", ", new[]
        {
            address.Street, address.Number, address.Complement, address.District, $"{address.City} - {address.State}", address.PostalCode, address.Country
        }.Where(part => !string.IsNullOrWhiteSpace(part)));
}
