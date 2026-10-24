#nullable enable
using Microsoft.Extensions.Logging;

namespace Template.Application.Logging;

/// <summary>
/// Stable structured-log event identifiers shared by every layer. Ids and names are a dashboard contract: never
/// reuse an id, never put dynamic values in a name, and document any rename with a compatibility mapping
/// (see <c>docs/refactoring/phase-13-report.md</c>). Ranges: 1xxx HTTP, 2xxx lifecycle and configuration, 3xxx cache,
/// 4xxx messaging, 5xxx search, 6xxx health, 7xxx orders, 8xxx products, 9xxx access (Phase 24), 10xxx enrichment (Phase 25), 11xxx procurement (Phase 27).
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

    // Never log tokens, cookies, hashes, passwords or user names with these events; family IDs are opaque.
    public static readonly EventId SessionLoginRejected = new(9000, nameof(SessionLoginRejected));
    public static readonly EventId SessionCreated = new(9001, nameof(SessionCreated));
    public static readonly EventId SessionReuseDetected = new(9002, nameof(SessionReuseDetected));
    public static readonly EventId SessionRevoked = new(9003, nameof(SessionRevoked));
    public static readonly EventId CsrfRejected = new(9004, nameof(CsrfRejected));
    public static readonly EventId AuthenticationRateLimited = new(9005, nameof(AuthenticationRateLimited));
    public static readonly EventId SecurityAuditFailed = new(9006, nameof(SecurityAuditFailed));

    // Phase 25 optional enrichment (INT-001): provider, operation, outcome and safe reasons only, never CNPJ, CEP,
    // names, addresses or contact data.
    public static readonly EventId EnrichmentLookupCompleted = new(10000, nameof(EnrichmentLookupCompleted));
    public static readonly EventId ProviderCallFailed = new(10001, nameof(ProviderCallFailed));
    public static readonly EventId ProviderCircuitOpened = new(10002, nameof(ProviderCircuitOpened));
    public static readonly EventId ProviderResponseInvalid = new(10003, nameof(ProviderResponseInvalid));

    // Phase 27 procurement (BUD-*, REQ-*): opaque IDs, event types, revisions, states and amounts only; never purposes,
    // line descriptions, attachment names or other free text.
    public static readonly EventId ProcurementEventRecorded = new(11000, nameof(ProcurementEventRecorded));
    public static readonly EventId ProcurementConcurrencyConflict = new(11001, nameof(ProcurementConcurrencyConflict));
    public static readonly EventId BudgetReservationRejected = new(11002, nameof(BudgetReservationRejected));
    public static readonly EventId OutboxBatchClaimed = new(11003, nameof(OutboxBatchClaimed));
    public static readonly EventId OutboxEventDispatched = new(11004, nameof(OutboxEventDispatched));
    public static readonly EventId OutboxDispatchFailed = new(11005, nameof(OutboxDispatchFailed));
    public static readonly EventId InboxEffectApplied = new(11006, nameof(InboxEffectApplied));
    public static readonly EventId InboxDuplicateSkipped = new(11007, nameof(InboxDuplicateSkipped));
    public static readonly EventId ProcurementMessageDeadLettered = new(11008, nameof(ProcurementMessageDeadLettered));

    // Phase 29 sourcing (SRC-*): currencies, outcomes, template names and counts only; never quote content.
    public static readonly EventId SourcingRateResolved = new(11100, nameof(SourcingRateResolved));
    public static readonly EventId SourcingRateUnavailable = new(11101, nameof(SourcingRateUnavailable));
    public static readonly EventId NotificationsQueued = new(11102, nameof(NotificationsQueued));

    // Phase 30 files and reports (FIL-*, RPT-*): job IDs, report types, safe failure codes, sizes and durations only;
    // never parameters' business content, file names, storage keys or exception text.
    public static readonly EventId ReportJobClaimed = new(11200, nameof(ReportJobClaimed));
    public static readonly EventId ReportJobCompleted = new(11201, nameof(ReportJobCompleted));
    public static readonly EventId ReportJobFailed = new(11202, nameof(ReportJobFailed));
    public static readonly EventId ReportJobLeaseLost = new(11203, nameof(ReportJobLeaseLost));
    public static readonly EventId ReportJobReleased = new(11204, nameof(ReportJobReleased));
    public static readonly EventId ReportMaintenanceCompleted = new(11205, nameof(ReportMaintenanceCompleted));
    public static readonly EventId FileStorageFailed = new(11206, nameof(FileStorageFailed));

    // Phase 31: approval workflow (IDs, levels and outcomes only; never names, amounts or reasons).
    public static readonly EventId ApprovalStarted = new(11300, nameof(ApprovalStarted));
    public static readonly EventId ApprovalStepAssigned = new(11301, nameof(ApprovalStepAssigned));
    public static readonly EventId ApprovalDecisionRecorded = new(11302, nameof(ApprovalDecisionRecorded));
    public static readonly EventId ApprovalDecisionDenied = new(11303, nameof(ApprovalDecisionDenied));
    public static readonly EventId ApprovalTimerProcessed = new(11304, nameof(ApprovalTimerProcessed));
    public static readonly EventId ApprovalFinalized = new(11305, nameof(ApprovalFinalized));
    public static readonly EventId ApprovalEnded = new(11306, nameof(ApprovalEnded));
    public static readonly EventId ApprovalSignalFailed = new(11307, nameof(ApprovalSignalFailed));
    public static readonly EventId ApprovalWorkflowStartRequested = new(11308, nameof(ApprovalWorkflowStartRequested));
    public static readonly EventId ApprovalReconciliationCompleted = new(11309, nameof(ApprovalReconciliationCompleted));
    public static readonly EventId ApprovalReconciliationRepairFailed = new(11310, nameof(ApprovalReconciliationRepairFailed));
}
