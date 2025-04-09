﻿using Microsoft.AspNetCore.Mvc;
using Template.WebApi.Models;
using Template.WebApi.Service;

namespace Template.WebApi.Controllers.v1;

[ApiController]
[ApiVersion("1")]
public class MessageController : BaseApiController  
{
    private readonly RabbitMqService _rabbitService;

    public MessageController(RabbitMqService rabbitService)
    {
        _rabbitService = rabbitService;
    }

    [HttpPost("create-order-fanout")]
    public IActionResult PostFanout([FromBody] Order order)
    {
        order.Id = Guid.NewGuid();
        _rabbitService.PublishOrderFanout(order);
        return Ok($"FANOUT - Order {order.Id} sent to queue.");
    }

    [HttpPost("create-order-direct")]
    public IActionResult PostDirect([FromBody] Order order, [FromQuery] string routingKey)
    {
        order.Id = Guid.NewGuid();
        _rabbitService.PublishOrderDirect(order, routingKey);
        return Ok($"DIRECT - Order {order.Id} sent with routing key '{routingKey}'.");
    }

    [HttpPost("create-order-topic")]
    public IActionResult PostTopic([FromBody] Order order, [FromQuery] string routingKey)
    {
        order.Id = Guid.NewGuid();
        _rabbitService.PublishOrderTopic(order, routingKey);
        return Ok($" TOPIC - Order {order.Id} sent with routing key '{routingKey}'.");
    }
}
