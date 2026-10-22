using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Template.Application.Interfaces.Messaging;
using Template.Infrastructure.Messaging.Consuming;
using Template.Infrastructure.Messaging.Deduplication;
using Template.Infrastructure.Messaging.Publishing;

namespace Template.Infrastructure.Messaging;

public static class ServiceRegistration
{
    /// <summary>
    /// Registers the order messaging adapter. With <paramref name="useBroker"/> and <c>RabbitMqSettings:Enabled</c>,
    /// publishing goes to RabbitMQ and the consumer hosted service runs (unless <c>ConsumersEnabled</c> is false);
    /// otherwise publishing fails explicitly and no consumer is hosted. No broker connection is opened while the
    /// container is built.
    /// </summary>
    public static IServiceCollection AddMessagingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool useBroker)
    {
        services.AddOptions<RabbitMqSettings>()
            .Bind(configuration.GetSection(RabbitMqSettings.SectionName))
            .Validate(RabbitMqSettings.IsValid, "RabbitMqSettings requires a host, a valid port, credentials, positive timeouts, ReconnectDelay <= MaxReconnectDelay, MaxDeliveryAttempts >= 1 and PrefetchCount >= 1 when Enabled.")
            .ValidateOnStart();

        services.AddSingleton<IProcessedMessageStore, InMemoryProcessedMessageStore>(_ => new InMemoryProcessedMessageStore());
        services.AddSingleton(_ => new DeliveryAttemptTracker());
        services.AddSingleton<OrderDeliveryProcessor>();

        if (!useBroker)
        {
            services.AddSingleton<IOrderEventPublisher, DisabledOrderEventPublisher>();
            return services;
        }

        services.AddSingleton<RabbitMqOrderEventPublisher>();
        services.AddSingleton<IOrderEventPublisher>(provider =>
            provider.GetRequiredService<IOptions<RabbitMqSettings>>().Value.Enabled
                ? provider.GetRequiredService<RabbitMqOrderEventPublisher>()
                : new DisabledOrderEventPublisher());

        services.AddSingleton<RabbitMqOrderConsumerService>();
        services.AddHostedService(provider => provider.GetRequiredService<RabbitMqOrderConsumerService>());
        return services;
    }

    /// <summary>
    /// The broker endpoint for a reachability probe, or null when messaging is disabled. Only host and port are
    /// returned; credentials never leave this project.
    /// </summary>
    public static DnsEndPoint? GetMessageBrokerEndpoint(IConfiguration configuration)
    {
        var settings = configuration.GetSection(RabbitMqSettings.SectionName).Get<RabbitMqSettings>() ?? new RabbitMqSettings();
        return settings.Enabled ? new DnsEndPoint(settings.HostName, settings.Port) : null;
    }

    /// <summary>Registers only the Phase 28 procurement transport for the separately deployable Worker.</summary>
    public static IServiceCollection AddProcurementMessagingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool useBroker)
    {
        services.AddOptions<RabbitMqSettings>()
            .Bind(configuration.GetSection(RabbitMqSettings.SectionName))
            .Validate(RabbitMqSettings.IsValid, "RabbitMqSettings is invalid when procurement messaging is enabled.")
            .ValidateOnStart();
        if (!useBroker)
            return services;
        services.AddSingleton<ProcurementDeliveryProcessor>();
        services.AddSingleton<RabbitMqProcurementEventPublisher>();
        services.AddSingleton<IProcurementEventPublisher>(provider => provider.GetRequiredService<RabbitMqProcurementEventPublisher>());
        services.AddHostedService<RabbitMqProcurementConsumerService>();
        return services;
    }
}
