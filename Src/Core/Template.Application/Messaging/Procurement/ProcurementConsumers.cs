#nullable enable
using System.Text.Json;
using Template.Application.Interfaces.Messaging;

namespace Template.Application.Messaging.Procurement;

/// <summary>Durable consumer names; each is part of the Inbox key (consumer, event ID).</summary>
public static class ProcurementConsumers
{
    public const string Projection = "procurement-projection-v1";
    public const string Notification = "procurement-notification-v1";

    /// <summary>Events the notification consumer receives (SRC-022: invitation, extension and reminder).</summary>
    public static IReadOnlySet<string> NotificationEventTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "procurement.rfq.published",
        "procurement.rfq.deadline-extended",
        "procurement.rfq.reminder-requested"
    };
}

/// <summary>
/// Turns a sourcing event into notification requests for the invited suppliers named in its payload. Recipients are
/// supplier IDs; contact data is resolved only by a future delivery adapter and never travels in events (NFR-SEC-7).
/// </summary>
public static class RfqNotificationPlanner
{
    public const string SupplierRecipient = "Supplier";
    public const string InvitationTemplate = "sourcing.rfq-invitation.v1";
    public const string DeadlineExtendedTemplate = "sourcing.rfq-deadline-extended.v1";
    public const string ReminderTemplate = "sourcing.rfq-reminder.v1";

    public static IReadOnlyList<NotificationRequest> Plan(IntegrationEventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
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
