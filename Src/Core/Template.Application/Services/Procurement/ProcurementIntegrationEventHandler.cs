#nullable enable
using Template.Application.Features.Approvals;
using Template.Application.Features.Purchasing;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Messaging;
using Template.Application.Messaging.Procurement;

namespace Template.Application.Services.Procurement;

/// <summary>
/// Application use case behind every Worker consumer: the projection consumer applies its protected projection effect;
/// the notification consumer (Phase 29, SRC-022) queues one local delivery per recipient. Both are effectively once
/// through the durable Inbox (INT-003). The approval consumer (Phase 31, APR-001) starts the orchestration, which is
/// idempotent by its deterministic instance ID. The purchasing consumer (Phase 32, PUR-001/007) commits each effect with
/// its Inbox entry.
/// </summary>
public sealed class ProcurementIntegrationEventHandler(
    IProcurementEventEffectStore effects,
    INotificationDeliveryStore notifications,
    ApprovalWorkflowStarter approvals,
    PurchasingEventHandler purchasing,
    IClock clock) : IProcurementIntegrationEventHandler
{
    public Task<InboxApplyResult> HandleAsync(
        string consumerName,
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        ProcurementIntegrationEventContract.Validate(envelope);
        if (string.Equals(consumerName, ProcurementConsumers.Approval, StringComparison.Ordinal))
            return approvals.HandleAsync(envelope, cancellationToken);
        if (string.Equals(consumerName, ProcurementConsumers.Purchasing, StringComparison.Ordinal))
            return purchasing.HandleAsync(envelope, cancellationToken);
        if (string.Equals(consumerName, ProcurementConsumers.Notification, StringComparison.Ordinal))
            return notifications.DeliverOnceAsync(consumerName, envelope, RfqNotificationPlanner.Plan(envelope), clock.UtcNow, cancellationToken);
        return effects.ApplyOnceAsync(consumerName, envelope, clock.UtcNow, cancellationToken);
    }
}
