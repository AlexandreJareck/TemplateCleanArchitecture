#nullable enable
using Template.Application.Messaging.Procurement;

namespace Template.Application.Interfaces.Messaging;

public sealed record OutboxLease(IntegrationEventEnvelope Envelope, int AttemptCount, string ClaimOwner);

public enum InboxApplyResult
{
    Applied,
    Duplicate
}

/// <summary>Stages immutable envelopes in the current application-database unit of work (INT-004).</summary>
public interface IIntegrationEventOutbox
{
    void Add(IntegrationEventEnvelope envelope);
}

/// <summary>Worker-facing durable Outbox operations. Claims are leased and safe across Worker instances.</summary>
public interface IOutboxStore
{
    Task<IReadOnlyList<OutboxLease>> ClaimAsync(
        string owner, DateTime nowUtc, TimeSpan leaseDuration, int batchSize, CancellationToken cancellationToken);
    Task MarkDispatchedAsync(Guid eventId, string owner, DateTime dispatchedAtUtc, CancellationToken cancellationToken);
    Task MarkFailedAsync(
        Guid eventId, string owner, DateTime nowUtc, DateTime nextAttemptAtUtc, int maxAttempts,
        string failureCode, CancellationToken cancellationToken);
    Task<int> DeleteDispatchedBeforeAsync(DateTime cutoffUtc, int maxRows, CancellationToken cancellationToken);
}

/// <summary>Confirmed broker publication. Completion means the configured transport accepted the persistent message.</summary>
public interface IProcurementEventPublisher
{
    Task PublishAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken);
}

/// <summary>Application use case invoked by the Worker consumer after transport validation.</summary>
public interface IProcurementIntegrationEventHandler
{
    Task<InboxApplyResult> HandleAsync(
        string consumerName, IntegrationEventEnvelope envelope, CancellationToken cancellationToken);
}

/// <summary>
/// Atomically acquires the durable Inbox entry, applies the local projection effect and completes the entry. A failed
/// transaction leaves the message retryable (INT-003).
/// </summary>
public interface IProcurementEventEffectStore
{
    Task<InboxApplyResult> ApplyOnceAsync(
        string consumerName, IntegrationEventEnvelope envelope, DateTime processedAtUtc, CancellationToken cancellationToken);
}
