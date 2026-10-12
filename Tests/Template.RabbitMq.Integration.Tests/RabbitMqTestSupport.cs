using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Template.Application.Interfaces.Messaging;
using Template.Application.Messaging;
using Template.Application.Messaging.Orders;
using Template.Infrastructure.Messaging;
using Template.Infrastructure.Messaging.Consuming;
using Template.Infrastructure.Messaging.Topology;
using Testcontainers.RabbitMq;

namespace Template.RabbitMq.Integration.Tests;

/// <summary>
/// The Phase 17 target broker, pinned exactly as in docker-compose.yml. Set TEMPLATE_RABBITMQ_TEST_IMAGE to run the same
/// suite against another pinned image, such as the Phase 14 source kept for rollback (<see cref="SourceImage"/>).
/// </summary>
public static class BrokerImage
{
    public const string TargetImage = "rabbitmq:4.3.6-management@sha256:8dd6e3570ddaa2ef82a6c3a8950e79c1f73893adfbdddbc374fc1f1ac8a0f5dd";

    public const string SourceImage = "rabbitmq:4.2.2@sha256:18cc2597e9b07bc29f5f991ca2836555245b4f94563773c19b662944f4b6d870";

    public static string Name { get; } =
        Environment.GetEnvironmentVariable("TEMPLATE_RABBITMQ_TEST_IMAGE") is { Length: > 0 } image ? image : TargetImage;

    /// <summary>The server version encoded in the image tag (for example 4.3.6).</summary>
    public static string ExpectedServerVersion => Name.Split('@')[0].Split(':')[1].Split('-')[0];

    public const string UserName = "phase9_test_user";

    public static readonly string Password = $"ephemeral-{Guid.NewGuid():N}";

    public static RabbitMqBuilder Builder() => new RabbitMqBuilder(Name).WithUsername(UserName).WithPassword(Password);

    public static int FreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

public sealed class RabbitMqFixture : IAsyncLifetime
{
    public RabbitMqContainer Container { get; } = BrokerImage.Builder().Build();

    public string Host => Container.Hostname;

    public int Port => Container.GetMappedPublicPort(5672);

    public Task InitializeAsync() => Container.StartAsync();

    public Task DisposeAsync() => Container.DisposeAsync().AsTask();

    public async Task<IConnection> ConnectAsync() =>
        await new ConnectionFactory { HostName = Host, Port = Port, UserName = BrokerImage.UserName, Password = BrokerImage.Password }
            .CreateConnectionAsync();

    /// <summary>Deletes every entity of the order topology so each test starts from an empty broker.</summary>
    public async Task ResetTopologyAsync()
    {
        await using var connection = await ConnectAsync();
        await using var channel = await connection.CreateChannelAsync();
        foreach (var subscription in OrderMessagingTopology.Subscriptions)
        {
            await channel.QueueDeleteAsync(subscription.Queue, ifUnused: false, ifEmpty: false);
            await channel.QueueDeleteAsync(subscription.DeadLetterQueue, ifUnused: false, ifEmpty: false);
        }

        foreach (var exchange in new[] { OrderMessagingTopology.FanoutExchange, OrderMessagingTopology.DirectExchange, OrderMessagingTopology.TopicExchange, OrderMessagingTopology.DeadLetterExchange })
            await channel.ExchangeDeleteAsync(exchange, ifUnused: false);
    }

    public async Task<QueueDeclareOk> InspectQueueAsync(string queue)
    {
        await using var connection = await ConnectAsync();
        await using var channel = await connection.CreateChannelAsync();
        return await channel.QueueDeclarePassiveAsync(queue);
    }

    public async Task<BasicGetResult?> GetAsync(string queue)
    {
        await using var connection = await ConnectAsync();
        await using var channel = await connection.CreateChannelAsync();
        return await channel.BasicGetAsync(queue, autoAck: true);
    }

    /// <summary>Publishes raw bytes the way an external or legacy publisher would.</summary>
    public async Task PublishRawAsync(string exchange, string routingKey, byte[] body, BasicProperties properties)
    {
        await using var connection = await ConnectAsync();
        await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true));
        await channel.BasicPublishAsync(exchange, routingKey, mandatory: true, properties, body);
    }
}

[CollectionDefinition(Name)]
public sealed class RabbitMqCollection : ICollectionFixture<RabbitMqFixture>
{
    public const string Name = "RabbitMQ broker";
}

/// <summary>Records handler calls; can fail, block or delegate per call.</summary>
public sealed class RecordingHandler : IOrderPlacedEventHandler
{
    private readonly ConcurrentQueue<(OrderPlacedEvent Event, IncomingMessageContext Context)> calls = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> waiters = new();

    public Func<OrderPlacedEvent, IncomingMessageContext, CancellationToken, Task>? Behavior { get; set; }

    public IReadOnlyList<(OrderPlacedEvent Event, IncomingMessageContext Context)> Calls => calls.ToArray();

