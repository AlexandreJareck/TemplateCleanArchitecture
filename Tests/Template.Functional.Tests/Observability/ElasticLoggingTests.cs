using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Shouldly;
using Template.Functional.Tests.Common;
using Template.Tests.Shared;
using Template.WebApi.Infrastructure.Logging;

namespace Template.Functional.Tests.Observability;

/// <summary>Console capture and the static Serilog logger are process-wide, so these tests never run in parallel.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SerialLoggingCollection
{
    public const string Name = "Serial logging";
}

/// <summary>
/// The Serilog bootstrap: console always, the Elasticsearch data-stream sink optional and fail-soft, its options
/// independent of search, and its diagnostics free of endpoints or credentials.
/// </summary>
[Collection(SerialLoggingCollection.Name)]
public sealed class ElasticLoggingTests : IAsyncLifetime
{
    private const string UnreachableUrl = "http://127.0.0.1:1";

    private FakeElasticsearchServer server = null!;

    public async Task InitializeAsync() => server = await FakeElasticsearchServer.StartAsync();

    public async Task DisposeAsync() => await server.DisposeAsync();

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Logger CreateLogger(Dictionary<string, string?> values, ElasticLogSinkDiagnostics diagnostics, ILogEventSink? extraSink = null)
    {
        var configuration = LoggingExtensions.ConfigureLogger(new LoggerConfiguration(), Configuration(values), diagnostics);
        if (extraSink is not null)
            configuration.WriteTo.Sink(extraSink);
        return configuration.CreateLogger();
    }

    private static ElasticLogSinkDiagnostics Diagnostics(TextWriter writer) => new(writer, TimeProvider.System, TimeSpan.FromMinutes(1));

