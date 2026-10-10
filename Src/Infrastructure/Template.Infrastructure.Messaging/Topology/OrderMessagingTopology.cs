using RabbitMQ.Client;

namespace Template.Infrastructure.Messaging.Topology;

/// <summary>A queue consumed by the application and the exchange binding that feeds it.</summary>
/// <param name="Name">Logical subscription name used for logs and deduplication.</param>
public sealed record OrderSubscription(string Name, string Queue, string Exchange, string BindingKey)
{
    public string DeadLetterQueue => $"{Queue}{OrderMessagingTopology.DeadLetterQueueSuffix}";
}

/// <summary>
/// Single owner of the order topology. Names, exchange types, durability and bindings are the ones used before
/// Phase 9; the dead-letter exchange and queues are additive. Every declaration is idempotent.
/// </summary>
public static class OrderMessagingTopology
{
    public const string FanoutExchange = "order_exchange_fanout";
    public const string DirectExchange = "order_exchange_direct";
    public const string TopicExchange = "order_exchange_topic";
    public const string DeadLetterExchange = "order_dead_letter_exchange";
    public const string DeadLetterQueueSuffix = ".dead_letter";

    public static readonly OrderSubscription FanoutA = new("fanout-a", "queue_fanout_a", FanoutExchange, string.Empty);
    public static readonly OrderSubscription FanoutB = new("fanout-b", "queue_fanout_b", FanoutExchange, string.Empty);
    public static readonly OrderSubscription UrgentOrders = new("direct-urgent-orders", "urgent_orders", DirectExchange, "order.urgent");
    public static readonly OrderSubscription BrazilOrders = new("topic-orders-brazil", "orders_brazil", TopicExchange, "order.brazil.*");

    public static IReadOnlyList<OrderSubscription> Subscriptions { get; } = [FanoutA, FanoutB, UrgentOrders, BrazilOrders];

    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken)
    {
        // The pre-Phase 9 exchanges were declared with the 6.x defaults (durable: false); redeclaring them as durable
        // would fail with PRECONDITION_FAILED on a running broker, so they stay non-durable.
        await channel.ExchangeDeclareAsync(FanoutExchange, ExchangeType.Fanout, durable: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(DirectExchange, ExchangeType.Direct, durable: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(TopicExchange, ExchangeType.Topic, durable: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: cancellationToken);

        foreach (var subscription in Subscriptions)
        {
            // Queue arguments must stay empty: the existing durable queues were declared without arguments.
            await channel.QueueDeclareAsync(subscription.Queue, durable: true, exclusive: false, autoDelete: false, arguments: null, cancellationToken: cancellationToken);
            await channel.QueueBindAsync(subscription.Queue, subscription.Exchange, subscription.BindingKey, cancellationToken: cancellationToken);

            await channel.QueueDeclareAsync(subscription.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, arguments: null, cancellationToken: cancellationToken);
            await channel.QueueBindAsync(subscription.DeadLetterQueue, DeadLetterExchange, subscription.Queue, cancellationToken: cancellationToken);
        }
    }
}
