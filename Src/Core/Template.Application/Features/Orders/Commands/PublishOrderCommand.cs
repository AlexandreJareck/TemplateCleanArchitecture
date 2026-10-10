#nullable enable
using MediatR;
using Template.Application.Interfaces.Messaging;
using Template.Application.Messaging.Orders;

namespace Template.Application.Features.Orders.Commands;

/// <summary>Assigns a new order id and publishes the order; returns the id once the broker accepted it.</summary>
public sealed record PublishOrderCommand(
    OrderDispatch Dispatch,
    string ProductName,
    int Quantity,
    decimal Price,
    string? RoutingKey = null) : IRequest<Guid>;

public sealed class PublishOrderCommandHandler(IOrderEventPublisher publisher) : IRequestHandler<PublishOrderCommand, Guid>
{
    public async Task<Guid> Handle(PublishOrderCommand request, CancellationToken cancellationToken)
    {
        var orderPlaced = new OrderPlacedEvent(Guid.NewGuid(), request.ProductName, request.Quantity, request.Price);

        switch (request.Dispatch)
        {
            case OrderDispatch.Broadcast:
                await publisher.PublishBroadcastAsync(orderPlaced, cancellationToken);
                break;
            case OrderDispatch.Direct:
                await publisher.PublishDirectAsync(orderPlaced, RequireRoutingKey(request), cancellationToken);
                break;
            case OrderDispatch.Topic:
                await publisher.PublishTopicAsync(orderPlaced, RequireRoutingKey(request), cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.Dispatch, "Unknown order dispatch.");
        }

        return orderPlaced.OrderId;
    }

    private static string RequireRoutingKey(PublishOrderCommand request) =>
        request.RoutingKey ?? throw new ArgumentException("Direct and topic dispatch require a routing key.", nameof(request));
}
