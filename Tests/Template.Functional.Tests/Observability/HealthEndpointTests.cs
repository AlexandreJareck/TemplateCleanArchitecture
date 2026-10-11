using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shouldly;
using Template.Application.Logging;
using Template.Functional.Tests.Common;
using Template.WebApi.Infrastructure.HealthChecks;

namespace Template.Functional.Tests.Observability;

/// <summary>
/// The three health routes on the Test host: legacy <c>/health</c> unchanged, <c>/health/live</c> free of remote
/// dependencies, and <c>/health/ready</c> applying the required/optional policy with sanitized output.
/// </summary>
public sealed class HealthEndpointTests
{
    [Fact]
    public async Task LegacyHealth_KeepsItsPreviousShapeAndChecks()
    {
        await using var factory = new HealthFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("status").GetString().ShouldBe("Healthy");
        // Only legacy-tagged checks (Elasticsearch outside Test) run here; readiness checks are not added to /health.
        body.RootElement.GetProperty("entries").EnumerateObject().ShouldBeEmpty();
    }

    [Fact]
    public async Task Liveness_IsHealthy_WithOnlyTheProcessCheck_EvenWhenEveryDependencyIsDown()
    {
        await using var factory = new HealthFactory(failDatabases: true, failCache: true);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var (status, checks) = await ReadAsync(response);
        status.ShouldBe("Healthy");
        checks.Keys.ShouldBe([HealthCheckExtensions.ProcessCheckName]);
    }

    [Fact]
    public async Task Readiness_IsHealthy_WhenRequiredAndOptionalDependenciesAreHealthy()
    {
        await using var factory = new HealthFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        var (status, checks) = await ReadAsync(response);
        status.ShouldBe("Healthy");
        checks.Keys.ShouldBe(
            [HealthCheckExtensions.ApplicationDatabaseCheckName, HealthCheckExtensions.CacheCheckName, HealthCheckExtensions.IdentityDatabaseCheckName],
            ignoreOrder: true);
        checks[HealthCheckExtensions.ApplicationDatabaseCheckName].Required.ShouldBeTrue();
        checks[HealthCheckExtensions.IdentityDatabaseCheckName].Required.ShouldBeTrue();
        checks[HealthCheckExtensions.CacheCheckName].Required.ShouldBeFalse();
    }

    [Theory]
    [InlineData(HealthCheckExtensions.ApplicationDatabaseCheckName)]
    [InlineData(HealthCheckExtensions.IdentityDatabaseCheckName)]
    public async Task Readiness_Fails_WhenARequiredDependencyIsUnavailable(string failing)
    {
        await using var factory = new HealthFactory(failingCheck: failing);
        using var client = factory.CreateClient();

        using var ready = await client.GetAsync("/health/ready");
        using var live = await client.GetAsync("/health/live");

        ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var (status, checks) = await ReadAsync(ready);
        status.ShouldBe("Unhealthy");
        checks[failing].Status.ShouldBe("Unhealthy");
        checks[failing].Description.ShouldBe("The database is unreachable.");
        live.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Readiness_IsDegradedButReady_WhenTheOptionalCacheIsUnavailable()
    {
        await using var factory = new HealthFactory(failCache: true);
        using var client = factory.CreateClient();

        using var ready = await client.GetAsync("/health/ready");
        using var live = await client.GetAsync("/health/live");

        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
        var (status, checks) = await ReadAsync(ready);
        status.ShouldBe("Degraded");
        checks[HealthCheckExtensions.CacheCheckName].Status.ShouldBe("Degraded");
        checks[HealthCheckExtensions.CacheCheckName].Description.ShouldBe("The cache is unreachable.");
        live.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Readiness_OutputContainsNoExceptionDetails_AndOutageIsLoggedOncePerChange()
    {
        await using var factory = new HealthFactory(throwingCheck: true);
        using var client = factory.CreateClient();

        for (var probe = 0; probe < 3; probe++)
        {
            using var response = await client.GetAsync("/health/ready");
            var text = await response.Content.ReadAsStringAsync();
            text.ShouldNotContain("secret-health-marker");
            text.ShouldContain(HealthResponseWriter.FailedDescription);
        }

        var changes = factory.Sink.WithEventName(nameof(LogEvents.HealthDependencyStatusChanged))
            .Where(logEvent => CapturingSink.Scalar(logEvent, "HealthCheckName") == HealthFactory.ThrowingCheckName)
            .ToArray();
        changes.ShouldHaveSingleItem();
        factory.Sink.AllText().ShouldNotContain("secret-health-marker");
    }

    [Fact]
    public async Task HealthRoutes_AreAnonymousAndExcludedFromOpenApi()
    {
        await using var factory = new HealthFactory();
        using var client = factory.CreateClient();

        (await client.GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var openApi = await client.GetStringAsync("/swagger/v1/swagger.json");
        openApi.ShouldNotContain("/health");
    }

    private static async Task<(string Status, Dictionary<string, CheckView> Checks)> ReadAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var checks = body.RootElement.GetProperty("checks").EnumerateArray().ToDictionary(
            check => check.GetProperty("name").GetString()!,
            check => new CheckView(
                check.GetProperty("status").GetString()!,
                check.GetProperty("description").GetString()!,
                check.GetProperty("required").GetBoolean()));
        return (body.RootElement.GetProperty("status").GetString()!, checks);
    }

    private sealed record CheckView(string Status, string Description, bool Required);

    private sealed class HealthFactory(
        bool failDatabases = false,
        bool failCache = false,
        string? failingCheck = null,
        bool throwingCheck = false) : LogCapturingFactory
    {
        public const string ThrowingCheckName = "throwing-probe";

        protected override void ConfigureTestServices(IServiceCollection services)
        {
            base.ConfigureTestServices(services);
            services.Configure<HealthCheckServiceOptions>(options =>
            {
                foreach (var registration in options.Registrations)
                {
                    var isDatabase = registration.Name is HealthCheckExtensions.ApplicationDatabaseCheckName or HealthCheckExtensions.IdentityDatabaseCheckName;
                    if ((failDatabases && isDatabase) || registration.Name == failingCheck)
                        registration.Factory = _ => new DatabaseHealthCheck(_ => Task.FromResult(false));
                    if (failCache && registration.Name == HealthCheckExtensions.CacheCheckName)
                        registration.Factory = _ => new DistributedCacheHealthCheck(new FailingCache());
                }

                if (throwingCheck)
                {
                    options.Registrations.Add(new HealthCheckRegistration(
                        ThrowingCheckName,
                        _ => new ThrowingCheck(),
                        HealthStatus.Degraded,
                        [HealthTags.Ready, HealthTags.Optional]));
                }
            });
        }
    }

    private sealed class ThrowingCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("secret-health-marker Password=hunter2");
    }

    internal sealed class FailingCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new InvalidOperationException("cache down secret-health-marker");
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("cache down secret-health-marker");
        public void Refresh(string key) => throw new NotSupportedException();
        public Task RefreshAsync(string key, CancellationToken token = default) => throw new NotSupportedException();
        public void Remove(string key) => throw new NotSupportedException();
        public Task RemoveAsync(string key, CancellationToken token = default) => throw new NotSupportedException();
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new NotSupportedException();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw new NotSupportedException();
    }
}
