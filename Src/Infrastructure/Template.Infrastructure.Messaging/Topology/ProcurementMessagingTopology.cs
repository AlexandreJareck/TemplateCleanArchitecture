using RabbitMQ.Client;
using Template.Application.Messaging.Procurement;

namespace Template.Infrastructure.Messaging.Topology;

public static class ProcurementMessagingTopology
{
    public const string Exchange = "procurement.events";
    public const string DeadLetterExchange = "procurement.events.dead-letter";
    public const string ProjectionConsumer = ProcurementConsumers.Projection;
    public const string ProjectionQueue = "procurement.projections.v1";
    public const string ProjectionDeadLetterQueue = "procurement.projections.v1.dead-letter";

    // Phase 29 (SRC-022): sourcing notifications have their own durable queue, Inbox consumer name and DLQ.
    public const string NotificationConsumer = ProcurementConsumers.Notification;
    public const string NotificationQueue = "procurement.notifications.v1";
    public const string NotificationDeadLetterQueue = "procurement.notifications.v1.dead-letter";

    // Phase 31 (APR-001): award submissions start the approval orchestration through their own queue and DLQ.
    public const string ApprovalConsumer = ProcurementConsumers.Approval;
    public const string ApprovalQueue = "procurement.approvals.v1";
    public const string ApprovalDeadLetterQueue = "procurement.approvals.v1.dead-letter";
    public const string ContractType = "procurement.integration-event.v1";

    /// <summary>Consumers hosted by the Worker: queue and Inbox consumer name.</summary>
    public static IReadOnlyList<(string Queue, string Consumer)> Consumers { get; } =
    [
        (ProjectionQueue, ProjectionConsumer),
        (NotificationQueue, NotificationConsumer),
        (ApprovalQueue, ApprovalConsumer)
    ];

    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(ProjectionQueue, durable: true, exclusive: false, autoDelete: false, arguments: null, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(ProjectionQueue, Exchange, "procurement.#", cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(ProjectionDeadLetterQueue, durable: true, exclusive: false, autoDelete: false, arguments: null, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(ProjectionDeadLetterQueue, DeadLetterExchange, ProjectionQueue, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(NotificationQueue, durable: true, exclusive: false, autoDelete: false, arguments: null, cancellationToken: cancellationToken);
        foreach (var eventType in ProcurementConsumers.NotificationEventTypes.Order(StringComparer.Ordinal))
            await channel.QueueBindAsync(NotificationQueue, Exchange, eventType, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(NotificationDeadLetterQueue, durable: true, exclusive: false, autoDelete: false, arguments: null, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(NotificationDeadLetterQueue, DeadLetterExchange, NotificationQueue, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(ApprovalQueue, durable: true, exclusive: false, autoDelete: false, arguments: null, cancellationToken: cancellationToken);
        foreach (var eventType in ProcurementConsumers.ApprovalEventTypes.Order(StringComparer.Ordinal))
            await channel.QueueBindAsync(ApprovalQueue, Exchange, eventType, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(ApprovalDeadLetterQueue, durable: true, exclusive: false, autoDelete: false, arguments: null, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(ApprovalDeadLetterQueue, DeadLetterExchange, ApprovalQueue, cancellationToken: cancellationToken);
    }
}
