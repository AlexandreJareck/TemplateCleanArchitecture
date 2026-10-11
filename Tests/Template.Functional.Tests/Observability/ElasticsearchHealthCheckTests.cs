using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shouldly;
using Template.Functional.Tests.Common;
using Template.Tests.Shared;
using Template.WebApi.Infrastructure.HealthChecks;

namespace Template.Functional.Tests.Observability;

/// <summary>The Elasticsearch health check: availability, failure policy, timeout, cancellation and safe output.</summary>
public sealed class ElasticsearchHealthCheckTests : IAsyncLifetime
{
    private const string UnreachableUrl = "http://127.0.0.1:1";

    private FakeElasticsearchServer server = null!;

    public async Task InitializeAsync() => server = await FakeElasticsearchServer.StartAsync();

    public async Task DisposeAsync() => await server.DisposeAsync();

    private static ServiceProvider BuildProvider(Dictionary<string, string?> configuration, bool includeElasticsearch = true)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHealthChecksService(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build(), includeElasticsearch);
        return services.BuildServiceProvider();
    }

    private static async Task<HealthReport> CheckAsync(Dictionary<string, string?> configuration, CancellationToken cancellationToken = default)
    {
        await using var provider = BuildProvider(configuration);
        return await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(cancellationToken);
    }

    [Fact]
    public async Task Available_IsHealthy_UsingAPing()
    {
        server.Handler = _ => new FakeResponse(200, "{}");

        var report = await CheckAsync(new() { ["ElasticSearchSettings:Url"] = server.Url.ToString() });

        report.Status.ShouldBe(HealthStatus.Healthy);
        report.Entries["elasticsearch"].Status.ShouldBe(HealthStatus.Healthy);
        server.Requests.Single().Method.ShouldBe("HEAD");
        server.Requests.Single().Path.ShouldBe("/");
    }

    [Fact]
    public async Task ErrorStatus_IsUnhealthy_ByDefault()
    {
        server.Handler = _ => new FakeResponse(503, "{}");

        var report = await CheckAsync(new() { ["ElasticSearchSettings:Url"] = server.Url.ToString() });

        report.Entries["elasticsearch"].Status.ShouldBe(HealthStatus.Unhealthy);
        report.Entries["elasticsearch"].Description.ShouldBe("Elasticsearch answered with an error status.");
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task AuthenticationFailure_IsUnhealthy_WithoutLeakingTheChallengeOrBody(int status)
    {
        // A secured 9.x cluster answers an anonymous ping with 401 (or 403 without the monitor privilege).
        server.Handler = _ => new FakeResponse(status, """{"error":{"type":"security_exception","reason":"secret-token-abc"}}""");

        var report = await CheckAsync(new() { ["ElasticSearchSettings:Url"] = server.Url.ToString() });

        var entry = report.Entries["elasticsearch"];
        entry.Status.ShouldBe(HealthStatus.Unhealthy);
        entry.Description.ShouldBe("Elasticsearch answered with an error status.");
        entry.Exception.ShouldBeNull();
        entry.Data.ShouldBeEmpty();
        (entry.Description + report.Status).ShouldNotContain("secret-token-abc");
    }

    [Fact]
    public async Task Unavailable_IsUnhealthy_Quickly_WithoutDetails()
    {
        var stopwatch = Stopwatch.StartNew();

        var report = await CheckAsync(new() { ["ElasticSearchSettings:Url"] = UnreachableUrl });

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
        var entry = report.Entries["elasticsearch"];
        entry.Status.ShouldBe(HealthStatus.Unhealthy);
        entry.Description.ShouldBe("Elasticsearch is unreachable.");
        entry.Exception.ShouldBeNull();
        entry.Data.ShouldBeEmpty();
    }

    [Fact]
    public async Task FailureStatus_CanBeDegraded()
    {
        var report = await CheckAsync(new()
        {
            ["ElasticSearchSettings:Url"] = UnreachableUrl,
            ["ElasticsearchHealthCheckSettings:FailureStatus"] = "Degraded"
        });

        report.Entries["elasticsearch"].Status.ShouldBe(HealthStatus.Degraded);
    }

    [Fact]
    public async Task SlowElasticsearch_TimesOut_WithinTheConfiguredBound()
    {
        server.Handler = _ => new FakeResponse(200, "{}", Delay: TimeSpan.FromSeconds(30));
        var stopwatch = Stopwatch.StartNew();

        var report = await CheckAsync(new()
        {
            ["ElasticSearchSettings:Url"] = server.Url.ToString(),
            ["ElasticsearchHealthCheckSettings:Timeout"] = "00:00:00.500"
        });

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
        report.Entries["elasticsearch"].Status.ShouldBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        server.Handler = _ => new FakeResponse(200, "{}", Delay: TimeSpan.FromSeconds(30));
        var check = new ElasticsearchHealthCheck(new HttpClient(), server.Url);
        var context = new HealthCheckContext { Registration = new HealthCheckRegistration("elasticsearch", check, HealthStatus.Unhealthy, null) };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Should.ThrowAsync<OperationCanceledException>(() => check.CheckHealthAsync(context, cancellation.Token));
    }

    [Fact]
    public void LegacyKeys_AreUsed_AndTheNewSectionWins()
    {
        var legacy = ElasticsearchHealthCheckSettings.From(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ElasticSearchSettings:Url"] = "http://legacy:9200",
            ["ElasticSearchSettings:Name"] = "legacy-name"
        }).Build());
        var overridden = ElasticsearchHealthCheckSettings.From(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ElasticSearchSettings:Url"] = "http://legacy:9200",
            ["ElasticsearchHealthCheckSettings:Url"] = "http://health:9200",
            ["ElasticsearchHealthCheckSettings:Timeout"] = "00:00:02"
        }).Build());

        legacy.Url.ShouldBe("http://legacy:9200");
        legacy.Name.ShouldBe("legacy-name");
        overridden.Url.ShouldBe("http://health:9200");
        overridden.Name.ShouldBe("elasticsearch");
        overridden.Timeout.ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task DisabledOrNotComposed_RegistersNoElasticsearchEntry()
    {
        await using var disabled = BuildProvider(new() { ["ElasticsearchHealthCheckSettings:Enabled"] = "false" });
        await using var notComposed = BuildProvider(new(), includeElasticsearch: false);

        (await disabled.GetRequiredService<HealthCheckService>().CheckHealthAsync()).Entries.ShouldBeEmpty();
        (await notComposed.GetRequiredService<HealthCheckService>().CheckHealthAsync()).Entries.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Url", "not-a-url")]
    [InlineData("Url", "http://user:secret@elasticsearch:9200")]
    [InlineData("Timeout", "00:00:00")]
    [InlineData("FailureStatus", "Healthy")]
    public void InvalidSettings_FailAtRegistration(string key, string value)
    {
        Should.Throw<InvalidOperationException>(() => BuildProvider(new() { [$"ElasticsearchHealthCheckSettings:{key}"] = value }));
    }
}

