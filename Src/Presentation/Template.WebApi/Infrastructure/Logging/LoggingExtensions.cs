using Elastic.Channels;
using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Serilog.Sinks;
using Serilog;

namespace Template.WebApi.Infrastructure.Logging;

public static class LoggingExtensions
{
    public static IHostBuilder ConfigureLog(this IHostBuilder hostBuilder)
    {
        hostBuilder.UseSerilog((context, configuration) =>
            ConfigureLogger(configuration, context.Configuration, ElasticLogSinkDiagnostics.Default));

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
        ElasticLogSinkDiagnostics diagnostics)
    {
        var settings = ElasticLoggingSettings.From(appConfiguration);

        configuration
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .WriteTo.Console();

        if (settings.Enabled && settings.TryGetEndpoint(out var endpoint))
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
