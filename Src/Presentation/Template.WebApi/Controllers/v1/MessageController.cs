using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Template.Application.Features.Orders.Commands;
using Template.Application.Messaging.Orders;
using Template.Domain.Order;

namespace Template.WebApi.Controllers.v1;

[ApiController]
[ApiVersion("1", Deprecated = true)]
public class MessageController(ISender sender) : BaseApiController
{
    // The domain Order stays as the v1 request body so the published OpenAPI schema is unchanged; its Id is ignored
    // because a new order id is assigned on every publish.
    [HttpPost("create-order-fanout")]
    public async Task<IActionResult> PostFanout([FromBody] Order order, CancellationToken cancellationToken)
    {
        var orderId = await sender.Send(ToCommand(order, OrderDispatch.Broadcast, routingKey: null), cancellationToken);
        return Ok($"FANOUT - Order {orderId} sent to queue.");
    }

    [HttpPost("create-order-direct")]
    public async Task<IActionResult> PostDirect([FromBody] Order order, [FromQuery] string routingKey, CancellationToken cancellationToken)
    {
        var orderId = await sender.Send(ToCommand(order, OrderDispatch.Direct, routingKey), cancellationToken);
        return Ok($"DIRECT - Order {orderId} sent with routing key '{routingKey}'.");
    }

    [HttpPost("create-order-topic")]
    public async Task<IActionResult> PostTopic([FromBody] Order order, [FromQuery] string routingKey, CancellationToken cancellationToken)
    {
        var orderId = await sender.Send(ToCommand(order, OrderDispatch.Topic, routingKey), cancellationToken);
        return Ok($" TOPIC - Order {orderId} sent with routing key '{routingKey}'.");
    }

    private static PublishOrderCommand ToCommand(Order order, OrderDispatch dispatch, string? routingKey) =>
        new(dispatch, order.ProductName, order.Quantity, order.Price, routingKey);
}
