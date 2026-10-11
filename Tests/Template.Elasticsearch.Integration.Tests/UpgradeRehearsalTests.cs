using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using Shouldly;
using Template.Application.Search.Persons;
using Template.WebApi.Infrastructure.HealthChecks;
using Template.WebApi.Infrastructure.Logging;

namespace Template.Elasticsearch.Integration.Tests;

/// <summary>
/// Rehearses the supported 8.x to 9.x path with synthetic data in two disposable clusters that share only a
/// test-owned snapshot directory: run the deprecation checks on the latest 8.19 patch, snapshot it, prove the snapshot
/// restores on 8.x (recovery point), restore it into a new 9.5.5 cluster without global state, migrate the
/// repository-owned templates explicitly, then validate counts, documents, queries, aliases, mappings, the data
/// stream, the 9.x adapter, sink and health check, restart persistence, and that the source was not modified.
/// No 9.x node ever opens 8.x data: the only transfer is the snapshot.
/// </summary>
public sealed class UpgradeRehearsalTests : IAsyncLifetime
{
    private const string Repository = "phase11-repo";
    private const string Snapshot = "phase11-source-8";
    private const string PersonsIndex = "phase11-persons";
    private const string PersonsAlias = "phase11-persons-read";
    private const string TemplatedIndex = "phase11-templated-a";
    private const string LogsDataStream = "logs-api-example-phase11";
    private const int SeededPersons = 50;
    private const int SeededLogEvents = 20;

    // Repository-owned or sink-owned templates that must exist on the target after the migration.
    private static readonly string[] ComponentTemplates = ["phase11-component", "ecs_8.11.0_base"];
    private static readonly string[] IndexTemplates = ["phase11-template", "logs-api-example-8.11.0"];

