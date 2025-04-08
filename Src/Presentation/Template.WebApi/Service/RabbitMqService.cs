using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using Template.WebApi.Models;

namespace Template.WebApi.Service;

public class RabbitMqService
{
    private readonly ConnectionFactory _factory;

    public RabbitMqService(IConfiguration config)
    {
        _factory = new ConnectionFactory
        {
            HostName = "localhost",
            UserName = "kalo",
            Password = "kalo"
        };
    }

    public void PublishPedido(Order order)
    {
        using var connection = _factory.CreateConnection();
        using var channel = connection.CreateModel();

        channel.QueueDeclare(
           queue: "orders",
           durable: true,
           exclusive: false,
           autoDelete: false,
           arguments: null
       );

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(order));

        var props = channel.CreateBasicProperties();
        props.Persistent = true;

        channel.BasicPublish(
           exchange: "",
           routingKey: "orders",
           basicProperties: null,
           body: body
       );
    }
}
