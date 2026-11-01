using System.Diagnostics;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shouldly;
using Template.Application.Search.Persons;
using Template.Infrastructure.Search.Persons;
using Template.WebApi.Infrastructure.HealthChecks;

namespace Template.Elasticsearch.Integration.Tests;

/// <summary>
/// Start without Elasticsearch, use it once it appears, fail fast while it is stopped, and recover after a restart,
/// all with the same adapter and health-check instances. The container keeps a fixed host port across restarts.
/// </summary>
public sealed class ElasticsearchAvailabilityTests : IAsyncLifetime
{
    private readonly int port = ElasticsearchImage.FreeTcpPort();
    private IContainer container = null!;

    public Task InitializeAsync()
    {
        container = ElasticsearchImage.Build(port);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact]
    public async Task Adapter_And_HealthCheck_Survive_Unavailability_And_Recover()
    {
        var url = $"http://127.0.0.1:{port}";
        var indexName = ElasticsearchImage.NewIndexName();
        using var index = ElasticsearchImage.CreateIndex(url, indexName);
        var check = new ElasticsearchHealthCheck(new HttpClient { Timeout = TimeSpan.FromSeconds(5) }, new Uri(url));
        var context = new HealthCheckContext { Registration = new HealthCheckRegistration("elasticsearch", check, HealthStatus.Unhealthy, null) };

        // Not started yet: constructing the adapter worked, operations report unavailability quickly.
        var stopwatch = Stopwatch.StartNew();
        (await index.UpsertAsync(new IndexedPerson(1, "Before", null), CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Unavailable);
        (await check.CheckHealthAsync(context)).Status.ShouldBe(HealthStatus.Unhealthy);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));

        await container.StartAsync();
        await ElasticsearchImage.WaitForYellowAsync(url);
        (await UpsertUntilSettledAsync(index, new IndexedPerson(1, "Available", null))).ShouldBe(PersonIndexStatus.Succeeded);
        (await check.CheckHealthAsync(context)).Status.ShouldBe(HealthStatus.Healthy);

        await container.StopAsync();
        stopwatch.Restart();
        (await index.GetAsync("1", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Unavailable);
        (await index.ListAsync(CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Unavailable);
        (await check.CheckHealthAsync(context)).Status.ShouldBe(HealthStatus.Unhealthy);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));

        await container.StartAsync();
        await ElasticsearchImage.WaitForYellowAsync(url); // shards of the existing index recover after the restart
        var restored = await index.GetAsync("1", CancellationToken.None);
        restored.Status.ShouldBe(PersonIndexStatus.Succeeded);
        restored.Value!.FirstName.ShouldBe("Available");
        (await check.CheckHealthAsync(context)).Status.ShouldBe(HealthStatus.Healthy);
    }

    /// <summary>
    /// The first write creates the index, which can exceed the adapter's 5 s request timeout on a slow CI runner and is
    /// then reported as Unavailable. Recovery is what is proven here, so that outcome alone is retried, bounded.
    /// </summary>
    private static async Task<PersonIndexStatus> UpsertUntilSettledAsync(ElasticsearchPersonSearchIndex index, IndexedPerson person)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            var status = (await index.UpsertAsync(person, CancellationToken.None)).Status;
            if (status != PersonIndexStatus.Unavailable || deadline.Elapsed > TimeSpan.FromSeconds(60))
                return status;
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}
