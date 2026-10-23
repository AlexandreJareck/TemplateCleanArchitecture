#nullable enable
using System.Text.Json;
using Template.Application.Interfaces.Messaging;

namespace Template.Application.Messaging.Procurement;

/// <summary>Durable consumer names; each is part of the Inbox key (consumer, event ID).</summary>
public static class ProcurementConsumers
{
    public const string Projection = "procurement-projection-v1";
    public const string Notification = "procurement-notification-v1";

    /// <summary>
    /// Events the notification consumer receives: SRC-022 invitation, extension and reminder, and the Phase 30 in-app
    /// report completion or failure for the requester (event catalog §2).
    /// </summary>
    public static IReadOnlySet<string> NotificationEventTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "procurement.rfq.published",
        "procurement.rfq.deadline-extended",
        "procurement.rfq.reminder-requested",
        "procurement.report.ready",
        "procurement.report.failed"
    };
}

/// <summary>
/// Turns a sourcing event into notification requests for the invited suppliers named in its payload, and a report
/// event into one in-app notification for its requester. Recipients are IDs; contact data is resolved only by a future
/// delivery adapter and never travels in events (NFR-SEC-7).
/// </summary>
public static class RfqNotificationPlanner
{
    public const string SupplierRecipient = "Supplier";
    public const string InvitationTemplate = "sourcing.rfq-invitation.v1";
    public const string DeadlineExtendedTemplate = "sourcing.rfq-deadline-extended.v1";
    public const string ReminderTemplate = "sourcing.rfq-reminder.v1";
    public const string UserRecipient = "User";
    public const string ReportReadyTemplate = "reports.report-ready.v1";
    public const string ReportFailedTemplate = "reports.report-failed.v1";

    public static IReadOnlyList<NotificationRequest> Plan(IntegrationEventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.EventType is "procurement.report.ready" or "procurement.report.failed")
        {
            if (!envelope.Payload.TryGetProperty("requestedBy", out var requester) || requester.ValueKind != JsonValueKind.String)
                throw new JsonException("The report payload has no requester.");
            var reportTemplate = envelope.EventType == "procurement.report.ready" ? ReportReadyTemplate : ReportFailedTemplate;
            return [new NotificationRequest(UserRecipient, requester.GetGuid(), reportTemplate, envelope.AggregateType, envelope.AggregateId)];
        }

        var (template, property) = envelope.EventType switch
        {
            "procurement.rfq.published" => (InvitationTemplate, "invitedSupplierIds"),
            "procurement.rfq.deadline-extended" => (DeadlineExtendedTemplate, "invitedSupplierIds"),
            "procurement.rfq.reminder-requested" => (ReminderTemplate, "supplierIds"),
            _ => throw new JsonException("The event type has no notification template.")
        };
        if (!envelope.Payload.TryGetProperty(property, out var recipients) || recipients.ValueKind != JsonValueKind.Array)
            throw new JsonException("The notification payload has no recipient list.");

        return recipients.EnumerateArray()
            .Select(element => element.GetGuid())
            .Distinct()
            .OrderBy(id => id)
            .Select(id => new NotificationRequest(SupplierRecipient, id, template, envelope.AggregateType, envelope.AggregateId))
            .ToArray();
    }
}
