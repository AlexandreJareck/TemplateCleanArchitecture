using System.Diagnostics;
using Shouldly;
using Template.Application.Search.Persons;
using Template.Tests.Shared;
using static Template.Integration.Tests.Search.SearchTestSupport;

namespace Template.Integration.Tests.Search;

/// <summary>
/// Elasticsearch adapter against a scripted loopback server: request shapes, response classification, document
/// format, timeouts, cancellation and recovery, without a real Elasticsearch.
/// </summary>
public sealed class ElasticsearchPersonSearchIndexTests : IAsyncLifetime
{
    private const string SensitiveName = "Sensitive-Marker-Name";

    private FakeElasticsearchServer server = null!;

    public async Task InitializeAsync() => server = await FakeElasticsearchServer.StartAsync();

    public async Task DisposeAsync() => await server.DisposeAsync();

    [Fact]
    public async Task EnsureIndex_CreatesAMissingIndex_AndIsIdempotent()
    {
        var created = false;
        server.Handler = request => (request.Method, request.Path) switch
        {
            ("HEAD", "/people-v1") => new FakeResponse(created ? 200 : 404),
            ("PUT", "/people-v1") => Create(),
            _ => new FakeResponse(500)
        };
        FakeResponse Create()
        {
            created = true;
            return FakeResponse.Json(200, """{"acknowledged":true,"shards_acknowledged":true,"index":"people-v1"}""");
        }
        using var index = CreateIndex(server, out _);

        (await index.EnsureIndexAsync("people-v1", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Succeeded);
        (await index.EnsureIndexAsync("people-v1", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Succeeded);

        server.Requests.Select(request => $"{request.Method} {request.Path}").ShouldBe(["HEAD /people-v1", "PUT /people-v1", "HEAD /people-v1"]);
    }

    [Fact]
    public async Task EnsureIndex_ConcurrentCreation_IsTreatedAsAlreadyExisting()
    {
        server.Handler = request => request.Method == "HEAD"
            ? new FakeResponse(404)
            : FakeResponse.Json(400, Error("resource_already_exists_exception", "index [people-v1/abc] already exists", 400));
        using var index = CreateIndex(server, out _);

        (await index.EnsureIndexAsync("people-v1", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Succeeded);
    }

    [Fact]
    public async Task EnsureIndex_InvalidName_IsRejected_NotReportedAsCreated()
    {
        server.Handler = request => request.Method == "HEAD"
            ? new FakeResponse(400)
            : FakeResponse.Json(400, Error("invalid_index_name_exception", "must be lowercase", 400));
        using var index = CreateIndex(server, out _);

        (await index.EnsureIndexAsync("Bad_Name", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Rejected);
    }

    [Fact]
    public async Task Upsert_NewPerson_SendsTheLegacyRequestAndDocumentFormat()
    {
        server.Handler = _ => FakeResponse.Json(201, IndexCreated("5"));
        using var index = CreateIndex(server, out _);

        var result = await index.UpsertAsync(new IndexedPerson(5, "Eva", null), CancellationToken.None);

        result.Status.ShouldBe(PersonIndexStatus.Succeeded);
        var request = server.Requests.Single();
        request.Method.ShouldBe("PUT");
        request.Path.ShouldBe("/persons/_doc/5");
        request.Query.ShouldBe("?refresh=wait_for");
        request.Body.ShouldBe("""{"id":5,"firstName":"Eva"}""");
    }

    [Fact]
    public async Task Upsert_ExistingPerson_ReplacesItWithTheSameRequest()
    {
        server.Handler = _ => FakeResponse.Json(200, IndexUpdated("5"));
        using var index = CreateIndex(server, out _);

        var result = await index.UpsertAsync(new IndexedPerson(5, "Eva", "Lima"), CancellationToken.None);

        result.Status.ShouldBe(PersonIndexStatus.Succeeded);
        server.Requests.Single().Body.ShouldBe("""{"id":5,"firstName":"Eva","lastName":"Lima"}""");
    }

    [Fact]
    public async Task Get_ReadsThePersonByKey()
    {
        server.Handler = _ => FakeResponse.Json(200, Found("5", """{"id":5,"firstName":"Eva"}"""));
        using var index = CreateIndex(server, out _);

        var result = await index.GetAsync("5", CancellationToken.None);

        result.Status.ShouldBe(PersonIndexStatus.Succeeded);
        result.Value.ShouldBe(new IndexedPerson(5, "Eva", null));
        server.Requests.Single().PathAndQuery.ShouldBe("/persons/_doc/5");
        server.Requests.Single().Method.ShouldBe("GET");
    }

    [Theory]
    [InlineData("""{"id":5,"firstName":"Eva","lastName":null}""", 5, "Eva", null)]
    [InlineData("""{"id":"78","firstName":"Str","unknown":{"a":1}}""", 78, "Str", null)]
    [InlineData("""{"firstName":"NoId"}""", 0, "NoId", null)]
    [InlineData("""{"id":7,"FirstName":"Pascal"}""", 7, null, null)]
    public async Task Get_ReadsDocumentsInTheCurrentFormat(string source, int id, string? firstName, string? lastName)
    {
        server.Handler = _ => FakeResponse.Json(200, Found("x", source));
        using var index = CreateIndex(server, out _);

        var result = await index.GetAsync("x", CancellationToken.None);

        result.Status.ShouldBe(PersonIndexStatus.Succeeded);
        result.Value.ShouldBe(new IndexedPerson(id, firstName, lastName));
    }

    [Fact]
    public async Task Get_MissingDocument_IsNotFound()
    {
        server.Handler = _ => FakeResponse.Json(404, NotFoundDocument("9"));
        using var index = CreateIndex(server, out _);

        var result = await index.GetAsync("9", CancellationToken.None);

        result.Status.ShouldBe(PersonIndexStatus.NotFound);
        result.Value.ShouldBeNull();
    }

    [Fact]
    public async Task Get_MissingIndex_IsNotFound()
    {
        server.Handler = _ => FakeResponse.Json(404, Error("index_not_found_exception", "no such index [persons]", 404));
        using var index = CreateIndex(server, out _);

        (await index.GetAsync("1", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.NotFound);
    }

    [Theory]
    [InlineData("""{"id":3000000000,"firstName":"Big"}""")]
    [InlineData("""{"id":1.5}""")]
    [InlineData("""{"id":"abc"}""")]
    [InlineData("""{"id":1,"firstName":{"nested":true}}""")]
    [InlineData("""[1,2]""")]
    public async Task Get_IncompatibleDocument_IsReported_NotThrown(string source)
    {
        server.Handler = _ => FakeResponse.Json(200, Found("1", source));
        using var index = CreateIndex(server, out _);

        (await index.GetAsync("1", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.IncompatibleDocument);
    }

    [Fact]
    public async Task List_ReturnsTheDefaultPage_WithTheLegacyRequest()
    {
        server.Handler = _ => FakeResponse.Json(200, Hits("""{"id":1,"firstName":"A"}""", """{"id":2,"lastName":"B"}"""));
        using var index = CreateIndex(server, out _);

        var result = await index.ListAsync(CancellationToken.None);

        result.Status.ShouldBe(PersonIndexStatus.Succeeded);
        result.Value.ShouldBe([new IndexedPerson(1, "A", null), new IndexedPerson(2, null, "B")]);
        var request = server.Requests.Single();
        request.Method.ShouldBe("POST");
        request.PathAndQuery.ShouldBe("/persons/_search");
        request.Body.ShouldBe("{}"); // no size: the server default page (10 hits), as before
    }

    [Fact]
    public async Task List_EmptyIndex_IsAnEmptySuccess()
    {
        server.Handler = _ => FakeResponse.Json(200, Hits());
        using var index = CreateIndex(server, out _);

        var result = await index.ListAsync(CancellationToken.None);

        result.Status.ShouldBe(PersonIndexStatus.Succeeded);
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task List_MissingIndex_IsNotFound()
    {
        server.Handler = _ => FakeResponse.Json(404, Error("index_not_found_exception", "no such index [persons]", 404));
        using var index = CreateIndex(server, out _);

        (await index.ListAsync(CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.NotFound);
    }

    [Fact]
    public async Task List_WithOneIncompatibleDocument_ReturnsNoPartialData()
    {
        server.Handler = _ => FakeResponse.Json(200, Hits("""{"id":1}""", """{"id":3000000000}"""));
        using var index = CreateIndex(server, out _);

        var result = await index.ListAsync(CancellationToken.None);

        result.Status.ShouldBe(PersonIndexStatus.IncompatibleDocument);
        result.Value.ShouldBeNull();
    }

    [Fact]
    public async Task Remove_DeletesByKey()
    {
        server.Handler = _ => FakeResponse.Json(200, Deleted("5"));
        using var index = CreateIndex(server, out _);

        (await index.RemoveAsync("5", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Succeeded);
        server.Requests.Single().Method.ShouldBe("DELETE");
        server.Requests.Single().PathAndQuery.ShouldBe("/persons/_doc/5");
    }

    [Fact]
    public async Task Remove_MissingDocument_IsNotFound()
    {
        server.Handler = _ => FakeResponse.Json(404, Deleted("9", "not_found"));
        using var index = CreateIndex(server, out _);

        (await index.RemoveAsync("9", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.NotFound);
    }

    [Fact]
    public async Task UpsertMany_AllAccepted_IsComplete_WithTheLegacyBulkRequest()
    {
        server.Handler = _ => FakeResponse.Json(200, Bulk(false, BulkItem("1", 201), BulkItem("2", 200)));
        using var index = CreateIndex(server, out _);

        var result = await index.UpsertManyAsync([new IndexedPerson(1, "A", null), new IndexedPerson(2, "B", "C")], CancellationToken.None);

        result.IsComplete.ShouldBeTrue();
        result.IndexedIds.ShouldBe([1, 2]);
        result.FailedIds.ShouldBeEmpty();
        var request = server.Requests.Single();
        request.PathAndQuery.ShouldBe("/persons/_bulk");
        request.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries).ShouldBe([
            """{"update":{"_id":"1"}}""",
            """{"doc":{"id":1,"firstName":"A"},"doc_as_upsert":true}""",
            """{"update":{"_id":"2"}}""",
            """{"doc":{"id":2,"firstName":"B","lastName":"C"},"doc_as_upsert":true}"""
        ]);
    }

    [Fact]
    public async Task UpsertMany_PartialFailure_IsNotReportedAsCompleteSuccess()
    {
        server.Handler = _ => FakeResponse.Json(200, Bulk(true, BulkItem("1", 201), BulkItem("2", 400, $"failed to parse field [firstName] value '{SensitiveName}'")));
        using var index = CreateIndex(server, out var logger);

        var result = await index.UpsertManyAsync([new IndexedPerson(1, "A", null), new IndexedPerson(2, SensitiveName, null)], CancellationToken.None);

        result.Status.ShouldBe(PersonIndexStatus.Succeeded);
        result.IsComplete.ShouldBeFalse();
        result.IsPartial.ShouldBeTrue();
        result.IndexedIds.ShouldBe([1]);
        result.FailedIds.ShouldBe([2]);
        logger.Entries.ShouldContain(entry => entry.Contains("partially failed"));
        logger.Entries.ShouldAllBe(entry => !entry.Contains(SensitiveName));
    }

    [Fact]
    public async Task UpsertMany_RequestFailure_ListsEveryPersonAsFailed()
    {
        server.Handler = _ => FakeResponse.Json(503, Error("cluster_block_exception", "blocked", 503));
        using var index = CreateIndex(server, out _);

        var result = await index.UpsertManyAsync([new IndexedPerson(1, "A", null), new IndexedPerson(2, "B", null)], CancellationToken.None);

        result.Status.ShouldBe(PersonIndexStatus.Unavailable);
        result.IsComplete.ShouldBeFalse();
        result.FailedIds.ShouldBe([1, 2]);
    }

    [Fact]
    public async Task UpsertMany_Empty_SendsNothing()
    {
        using var index = CreateIndex(server, out _);

        (await index.UpsertManyAsync([], CancellationToken.None)).IsComplete.ShouldBeTrue();
        server.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(503)]
    [InlineData(500)]
    [InlineData(429)]
    public async Task ServerSideFailures_AreUnavailable(int status)
    {
        server.Handler = _ => FakeResponse.Json(status, Error("server_error", "unavailable", status));
        using var index = CreateIndex(server, out _);

        (await index.GetAsync("1", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Unavailable);
        (await index.UpsertAsync(new IndexedPerson(1, "A", null), CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Unavailable);
        (await index.ListAsync(CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Unavailable);
        (await index.RemoveAsync("1", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Unavailable);
        (await index.EnsureIndexAsync("people", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Unavailable);
    }

    [Fact]
    public async Task OtherClientErrors_AreRejected()
    {
        server.Handler = _ => FakeResponse.Json(400, Error("document_parsing_exception", $"failed to parse '{SensitiveName}'", 400));
        using var index = CreateIndex(server, out var logger);

        (await index.UpsertAsync(new IndexedPerson(1, SensitiveName, null), CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Rejected);
        logger.Entries.ShouldContain(entry => entry.Contains("document_parsing_exception"));
        logger.Entries.ShouldAllBe(entry => !entry.Contains(SensitiveName));
    }

    [Theory]
    [InlineData("<html><body>Bad gateway page</body></html>", true)] // unreadable body
    [InlineData("{\"_index\":\"persons\",\"found\":", true)] // truncated body
    [InlineData("""{"_index":"persons","_id":"1","found":true,"_source":{"id":1}}""", false)] // not identified as Elasticsearch
    public async Task InvalidResponses_AreUnavailable_NotThrown(string body, bool productHeader)
    {
        server.Handler = _ => new FakeResponse(200, body, productHeader);
        using var index = CreateIndex(server, out var logger);

        (await index.GetAsync("1", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Unavailable);
        (await index.ListAsync(CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Unavailable);
        logger.Entries.ShouldContain(entry => entry.Contains("could not read"));
    }

    [Fact]
    public async Task UnreachableServer_IsUnavailable_Quickly()
    {
        using var index = CreateIndex(UnreachableUrl, out var logger);
        var stopwatch = Stopwatch.StartNew();

        (await index.GetAsync("1", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Unavailable);
        (await index.UpsertAsync(new IndexedPerson(1, "A", null), CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Unavailable);
        (await index.ListAsync(CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Unavailable);
        (await index.RemoveAsync("1", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Unavailable);
        (await index.EnsureIndexAsync("people", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Unavailable);
        (await index.UpsertManyAsync([new IndexedPerson(1, "A", null)], CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Unavailable);

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
        logger.Entries.ShouldContain(entry => entry.Contains("Unavailable"));
    }

    [Fact]
    public async Task SlowServer_TimesOut_AsUnavailable_WithinTheConfiguredBound()
    {
        server.Handler = _ => new FakeResponse(200, Found("1", """{"id":1}"""), Delay: TimeSpan.FromSeconds(30));
        using var index = CreateIndex(server, out _, requestTimeout: TimeSpan.FromMilliseconds(500));
        var stopwatch = Stopwatch.StartNew();

        var result = await index.GetAsync("1", CancellationToken.None);

        result.Status.ShouldBe(PersonIndexStatus.Unavailable);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task CallerCancellation_BeforeTheCall_Throws()
    {
        using var index = CreateIndex(server, out _);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => index.GetAsync("1", cancellation.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => index.UpsertAsync(new IndexedPerson(1, "A", null), cancellation.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => index.ListAsync(cancellation.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => index.RemoveAsync("1", cancellation.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => index.EnsureIndexAsync("people", cancellation.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => index.UpsertManyAsync([new IndexedPerson(1, "A", null)], cancellation.Token));
    }

    [Fact]
    public async Task CallerCancellation_DuringTheCall_Throws_InsteadOfReportingUnavailable()
    {
        server.Handler = _ => new FakeResponse(200, Found("1", """{"id":1}"""), Delay: TimeSpan.FromSeconds(30));
        using var index = CreateIndex(server, out _, requestTimeout: TimeSpan.FromSeconds(20));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Should.ThrowAsync<OperationCanceledException>(() => index.GetAsync("1", cancellation.Token));
    }

    [Fact]
    public async Task RestoredServer_IsUsedAgain_WithoutRecreatingTheAdapter()
    {
        server.Handler = _ => FakeResponse.Json(200, Found("1", """{"id":1,"firstName":"Back"}"""));
        using var index = CreateIndex(server, out _);
        (await index.GetAsync("1", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Succeeded);

        await server.StopAsync();
        (await index.GetAsync("1", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Unavailable);

        await server.RestartAsync();
        var restored = await index.GetAsync("1", CancellationToken.None);
        restored.Status.ShouldBe(PersonIndexStatus.Succeeded);
        restored.Value!.FirstName.ShouldBe("Back");
    }

    [Fact]
    public async Task ConfiguredIndex_IsUsedByEveryOperation()
    {
        server.Handler = request => request.Path.EndsWith("_search", StringComparison.Ordinal)
            ? FakeResponse.Json(200, Hits())
            : request.Path.EndsWith("_bulk", StringComparison.Ordinal)
                ? FakeResponse.Json(200, Bulk(false, BulkItem("1", 201)))
                : request.Method == "GET"
                    ? FakeResponse.Json(200, Found("1", """{"id":1}"""))
                    : FakeResponse.Json(200, IndexCreated("1"));
        using var index = CreateIndex(server, out _, indexName: "people-custom");

        await index.UpsertAsync(new IndexedPerson(1, "A", null), CancellationToken.None);
        await index.UpsertManyAsync([new IndexedPerson(1, "A", null)], CancellationToken.None);
        await index.GetAsync("1", CancellationToken.None);
        await index.ListAsync(CancellationToken.None);
        await index.RemoveAsync("1", CancellationToken.None);

        server.Requests.Select(request => request.Path).ShouldBe([
            "/people-custom/_doc/1", "/people-custom/_bulk", "/people-custom/_doc/1", "/people-custom/_search", "/people-custom/_doc/1"
        ]);
    }

    [Fact]
    public async Task Logs_NeverContainNamesKeysDocumentsOrBodies()
    {
        const string sensitiveKey = "key-sensitive-4242";
        server.Handler = request => request.Method switch
        {
            "PUT" => FakeResponse.Json(400, Error("document_parsing_exception", $"value '{SensitiveName}' in document '{sensitiveKey}'", 400)),
            "GET" => FakeResponse.Json(200, Found(sensitiveKey, $$"""{"id":3000000000,"firstName":"{{SensitiveName}}"}""")),
            "DELETE" => FakeResponse.Json(503, Error("unavailable", $"while deleting '{sensitiveKey}'", 503)),
            _ => FakeResponse.Json(200, Hits($$"""{"id":"bad","lastName":"{{SensitiveName}}"}"""))
        };
        using var index = CreateIndex(server, out var logger);

        await index.UpsertAsync(new IndexedPerson(4242, SensitiveName, SensitiveName), CancellationToken.None);
        await index.GetAsync(sensitiveKey, CancellationToken.None);
        await index.ListAsync(CancellationToken.None);
        await index.RemoveAsync(sensitiveKey, CancellationToken.None);

        logger.Entries.Count.ShouldBeGreaterThan(1);
        logger.Entries.ShouldAllBe(entry => !entry.Contains(SensitiveName) && !entry.Contains(sensitiveKey) && !entry.Contains("4242"));
    }

    [Fact]
    public async Task ProgrammingErrors_AreNotTranslatedIntoStatuses()
    {
        using var index = CreateIndex(server, out _);

        await Should.ThrowAsync<ArgumentNullException>(() => index.UpsertAsync(null!, CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(() => index.GetAsync(" ", CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(() => index.RemoveAsync(string.Empty, CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(() => index.EnsureIndexAsync(" ", CancellationToken.None));
        await Should.ThrowAsync<ArgumentNullException>(() => index.UpsertManyAsync(null!, CancellationToken.None));
        server.Requests.ShouldBeEmpty();
    }
}