    private readonly string snapshotDirectory = Path.Combine(Path.GetTempPath(), $"phase11-rehearsal-{Guid.NewGuid():N}");
    private readonly int targetPort = ElasticsearchImage.FreeTcpPort();
    private IContainer source = null!;
    private IContainer target = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(snapshotDirectory);
        source = ElasticsearchImage.Build(image: ElasticsearchImage.Source, clusterName: "phase11-source", snapshotDirectory: snapshotDirectory);
        target = ElasticsearchImage.Build(targetPort, ElasticsearchImage.Target, "phase11-target", snapshotDirectory);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await target.DisposeAsync();
        await source.DisposeAsync();
        try
        {
            // Test-owned synthetic snapshot directory only.
            Directory.Delete(snapshotDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task Supported_8_19_To_9_5_Path_PreservesSyntheticData_AndLeavesTheSourceUntouched()
    {
        // 1. Source 8.19 cluster with representative synthetic data.
        await source.StartAsync();
        var sourceUrl = ElasticsearchImage.Url(source);
        await ElasticsearchImage.WaitForYellowAsync(sourceUrl);
        using var src = new EsHttp(sourceUrl);
        (await src.GetJsonAsync("/"))["version"]!["number"]!.GetValue<string>().ShouldBe(ElasticsearchImage.SourceVersion);
        await SeedSourceAsync(src);
        var before = await CaptureAsync(src);
        before.PersonCount.ShouldBe(SeededPersons);
        before.LogCount.ShouldBe(SeededLogEvents);

        // The 9.x client is not compatible with an 8.x server (official matrix): rollback must also revert the client.
        using (var adapterOnSource = ElasticsearchImage.CreateIndex(sourceUrl, PersonsIndex))
            (await adapterOnSource.GetAsync("1", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Rejected);

        // 2. Deprecation and system-feature checks on the latest 8.19 patch: nothing critical, nothing to migrate.
        var deprecations = await src.GetJsonAsync("/_migration/deprecations");
        CriticalDeprecations(deprecations).ShouldBeEmpty();
        (await src.GetJsonAsync("/_migration/system_features"))["migration_status"]!.GetValue<string>().ShouldBe("NO_MIGRATION_NEEDED");

        // 3. Recovery point: a completed snapshot, verified by restoring it on 8.x under new names.
        await src.PutJsonAsync($"/_snapshot/{Repository}", $$$"""{"type":"fs","settings":{"location":"{{{ElasticsearchImage.SnapshotPath}}}"}}""");
        (await src.PostAsync($"/_snapshot/{Repository}/_verify")).ShouldNotBeNull();
        var snapshot = await src.PutJsonAsync(
            $"/_snapshot/{Repository}/{Snapshot}?wait_for_completion=true",
            $$$"""{"indices":"phase11-*,{{{LogsDataStream}}}","include_global_state":false,"metadata":{"purpose":"phase-11 rehearsal, synthetic data"}}""");
        snapshot["snapshot"]!["state"]!.GetValue<string>().ShouldBe("SUCCESS");
        snapshot["snapshot"]!["shards"]!["failed"]!.GetValue<int>().ShouldBe(0);
        var included = snapshot["snapshot"]!["indices"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        included.ShouldContain(PersonsIndex);
        included.ShouldContain(TemplatedIndex);
        snapshot["snapshot"]!["data_streams"]!.AsArray().Select(node => node!.GetValue<string>()).ShouldBe([LogsDataStream]);

        await src.PostJsonAsync(
            $"/_snapshot/{Repository}/{Snapshot}/_restore?wait_for_completion=true",
            $$"""{"indices":"{{PersonsIndex}}","rename_pattern":"(.+)","rename_replacement":"rollback-check-$1","include_aliases":false}""");
        await src.PostAsync("/rollback-check-*/_refresh");
        (await src.CountAsync($"rollback-check-{PersonsIndex}")).ShouldBe(SeededPersons);
        (await src.DeleteAsync($"/rollback-check-{PersonsIndex}")).IsSuccessStatusCode.ShouldBeTrue();

        // 4. New 9.5 cluster: read-only repository, restore indices and data stream without global state.
        await target.StartAsync();
        var targetUrl = $"http://127.0.0.1:{targetPort}";
        await ElasticsearchImage.WaitForYellowAsync(targetUrl);
        using var tgt = new EsHttp(targetUrl);
        (await tgt.GetJsonAsync("/"))["version"]!["number"]!.GetValue<string>().ShouldBe(ElasticsearchImage.TargetVersion);
        await tgt.PutJsonAsync($"/_snapshot/{Repository}", $$$"""{"type":"fs","settings":{"location":"{{{ElasticsearchImage.SnapshotPath}}}","readonly":true}}""");

        // Repository-owned templates are migrated explicitly (definitions read from the source) before the restore,
        // so the restored data stream keeps a matching template and the 9.x built-in templates are not overwritten.
        foreach (var name in ComponentTemplates)
            await tgt.PutJsonAsync($"/_component_template/{name}", (await src.GetJsonAsync($"/_component_template/{name}"))["component_templates"]![0]!["component_template"]!.ToJsonString());
        foreach (var name in IndexTemplates)
            await tgt.PutJsonAsync($"/_index_template/{name}", (await src.GetJsonAsync($"/_index_template/{name}"))["index_templates"]![0]!["index_template"]!.ToJsonString());

        var restore = await tgt.PostJsonAsync(
            $"/_snapshot/{Repository}/{Snapshot}/_restore?wait_for_completion=true",
            $$"""{"indices":"phase11-*,{{LogsDataStream}}","include_global_state":false,"include_aliases":true}""");
        restore["snapshot"]!["shards"]!["failed"]!.GetValue<int>().ShouldBe(0);
        await ElasticsearchImage.WaitForYellowAsync(targetUrl);

        // 5. Parity: counts, documents, queries, alias, mappings, templates and the data stream.
        var after = await CaptureAsync(tgt);
        after.PersonCount.ShouldBe(before.PersonCount);
        after.LogCount.ShouldBe(before.LogCount);
        after.TemplatedCount.ShouldBe(before.TemplatedCount);
        after.Sources.ShouldBe(before.Sources);
        after.MatchQueryIds.ShouldBe(before.MatchQueryIds);
        after.TermQueryIds.ShouldBe(before.TermQueryIds);
        after.DefaultPageCount.ShouldBe(before.DefaultPageCount);
        after.AliasCount.ShouldBe(before.AliasCount);
        after.PersonsMapping.ShouldBe(before.PersonsMapping);
        after.TemplatedSettingReplicas.ShouldBe("0");
        after.CreatedVersionMajor.ShouldBe(8); // still an 8.x-created index, read by 9.x without reindexing
        (await tgt.GetJsonAsync($"/_data_stream/{LogsDataStream}"))["data_streams"]![0]!["template"]!.GetValue<string>().ShouldBe("logs-api-example-8.11.0");

        // 6. The 9.x adapter, health check and sink on the migrated data.
        using (var adapter = ElasticsearchImage.CreateIndex(targetUrl, PersonsIndex))
        {
            (await adapter.EnsureIndexAsync(PersonsIndex, CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Succeeded);
            (await adapter.GetAsync("7", CancellationToken.None)).Value.ShouldBe(SyntheticPerson(7));
            var listed = await adapter.ListAsync(CancellationToken.None);
            listed.Status.ShouldBe(PersonIndexStatus.Succeeded);
            listed.Value!.Count.ShouldBe(10);
            (await adapter.UpsertAsync(new IndexedPerson(1001, "Pós", "Migração"), CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Succeeded);
            (await adapter.UpsertAsync(new IndexedPerson(7, "Updated", null), CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Succeeded);
            (await adapter.UpsertManyAsync([new IndexedPerson(1002, "Bulk", null), new IndexedPerson(1003, "Bulk", "Two")], CancellationToken.None)).IsComplete.ShouldBeTrue();
            (await adapter.RemoveAsync("8", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Succeeded);
            (await adapter.RemoveAsync("8", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.NotFound);
            (await adapter.GetAsync("8", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.NotFound);
        }

        using (var adapterOnAlias = ElasticsearchImage.CreateIndex(targetUrl, PersonsAlias))
            (await adapterOnAlias.GetAsync("1001", CancellationToken.None)).Value.ShouldBe(new IndexedPerson(1001, "Pós", "Migração"));

        var check = new ElasticsearchHealthCheck(new HttpClient { Timeout = TimeSpan.FromSeconds(5) }, new Uri(targetUrl));
        var context = new HealthCheckContext { Registration = new HealthCheckRegistration("elasticsearch", check, HealthStatus.Unhealthy, null) };
        (await check.CheckHealthAsync(context)).Status.ShouldBe(HealthStatus.Healthy);

        // The 9.0.0 sink bootstraps its templates next to the migrated 8.11.0 ones (higher priority, no collision)
        // and keeps writing to the migrated data stream; the next rollover picks the 9.0.0 template.
        var marker = $"phase11-rehearsal-{Guid.NewGuid():N}";
        await WriteLogAsync(targetUrl, marker);
        await WaitForHitAsync(tgt, LogsDataStream, marker);
        (await tgt.GetJsonAsync($"/_data_stream/{LogsDataStream}"))["data_streams"]![0]!["template"]!.GetValue<string>().ShouldBe("logs-api-example-9.0.0");
        var rollover = await tgt.PostJsonAsync($"/{LogsDataStream}/_rollover", "{}");
        rollover["rolled_over"]!.GetValue<bool>().ShouldBeTrue();
        (await tgt.CountAsync(LogsDataStream)).ShouldBe(SeededLogEvents + 1);

        // 7. Restart the target: migrated and new data persist.
        await target.StopAsync();
        await target.StartAsync();
        await ElasticsearchImage.WaitForYellowAsync(targetUrl);
        (await tgt.CountAsync(PersonsIndex)).ShouldBe(SeededPersons + 2); // +1001, +1002, +1003, -8
        var persisted = await tgt.GetJsonAsync($"/{PersonsIndex}/_source/1001");
        persisted["firstName"]!.GetValue<string>().ShouldBe("Pós");
        persisted["lastName"]!.GetValue<string>().ShouldBe("Migração");
        (await tgt.GetStringAsync($"/{PersonsIndex}/_source/1002")).ShouldBe("""{"id":1002,"firstName":"Bulk"}""");
        (await tgt.CountAsync(LogsDataStream)).ShouldBe(SeededLogEvents + 1);
        (await tgt.CountAsync(PersonsAlias)).ShouldBe(SeededPersons + 2);
        (await check.CheckHealthAsync(context)).Status.ShouldBe(HealthStatus.Healthy);

        // 8. The source is untouched and still serves the original data; its snapshot is still complete.
        var untouched = await CaptureAsync(src);
        untouched.ShouldBe(before);
        (await src.GetJsonAsync("/"))["version"]!["number"]!.GetValue<string>().ShouldBe(ElasticsearchImage.SourceVersion);
        (await src.GetJsonAsync($"/_snapshot/{Repository}/{Snapshot}"))["snapshots"]![0]!["state"]!.GetValue<string>().ShouldBe("SUCCESS");
    }

    private static IndexedPerson SyntheticPerson(int id) => new(
        id,
        id % 5 == 0 ? "Ana" : $"Pessoa{id}",
        id % 3 == 0 ? null : (id % 2 == 0 ? "Souza" : "Lima"));

    private static string LegacyJson(IndexedPerson person)
    {
        // Byte format written by the 8.x adapter: camelCase, nulls omitted, numeric id repeated in the document.
        var body = new JsonObject { ["id"] = person.Id };
        if (person.FirstName is not null)
            body["firstName"] = person.FirstName;
        if (person.LastName is not null)
            body["lastName"] = person.LastName;
        return body.ToJsonString();
    }

    private static async Task SeedSourceAsync(EsHttp es)
    {
        // Person index as the 8.x adapter created and wrote it: plain create, dynamic mapping, per-document refresh.
        await es.PutJsonAsync($"/{PersonsIndex}", "{}");
        for (var id = 1; id <= 40; id++)
            await es.PutJsonAsync($"/{PersonsIndex}/_doc/{id}?refresh=wait_for", LegacyJson(SyntheticPerson(id)));

        var bulk = new StringBuilder();
        for (var id = 41; id <= SeededPersons; id++)
        {
            bulk.Append($$$"""{"update":{"_id":"{{{id}}}"}}""").Append('\n');
            bulk.Append($$"""{"doc":{{LegacyJson(SyntheticPerson(id))}},"doc_as_upsert":true}""").Append('\n');
        }

        (await es.PostNdjsonAsync($"/{PersonsIndex}/_bulk", bulk.ToString()))["errors"]!.GetValue<bool>().ShouldBeFalse();
        await es.PostJsonAsync("/_aliases", $$$"""{"actions":[{"add":{"index":"{{{PersonsIndex}}}","alias":"{{{PersonsAlias}}}"}}]}""");

        // Repository-owned style composable templates and an index created from them.
        await es.PutJsonAsync("/_component_template/phase11-component", """{"template":{"settings":{"number_of_replicas":0},"mappings":{"properties":{"createdAt":{"type":"date"}}}}}""");
        await es.PutJsonAsync("/_index_template/phase11-template", """{"index_patterns":["phase11-templated-*"],"priority":200,"composed_of":["phase11-component"],"template":{"mappings":{"properties":{"name":{"type":"keyword"}}}}}""");
        await es.PutJsonAsync($"/{TemplatedIndex}/_doc/1?refresh=wait_for", """{"name":"templated","createdAt":"2026-10-11T00:00:00Z"}""");

        // Synthetic replica of the 8.12.3 sink layout: same index-template name, pattern and priority (captured from
        // the 8.x WebApi bootstrap), reduced to one ECS component template, with a data stream of log events.
        await es.PutJsonAsync("/_component_template/ecs_8.11.0_base", """{"_meta":{"ecs_version":"8.11.0"},"template":{"mappings":{"properties":{"@timestamp":{"type":"date"},"message":{"type":"match_only_text"},"tags":{"type":"keyword","ignore_above":1024}}}}}""");
        await es.PutJsonAsync("/_index_template/logs-api-example-8.11.0", """{"_meta":{"description":"Synthetic replica of the template installed by ECS.NET 8.11.0","ecs_version":"8.11.0"},"index_patterns":["logs-api-example-*"],"priority":527104,"data_stream":{},"composed_of":["ecs_8.11.0_base","data-streams-mappings"],"template":{"mappings":{"date_detection":false,"dynamic_templates":[{"strings_as_keyword":{"mapping":{"ignore_above":1024,"type":"keyword"},"match_mapping_type":"string"}}]},"settings":{"index":{"codec":"best_compression"}}}}""");
        var logs = new StringBuilder();
        for (var n = 1; n <= SeededLogEvents; n++)
        {
            logs.Append("""{"create":{}}""").Append('\n');
            logs.Append($$$"""{"@timestamp":"2026-10-11T00:00:{{{n:00}}}Z","message":"synthetic event {{{n}}}","log":{"level":"Information"}}""").Append('\n');
        }

        (await es.PostNdjsonAsync($"/{LogsDataStream}/_bulk?refresh=true", logs.ToString()))["errors"]!.GetValue<bool>().ShouldBeFalse();
        await es.PostAsync("/_refresh");
    }

    private static async Task<ClusterView> CaptureAsync(EsHttp es)
    {
        var sources = new SortedDictionary<int, string>();
        foreach (var id in new[] { 1, 3, 5, 7, 10, 15, 30, 41, 50 })
            sources[id] = await es.GetStringAsync($"/{PersonsIndex}/_source/{id}");

        var match = await es.PostJsonAsync($"/{PersonsIndex}/_search", """{"size":100,"query":{"match":{"firstName":"Ana"}},"_source":false,"sort":[{"id":"asc"}]}""");
        var term = await es.PostJsonAsync($"/{PersonsIndex}/_search", """{"size":100,"query":{"term":{"lastName.keyword":"Souza"}},"_source":false,"sort":[{"id":"asc"}]}""");
        var page = await es.PostJsonAsync($"/{PersonsIndex}/_search", "{}");
        var mapping = await es.GetJsonAsync($"/{PersonsIndex}/_mapping");
        var templated = await es.GetJsonAsync($"/{TemplatedIndex}/_settings");
        var settings = await es.GetJsonAsync($"/{PersonsIndex}/_settings?flat_settings=true");

        return new ClusterView(
            await es.CountAsync(PersonsIndex),
            await es.CountAsync(LogsDataStream),
            await es.CountAsync(TemplatedIndex),
            await es.CountAsync(PersonsAlias),
            string.Join(",", sources.Select(pair => $"{pair.Key}={pair.Value}")),
            Ids(match),
            Ids(term),
            page["hits"]!["hits"]!.AsArray().Count,
            mapping[PersonsIndex]!["mappings"]!.ToJsonString(),
            templated[TemplatedIndex]!["settings"]!["index"]!["number_of_replicas"]!.GetValue<string>(),
            int.Parse(settings[PersonsIndex]!["settings"]!["index.version.created"]!.GetValue<string>()[..1]));
    }

    private static string Ids(JsonNode search) =>
        string.Join(",", search["hits"]!["hits"]!.AsArray().Select(hit => hit!["_id"]!.GetValue<string>()));

    private static IEnumerable<string> CriticalDeprecations(JsonNode deprecations)
    {
        foreach (var (section, value) in deprecations.AsObject())
        {
            var issues = value switch
            {
                JsonArray array => array.OfType<JsonNode>(),
                JsonObject byName => byName.SelectMany(entry => entry.Value?.AsArray().OfType<JsonNode>() ?? []),
                _ => []
            };

            foreach (var issue in issues.Where(issue => issue["level"]?.GetValue<string>() == "critical"))
                yield return $"{section}: {issue["message"]}";
        }
    }

    private static async Task WriteLogAsync(string url, string marker)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ElasticLoggingSettings:Url"] = url,
            ["ElasticLoggingSettings:Namespace"] = "phase11"
        }).Build();
        using var logger = LoggingExtensions.ConfigureLogger(
            new LoggerConfiguration(),
            configuration,
            new ElasticLogSinkDiagnostics(TextWriter.Null, TimeProvider.System, TimeSpan.FromMinutes(1))).CreateLogger();
        logger.Information("Rehearsal event {Marker}", marker);
    }

    private static async Task WaitForHitAsync(EsHttp es, string target, string marker)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(30))
        {
            await es.PostAsync($"/{target}/_refresh");
            var hits = await es.PostJsonAsync($"/{target}/_search", $$$$"""{"query":{"match_phrase":{"message":"{{{{marker}}}}"}}}""");
            if (hits["hits"]!["total"]!["value"]!.GetValue<int>() > 0)
                return;
            await Task.Delay(500);
        }

        throw new TimeoutException($"The log event did not reach {target}.");
    }

    private sealed record ClusterView(
        long PersonCount,
        long LogCount,
        long TemplatedCount,
        long AliasCount,
        string Sources,
        string MatchQueryIds,
        string TermQueryIds,
        int DefaultPageCount,
        string PersonsMapping,
        string TemplatedSettingReplicas,
        int CreatedVersionMajor);
}

/// <summary>Raw JSON over HTTP, independent of the client version, so the same calls work on 8.x and 9.x.</summary>
internal sealed class EsHttp(string url) : IDisposable
{
    private readonly HttpClient http = new() { BaseAddress = new Uri(url), Timeout = TimeSpan.FromMinutes(2) };

    public async Task<JsonNode> GetJsonAsync(string path) => JsonNode.Parse(await GetStringAsync(path))!;

    public async Task<string> GetStringAsync(string path)
    {
        using var response = await http.GetAsync(path);
        return await ReadAsync(response, path);
    }

    public Task<JsonNode> PutJsonAsync(string path, string json) => SendAsync(HttpMethod.Put, path, json, "application/json");

    public Task<JsonNode> PostJsonAsync(string path, string json) => SendAsync(HttpMethod.Post, path, json, "application/json");

    /// <summary>POST without a body (refresh, verify): Elasticsearch rejects a body on these endpoints.</summary>
    public Task<JsonNode> PostAsync(string path) => SendAsync(HttpMethod.Post, path, null, null);

    public Task<JsonNode> PostNdjsonAsync(string path, string ndjson) => SendAsync(HttpMethod.Post, path, ndjson, "application/x-ndjson");

    public Task<HttpResponseMessage> DeleteAsync(string path) => http.DeleteAsync(path);

    public async Task<long> CountAsync(string target)
    {
        await PostAsync($"/{target}/_refresh");
        return (await GetJsonAsync($"/{target}/_count"))["count"]!.GetValue<long>();
    }

    public void Dispose() => http.Dispose();

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, string? body, string? mediaType)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, mediaType);
        using var response = await http.SendAsync(request);
        return JsonNode.Parse(await ReadAsync(response, path))!;
    }

    private static async Task<string> ReadAsync(HttpResponseMessage response, string path)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)response.StatusCode} on {path}: {text}");
        return text;
    }
}
