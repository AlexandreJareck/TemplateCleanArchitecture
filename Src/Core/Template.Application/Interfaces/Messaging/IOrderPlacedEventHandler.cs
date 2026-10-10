#nullable enable
using Template.Application.Messaging;
using Template.Application.Messaging.Orders;

namespace Template.Application.Interfaces.Messaging;

/// <summary>
/// Processes a received <see cref="OrderPlacedEvent"/>. Delivery is at-least-once, so implementations must tolerate
/// duplicates. Returning normally means the message was processed; throwing means it was not.
/// </summary>
public interface IOrderPlacedEventHandler
{
    Task HandleAsync(OrderPlacedEvent orderPlaced, IncomingMessageContext context, CancellationToken cancellationToken);
}
