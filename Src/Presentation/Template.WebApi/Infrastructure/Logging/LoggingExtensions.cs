using Elastic.Channels;
using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Serilog.Sinks;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Template.WebApi.Infrastructure.Logging;

public static class LoggingExtensions
{
    /// <summary>
    /// Builds the host logger. Sinks and enrichers registered in DI (<see cref="ILogEventSink"/>) are added too, which is
    /// how functional tests capture events. The Test environment never adds the Elasticsearch sink and keeps the static
    /// logger untouched, so parallel test hosts do not share one logger.
    /// </summary>
    public static IHostBuilder ConfigureLog(this IHostBuilder hostBuilder, bool isTestEnvironment = false)
    {
        hostBuilder.UseSerilog(
            (context, services, configuration) =>
            {
                ConfigureLogger(
                    configuration,
                    context.Configuration,
                    ElasticLogSinkDiagnostics.Default,
                    includeElasticsearchSink: !isTestEnvironment);
                configuration.ReadFrom.Services(services);
            },
            preserveStaticLogger: isTestEnvironment);

        return hostBuilder;
    }

    /// <summary>
    /// Console first, then the optional Elasticsearch data-stream sink, then the <c>Serilog</c> section. The sink
    /// bootstraps its templates silently: an unavailable Elasticsearch never fails startup, events are buffered and
    /// exported in the background (best effort, no delivery guarantee), and failures go to
    /// <see cref="ElasticLogSinkDiagnostics"/> instead of the logging pipeline.
    /// </summary>
    public static LoggerConfiguration ConfigureLogger(
        LoggerConfiguration configuration,
        IConfiguration appConfiguration,
        ElasticLogSinkDiagnostics diagnostics,
        bool includeElasticsearchSink = true)
    {
        var settings = ElasticLoggingSettings.From(appConfiguration);

        configuration
            .MinimumLevel.Verbose()
            // The health status tracker logs outcome changes once; the framework would log every failing probe.
            .MinimumLevel.Override("Microsoft.Extensions.Diagnostics.HealthChecks", LogEventLevel.Fatal)
            .Enrich.FromLogContext()
            .Enrich.With<SensitiveDataRedactionEnricher>()
            .WriteTo.Console();

        if (includeElasticsearchSink && settings.Enabled && settings.TryGetEndpoint(out var endpoint))
        {
            configuration.WriteTo.Elasticsearch(
                [endpoint],
                options => ConfigureSink(options, settings, diagnostics),
                transport => transport.RequestTimeout(settings.RequestTimeout));
        }

        return configuration.ReadFrom.Configuration(appConfiguration);
    }

    internal static void ConfigureSink(
        ElasticsearchSinkOptions options,
        ElasticLoggingSettings settings,
        ElasticLogSinkDiagnostics diagnostics)
    {
        options.DataStream = new DataStreamName(settings.Type, settings.DataSet, settings.Namespace);
        options.BootstrapMethod = BootstrapMethod.Silent;
        options.ConfigureChannel = channel =>
        {
            channel.ExportExceptionCallback = diagnostics.ReportException;
            channel.ExportResponseCallback = (response, _) =>
            {
                if (!response.ApiCallDetails.HasSuccessfulStatusCode)
                    diagnostics.ReportFailedResponse(response.ApiCallDetails.HttpStatusCode);
            };
        };
    }
}
