using RabbitMQ.Client.Events;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using Template.WebApi.Models;

namespace Template.WebApi.Consumers;

public class OrderConsumer : BackgroundService
{
    private IConnection _connection;
    private IModel _channel;
    private ILogger<OrderConsumer> _logger;

    public OrderConsumer(ILogger<OrderConsumer> logger)
    {
        _logger = logger;
        var factory = new ConnectionFactory
        {
            HostName = "localhost",
            UserName = "kalo",
            Password = "kalo"
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        _channel.QueueDeclare(queue: "orders",
                              durable: true,
                              exclusive: false,
                              autoDelete: false,
                              arguments: null);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumer = new EventingBasicConsumer(_channel);

        consumer.Received += (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var json = Encoding.UTF8.GetString(body);
            var order = JsonSerializer.Deserialize<Order>(json);

            _logger.LogInformation($"🛒 Order received: {order?.Id} - {order?.ProductName} ({order?.Quantity}) - {order?.Price}");

            _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
        };

        _channel.BasicConsume(queue: "orders",
                              autoAck: false, 
                              consumer: consumer);

        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        base.Dispose();
    }
}
