using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using HealthStatus = Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus;
using Serilog;
using Shouldly;
using Template.Application.Search.Persons;
using Template.WebApi.Infrastructure.HealthChecks;
using Template.WebApi.Infrastructure.Logging;

namespace Template.Elasticsearch.Integration.Tests;

/// <summary>The 9.x adapter against a real Elasticsearch 9.5.5, including compatibility with the 8.x document format.</summary>
public sealed class PersonSearchIndexElasticsearchTests(ElasticsearchFixture fixture) : IClassFixture<ElasticsearchFixture>
{
    [Fact]
    public async Task Container_IsTheExpectedVersion_AndHealthy()
    {
        using var root = JsonDocument.Parse(await fixture.GetStringAsync("/"));
        using var health = JsonDocument.Parse(await fixture.GetStringAsync("/_cluster/health"));

        root.RootElement.GetProperty("version").GetProperty("number").GetString().ShouldBe(ElasticsearchImage.TargetVersion);
        health.RootElement.GetProperty("status").GetString().ShouldBeOneOf("green", "yellow");
    }

    [Fact]
    public async Task EnsureIndex_CreatesTheIndex_AndIsIdempotent()
    {
        var indexName = fixture.OwnIndex();
        using var index = ElasticsearchImage.CreateIndex(fixture.Url, "persons-unused");

        (await index.EnsureIndexAsync(indexName, CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Succeeded);
        (await index.EnsureIndexAsync(indexName, CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Succeeded);
        (await fixture.Http.GetAsync($"/{indexName}")).IsSuccessStatusCode.ShouldBeTrue();
        (await index.EnsureIndexAsync("Invalid_Name", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Rejected);
    }

    [Fact]
    public async Task Upsert_Get_Update_List_And_Remove_RoundTrip()
    {
        var indexName = fixture.OwnIndex();
        using var index = ElasticsearchImage.CreateIndex(fixture.Url, indexName);

        (await index.UpsertAsync(new IndexedPerson(5, "Eva", null), CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Succeeded);
        (await index.GetAsync("5", CancellationToken.None)).Value.ShouldBe(new IndexedPerson(5, "Eva", null));

        (await index.UpsertAsync(new IndexedPerson(5, "Eva", "Lima"), CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Succeeded);
        (await index.GetAsync("5", CancellationToken.None)).Value.ShouldBe(new IndexedPerson(5, "Eva", "Lima"));

        // refresh=wait_for makes the upsert visible to searches immediately, as before.
        (await index.ListAsync(CancellationToken.None)).Value.ShouldBe([new IndexedPerson(5, "Eva", "Lima")]);

        (await index.RemoveAsync("5", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Succeeded);
        (await index.GetAsync("5", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.NotFound);
        (await index.RemoveAsync("5", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.NotFound);
    }

    [Fact]
    public async Task List_KeepsTheDefaultPageOfTenHits()
    {
        var indexName = fixture.OwnIndex();
        using var index = ElasticsearchImage.CreateIndex(fixture.Url, indexName);
        var persons = Enumerable.Range(1, 12).Select(id => new IndexedPerson(id, $"P{id}", null)).ToArray();

        (await index.UpsertManyAsync(persons, CancellationToken.None)).IsComplete.ShouldBeTrue();
        await fixture.Http.PostAsync($"/{indexName}/_refresh", null);

        var listed = await index.ListAsync(CancellationToken.None);
        listed.Status.ShouldBe(PersonIndexStatus.Succeeded);
        listed.Value!.Count.ShouldBe(10);
    }

    [Fact]
    public async Task UpsertMany_MergesDocuments_AndReportsPartialFailures()
    {
        var indexName = fixture.OwnIndex();
        (await fixture.PutJsonAsync($"/{indexName}", """{"mappings":{"properties":{"id":{"type":"long"},"lastName":{"type":"integer"}}}}""")).IsSuccessStatusCode.ShouldBeTrue();
        using var index = ElasticsearchImage.CreateIndex(fixture.Url, indexName);

        var result = await index.UpsertManyAsync(
            [new IndexedPerson(1, "Ana", null), new IndexedPerson(2, "Bia", "not-a-number"), new IndexedPerson(3, "Caio", "42")],
            CancellationToken.None);

        result.Status.ShouldBe(PersonIndexStatus.Succeeded);
        result.IsComplete.ShouldBeFalse();
        result.IndexedIds.ShouldBe([1, 3]);
        result.FailedIds.ShouldBe([2]);

        // doc_as_upsert merges: a later bulk update without lastName keeps the stored one.
        (await index.UpsertManyAsync([new IndexedPerson(3, "Caio Updated", null)], CancellationToken.None)).IsComplete.ShouldBeTrue();
        using var source = JsonDocument.Parse(await fixture.GetStringAsync($"/{indexName}/_source/3"));
        source.RootElement.GetProperty("firstName").GetString().ShouldBe("Caio Updated");
        source.RootElement.GetProperty("lastName").GetString().ShouldBe("42");
    }

    [Fact]
    public async Task MissingIndex_IsNotFound_ForGetAndList()
    {
        using var index = ElasticsearchImage.CreateIndex(fixture.Url, ElasticsearchImage.NewIndexName());

        (await index.GetAsync("1", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.NotFound);
        (await index.ListAsync(CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.NotFound);
    }

    [Fact]
    public async Task IncompatibleStoredDocument_IsReported_NotThrown()
    {
        var indexName = fixture.OwnIndex();
        (await fixture.PutJsonAsync($"/{indexName}/_doc/77?refresh=true", """{"id":3000000000,"firstName":"Big"}""")).IsSuccessStatusCode.ShouldBeTrue();
        using var index = ElasticsearchImage.CreateIndex(fixture.Url, indexName);

        (await index.GetAsync("77", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.IncompatibleDocument);
        (await index.ListAsync(CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.IncompatibleDocument);
    }

    [Fact]
    public async Task Documents_StayCompatible_WithThe8xFormat()
    {
        var indexName = fixture.OwnIndex();
        using var index = ElasticsearchImage.CreateIndex(fixture.Url, indexName);

        // Written with the exact request and body the 8.x adapter (client 8.17.1) sent, then read by the 9.x adapter.
        (await fixture.PutJsonAsync($"/{indexName}/_doc/10?refresh=wait_for", """{"id":10,"firstName":"Legacy"}""")).IsSuccessStatusCode.ShouldBeTrue();
        (await index.GetAsync("10", CancellationToken.None)).Value.ShouldBe(new IndexedPerson(10, "Legacy", null));

        // Written by the 9.x adapter: byte-identical to the 8.x format, so the 8.x adapter still reads it.
        (await index.UpsertAsync(new IndexedPerson(11, "Novo", "Formato"), CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Succeeded);
        (await index.UpsertManyAsync([new IndexedPerson(12, "Bulk", null)], CancellationToken.None)).IsComplete.ShouldBeTrue();
        (await fixture.GetStringAsync($"/{indexName}/_source/10")).ShouldBe("""{"id":10,"firstName":"Legacy"}""");
        (await fixture.GetStringAsync($"/{indexName}/_source/11")).ShouldBe("""{"id":11,"firstName":"Novo","lastName":"Formato"}""");
        (await fixture.GetStringAsync($"/{indexName}/_source/12")).ShouldBe("""{"id":12,"firstName":"Bulk"}""");

        // Same dynamic mapping as the 8.x index: id long, names text with a keyword subfield.
        using var mapping = JsonDocument.Parse(await fixture.GetStringAsync($"/{indexName}/_mapping"));
        var properties = mapping.RootElement.GetProperty(indexName).GetProperty("mappings").GetProperty("properties");
        properties.GetProperty("id").GetProperty("type").GetString().ShouldBe("long");
        properties.GetProperty("firstName").GetProperty("type").GetString().ShouldBe("text");
        properties.GetProperty("firstName").GetProperty("fields").GetProperty("keyword").GetProperty("type").GetString().ShouldBe("keyword");
    }

    [Fact]
    public async Task CallerCancellation_Throws_AndNothingIsWritten()
    {
        var indexName = fixture.OwnIndex();
        using var index = ElasticsearchImage.CreateIndex(fixture.Url, indexName);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => index.UpsertAsync(new IndexedPerson(1, "Never", null), cancelled.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => index.UpsertManyAsync([new IndexedPerson(2, "Never", null)], cancelled.Token));
        (await index.GetAsync("1", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.NotFound);
    }

    [Fact]
    public async Task HealthCheck_IsHealthy_WhenElasticsearchIsAvailable()
    {
        var check = new ElasticsearchHealthCheck(new HttpClient { Timeout = TimeSpan.FromSeconds(5) }, new Uri(fixture.Url));
        var context = new HealthCheckContext { Registration = new HealthCheckRegistration("elasticsearch", check, HealthStatus.Unhealthy, null) };

        (await check.CheckHealthAsync(context)).Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task LogSink_WritesToTheConfiguredDataStream()
    {
        var @namespace = $"phase11test{Guid.NewGuid():N}";
        var marker = $"phase11-log-marker-{Guid.NewGuid():N}";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ElasticLoggingSettings:Url"] = fixture.Url,
            ["ElasticLoggingSettings:Namespace"] = @namespace
        }).Build();
        var dataStream = $"logs-api-example-{@namespace}";

        var diagnostics = new StringWriter();
        using (var logger = LoggingExtensions.ConfigureLogger(new LoggerConfiguration(), configuration, new ElasticLogSinkDiagnostics(diagnostics, TimeProvider.System, TimeSpan.FromMinutes(1))).CreateLogger())
        {
            logger.Information("Data stream check {Marker}", marker);
        }

        // A second logger bootstraps again over the installed templates: idempotent, no export failure.
        using (var logger = LoggingExtensions.ConfigureLogger(new LoggerConfiguration(), configuration, new ElasticLogSinkDiagnostics(diagnostics, TimeProvider.System, TimeSpan.FromMinutes(1))).CreateLogger())
        {
            logger.Information("Data stream check {Marker} again", marker);
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            while (true)
            {
                using var response = await fixture.Http.GetAsync($"/{dataStream}/_search?q=%22{marker}%22");
                if (response.IsSuccessStatusCode)
                {
                    using var hits = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    if (hits.RootElement.GetProperty("hits").GetProperty("total").GetProperty("value").GetInt32() > 0)
                        break;
                }

                if (stopwatch.Elapsed > TimeSpan.FromSeconds(30))
                    throw new TimeoutException($"The log event did not reach {dataStream}.");
                await Task.Delay(500);
            }

            using var stream = JsonDocument.Parse(await fixture.GetStringAsync($"/_data_stream/{dataStream}"));
            var created = stream.RootElement.GetProperty("data_streams")[0];
            created.GetProperty("name").GetString().ShouldBe(dataStream);
            created.GetProperty("template").GetString().ShouldBe("logs-api-example-9.0.0");

            // Sink 9.0.0 templates: ECS 9.0.0 component templates and one index template above the built-in logs-*-*.
            using var template = JsonDocument.Parse(await fixture.GetStringAsync("/_index_template/logs-api-example-9.0.0"));
            var body = template.RootElement.GetProperty("index_templates")[0].GetProperty("index_template");
            body.GetProperty("index_patterns")[0].GetString().ShouldBe("logs-api-example-*");
            body.GetProperty("priority").GetInt64().ShouldBe(589824);
            (await fixture.Http.GetAsync("/_component_template/ecs_9.0.0_base")).IsSuccessStatusCode.ShouldBeTrue();
            diagnostics.ToString().ShouldBeEmpty();
        }
        finally
        {
            await fixture.Http.DeleteAsync($"/_data_stream/{dataStream}");
        }
    }

    private sealed class LegacyPerson
    {
        public int Id { get; set; }

        public string? FirstName { get; set; }

        public string? LastName { get; set; }
    }
}
