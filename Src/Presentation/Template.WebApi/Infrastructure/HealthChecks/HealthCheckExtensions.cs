using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Template.Infrastructure.Caching;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Persistence;
using MessagingRegistration = Template.Infrastructure.Messaging.ServiceRegistration;

namespace Template.WebApi.Infrastructure.HealthChecks;

/// <summary>
/// Health model. <c>/health</c> (legacy, unchanged) runs only <see cref="HealthTags.Legacy"/> checks with the previous
/// writer; <c>/health/live</c> runs only network-free <see cref="HealthTags.Live"/> checks; <c>/health/ready</c> runs
/// <see cref="HealthTags.Ready"/> checks under the policy of <see cref="HealthResponseWriter.EvaluateReadiness"/>.
/// </summary>
public static class HealthCheckExtensions
{
    public const string ElasticsearchHttpClientName = "elasticsearch-health";

    public const string LegacyPath = "/health";
    public const string LivenessPath = "/health/live";
    public const string ReadinessPath = "/health/ready";

    public const string ProcessCheckName = "process";
    public const string ApplicationDatabaseCheckName = "application-database";
    public const string IdentityDatabaseCheckName = "identity-database";
    public const string CacheCheckName = "cache";
    public const string MessageBrokerCheckName = "message-broker";

    public static readonly TimeSpan DatabaseTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan CacheTimeout = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan MessageBrokerTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Registers the Elasticsearch check. It reads its own options from configuration at registration time
    /// (no intermediate service provider) and is skipped when <paramref name="includeElasticsearch"/> is false (Test
    /// environment) or <c>ElasticsearchHealthCheckSettings:Enabled</c> is false. It serves the legacy <c>/health</c>
    /// with its configured failure status and is an optional readiness dependency.
    /// </summary>
    public static IServiceCollection AddHealthChecksService(
        this IServiceCollection services,
        IConfiguration configuration,
        bool includeElasticsearch = true)
    {
        var healthChecks = services.AddHealthChecks();
        if (!includeElasticsearch)
            return services;

        var settings = ElasticsearchHealthCheckSettings.From(configuration);
        if (!settings.IsValid())
            throw new InvalidOperationException("ElasticsearchHealthCheckSettings requires an absolute http(s) Url without credentials, a Name, a Timeout between 0 and 1 minute and a non-Healthy FailureStatus when Enabled.");
        if (!settings.Enabled || !settings.TryGetEndpoint(out var endpoint))
            return services;

        // The check result is the signal; the client's own request logs would repeat each failure with its exception.
        services.AddHttpClient(ElasticsearchHttpClientName, client => client.Timeout = settings.Timeout).RemoveAllLoggers();
        healthChecks.Add(new HealthCheckRegistration(
            settings.Name,
            provider => new ElasticsearchHealthCheck(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient(ElasticsearchHttpClientName),
                endpoint),
            settings.FailureStatus,
            tags: [HealthTags.Legacy, HealthTags.Ready, HealthTags.Optional],
            timeout: settings.Timeout));

        return services;
    }

    /// <summary>
    /// Registers liveness and the remaining readiness dependencies: both databases (required), the cache backend
    /// (optional, only when <c>CacheSettings:Enabled</c>) and the message broker port (optional, only when
    /// <paramref name="includeMessageBroker"/> and messaging is enabled). Disabled features are not
    /// registered. Nothing here runs at startup.
    /// </summary>
    public static IServiceCollection AddDependencyHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration,
        bool includeMessageBroker = true)
    {
        services.AddSingleton<HealthStatusTracker>();
        var healthChecks = services.AddHealthChecks()
            .AddCheck<ProcessHealthCheck>(ProcessCheckName, tags: [HealthTags.Live])
            .Add(new HealthCheckRegistration(
                ApplicationDatabaseCheckName,
                provider => new DatabaseHealthCheck(provider.CanConnectToApplicationDatabaseAsync),
                HealthStatus.Unhealthy,
                [HealthTags.Ready, HealthTags.Required],
                DatabaseTimeout))
            .Add(new HealthCheckRegistration(
                IdentityDatabaseCheckName,
                provider => new DatabaseHealthCheck(provider.CanConnectToIdentityDatabaseAsync),
                HealthStatus.Unhealthy,
                [HealthTags.Ready, HealthTags.Required],
                DatabaseTimeout));

        var cache = configuration.GetSection(CacheSettings.SectionName).Get<CacheSettings>() ?? new CacheSettings();
        if (cache.Enabled)
        {
            healthChecks.Add(new HealthCheckRegistration(
                CacheCheckName,
                provider => ActivatorUtilities.CreateInstance<DistributedCacheHealthCheck>(provider),
                HealthStatus.Degraded,
                [HealthTags.Ready, HealthTags.Optional],
                CacheTimeout));
        }

        var broker = includeMessageBroker ? MessagingRegistration.GetMessageBrokerEndpoint(configuration) : null;
        if (broker is not null)
        {
            healthChecks.Add(new HealthCheckRegistration(
                MessageBrokerCheckName,
                _ => new TcpEndpointHealthCheck(broker.Host, broker.Port, "message broker"),
                HealthStatus.Degraded,
                [HealthTags.Ready, HealthTags.Optional],
                MessageBrokerTimeout));
        }

        return services;
    }

    public static IApplicationBuilder ConfigureHealthCheck(this IApplicationBuilder builder)
    {
        // Each path matches exactly, so /health never answers for /health/live or /health/ready.
        builder.UseHealthChecks(LegacyPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HealthTags.Legacy),
            ResponseWriter = (context, report) =>
            {
                context.RequestServices.GetService<HealthStatusTracker>()?.Observe(report);
                return global::HealthChecks.UI.Client.UIResponseWriter.WriteHealthCheckUIResponse(context, report);
            }
        });
        builder.UseHealthChecks(LivenessPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HealthTags.Live),
            ResponseWriter = HealthResponseWriter.WriteLivenessAsync
        });
        builder.UseHealthChecks(ReadinessPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HealthTags.Ready),
            ResponseWriter = HealthResponseWriter.WriteReadinessAsync
        });
        return builder;
    }
}
