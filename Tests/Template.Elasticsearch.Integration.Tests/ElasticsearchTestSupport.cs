using System.Net;
using System.Net.Sockets;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Template.Infrastructure.Search;
using Template.Infrastructure.Search.Persons;

namespace Template.Elasticsearch.Integration.Tests;

/// <summary>
/// Official images pinned by digest, security disabled as in compose, no volumes. <see cref="Target"/> is the selected
/// 9.x server (same version and digest as the compose service) and the only one the 9.x client supports;
/// <see cref="Source"/> is the latest 8.19 patch, the mandatory hop before a 9.x upgrade.
/// </summary>
public static class ElasticsearchImage
{
    public const string Target =
        "docker.elastic.co/elasticsearch/elasticsearch:9.5.5@sha256:13b4a40bb095774f5fda635e45fd5677cc6a26565e42b24997b0c72b88e75984";

    public const string TargetVersion = "9.5.5";

    public const string Source =
        "docker.elastic.co/elasticsearch/elasticsearch:8.19.23@sha256:4d0724bb8d78d7a2330693623e26ca4d621079d062ecc8a30b3e1fc180ec14c1";

    public const string SourceVersion = "8.19.23";

    /// <summary>Container path of the shared snapshot repository (<c>path.repo</c>).</summary>
    public const string SnapshotPath = "/snapshots";

    public static IContainer Build(int? hostPort = null, string image = Target, string clusterName = "phase11-test", string? snapshotDirectory = null)
    {
        var builder = new ContainerBuilder(image)
            .WithEnvironment("cluster.name", clusterName)
            .WithEnvironment("discovery.type", "single-node")
            .WithEnvironment("xpack.security.enabled", "false")
            .WithEnvironment("ingest.geoip.downloader.enabled", "false")
            .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(
                request => request.ForPort(9200).ForPath("/_cluster/health"),
                strategy => strategy.WithTimeout(TimeSpan.FromMinutes(3))));

        if (snapshotDirectory is not null)
        {
            builder = builder
                .WithEnvironment("path.repo", SnapshotPath)
                .WithBindMount(snapshotDirectory, SnapshotPath);
        }

        builder = hostPort is { } port ? builder.WithPortBinding(port, 9200) : builder.WithPortBinding(9200, true);
        return builder.Build();
    }

    public static int FreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public static string Url(IContainer container) => $"http://127.0.0.1:{container.GetMappedPublicPort(9200)}";

    /// <summary>
    /// The container wait strategy only proves the HTTP port answers; after a restart the shards of existing indices
    /// are still recovering (Elasticsearch answers 503). Waits, bounded, until the cluster reports at least yellow.
    /// </summary>
    public static async Task WaitForYellowAsync(string url)
    {
        using var http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(70) };
        using var response = await http.GetAsync("/_cluster/health?wait_for_status=yellow&timeout=60s");
        response.EnsureSuccessStatusCode();
    }

    /// <summary>A unique, test-owned index name; tests delete only names created this way.</summary>
    public static string NewIndexName() => $"phase11-test-{Guid.NewGuid():N}";

    public static ElasticsearchPersonSearchIndex CreateIndex(string url, string indexName) =>
        new(Options.Create(new PersonSearchSettings { Url = url, IndexName = indexName, RequestTimeout = TimeSpan.FromSeconds(5) }),
            NullLogger<ElasticsearchPersonSearchIndex>.Instance);
}

/// <summary>One disposable Elasticsearch 9.x shared by a test class, plus raw HTTP helpers that bypass the adapter.</summary>
public sealed class ElasticsearchFixture : IAsyncLifetime
{
    private readonly List<string> ownedIndices = [];

    public IContainer Container { get; } = ElasticsearchImage.Build();

    private HttpClient? http;

    public HttpClient Http => http ?? throw new InvalidOperationException("The Elasticsearch container did not start.");

    public string Url => ElasticsearchImage.Url(Container);

    public async Task InitializeAsync()
    {
        await Container.StartAsync();
        await ElasticsearchImage.WaitForYellowAsync(Url);
        http = new HttpClient { BaseAddress = new Uri(Url), Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task DisposeAsync()
    {
        if (http is not null)
        {
            foreach (var index in ownedIndices)
                await http.DeleteAsync($"/{index}?ignore_unavailable=true");
            http.Dispose();
        }

        await Container.DisposeAsync();
    }

    public string OwnIndex()
    {
        var name = ElasticsearchImage.NewIndexName();
        lock (ownedIndices)
            ownedIndices.Add(name);
        return name;
    }

    public async Task<HttpResponseMessage> PutJsonAsync(string path, string json) =>
        await Http.PutAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));

    public async Task<string> GetStringAsync(string path) => await Http.GetStringAsync(path);
}
