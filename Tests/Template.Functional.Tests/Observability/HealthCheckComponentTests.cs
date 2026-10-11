using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Template.Application.Logging;
using Template.WebApi.Infrastructure.HealthChecks;

namespace Template.Functional.Tests.Observability;

/// <summary>Dependency classification, readiness policy, timeouts, cancellation and recovery of the health checks.</summary>
public sealed class HealthCheckComponentTests
{
    private static HealthCheckRegistration[] Registrations(Dictionary<string, string?> values, bool includeMessageBroker = true, bool includeElasticsearch = false)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddHealthChecksService(configuration, includeElasticsearch);
        services.AddDependencyHealthChecks(configuration, includeMessageBroker);
        using var provider = services.BuildServiceProvider();
        return [.. provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations];
    }

    [Fact]
    public void Classification_RequiredOptionalAndDisabledDependencies()
    {
        var registrations = Registrations(new()
        {
            ["ElasticSearchSettings:Url"] = "http://127.0.0.1:1",
            ["RabbitMqSettings:Enabled"] = "true"
        }, includeElasticsearch: true).ToDictionary(registration => registration.Name);

        registrations["process"].Tags.ShouldBe([HealthTags.Live], ignoreOrder: true);
        registrations["application-database"].Tags.ShouldBe([HealthTags.Ready, HealthTags.Required], ignoreOrder: true);
        registrations["identity-database"].Tags.ShouldBe([HealthTags.Ready, HealthTags.Required], ignoreOrder: true);
        registrations["cache"].Tags.ShouldBe([HealthTags.Ready, HealthTags.Optional], ignoreOrder: true);
        registrations["cache"].FailureStatus.ShouldBe(HealthStatus.Degraded);
        registrations["message-broker"].Tags.ShouldBe([HealthTags.Ready, HealthTags.Optional], ignoreOrder: true);
        registrations["message-broker"].FailureStatus.ShouldBe(HealthStatus.Degraded);
        registrations["elasticsearch"].Tags.ShouldBe([HealthTags.Legacy, HealthTags.Ready, HealthTags.Optional], ignoreOrder: true);
        registrations["elasticsearch"].FailureStatus.ShouldBe(HealthStatus.Unhealthy); // legacy /health semantics kept
        registrations.Values.ShouldAllBe(registration => registration.Timeout > TimeSpan.Zero && registration.Timeout <= TimeSpan.FromSeconds(5)
            || registration.Name == "process");
    }

    [Fact]
    public void DisabledFeatures_AreNotRegistered()
    {
        var names = Registrations(new()
        {
            ["CacheSettings:Enabled"] = "false",
            ["RabbitMqSettings:Enabled"] = "false"
        }).Select(registration => registration.Name).ToArray();
        var testHostNames = Registrations(new(), includeMessageBroker: false).Select(registration => registration.Name).ToArray();

        names.ShouldBe(["process", "application-database", "identity-database"], ignoreOrder: true);
        testHostNames.ShouldNotContain("message-broker");
        testHostNames.ShouldNotContain("elasticsearch");
    }

    [Theory]
    [InlineData(new[] { HealthStatus.Healthy, HealthStatus.Healthy }, HealthStatus.Healthy)]
    [InlineData(new[] { HealthStatus.Healthy, HealthStatus.Degraded }, HealthStatus.Degraded)]
    [InlineData(new[] { HealthStatus.Healthy, HealthStatus.Unhealthy }, HealthStatus.Degraded)]
    [InlineData(new[] { HealthStatus.Unhealthy, HealthStatus.Healthy }, HealthStatus.Unhealthy)]
    public void ReadinessPolicy_OnlyRequiredFailuresMakeTheProcessUnready(HealthStatus[] statuses, HealthStatus expected)
    {
        var report = new HealthReport(new Dictionary<string, HealthReportEntry>
        {
            ["required"] = new(statuses[0], null, TimeSpan.Zero, null, null, [HealthTags.Ready, HealthTags.Required]),
            ["optional"] = new(statuses[1], null, TimeSpan.Zero, null, null, [HealthTags.Ready, HealthTags.Optional])
        }, TimeSpan.Zero);

        HealthResponseWriter.EvaluateReadiness(report).ShouldBe(expected);
    }

    [Fact]
    public async Task MessageBroker_Healthy_Unavailable_AndRecovered()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var check = new TcpEndpointHealthCheck("127.0.0.1", port, "message broker");

        (await RunAsync(check, HealthStatus.Degraded)).Status.ShouldBe(HealthStatus.Healthy);
        listener.Stop();
        var down = await RunAsync(check, HealthStatus.Degraded);
        down.Status.ShouldBe(HealthStatus.Degraded);
        down.Description.ShouldBe("The message broker is unreachable.");
        down.Exception.ShouldBeNull();
        listener = new TcpListener(IPAddress.Loopback, port);
        listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        listener.Start();
        try
        {
            (await RunAsync(check, HealthStatus.Degraded)).Status.ShouldBe(HealthStatus.Healthy);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Cache_Healthy_Unavailable_AndTimedOutWithinItsBound()
    {
        var healthy = new DistributedCacheHealthCheck(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        (await RunAsync(healthy, HealthStatus.Degraded)).Status.ShouldBe(HealthStatus.Healthy);
        (await RunAsync(new DistributedCacheHealthCheck(new HealthEndpointTests.FailingCache()), HealthStatus.Degraded)).Status.ShouldBe(HealthStatus.Degraded);

        var services = new ServiceCollection().AddLogging();
        services.AddHealthChecks().Add(new HealthCheckRegistration(
            "cache",
            _ => new DistributedCacheHealthCheck(new HangingCache()),
            HealthStatus.Degraded,
            [HealthTags.Ready, HealthTags.Optional],
            TimeSpan.FromMilliseconds(200)));
        await using var provider = services.BuildServiceProvider();
        var stopwatch = Stopwatch.StartNew();

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        report.Entries["cache"].Status.ShouldBe(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CallerCancellation_IsRespected()
    {
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            new DistributedCacheHealthCheck(new HangingCache()).CheckHealthAsync(Context(HealthStatus.Degraded), canceled.Token));
        await Should.ThrowAsync<OperationCanceledException>(() =>
            new DatabaseHealthCheck(token => Task.Delay(Timeout.Infinite, token).ContinueWith(_ => true, token)).CheckHealthAsync(Context(HealthStatus.Unhealthy), canceled.Token));
    }

    [Fact]
    public async Task Database_InvalidConfigurationOrFailure_IsUnhealthyWithoutDetails()
    {
        var result = await new DatabaseHealthCheck(_ => throw new InvalidOperationException("Host=db;Password=secret")).CheckHealthAsync(Context(HealthStatus.Unhealthy));

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldBe("The database is unreachable.");
        result.Exception.ShouldBeNull();
    }

    [Fact]
    public void StatusTracker_LogsOnlyChanges_IncludingRecovery()
    {
        var logger = new ListLogger<HealthStatusTracker>();
        var tracker = new HealthStatusTracker(logger);

        tracker.Observe(Report(HealthStatus.Healthy));
        tracker.Observe(Report(HealthStatus.Unhealthy));
        tracker.Observe(Report(HealthStatus.Unhealthy));
        tracker.Observe(Report(HealthStatus.Unhealthy));
        tracker.Observe(Report(HealthStatus.Healthy));

        logger.Entries.Select(entry => (entry.EventId, entry.Level)).ShouldBe([
            (LogEvents.HealthDependencyStatusChanged, LogLevel.Warning),
            (LogEvents.HealthDependencyStatusChanged, LogLevel.Information)
        ]);
    }

    private static HealthReport Report(HealthStatus status) =>
        new(new Dictionary<string, HealthReportEntry> { ["dependency"] = new(status, "fixed", TimeSpan.Zero, null, null) }, TimeSpan.Zero);

    private static HealthCheckContext Context(HealthStatus failureStatus) =>
        new() { Registration = new HealthCheckRegistration("probe", _ => null!, failureStatus, null) };

    private static async Task<HealthCheckResult> RunAsync(IHealthCheck check, HealthStatus failureStatus)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await check.CheckHealthAsync(Context(failureStatus), timeout.Token);
    }

    private sealed class HangingCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new NotSupportedException();
        // Ignores the token, like a client stuck while connecting; the check must still be bounded.
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => new TaskCompletionSource<byte[]?>().Task;
        public void Refresh(string key) => throw new NotSupportedException();
        public Task RefreshAsync(string key, CancellationToken token = default) => throw new NotSupportedException();
        public void Remove(string key) => throw new NotSupportedException();
        public Task RemoveAsync(string key, CancellationToken token = default) => throw new NotSupportedException();
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new NotSupportedException();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw new NotSupportedException();
    }

    internal sealed class ListLogger<T> : ILogger<T>
    {
        public List<(EventId EventId, LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((eventId, logLevel, formatter(state, exception)));
    }
}
