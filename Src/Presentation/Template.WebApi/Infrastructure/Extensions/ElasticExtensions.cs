using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Serilog.Sinks;
using Microsoft.Extensions.Options;
using Serilog;
using Template.WebApi.Infrastructure.Settings;

namespace Template.WebApi.Infrastructure.Extensions;

public static class ElasticExtensions
{
    public static IServiceCollection AddHealthChecksService(this IServiceCollection services)
    {
        var serviceProvider = services.BuildServiceProvider();
        var elasticSearchSettings = serviceProvider?.GetService<IOptions<ElasticSearchSettings>>()?.Value;

        services.AddHealthChecks()
            .AddElasticsearch(
                elasticSearchSettings?.Url ?? "http://elasticsearch:9200",
                elasticSearchSettings?.Name ?? "elasticsearch"
            );

        return services;
    }

    public static IHostBuilder ConfigureLog(this IHostBuilder hostBuilder)
    {
        hostBuilder.UseSerilog((context, configuration) =>
        {
            var elasticSearchSettings = context.Configuration.GetSection("ElasticSearchSettings").Get<ElasticSearchSettings>();

            configuration
                .MinimumLevel.Verbose()
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo
                .Elasticsearch([new Uri(elasticSearchSettings?.Url ?? "http://elasticsearch:9200")], opts =>
                {
                    opts.DataStream = new DataStreamName(
                        elasticSearchSettings?.Type ?? "logs-api",
                        elasticSearchSettings?.DataSet ?? "example",
                        elasticSearchSettings?.Namespace ?? "demo");
                    opts.BootstrapMethod = BootstrapMethod.Failure;
                })
                .ReadFrom.Configuration(context.Configuration);
        });

        return hostBuilder;
    }

    public static IApplicationBuilder ConfigureHealthCheck(this IApplicationBuilder builder)
    {
        builder.UseHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = p => true,
            ResponseWriter = HealthChecks.UI.Client.UIResponseWriter.WriteHealthCheckUIResponse
        });
        return builder;
    }
}