    public IEnumerable<(OrderPlacedEvent Event, IncomingMessageContext Context)> CallsFor(OrderSubscription subscription) =>
        Calls.Where(call => call.Context.Subscription == subscription.Name);

    public async Task HandleAsync(OrderPlacedEvent orderPlaced, IncomingMessageContext context, CancellationToken cancellationToken)
    {
        calls.Enqueue((orderPlaced, context));
        try
        {
            if (Behavior is not null)
                await Behavior(orderPlaced, context, cancellationToken);
        }
        finally
        {
            Signal($"{context.Subscription}|{orderPlaced.OrderId}");
            Signal($"{context.Subscription}|*");
        }
    }

    /// <summary>Completes after the handler returned (or threw) for the given order on the subscription.</summary>
    public Task WhenHandledAsync(OrderSubscription subscription, Guid orderId, TimeSpan? timeout = null) =>
        waiters.GetOrAdd($"{subscription.Name}|{orderId}", _ => NewSignal()).Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(30));

    public Task WhenAnyHandledAsync(OrderSubscription subscription, TimeSpan? timeout = null) =>
        waiters.GetOrAdd($"{subscription.Name}|*", _ => NewSignal()).Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(30));

    public void ResetSignals() => waiters.Clear();

    private void Signal(string key) => waiters.GetOrAdd(key, _ => NewSignal()).TrySetResult();

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>The production adapter composition (AddMessagingInfrastructure) pointed at a test broker.</summary>
public sealed class MessagingHarness : IAsyncDisposable
{
    private readonly ServiceProvider provider;

    private MessagingHarness(ServiceProvider provider) => this.provider = provider;

    public IOrderEventPublisher Publisher => provider.GetRequiredService<IOrderEventPublisher>();

    public RabbitMqOrderConsumerService Consumer => provider.GetServices<IHostedService>().OfType<RabbitMqOrderConsumerService>().Single();

    public T Get<T>() where T : notnull => provider.GetRequiredService<T>();

    public static MessagingHarness Create(
        string host,
        int port,
        IOrderPlacedEventHandler handler,
        IReadOnlyDictionary<string, string?>? overrides = null,
        Action<IServiceCollection>? configure = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["RabbitMqSettings:HostName"] = host,
            ["RabbitMqSettings:Port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RabbitMqSettings:UserName"] = BrokerImage.UserName,
            ["RabbitMqSettings:Password"] = BrokerImage.Password,
            ["RabbitMqSettings:ConnectionTimeout"] = "00:00:03",
            ["RabbitMqSettings:PublishTimeout"] = "00:00:05",
            ["RabbitMqSettings:ReconnectDelay"] = "00:00:00.250",
            ["RabbitMqSettings:MaxReconnectDelay"] = "00:00:01",
            ["RabbitMqSettings:MaxDeliveryAttempts"] = "3",
            ["RabbitMqSettings:RetryDelay"] = "00:00:00.050"
        };
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
            values[$"RabbitMqSettings:{key}"] = value;

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(CapturingLogger<>));
        services.AddScoped(_ => handler);
        services.AddMessagingInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), useBroker: true);
        configure?.Invoke(services);
        return new MessagingHarness(services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
    }

    public async Task StartConsumersAsync(bool waitUntilConsuming = true)
    {
        await Consumer.StartAsync(CancellationToken.None);
        if (!waitUntilConsuming)
            return;

        try
        {
            await Consumer.WaitUntilConsumingAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException($"Consumers did not start. Adapter log:{Environment.NewLine}{CapturedLog.Dump()}", exception);
        }
    }

    public Task StopConsumersAsync() => Consumer.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopConsumersAsync();
        }
        finally
        {
            await provider.DisposeAsync();
        }
    }
}

/// <summary>Keeps the adapter's recent log entries (no payloads are logged) to explain test timeouts.</summary>
public static class CapturedLog
{
    private static readonly ConcurrentQueue<string> Entries = new();

    public static void Add(string entry)
    {
        Entries.Enqueue($"{DateTime.UtcNow:HH:mm:ss.fff} {entry}");
        while (Entries.Count > 200 && Entries.TryDequeue(out _))
        {
        }
    }

    public static string Dump() => string.Join(Environment.NewLine, Entries);
}

public sealed class CapturingLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel))
            CapturedLog.Add($"[{logLevel}] {typeof(T).Name}: {formatter(state, exception)}{(exception is null ? string.Empty : $" | {exception.GetType().Name}: {exception.Message}")}");
    }
}

public static class Eventually
{
    /// <summary>Polls a broker-side condition with a bounded timeout and a diagnostic message.</summary>
    public static async Task TrueAsync(Func<Task<bool>> condition, string because, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException($"Condition not met in time: {because}. Adapter log:{Environment.NewLine}{CapturedLog.Dump()}");
    }
}
