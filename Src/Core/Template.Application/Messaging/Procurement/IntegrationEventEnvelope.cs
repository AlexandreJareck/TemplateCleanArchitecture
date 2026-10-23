#nullable enable
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Template.Domain.Budgeting;
using Template.Domain.Common;
using Template.Domain.Reporting;
using Template.Domain.Requisitions;
using Template.Domain.Sourcing;

namespace Template.Application.Messaging.Procurement;

/// <summary>
/// Provider-neutral v1 integration-event envelope from the approved event catalog. The event id is stable across
/// retries and broker redelivery; transport delivery is at-least-once.
/// </summary>
public sealed record IntegrationEventEnvelope(
    Guid EventId,
    string EventType,
    int EventVersion,
    DateTime OccurredAtUtc,
    string CorrelationId,
    Guid? CausationId,
    string AggregateType,
    Guid AggregateId,
    string AggregateVersion,
    JsonElement Payload);

public static class ProcurementIntegrationEventContract
{
    public const int Version = 1;

    public static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static IntegrationEventEnvelope FromDomainEvent(
        DomainEvent domainEvent,
        string correlationId,
        Guid? causationId = null,
        Guid? eventId = null)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var mapped = domainEvent switch
        {
            RequisitionSubmitted value => Map(value, new
            {
                requisitionId = value.RequisitionId,
                number = value.Number,
                revision = value.Revision,
                costCenterId = value.CostCenterId,
                estimatedTotal = new { amount = value.EstimatedTotal.Amount, currency = value.EstimatedTotal.Currency },
                neededBy = value.NeededBy
            }),
            RequisitionWithdrawn value => Map(value, new { requisitionId = value.RequisitionId, revision = value.Revision }),
            RequisitionCancelled value => Map(value, new
            {
                requisitionId = value.RequisitionId,
                revision = value.Revision,
                cancelledBy = value.CancelledBy
            }),
            RequisitionExpired value => Map(value, new { requisitionId = value.RequisitionId, revision = value.Revision }),
            BudgetReservationCreated value => Map(value, new
            {
                reservationId = value.ReservationId,
                requisitionId = value.RequisitionId,
                allocationId = value.AllocationId,
                amount = new { amount = value.Amount.Amount, currency = value.Amount.Currency }
            }),
            BudgetReservationCommitted value => Map(value, new
            {
                reservationId = value.ReservationId,
                requisitionId = value.RequisitionId,
                amount = new { amount = value.Amount.Amount, currency = value.Amount.Currency }
            }),
            BudgetReservationReleased value => Map(value, new
            {
                reservationId = value.ReservationId,
                requisitionId = value.RequisitionId,
                releasedAmount = new { amount = value.ReleasedAmount.Amount, currency = value.ReleasedAmount.Currency },
                reasonCode = value.Reason.ToString()
            }),
            BudgetReservationAdjusted value => Map(nameof(BudgetReservation), value.ReservationId, new
            {
                reservationId = value.ReservationId,
                requisitionId = value.RequisitionId,
                awardId = value.AwardId,
                previousAmount = MoneyPayload(value.PreviousAmount),
                newAmount = MoneyPayload(value.NewAmount)
            }),
            RfqPublished value => Map(nameof(RequestForQuotation), value.RfqId, new
            {
                rfqId = value.RfqId,
                number = value.Number,
                requisitionId = value.RequisitionId,
                deadline = value.DeadlineUtc,
                invitedSupplierIds = value.InvitedSupplierIds,
                sourcingMethod = value.Method.ToString()
            }),
            RfqDeadlineExtended value => Map(nameof(RequestForQuotation), value.RfqId, new
            {
                rfqId = value.RfqId,
                previousDeadline = value.PreviousDeadlineUtc,
                newDeadline = value.NewDeadlineUtc,
                invitedSupplierIds = value.InvitedSupplierIds
            }),
            RfqReminderRequested value => Map(nameof(RequestForQuotation), value.RfqId, new
            {
                rfqId = value.RfqId,
                deadline = value.DeadlineUtc,
                supplierIds = value.SupplierIds
            }),
            RfqClosed value => Map(nameof(RequestForQuotation), value.RfqId, new
            {
                rfqId = value.RfqId,
                closedAt = value.ClosedAtUtc,
                validQuoteCount = value.ValidQuoteCount,
                comparisonStatus = value.ComparisonStatus.ToString()
            }),
            RfqCancelled value => Map(nameof(RequestForQuotation), value.RfqId, new
            {
                rfqId = value.RfqId,
                requisitionId = value.RequisitionId,
                reasonCode = value.ReasonCode
            }),
            QuoteRecorded value => Map(nameof(RequestForQuotation), value.RfqId, new
            {
                rfqId = value.RfqId,
                quoteId = value.QuoteId,
                revision = value.Revision,
                supplierId = value.SupplierId
            }),
            AwardSubmitted value => Map(nameof(RequestForQuotation), value.RfqId, new
            {
                awardId = value.AwardId,
                rfqId = value.RfqId,
                requisitionId = value.RequisitionId,
                supplierId = value.SupplierId,
                normalizedTotal = MoneyPayload(value.NormalizedTotal),
                requisitionCurrencyTotal = MoneyPayload(value.RequisitionCurrencyTotal),
                policyRef = value.PolicyRef
            }),
            ReportRequested value => Map(nameof(ReportJob), value.ReportJobId, new
            {
                reportJobId = value.ReportJobId,
                reportType = value.ReportType.ToString(),
                requestedBy = value.RequestedBy
            }),
            ReportReady value => Map(nameof(ReportJob), value.ReportJobId, new
            {
                reportJobId = value.ReportJobId,
                reportType = value.ReportType.ToString(),
                requestedBy = value.RequestedBy
            }),
            ReportFailed value => Map(nameof(ReportJob), value.ReportJobId, new
            {
                reportJobId = value.ReportJobId,
                reportType = value.ReportType.ToString(),
                requestedBy = value.RequestedBy,
                failureCode = value.FailureCode
            }),
            _ => throw new NotSupportedException($"Domain event '{domainEvent.GetType().Name}' has no approved integration contract.")
        };

