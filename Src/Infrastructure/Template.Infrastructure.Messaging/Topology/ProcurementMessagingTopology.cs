using RabbitMQ.Client;

namespace Template.Infrastructure.Messaging.Topology;

public static class ProcurementMessagingTopology
{
    public const string Exchange = "procurement.events";
    public const string DeadLetterExchange = "procurement.events.dead-letter";
    public const string ProjectionConsumer = "procurement-projection-v1";
    public const string ProjectionQueue = "procurement.projections.v1";
    public const string ProjectionDeadLetterQueue = "procurement.projections.v1.dead-letter";
    public const string ContractType = "procurement.integration-event.v1";

    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(ProjectionQueue, durable: true, exclusive: false, autoDelete: false, arguments: null, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(ProjectionQueue, Exchange, "procurement.#", cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(ProjectionDeadLetterQueue, durable: true, exclusive: false, autoDelete: false, arguments: null, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(ProjectionDeadLetterQueue, DeadLetterExchange, ProjectionQueue, cancellationToken: cancellationToken);
    }
}
