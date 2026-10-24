#nullable enable
using System.Text.Json;
using Template.Application.Interfaces.Messaging;

namespace Template.Application.Messaging.Procurement;

/// <summary>Durable consumer names; each is part of the Inbox key (consumer, event ID).</summary>
public static class ProcurementConsumers
{
    public const string Projection = "procurement-projection-v1";
    public const string Notification = "procurement-notification-v1";

    /// <summary>Phase 31 (APR-001): starts the approval orchestration for each submitted award.</summary>
    public const string Approval = "procurement-approval-v1";

    public static IReadOnlySet<string> ApprovalEventTypes { get; } = new HashSet<string>(StringComparer.Ordinal) { "procurement.award.submitted" };

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
        "procurement.report.failed",
        "procurement.approval.step-assigned",
        "procurement.approval.reminder-due",
        "procurement.approval.step-escalated",
        "procurement.approval.completed",
        "procurement.approval.rejected",
        "procurement.approval.expired"
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
    public const string RoleRecipient = "Role";
    public const string ApprovalAssignedTemplate = "approvals.step-assigned.v1";
    public const string ApprovalReminderTemplate = "approvals.step-reminder.v1";
    public const string ApprovalEscalatedTemplate = "approvals.step-escalated.v1";
    public const string ApprovalApprovedTemplate = "approvals.approved.v1";
    public const string ApprovalRejectedTemplate = "approvals.rejected.v1";
    public const string ApprovalExpiredTemplate = "approvals.expired.v1";

    /// <summary>
    /// Recipient ID of a role-group delivery (Phase 31): there is one per event and template, so the ID is empty and the
    /// future delivery adapter (Phase 33) resolves the group from the event's <c>assigneeRole</c> and its members when it
    /// sends; events never carry member lists.
    /// </summary>
    public static readonly Guid RoleGroupRecipientId = Guid.Empty;

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

        if (envelope.EventType.StartsWith("procurement.approval.", StringComparison.Ordinal))
            return PlanApproval(envelope);

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

    /// <summary>Approval events: the assignee (user or role group) of a step, or the requester of an ended approval.</summary>
    private static IReadOnlyList<NotificationRequest> PlanApproval(IntegrationEventEnvelope envelope)
    {
        var template = envelope.EventType switch
        {
            "procurement.approval.step-assigned" => ApprovalAssignedTemplate,
            "procurement.approval.reminder-due" => ApprovalReminderTemplate,
            "procurement.approval.step-escalated" => ApprovalEscalatedTemplate,
            "procurement.approval.completed" => ApprovalApprovedTemplate,
            "procurement.approval.rejected" => ApprovalRejectedTemplate,
            "procurement.approval.expired" => ApprovalExpiredTemplate,
            _ => throw new JsonException("The event type has no notification template.")
        };
        var payload = envelope.Payload;
        if (payload.TryGetProperty("requesterId", out var requester) && requester.ValueKind == JsonValueKind.String)
            return [new NotificationRequest(UserRecipient, requester.GetGuid(), template, envelope.AggregateType, envelope.AggregateId)];
        if (payload.TryGetProperty("assigneeUserId", out var user) && user.ValueKind == JsonValueKind.String)
            return [new NotificationRequest(UserRecipient, user.GetGuid(), template, envelope.AggregateType, envelope.AggregateId)];
        if (payload.TryGetProperty("assigneeRole", out var role) && role.ValueKind == JsonValueKind.String && role.GetString() is { Length: > 0 })
            return [new NotificationRequest(RoleRecipient, RoleGroupRecipientId, template, envelope.AggregateType, envelope.AggregateId)];
        throw new JsonException("The approval payload has no recipient.");
    }
}
