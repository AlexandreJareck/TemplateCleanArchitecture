#nullable enable
using Template.Application.Interfaces;
using Template.Application.Interfaces.Messaging;
using Template.Application.Messaging.Procurement;

namespace Template.Application.Services.Procurement;

public sealed class ProcurementIntegrationEventHandler(
    IProcurementEventEffectStore effects,
    IClock clock) : IProcurementIntegrationEventHandler
{
    public Task<InboxApplyResult> HandleAsync(
        string consumerName,
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        ProcurementIntegrationEventContract.Validate(envelope);
        return effects.ApplyOnceAsync(consumerName, envelope, clock.UtcNow, cancellationToken);
    }
}
