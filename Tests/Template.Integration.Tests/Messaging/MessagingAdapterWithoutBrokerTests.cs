using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Template.Application.Interfaces.Messaging;
using Template.Application.Messaging;
using Template.Application.Messaging.Orders;
using Template.Infrastructure.Messaging;
using Template.Infrastructure.Messaging.Consuming;
using Template.Infrastructure.Messaging.Publishing;

namespace Template.Integration.Tests.Messaging;

/// <summary>Adapter behavior when no broker is reachable. Uses loopback port 1, where no service listens.</summary>
public sealed class MessagingAdapterWithoutBrokerTests
{
    private static readonly OrderPlacedEvent Order = new(Guid.NewGuid(), "Keyboard", 1, 10m);

    private static Dictionary<string, string?> UnreachableBroker(params (string Key, string? Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["RabbitMqSettings:HostName"] = "127.0.0.1",
            ["RabbitMqSettings:Port"] = "1",
            ["RabbitMqSettings:UserName"] = "phase9-test",
            ["RabbitMqSettings:Password"] = "phase9-test-only",
            ["RabbitMqSettings:ConnectionTimeout"] = "00:00:01",
            ["RabbitMqSettings:PublishTimeout"] = "00:00:02",
            ["RabbitMqSettings:ReconnectDelay"] = "00:00:00.200",
            ["RabbitMqSettings:MaxReconnectDelay"] = "00:00:00.400"
        };
        foreach (var (key, value) in overrides)
            values[$"RabbitMqSettings:{key}"] = value;
        return values;
    }

    private static ServiceProvider BuildProvider(bool useBroker, Dictionary<string, string?> configuration)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddScoped<IOrderPlacedEventHandler, NoOpHandler>();
        services.AddMessagingInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build(), useBroker);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [Fact]
    public async Task Publisher_UnreachableBroker_FailsWithMessagePublishException_WithinTheTimeout()
    {
        await using var provider = BuildProvider(useBroker: true, UnreachableBroker());
        var publisher = provider.GetRequiredService<IOrderEventPublisher>();
        publisher.ShouldBeOfType<RabbitMqOrderEventPublisher>();

        var stopwatch = Stopwatch.StartNew();
        var exception = await Should.ThrowAsync<MessagePublishException>(() => publisher.PublishBroadcastAsync(Order, CancellationToken.None));

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
        exception.Message.ShouldNotContain("phase9-test-only");
        (exception.InnerException?.ToString() ?? string.Empty).ShouldNotContain("phase9-test-only");
    }

    [Fact]
    public async Task Publisher_CallerCancellation_IsNotReportedAsABrokerFailure()
    {
        await using var provider = BuildProvider(useBroker: true, UnreachableBroker());
        var publisher = provider.GetRequiredService<IOrderEventPublisher>();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => publisher.PublishTopicAsync(Order, "order.brazil.sp", cancellation.Token));
    }

    [Fact]
    public async Task Publisher_RejectsNullRoutingKey_AsAProgrammingError()
    {
        await using var provider = BuildProvider(useBroker: true, UnreachableBroker());
        var publisher = provider.GetRequiredService<IOrderEventPublisher>();

        await Should.ThrowAsync<ArgumentNullException>(() => publisher.PublishDirectAsync(Order, null!, CancellationToken.None));
    }

    [Fact]
    public async Task DisabledMessaging_NeverReportsSuccess_AndHostsNoConsumer()
    {
        await using var provider = BuildProvider(useBroker: true, UnreachableBroker(("Enabled", "false")));

        var publisher = provider.GetRequiredService<IOrderEventPublisher>();
        publisher.ShouldBeOfType<DisabledOrderEventPublisher>();
        await Should.ThrowAsync<MessagePublishException>(() => publisher.PublishBroadcastAsync(Order, CancellationToken.None));

        var consumer = provider.GetServices<IHostedService>().OfType<RabbitMqOrderConsumerService>().Single();
        await consumer.StartAsync(CancellationToken.None);
        await consumer.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        await consumer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WithoutBroker_TheDisabledPublisherIsUsed_AndNoHostedServiceIsRegistered()
    {
        await using var provider = BuildProvider(useBroker: false, UnreachableBroker());

        provider.GetRequiredService<IOrderEventPublisher>().ShouldBeOfType<DisabledOrderEventPublisher>();
        provider.GetServices<IHostedService>().ShouldBeEmpty();
        provider.GetRequiredService<IProcessedMessageStore>().ShouldNotBeNull();
    }

    [Fact]
    public async Task Consumer_UnreachableBroker_DoesNotBlockStartup_AndStopsPromptly()
    {
        await using var provider = BuildProvider(useBroker: true, UnreachableBroker());
        var consumer = provider.GetServices<IHostedService>().OfType<RabbitMqOrderConsumerService>().Single();

        var stopwatch = Stopwatch.StartNew();
        await consumer.StartAsync(CancellationToken.None);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));

        await Task.Delay(TimeSpan.FromSeconds(1.5)); // several bounded reconnect attempts
        consumer.ExecuteTask!.IsCompleted.ShouldBeFalse();
        consumer.WaitUntilConsumingAsync(CancellationToken.None).IsCompleted.ShouldBeFalse();

        stopwatch.Restart();
        await consumer.StopAsync(CancellationToken.None);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        consumer.ExecuteTask.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task ConsumersDisabled_KeepsPublishingEnabled()
    {
        await using var provider = BuildProvider(useBroker: true, UnreachableBroker(("ConsumersEnabled", "false")));

        provider.GetRequiredService<IOrderEventPublisher>().ShouldBeOfType<RabbitMqOrderEventPublisher>();
        var consumer = provider.GetServices<IHostedService>().OfType<RabbitMqOrderConsumerService>().Single();
        await consumer.StartAsync(CancellationToken.None);
        await consumer.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("UserName", "")]
    [InlineData("Password", "")]
    [InlineData("HostName", " ")]
    [InlineData("Port", "0")]
    [InlineData("PublishTimeout", "00:00:00")]
    [InlineData("MaxReconnectDelay", "00:00:00.100")]
    [InlineData("MaxDeliveryAttempts", "0")]
    [InlineData("PrefetchCount", "0")]
    public async Task InvalidSettings_AreRejected_WhenEnabled(string key, string value)
    {
        await using var provider = BuildProvider(useBroker: true, UnreachableBroker((key, value)));

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<RabbitMqSettings>>().Value);
    }

    [Fact]
    public async Task InvalidSettings_AreIgnored_WhenDisabled()
    {
        await using var provider = BuildProvider(useBroker: true, UnreachableBroker(("Enabled", "false"), ("Password", "")));

        provider.GetRequiredService<IOptions<RabbitMqSettings>>().Value.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void Defaults_HaveNoCredentials()
    {
        var defaults = new RabbitMqSettings();

        defaults.UserName.ShouldBeEmpty();
        defaults.Password.ShouldBeEmpty();
        RabbitMqSettings.IsValid(defaults).ShouldBeFalse();
    }

    private sealed class NoOpHandler : IOrderPlacedEventHandler
    {
        public Task HandleAsync(OrderPlacedEvent orderPlaced, IncomingMessageContext context, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