/// <summary>/health with an unreachable Elasticsearch: degraded entry, unchanged format, other endpoints unaffected.</summary>
public sealed class HealthEndpointWithElasticsearchTests(HealthEndpointWithElasticsearchTests.UnreachableElasticsearchFactory factory)
    : IClassFixture<HealthEndpointWithElasticsearchTests.UnreachableElasticsearchFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task Health_ReportsElasticsearchUnhealthy_WithoutSensitiveDetails()
    {
        using var response = await client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldStartWith("""{"status":"Unhealthy","totalDuration":""");
        body.ShouldContain("\"elasticsearch\":{\"data\":{},\"description\":\"Elasticsearch is unreachable.\"");
        body.ShouldNotContain("127.0.0.1");
        body.ShouldNotContain("Exception");
        body.ShouldNotContain("exception\":\"");
    }

    [Fact]
    public async Task UnrelatedEndpoints_KeepWorking_WhileElasticsearchIsUnavailable()
    {
        using var products = await client.GetAsync(ApiRoutes.V2.Product.GetPagedListProduct);
        using var swagger = await client.GetAsync("/swagger/v1/swagger.json");

        products.StatusCode.ShouldBe(HttpStatusCode.OK);
        swagger.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    public sealed class UnreachableElasticsearchFactory : CustomWebApplicationFactory<Program>
    {
        protected override void ConfigureTestServices(IServiceCollection services) =>
            services.AddHealthChecksService(
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ElasticsearchHealthCheckSettings:Url"] = "http://127.0.0.1:1",
                    ["ElasticsearchHealthCheckSettings:Timeout"] = "00:00:10"
                }).Build(),
                includeElasticsearch: true);
    }
}
