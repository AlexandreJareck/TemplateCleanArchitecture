using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Template.Application.Interfaces.Search;
using Template.Application.Search.Persons;
using Template.Functional.Tests.Common;
using Template.Infrastructure.Search.Persons;

namespace Template.Functional.Tests.ApiEndpoints;

/// <summary>The v1 person endpoints over a recording fake index: routes, statuses, texts and JSON are unchanged.</summary>
public sealed class PersonFunctionalTests(PersonFunctionalTests.RecordingSearchIndexFactory factory)
    : IClassFixture<PersonFunctionalTests.RecordingSearchIndexFactory>
{
    private const string Base = "/api/v1/person";

    private readonly HttpClient client = factory.CreateClient();
    private RecordingPersonSearchIndex Index => factory.Index;

    [Fact]
    public async Task CreateIndex_EnsuresTheNamedIndex_AndKeepsTheResponseText()
    {
        Index.Reset();

        using var response = await client.PostAsync($"{Base}/create-index?indexName=people-v1", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Index people-v1 created or already exists.");
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
        Index.Calls.Single().ShouldBe(("EnsureIndex", "people-v1", true));
    }

    [Theory]
    [InlineData(PersonIndexStatus.Unavailable)]
    [InlineData(PersonIndexStatus.Rejected)]
    public async Task CreateIndex_Failure_IsNoLongerReportedAsCreated(PersonIndexStatus status)
    {
        Index.Reset();
        Index.NextStatus = status;

        using var response = await client.PostAsync($"{Base}/create-index?indexName=people-v1", null);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Error creating index.");
    }

    [Fact]
    public async Task CreateIndex_WithoutIndexName_IsStillAValidationError()
    {
        Index.Reset();

        using var response = await client.PostAsync($"{Base}/create-index", null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        Index.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("add-person")]
    [InlineData("update-person")]
    public async Task AddAndUpdate_UpsertThePerson_AndKeepTheResponseText(string route)
    {
        Index.Reset();

        using var response = await client.PostAsJsonAsync($"{Base}/{route}", new { id = 5, firstName = "Eva", lastName = (string?)null });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Person added or updated successfully.");
        Index.Upserted.Single().ShouldBe(new IndexedPerson(5, "Eva", null));
        Index.Calls.Single().TokenCanBeCanceled.ShouldBeTrue();
    }

    [Theory]
    [InlineData(PersonIndexStatus.Unavailable)]
    [InlineData(PersonIndexStatus.Rejected)]
    public async Task AddAndUpdate_Failure_KeepsTheLegacy500(PersonIndexStatus status)
    {
        Index.Reset();
        Index.NextStatus = status;

        using var response = await client.PostAsJsonAsync($"{Base}/add-person", new { id = 5, firstName = "Eva" });

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Error adding or updating person.");
    }

    [Fact]
    public async Task GetPerson_ReturnsTheLegacyJson()
    {
        Index.Reset();
        Index.Stored = new IndexedPerson(5, "Eva", null);

        using var response = await client.GetAsync($"{Base}/get-person/5");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        (await response.Content.ReadAsStringAsync()).ShouldBe("""{"id":5,"firstName":"Eva","lastName":null}""");
        Index.Calls.Single().ShouldBe(("Get", "5", true));
    }

    [Fact]
    public async Task GetPerson_Missing_IsStill404()
    {
        Index.Reset();
        Index.NextStatus = PersonIndexStatus.NotFound;

        using var response = await client.GetAsync($"{Base}/get-person/9");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Person not found.");
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
    }

    [Theory]
    [InlineData(PersonIndexStatus.Unavailable)]
    [InlineData(PersonIndexStatus.Rejected)]
    [InlineData(PersonIndexStatus.IncompatibleDocument)]
    public async Task GetPerson_SearchFailure_IsNotReportedAsMissing(PersonIndexStatus status)
    {
        Index.Reset();
        Index.NextStatus = status;

        using var response = await client.GetAsync($"{Base}/get-person/5");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Error retrieving person.");
    }

    [Fact]
    public async Task GetAllPersons_ReturnsTheLegacyJsonArray()
    {
        Index.Reset();
        Index.Listed = [new IndexedPerson(1, "A", null), new IndexedPerson(2, null, "B")];

        using var response = await client.GetAsync($"{Base}/get-all-persons");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("""[{"id":1,"firstName":"A","lastName":null},{"id":2,"firstName":null,"lastName":"B"}]""");
        Index.Calls.Single().ShouldBe(("List", null, true));
    }

    [Theory]
    [InlineData(PersonIndexStatus.NotFound)]
    [InlineData(PersonIndexStatus.Unavailable)]
    [InlineData(PersonIndexStatus.IncompatibleDocument)]
    public async Task GetAllPersons_Failure_KeepsTheLegacy500(PersonIndexStatus status)
    {
        Index.Reset();
        Index.NextStatus = status;

        using var response = await client.GetAsync($"{Base}/get-all-persons");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Error retrieving persons.");
    }

    [Fact]
    public async Task DeletePerson_RemovesByKey_AndKeepsTheResponseText()
    {
        Index.Reset();

        using var response = await client.DeleteAsync($"{Base}/delete-person/5");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Person deleted successfully.");
        Index.Calls.Single().ShouldBe(("Remove", "5", true));
    }

    [Theory]
    [InlineData(PersonIndexStatus.NotFound)] // legacy: deleting a missing person was a 500
    [InlineData(PersonIndexStatus.Unavailable)]
    public async Task DeletePerson_Failure_KeepsTheLegacy500(PersonIndexStatus status)
    {
        Index.Reset();
        Index.NextStatus = status;

        using var response = await client.DeleteAsync($"{Base}/delete-person/9");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Error deleting person.");
    }

    [Fact]
    public async Task ClientCancellation_ReachesTheSearchIndex()
    {
        Index.Reset();
        Index.BlockUntilCancelled = true;
        using var cancellation = new CancellationTokenSource();

        var request = client.GetAsync($"{Base}/get-all-persons", cancellation.Token);
        await Index.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => request);
        await Index.ObservedCancellation.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Index.BlockUntilCancelled = false;
    }

    public sealed class RecordingSearchIndexFactory : CustomWebApplicationFactory<Program>
    {
        public RecordingPersonSearchIndex Index { get; } = new();

        protected override void ConfigureTestServices(IServiceCollection services) =>
            services.AddSingleton<IPersonSearchIndex>(Index);
    }
}

/// <summary>Default Test composition: search is not composed, so person endpoints fail while the rest of the API works.</summary>
public sealed class PersonWithoutSearchEngineFunctionalTests(CustomWebApplicationFactory<Program> factory)
    : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task WithoutSearchEngine_PersonEndpointsFail_AndOtherEndpointsStillWork()
    {
        factory.Services.GetRequiredService<IPersonSearchIndex>().ShouldBeOfType<DisabledPersonSearchIndex>();

        using var get = await client.GetAsync("/api/v1/person/get-person/1");
        using var list = await client.GetAsync("/api/v1/person/get-all-persons");
        using var add = await client.PostAsJsonAsync("/api/v1/person/add-person", new { id = 1, firstName = "A" });
        using var products = await client.GetAsync(ApiRoutes.V2.Product.GetPagedListProduct);
        using var swagger = await client.GetAsync("/swagger/v1/swagger.json");

        get.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await get.Content.ReadAsStringAsync()).ShouldBe("Error retrieving person.");
        list.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        add.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        products.StatusCode.ShouldBe(HttpStatusCode.OK);
        swagger.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_InTestEnvironment_HasNoElasticsearchEntry()
    {
        using var response = await client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldStartWith("""{"status":"Healthy","totalDuration":""");
        body.ShouldEndWith("\",\"entries\":{}}");
    }
}

