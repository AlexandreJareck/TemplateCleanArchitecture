#nullable enable
namespace Template.Infrastructure.Persistence.Messaging;

public sealed class OutboxMessage
{
    private OutboxMessage() { }

    public OutboxMessage(
        Guid eventId, string eventType, int eventVersion, DateTime occurredAtUtc, string correlationId,
        Guid? causationId, string aggregateType, Guid aggregateId, string aggregateVersion, string payloadJson,
        DateTime createdAtUtc)
    {
        EventId = eventId;
        EventType = eventType;
        EventVersion = eventVersion;
        OccurredAtUtc = occurredAtUtc;
        CorrelationId = correlationId;
        CausationId = causationId;
        AggregateType = aggregateType;
        AggregateId = aggregateId;
        AggregateVersion = aggregateVersion;
        PayloadJson = payloadJson;
        CreatedAtUtc = createdAtUtc;
        NextAttemptAtUtc = createdAtUtc;
    }

    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public int EventVersion { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public Guid? CausationId { get; private set; }
    public string AggregateType { get; private set; } = string.Empty;
    public Guid AggregateId { get; private set; }
    public string AggregateVersion { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = "{}";
    public DateTime CreatedAtUtc { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime NextAttemptAtUtc { get; private set; }
    public string? ClaimOwner { get; private set; }
    public DateTime? ClaimExpiresAtUtc { get; private set; }
    public DateTime? DispatchedAtUtc { get; private set; }
    public DateTime? PoisonedAtUtc { get; private set; }
    public string? LastFailureCode { get; private set; }

    public void Claim(string owner, DateTime expiresAtUtc)
    {
        ClaimOwner = owner;
        ClaimExpiresAtUtc = expiresAtUtc;
    }

    public void MarkDispatched(DateTime atUtc)
    {
        DispatchedAtUtc = atUtc;
        ClaimOwner = null;
        ClaimExpiresAtUtc = null;
        LastFailureCode = null;
    }

    public void MarkFailed(DateTime atUtc, DateTime nextAttemptAtUtc, int maxAttempts, string failureCode)
    {
        AttemptCount++;
        LastFailureCode = failureCode;
        ClaimOwner = null;
        ClaimExpiresAtUtc = null;
        NextAttemptAtUtc = nextAttemptAtUtc;
        if (AttemptCount >= maxAttempts)
            PoisonedAtUtc = atUtc;
    }
}

public sealed class InboxMessage
{
    private InboxMessage() { }

    public InboxMessage(string consumerName, Guid eventId, string eventType, DateTime receivedAtUtc)
    {
        ConsumerName = consumerName;
        EventId = eventId;
        EventType = eventType;
        ReceivedAtUtc = receivedAtUtc;
    }

    public string ConsumerName { get; private set; } = string.Empty;
    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public void Complete(DateTime atUtc) => CompletedAtUtc = atUtc;
}

public sealed class ProcurementProjectionState
{
    private ProcurementProjectionState() { }

    public ProcurementProjectionState(
        string consumerName, string aggregateType, Guid aggregateId, string eventType,
        Guid eventId, string aggregateVersion, DateTime processedAtUtc)
    {
        ConsumerName = consumerName;
        AggregateType = aggregateType;
        AggregateId = aggregateId;
        EventType = eventType;
        LastEventId = eventId;
        LastAggregateVersion = aggregateVersion;
        ProcessedCount = 1;
        UpdatedAtUtc = processedAtUtc;
    }

    public string ConsumerName { get; private set; } = string.Empty;
    public string AggregateType { get; private set; } = string.Empty;
    public Guid AggregateId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public Guid LastEventId { get; private set; }
    public string LastAggregateVersion { get; private set; } = string.Empty;
    public long ProcessedCount { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public void Apply(Guid eventId, string aggregateVersion, DateTime processedAtUtc)
    {
        LastEventId = eventId;
        LastAggregateVersion = aggregateVersion;
        ProcessedCount++;
        UpdatedAtUtc = processedAtUtc;
    }
}

/// <summary>
/// Local notification delivery (Phase 29 fake/local adapter, SRC-022): one row per (event, recipient, template),
/// recorded in the same transaction as the Inbox entry. Phase 33 replaces the local sink with Mailpit.
/// </summary>
public sealed class NotificationDelivery
{
    public const string LocalChannel = "Local";
    public const string RecordedStatus = "Recorded";

    private NotificationDelivery() { }

    public NotificationDelivery(
        Guid id, string consumerName, Guid eventId, string eventType, string recipientType, Guid recipientId, string template,
        string aggregateType, Guid aggregateId, DateTime createdAtUtc)
    {
        Id = id;
        ConsumerName = consumerName;
        EventId = eventId;
        EventType = eventType;
        RecipientType = recipientType;
        RecipientId = recipientId;
        Template = template;
        AggregateType = aggregateType;
        AggregateId = aggregateId;
        Channel = LocalChannel;
        Status = RecordedStatus;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public string ConsumerName { get; private set; } = string.Empty;
    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string RecipientType { get; private set; } = string.Empty;
    public Guid RecipientId { get; private set; }
    public string Template { get; private set; } = string.Empty;
    public string AggregateType { get; private set; } = string.Empty;
    public Guid AggregateId { get; private set; }
    public string Channel { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
}
