#nullable enable
using Template.Application.Messaging;
using Template.Application.Messaging.Orders;

namespace Template.Application.Interfaces.Messaging;

/// <summary>
/// Publishes <see cref="OrderPlacedEvent"/>. A completed task means the broker confirmed it accepted the message; it
/// does not mean that any subscriber matched or processed it. Throws <see cref="MessagePublishException"/> when the
/// message was not accepted, and <see cref="OperationCanceledException"/> when the caller cancels (outcome unknown).
/// </summary>
public interface IOrderEventPublisher
{
    Task PublishBroadcastAsync(OrderPlacedEvent orderPlaced, CancellationToken cancellationToken);

    Task PublishDirectAsync(OrderPlacedEvent orderPlaced, string routingKey, CancellationToken cancellationToken);

    Task PublishTopicAsync(OrderPlacedEvent orderPlaced, string routingKey, CancellationToken cancellationToken);
}
