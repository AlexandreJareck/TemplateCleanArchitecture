#nullable enable
using Microsoft.Extensions.Logging;

namespace Template.Application.Logging;

/// <summary>
/// Stable structured-log event identifiers shared by every layer. Ids and names are a dashboard contract: never
/// reuse an id, never put dynamic values in a name, and document any rename with a compatibility mapping
/// (see <c>docs/refactoring/phase-13-report.md</c>). Ranges: 1xxx HTTP, 2xxx lifecycle and configuration, 3xxx cache,
/// 4xxx messaging, 5xxx search, 6xxx health, 7xxx orders, 8xxx products.
/// </summary>
public static class LogEvents
{
    public static readonly EventId RequestCompleted = new(1000, nameof(RequestCompleted));
    public static readonly EventId UnhandledException = new(1001, nameof(UnhandledException));
    public static readonly EventId RequestCanceled = new(1002, nameof(RequestCanceled));
    public static readonly EventId AuthenticationChallenged = new(1003, nameof(AuthenticationChallenged));
    public static readonly EventId AuthorizationForbidden = new(1004, nameof(AuthorizationForbidden));

    public static readonly EventId ApplicationStarted = new(2000, nameof(ApplicationStarted));
    public static readonly EventId ApplicationStopping = new(2001, nameof(ApplicationStopping));
    public static readonly EventId ApplicationStartupFailed = new(2002, nameof(ApplicationStartupFailed));

    public static readonly EventId CacheOperationFailed = new(3000, nameof(CacheOperationFailed));
    public static readonly EventId CacheEntryUnreadable = new(3001, nameof(CacheEntryUnreadable));

    public static readonly EventId MessagingConsumersDisabled = new(4000, nameof(MessagingConsumersDisabled));
    public static readonly EventId MessagingConsumersStarted = new(4001, nameof(MessagingConsumersStarted));
    public static readonly EventId MessagingConsumersStopped = new(4002, nameof(MessagingConsumersStopped));
    public static readonly EventId MessagingBrokerUnreachable = new(4003, nameof(MessagingBrokerUnreachable));
    public static readonly EventId MessagingConnectionLost = new(4004, nameof(MessagingConnectionLost));
    public static readonly EventId MessagingConnectionRecovered = new(4005, nameof(MessagingConnectionRecovered));
    public static readonly EventId MessagingPublisherConnected = new(4006, nameof(MessagingPublisherConnected));
    public static readonly EventId MessagePublished = new(4007, nameof(MessagePublished));
    public static readonly EventId MessagePublishFailed = new(4008, nameof(MessagePublishFailed));
    public static readonly EventId MessagePublishTimedOut = new(4009, nameof(MessagePublishTimedOut));
    public static readonly EventId MessageRejected = new(4010, nameof(MessageRejected));
    public static readonly EventId MessageRetryScheduled = new(4011, nameof(MessageRetryScheduled));
    public static readonly EventId MessageRetriesExhausted = new(4012, nameof(MessageRetriesExhausted));
    public static readonly EventId MessageDeadLettered = new(4013, nameof(MessageDeadLettered));
    public static readonly EventId MessageDeadLetterFailed = new(4014, nameof(MessageDeadLetterFailed));
    public static readonly EventId MessageSettleFailed = new(4015, nameof(MessageSettleFailed));
    public static readonly EventId MessageProcessingCanceled = new(4016, nameof(MessageProcessingCanceled));
    public static readonly EventId MessagingChannelClosed = new(4017, nameof(MessagingChannelClosed));

    public static readonly EventId SearchIndexConfigured = new(5000, nameof(SearchIndexConfigured));
    public static readonly EventId SearchOperationFailed = new(5001, nameof(SearchOperationFailed));
    public static readonly EventId SearchBulkPartiallyFailed = new(5002, nameof(SearchBulkPartiallyFailed));
    public static readonly EventId SearchResponseUnreadable = new(5003, nameof(SearchResponseUnreadable));

    public static readonly EventId HealthDependencyStatusChanged = new(6000, nameof(HealthDependencyStatusChanged));

    public static readonly EventId OrderReceived = new(7000, nameof(OrderReceived));
    public static readonly EventId OrderDuplicateSkipped = new(7001, nameof(OrderDuplicateSkipped));

    public static readonly EventId ProductCacheHit = new(8000, nameof(ProductCacheHit));
    public static readonly EventId ProductCached = new(8001, nameof(ProductCached));
}
