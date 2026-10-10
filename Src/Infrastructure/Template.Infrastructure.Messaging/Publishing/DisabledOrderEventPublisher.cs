using Template.Application.Interfaces.Messaging;
using Template.Application.Messaging;
using Template.Application.Messaging.Orders;

namespace Template.Infrastructure.Messaging.Publishing;

/// <summary>
/// Used when messaging is disabled by configuration or no broker is composed (Test environment). It never reports a
/// message as published.
/// </summary>
public sealed class DisabledOrderEventPublisher : IOrderEventPublisher
{
    public Task PublishBroadcastAsync(OrderPlacedEvent orderPlaced, CancellationToken cancellationToken) => Fail(cancellationToken);

    public Task PublishDirectAsync(OrderPlacedEvent orderPlaced, string routingKey, CancellationToken cancellationToken) => Fail(cancellationToken);

    public Task PublishTopicAsync(OrderPlacedEvent orderPlaced, string routingKey, CancellationToken cancellationToken) => Fail(cancellationToken);

    private static Task Fail(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new MessagePublishException("Messaging is disabled; the message was not published.");
    }
}