    [Fact]
    public void UnavailableElasticsearch_DoesNotBlockStartup_AndConsoleLoggingContinues()
    {
        var console = new StringWriter();
        var original = Console.Out;
        Console.SetOut(console);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            using (var logger = CreateLogger(new() { ["ElasticSearchSettings:Url"] = UnreachableUrl }, Diagnostics(TextWriter.Null)))
            {
                stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
                logger.Information("console-marker-{Value}", 42);
            }

            console.ToString().ShouldContain("console-marker-42");
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public async Task UnavailableElasticsearch_IsDiagnosedOnStandardError_WithoutEndpointOrPayload()
    {
        var diagnostics = new StringWriter();
        using (var logger = CreateLogger(
            new() { ["ElasticLoggingSettings:Url"] = UnreachableUrl, ["ElasticLoggingSettings:RequestTimeout"] = "00:00:02" },
            Diagnostics(diagnostics)))
        {
            logger.Information("payload-marker {Secret}", "token-should-not-leak");
        }

        await WaitUntilAsync(() => diagnostics.ToString().Contains("[elastic-log-sink]"), TimeSpan.FromSeconds(20));
        var output = diagnostics.ToString();
        output.ShouldNotContain("127.0.0.1");
        output.ShouldNotContain("payload-marker");
        output.ShouldNotContain("token-should-not-leak");
    }

    [Fact]
    public async Task AvailableElasticsearch_BootstrapsAndWritesTheConfiguredDataStream()
    {
        // Templates are missing (HEAD 404), so the silent bootstrap installs them before events are exported.
        server.Handler = request => request.Path.EndsWith("/_bulk", StringComparison.Ordinal)
            ? FakeResponse.Json(200, """{"took":1,"errors":false,"items":[{"create":{"_index":"x","status":201}}]}""")
            : request.Method == "HEAD" && request.Path.Contains("_template", StringComparison.Ordinal)
                ? new FakeResponse(404)
                : FakeResponse.Json(200, """{"acknowledged":true}""");

        using (var logger = CreateLogger(new() { ["ElasticSearchSettings:Url"] = server.Url.ToString() }, Diagnostics(TextWriter.Null)))
        {
            logger.Information("datastream-marker-{Value}", 7);
        }

        await WaitUntilAsync(() => server.Requests.Any(request => request.Body.Contains("datastream-marker-7")), TimeSpan.FromSeconds(20));
        var export = server.Requests.First(request => request.Body.Contains("datastream-marker-7"));
        (export.Path + export.Body).ShouldContain("logs-api-example-demo");
        // Sink 9.0.0 installs ECS 9.0.0 component templates and a versioned index template for the data stream.
        server.Requests.ShouldContain(request => request.Method == "PUT" && request.Path.StartsWith("/_component_template/ecs_9.0.0_", StringComparison.Ordinal));
        server.Requests.ShouldContain(request => request.Method == "PUT" && request.Path == "/_index_template/logs-api-example-9.0.0");
    }

    [Fact]
    public async Task NewSection_OverridesTheLegacyDataStreamKeys()
    {
        server.Handler = request => request.Path.EndsWith("/_bulk", StringComparison.Ordinal)
            ? FakeResponse.Json(200, """{"took":1,"errors":false,"items":[{"create":{"_index":"x","status":201}}]}""")
            : FakeResponse.Json(200, """{"acknowledged":true}""");

        using (var logger = CreateLogger(new()
        {
            ["ElasticSearchSettings:Url"] = UnreachableUrl,
            ["ElasticSearchSettings:Namespace"] = "demo",
            ["ElasticLoggingSettings:Url"] = server.Url.ToString(),
            ["ElasticLoggingSettings:Namespace"] = "phase10"
        }, Diagnostics(TextWriter.Null)))
        {
            logger.Information("override-marker");
        }

        await WaitUntilAsync(() => server.Requests.Any(request => request.Body.Contains("override-marker")), TimeSpan.FromSeconds(20));
        var export = server.Requests.First(request => request.Body.Contains("override-marker"));
        (export.Path + export.Body).ShouldContain("logs-api-example-phase10");
    }

    [Fact]
    public async Task Disabled_AddsNoElasticsearchSink_ButKeepsLogging()
    {
        var captured = new CapturingSink();
        using (var logger = CreateLogger(
            new() { ["ElasticSearchSettings:Url"] = server.Url.ToString(), ["ElasticLoggingSettings:Enabled"] = "false" },
            Diagnostics(TextWriter.Null),
            captured))
        {
            logger.Information("disabled-marker");
        }

        await Task.Delay(TimeSpan.FromSeconds(1));
        captured.Messages.ShouldContain("disabled-marker");
        server.Requests.ShouldBeEmpty();
    }

    [Fact]
    public void LoggingOptions_DoNotReadSearchSettings()
    {
        var settings = ElasticLoggingSettings.From(Configuration(new()
        {
            ["PersonSearchSettings:Url"] = "http://search-only:9200",
            ["PersonSearchSettings:IndexName"] = "people"
        }));

        settings.Url.ShouldBe("http://elasticsearch:9200");
        settings.Type.ShouldBe("logs-api");
        settings.DataSet.ShouldBe("example");
        settings.Namespace.ShouldBe("demo");
    }

    [Fact]
    public void Diagnostics_AreThrottled_AndNeverThrow()
    {
        var writer = new StringWriter();
        var time = new ManualTimeProvider();
        var diagnostics = new ElasticLogSinkDiagnostics(writer, time, TimeSpan.FromMinutes(1));

        diagnostics.ReportException(new HttpRequestException("http://user:secret@host:9200 refused"));
        diagnostics.ReportFailedResponse(503);
        time.Advance(TimeSpan.FromMinutes(2));
        diagnostics.ReportFailedResponse(null);
        new ElasticLogSinkDiagnostics(new ThrowingWriter(), time, TimeSpan.Zero).ReportFailedResponse(500);

        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(2);
        lines[0].ShouldContain("HttpRequestException");
        lines[1].ShouldContain("no response");
        writer.ToString().ShouldNotContain("secret");
        writer.ToString().ShouldNotContain("host:9200");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > timeout)
                throw new TimeoutException("Condition not met within " + timeout);
            await Task.Delay(100);
        }
    }

    private sealed class CapturingSink : ILogEventSink
    {
        private readonly List<string> messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (messages)
                    return messages.ToArray();
            }
        }

        public void Emit(LogEvent logEvent)
        {
            lock (messages)
                messages.Add(logEvent.RenderMessage());
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }

    private sealed class ThrowingWriter : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public override void WriteLine(string? value) => throw new IOException("closed");
    }
}

/// <summary>A host that bootstraps Serilog with an unreachable Elasticsearch sink still serves requests.</summary>
[Collection(SerialLoggingCollection.Name)]
public sealed class LoggingSinkUnavailableHostTests
{
    [Fact]
    public async Task HostWithUnreachableLogSink_StartsAndServesUnrelatedEndpoints()
    {
        await using var factory = new UnreachableLogSinkFactory();
        using var client = factory.CreateClient();

        using var products = await client.GetAsync(ApiRoutes.V2.Product.GetPagedListProduct);
        using var swagger = await client.GetAsync("/swagger/v1/swagger.json");

        products.StatusCode.ShouldBe(HttpStatusCode.OK);
        swagger.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private sealed class UnreachableLogSinkFactory : CustomWebApplicationFactory<Program>
    {
        protected override IReadOnlyDictionary<string, string?> AdditionalConfiguration { get; } = new Dictionary<string, string?>
        {
            ["ElasticLoggingSettings:Url"] = "http://127.0.0.1:1",
            ["ElasticLoggingSettings:RequestTimeout"] = "00:00:02"
        };

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureLog();
            return base.CreateHost(builder);
        }
    }
}
