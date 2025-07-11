﻿using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using Template.Application.Settings;
using Template.Domain.Order;

namespace Template.Application.Consumers;

public class OrderConsumerTopic : BackgroundService
{
    private IConnection _connection;
    private IModel _channel;
    private readonly ILogger<OrderConsumerTopic> _logger;
    private readonly RabbitMqSettings _rabbitMqSettings;

    public OrderConsumerTopic(IOptions<RabbitMqSettings> options, ILogger<OrderConsumerTopic> logger)
    {
        _rabbitMqSettings = options.Value;
        _logger = logger;
        var factory = new ConnectionFactory
        {
            HostName = _rabbitMqSettings.HostName,
            UserName = _rabbitMqSettings.UserName,
            Password = _rabbitMqSettings.Password,
            Port = _rabbitMqSettings.Port
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        _channel.ExchangeDeclare("order_exchange_topic", ExchangeType.Topic);
        _channel.QueueDeclare("orders_brazil", durable: true, exclusive: false, autoDelete: false);
        _channel.QueueBind("orders_brazil", "order_exchange_topic", "order.brazil.*");
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumer = new EventingBasicConsumer(_channel);

        consumer.Received += (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var json = Encoding.UTF8.GetString(body);
            var order = JsonSerializer.Deserialize<Order>(json);

            _logger.LogInformation($"TOPIC - Order: {order?.Id} - {order?.ProductName}");

            _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
        };

        _channel.BasicConsume(queue: "orders_brazil", autoAck: false, consumer: consumer);

        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        base.Dispose();
    }
}
public class OrderConsumerDirect : BackgroundService
{
    private IConnection _connection;
    private IModel _channel;
    private readonly ILogger<OrderConsumerDirect> _logger;
    private readonly RabbitMqSettings _rabbitMqSettings;

    public OrderConsumerDirect(IOptions<RabbitMqSettings> options, ILogger<OrderConsumerDirect> logger)
    {
        _logger = logger;
        _rabbitMqSettings = options.Value;
        var factory = new ConnectionFactory
        {
            HostName = _rabbitMqSettings.HostName,
            UserName = _rabbitMqSettings.UserName,
            Password = _rabbitMqSettings.Password,
            Port = _rabbitMqSettings.Port
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        _channel.ExchangeDeclare("order_exchange_direct", ExchangeType.Direct);
        _channel.QueueDeclare("urgent_orders", durable: true, exclusive: false, autoDelete: false);
        _channel.QueueBind("urgent_orders", "order_exchange_direct", "order.urgent");
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumer = new EventingBasicConsumer(_channel);

        consumer.Received += (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var json = Encoding.UTF8.GetString(body);
            var order = JsonSerializer.Deserialize<Order>(json);

            _logger.LogInformation($"DIRECT - Order: {order?.Id} - {order?.ProductName}");

            _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
        };

        _channel.BasicConsume(queue: "urgent_orders", autoAck: false, consumer: consumer);

        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        base.Dispose();
    }
}
public class OrderConsumerFanoutA : BackgroundService
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly ILogger<OrderConsumerFanoutA> _logger;
    private readonly RabbitMqSettings _rabbitMqSettings;

    public OrderConsumerFanoutA(IOptions<RabbitMqSettings> options, ILogger<OrderConsumerFanoutA> logger)
    {
        _logger = logger;
        _rabbitMqSettings = options.Value;
        var factory = new ConnectionFactory
        {
            HostName = _rabbitMqSettings.HostName,
            UserName = _rabbitMqSettings.UserName,
            Password = _rabbitMqSettings.Password,
            Port = _rabbitMqSettings.Port
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        _channel.ExchangeDeclare("order_exchange_fanout", ExchangeType.Fanout);

        _channel.QueueDeclare("queue_fanout_a", true, false, false, null);
        _channel.QueueBind("queue_fanout_a", "order_exchange_fanout", "");

        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var json = Encoding.UTF8.GetString(body);
            var order = JsonSerializer.Deserialize<Order>(json);

            _logger.LogInformation($"[A] Received Order: {order?.Id} - {order?.ProductName}");

            _channel.BasicAck(ea.DeliveryTag, false);
        };

        _channel.BasicConsume("queue_fanout_a", false, consumer);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;

    public override void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        base.Dispose();
    }
}
public class OrderConsumerFanoutB : BackgroundService
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly ILogger<OrderConsumerFanoutB> _logger;
    private readonly RabbitMqSettings _rabbitMqSettings;

    public OrderConsumerFanoutB(IOptions<RabbitMqSettings> options, ILogger<OrderConsumerFanoutB> logger)
    {
        _logger = logger;
        _rabbitMqSettings = options.Value;
        var factory = new ConnectionFactory
        {
            HostName = _rabbitMqSettings.HostName,
            UserName = _rabbitMqSettings.UserName,
            Password = _rabbitMqSettings.Password,
            Port = _rabbitMqSettings.Port
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        _channel.ExchangeDeclare("order_exchange_fanout", ExchangeType.Fanout);

        _channel.QueueDeclare("queue_fanout_b", true, false, false, null);
        _channel.QueueBind("queue_fanout_b", "order_exchange_fanout", "");

        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var json = Encoding.UTF8.GetString(body);
            var order = JsonSerializer.Deserialize<Order>(json);

            _logger.LogInformation($"[B] Received Order: {order?.Id} - {order?.ProductName}");

            _channel.BasicAck(ea.DeliveryTag, false);
        };

        _channel.BasicConsume("queue_fanout_b", false, consumer);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;

    public override void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        base.Dispose();
    }
}

