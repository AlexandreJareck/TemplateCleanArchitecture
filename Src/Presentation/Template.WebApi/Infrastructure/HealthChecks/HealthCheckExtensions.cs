using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Template.WebApi.Infrastructure.HealthChecks;

public static class HealthCheckExtensions
{
    public const string ElasticsearchHttpClientName = "elasticsearch-health";

    /// <summary>
    /// Registers health checks. The Elasticsearch entry reads its own options from configuration at registration time
    /// (no intermediate service provider) and is skipped when <paramref name="includeElasticsearch"/> is false (Test
    /// environment) or <c>ElasticsearchHealthCheckSettings:Enabled</c> is false.
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
            tags: null,
            timeout: settings.Timeout));

        return services;
    }

    public static IApplicationBuilder ConfigureHealthCheck(this IApplicationBuilder builder)
    {
        builder.UseHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = _ => true,
            ResponseWriter = global::HealthChecks.UI.Client.UIResponseWriter.WriteHealthCheckUIResponse
        });
        return builder;
    }
}