        return new IntegrationEventEnvelope(
            eventId ?? Guid.NewGuid(), domainEvent.EventType, Version, EnsureUtc(domainEvent.OccurredAtUtc),
            correlationId.Length <= 128 ? correlationId : correlationId[..128], causationId,
            mapped.AggregateType, mapped.AggregateId, mapped.AggregateVersion,
            JsonSerializer.SerializeToElement(mapped.Payload, JsonOptions));
    }

    public static byte[] Serialize(IntegrationEventEnvelope envelope) =>
        JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);

    public static IntegrationEventEnvelope Deserialize(ReadOnlySpan<byte> body)
    {
        var envelope = JsonSerializer.Deserialize<IntegrationEventEnvelope>(body, JsonOptions)
            ?? throw new JsonException("The integration-event envelope is null.");
        Validate(envelope);
        return envelope;
    }

    public static void Validate(IntegrationEventEnvelope envelope)
    {
        if (envelope.EventId == Guid.Empty || envelope.AggregateId == Guid.Empty
            || envelope.EventVersion != Version || envelope.OccurredAtUtc.Kind != DateTimeKind.Utc
            || string.IsNullOrWhiteSpace(envelope.EventType) || string.IsNullOrWhiteSpace(envelope.CorrelationId)
            || envelope.CorrelationId.Length > 128 || string.IsNullOrWhiteSpace(envelope.AggregateType)
            || string.IsNullOrWhiteSpace(envelope.AggregateVersion) || envelope.Payload.ValueKind != JsonValueKind.Object)
            throw new JsonException("The integration-event envelope is invalid or incompatible.");

        if (!SupportedEventTypes.Contains(envelope.EventType))
            throw new JsonException("The integration-event type is not supported by this contract version.");
    }

    public static IReadOnlySet<string> SupportedEventTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "procurement.requisition.submitted",
        "procurement.requisition.withdrawn",
        "procurement.requisition.cancelled",
        "procurement.requisition.expired",
        "procurement.budget.reservation-created",
        "procurement.budget.reservation-committed",
        "procurement.budget.reservation-released",
        "procurement.budget.reservation-adjusted",
        "procurement.rfq.published",
        "procurement.rfq.deadline-extended",
        "procurement.rfq.reminder-requested",
        "procurement.rfq.closed",
        "procurement.rfq.cancelled",
        "procurement.quote.recorded",
        "procurement.award.submitted",
        "procurement.report.requested",
        "procurement.report.ready",
        "procurement.report.failed"
    };

    /// <summary>
    /// Phase 29 sourcing and Phase 30 report events carry the aggregate type and ID; their <c>aggregateVersion</c> is the constant "1"
    /// like reservation events (consumers order by the per-aggregate Outbox sequence and use the payload state).
    /// </summary>
    private static (string AggregateType, Guid AggregateId, string AggregateVersion, object Payload) Map(
        string aggregateType, Guid aggregateId, object payload) =>
        (aggregateType, aggregateId, "1", payload);

    private static object MoneyPayload(Money money) => new { amount = money.Amount, currency = money.Currency };

    private static (string AggregateType, Guid AggregateId, string AggregateVersion, object Payload) Map(
        RequisitionSubmitted value, object payload) =>
        (nameof(PurchaseRequisition), value.RequisitionId, value.Revision.ToString(CultureInfo.InvariantCulture), payload);

    private static (string AggregateType, Guid AggregateId, string AggregateVersion, object Payload) Map(
        RequisitionWithdrawn value, object payload) =>
        (nameof(PurchaseRequisition), value.RequisitionId, value.Revision.ToString(CultureInfo.InvariantCulture), payload);

    private static (string AggregateType, Guid AggregateId, string AggregateVersion, object Payload) Map(
        RequisitionCancelled value, object payload) =>
        (nameof(PurchaseRequisition), value.RequisitionId, value.Revision.ToString(CultureInfo.InvariantCulture), payload);

    private static (string AggregateType, Guid AggregateId, string AggregateVersion, object Payload) Map(
        RequisitionExpired value, object payload) =>
        (nameof(PurchaseRequisition), value.RequisitionId, value.Revision.ToString(CultureInfo.InvariantCulture), payload);

    private static (string AggregateType, Guid AggregateId, string AggregateVersion, object Payload) Map(
        BudgetReservationCreated value, object payload) =>
        (nameof(BudgetReservation), value.ReservationId, "1", payload);

    private static (string AggregateType, Guid AggregateId, string AggregateVersion, object Payload) Map(
        BudgetReservationCommitted value, object payload) =>
        (nameof(BudgetReservation), value.ReservationId, "1", payload);

    private static (string AggregateType, Guid AggregateId, string AggregateVersion, object Payload) Map(
        BudgetReservationReleased value, object payload) =>
        (nameof(BudgetReservation), value.ReservationId, "1", payload);

    private static DateTime EnsureUtc(DateTime value) => value.Kind == DateTimeKind.Utc
        ? value
        : throw new ArgumentException("Integration-event occurrence time must be UTC.", nameof(value));

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        return options;
    }
}
