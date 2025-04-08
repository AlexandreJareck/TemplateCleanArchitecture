using Microsoft.AspNetCore.Mvc;
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

    [HttpPost("create-order")]
    public IActionResult Post([FromBody] Order order)
    {
        order.Id = Guid.NewGuid();
        _rabbitService.PublishPedido(order);
        return Ok($"order {order.Id} send for queue.");
    }
}
