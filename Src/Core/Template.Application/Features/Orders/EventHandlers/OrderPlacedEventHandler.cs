#nullable enable
using Microsoft.Extensions.Logging;
using Template.Application.Interfaces.Messaging;
using Template.Application.Messaging;
using Template.Application.Messaging.Orders;

namespace Template.Application.Features.Orders.EventHandlers;

/// <summary>
/// Records the received order (the current use case only logs it). Idempotent per subscription: a message id that was
/// already processed is accepted without repeating the side effect. The id is marked only after success.
/// </summary>
public sealed class OrderPlacedEventHandler(IProcessedMessageStore processedMessages, ILogger<OrderPlacedEventHandler> logger)
    : IOrderPlacedEventHandler
{
    public async Task HandleAsync(OrderPlacedEvent orderPlaced, IncomingMessageContext context, CancellationToken cancellationToken)
    {
        if (await processedMessages.WasProcessedAsync(context.Subscription, context.MessageId, cancellationToken))
        {
            logger.LogInformation(
                "Skipping duplicate order {OrderId} on {Subscription} (message {MessageId})",
                orderPlaced.OrderId, context.Subscription, context.MessageId);
            return;
        }

        logger.LogInformation(
            "Order {OrderId} received on {Subscription} (redelivery: {IsRedelivery})",
            orderPlaced.OrderId, context.Subscription, context.IsRedelivery);

        await processedMessages.MarkProcessedAsync(context.Subscription, context.MessageId, cancellationToken);
    }
}
