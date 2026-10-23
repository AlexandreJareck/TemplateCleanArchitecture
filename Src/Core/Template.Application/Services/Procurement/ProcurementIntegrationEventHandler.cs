#nullable enable
using Template.Application.Interfaces;
using Template.Application.Interfaces.Messaging;
using Template.Application.Messaging.Procurement;

namespace Template.Application.Services.Procurement;

/// <summary>
/// Application use case behind every Worker consumer: the projection consumer applies its protected projection effect;
/// the notification consumer (Phase 29, SRC-022) queues one local delivery per invited supplier. Both are effectively
/// once through the durable Inbox (INT-003).
/// </summary>
public sealed class ProcurementIntegrationEventHandler(
    IProcurementEventEffectStore effects,
    INotificationDeliveryStore notifications,
    IClock clock) : IProcurementIntegrationEventHandler
{
    public Task<InboxApplyResult> HandleAsync(
        string consumerName,
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        ProcurementIntegrationEventContract.Validate(envelope);
        if (string.Equals(consumerName, ProcurementConsumers.Notification, StringComparison.Ordinal))
            return notifications.DeliverOnceAsync(consumerName, envelope, RfqNotificationPlanner.Plan(envelope), clock.UtcNow, cancellationToken);
        return effects.ApplyOnceAsync(consumerName, envelope, clock.UtcNow, cancellationToken);
    }
}