public sealed class RecordingPersonSearchIndex : IPersonSearchIndex
{
    private readonly ConcurrentQueue<(string Operation, string? Argument, bool TokenCanBeCanceled)> calls = new();
    private readonly ConcurrentQueue<IndexedPerson> upserted = new();

    public IReadOnlyList<(string Operation, string? Argument, bool TokenCanBeCanceled)> Calls => calls.ToArray();

    public IReadOnlyList<IndexedPerson> Upserted => upserted.ToArray();

    public PersonIndexStatus NextStatus { get; set; } = PersonIndexStatus.Succeeded;

    public IndexedPerson Stored { get; set; } = new(1, "A", null);

    public IReadOnlyList<IndexedPerson> Listed { get; set; } = [];

    public bool BlockUntilCancelled { get; set; }

    public TaskCompletionSource Started { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource ObservedCancellation { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Reset()
    {
        calls.Clear();
        upserted.Clear();
        NextStatus = PersonIndexStatus.Succeeded;
        Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ObservedCancellation = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public async Task<PersonIndexResult> EnsureIndexAsync(string indexName, CancellationToken cancellationToken)
    {
        await RecordAsync("EnsureIndex", indexName, cancellationToken);
        return Result();
    }

    public async Task<PersonIndexResult> UpsertAsync(IndexedPerson person, CancellationToken cancellationToken)
    {
        await RecordAsync("Upsert", person.Id.ToString(), cancellationToken);
        upserted.Enqueue(person);
        return Result();
    }

    public Task<PersonBulkIndexResult> UpsertManyAsync(IReadOnlyCollection<IndexedPerson> persons, CancellationToken cancellationToken) =>
        throw new NotSupportedException("No v1 endpoint indexes in bulk.");

    public async Task<PersonIndexResult<IndexedPerson>> GetAsync(string key, CancellationToken cancellationToken)
    {
        await RecordAsync("Get", key, cancellationToken);
        return NextStatus == PersonIndexStatus.Succeeded
            ? PersonIndexResult<IndexedPerson>.Success(Stored)
            : PersonIndexResult<IndexedPerson>.Failure(NextStatus);
    }

    public async Task<PersonIndexResult<IReadOnlyList<IndexedPerson>>> ListAsync(CancellationToken cancellationToken)
    {
        await RecordAsync("List", null, cancellationToken);
        return NextStatus == PersonIndexStatus.Succeeded
            ? PersonIndexResult<IReadOnlyList<IndexedPerson>>.Success(Listed)
            : PersonIndexResult<IReadOnlyList<IndexedPerson>>.Failure(NextStatus);
    }

    public async Task<PersonIndexResult> RemoveAsync(string key, CancellationToken cancellationToken)
    {
        await RecordAsync("Remove", key, cancellationToken);
        return Result();
    }

    private PersonIndexResult Result() =>
        NextStatus == PersonIndexStatus.Succeeded ? PersonIndexResult.Success() : PersonIndexResult.Failure(NextStatus);

    private async Task RecordAsync(string operation, string? argument, CancellationToken cancellationToken)
    {
        Started.TrySetResult();
        if (BlockUntilCancelled)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation.TrySetResult();
                throw;
            }
        }

        calls.Enqueue((operation, argument, cancellationToken.CanBeCanceled));
    }
}
