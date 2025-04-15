using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using Template.Domain.Order;

namespace Template.Application.Services.RabbitMq;

public class RabbitMqService : IRabbitMqService
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

    public void PublishOrderFanout(Order order)
    {
        using var connection = _factory.CreateConnection();
        using var channel = connection.CreateModel();

        channel.ExchangeDeclare("order_exchange_fanout", ExchangeType.Fanout);

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(order));
        var props = channel.CreateBasicProperties();
        props.Persistent = true;

        channel.BasicPublish(
           exchange: "order_exchange_fanout",
           routingKey: "",
           basicProperties: props,
           body: body
        );
    }

    public void PublishOrderDirect(Order order, string routingKey)
    {
        using var connection = _factory.CreateConnection();
        using var channel = connection.CreateModel();

        channel.ExchangeDeclare("order_exchange_direct", ExchangeType.Direct);

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(order));
        var props = channel.CreateBasicProperties();
        props.Persistent = true;

        channel.BasicPublish(
           exchange: "order_exchange_direct",
           routingKey: routingKey,
           basicProperties: props,
           body: body
        );
    }

    public void PublishOrderTopic(Order order, string routingKey)
    {
        using var connection = _factory.CreateConnection();
        using var channel = connection.CreateModel();

        channel.ExchangeDeclare("order_exchange_topic", ExchangeType.Topic);

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(order));
        var props = channel.CreateBasicProperties();
        props.Persistent = true;

        channel.BasicPublish(
           exchange: "order_exchange_topic",
           routingKey: routingKey,
           basicProperties: props,
           body: body
        );
    }
}
