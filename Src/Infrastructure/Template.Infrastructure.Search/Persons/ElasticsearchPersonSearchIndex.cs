using System.Text.Json;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Elastic.Transport.Products.Elasticsearch;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Template.Application.Interfaces.Search;
using Template.Application.Search.Persons;

namespace Template.Infrastructure.Search.Persons;

/// <summary>
/// Person search index on Elasticsearch 8.x. It owns one long-lived <see cref="ElasticsearchClient"/> (thread-safe and
/// meant to be reused) and sends the same requests as the previous implementation: index with
/// <c>refresh=wait_for</c>, bulk update with <c>doc_as_upsert</c>, get, search with the server's default page, and
/// delete. Transport failures, timeouts and server errors become <see cref="PersonIndexStatus.Unavailable"/>, other
/// refusals <see cref="PersonIndexStatus.Rejected"/>; caller cancellation always throws. Logs carry the operation,
/// outcome, HTTP status and error type only, never documents, names, keys or endpoints with credentials.
/// </summary>
public sealed class ElasticsearchPersonSearchIndex : IPersonSearchIndex, IDisposable
{
    private const string ResourceAlreadyExists = "resource_already_exists_exception";

    private readonly ElasticsearchClientSettings _clientSettings;
    private readonly ElasticsearchClient _client;
    private readonly string _indexName;
    private readonly ILogger<ElasticsearchPersonSearchIndex> _logger;

    public ElasticsearchPersonSearchIndex(IOptions<PersonSearchSettings> options, ILogger<ElasticsearchPersonSearchIndex> logger)
    {
        var settings = options.Value;
        var endpoint = new Uri(settings.Url);
        _indexName = settings.IndexName;
        _logger = logger;

        // A single-node pool never sniffs or pings, so constructing the client opens no connection.
        _clientSettings = new ElasticsearchClientSettings(endpoint)
            .DefaultIndex(settings.IndexName)
            .RequestTimeout(settings.RequestTimeout);
        _client = new ElasticsearchClient(_clientSettings);

        _logger.LogInformation(
            "Person search index '{IndexName}' on {Endpoint} with a request timeout of {RequestTimeout}.",
            _indexName,
            endpoint.GetLeftPart(UriPartial.Authority),
            settings.RequestTimeout);
    }

    public async Task<PersonIndexResult> EnsureIndexAsync(string indexName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);

        var exists = await CallAsync(() => _client.Indices.ExistsAsync(indexName, cancellationToken), cancellationToken);
        if (exists is null)
            return PersonIndexResult.Failure(InvalidResponse("ensure-index", cancellationToken));
        if (exists.IsValidResponse && exists.Exists)
            return PersonIndexResult.Success();
        if (exists.ApiCallDetails.HttpStatusCode != 404)
            return PersonIndexResult.Failure(Classify("ensure-index", exists, cancellationToken));

        var created = await CallAsync(() => _client.Indices.CreateAsync(indexName, cancellationToken), cancellationToken);
        if (created is null)
            return PersonIndexResult.Failure(InvalidResponse("ensure-index", cancellationToken));
        if (created.IsValidResponse || created.ElasticsearchServerError?.Error?.Type == ResourceAlreadyExists)
            return PersonIndexResult.Success();

        return PersonIndexResult.Failure(Classify("ensure-index", created, cancellationToken));
    }

    public async Task<PersonIndexResult> UpsertAsync(IndexedPerson person, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(person);
        var document = PersonDocument.From(person);

        var response = await CallAsync(
            () => _client.IndexAsync(
                document,
                request => request.Index(_indexName).Id(document.Id).Refresh(Refresh.WaitFor),
                cancellationToken),
            cancellationToken);

        if (response is null)
            return PersonIndexResult.Failure(InvalidResponse("upsert", cancellationToken));
        return response.IsValidResponse
            ? PersonIndexResult.Success()
            : PersonIndexResult.Failure(Classify("upsert", response, cancellationToken));
    }

    public async Task<PersonBulkIndexResult> UpsertManyAsync(IReadOnlyCollection<IndexedPerson> persons, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(persons);
        cancellationToken.ThrowIfCancellationRequested();

        var documents = persons.Select(PersonDocument.From).ToArray();
        var requestedIds = documents.Select(document => document.Id).ToArray();
        if (documents.Length == 0)
            return PersonBulkIndexResult.Completed([], []);

        var response = await CallAsync(
            () => _client.BulkAsync(
                bulk => bulk
                    .Index(_indexName)
                    .UpdateMany(documents, (update, document) => update.Id(document.Id).Doc(document).DocAsUpsert(true)),
                cancellationToken),
            cancellationToken);

        if (response is null)
            return PersonBulkIndexResult.Failure(InvalidResponse("upsert-many", cancellationToken), requestedIds);

        var httpStatus = response.ApiCallDetails.HttpStatusCode;
        if (httpStatus is not (>= 200 and < 300) || response.Items is null)
            return PersonBulkIndexResult.Failure(Classify("upsert-many", response, cancellationToken), requestedIds);

        // Items echo the request order; an item is indexed only when the server acknowledged it.
        var indexed = new List<int>();
        var failed = new List<int>();
        for (var position = 0; position < requestedIds.Length; position++)
        {
            var item = position < response.Items.Count ? response.Items.ElementAt(position) : null;
            if (item is not null && item.Status is >= 200 and < 300 && item.Error is null)
                indexed.Add(requestedIds[position]);
            else
                failed.Add(requestedIds[position]);
        }

        if (failed.Count > 0)
        {
            _logger.LogWarning(
                "Person search {Operation} partially failed: {FailedCount} of {RequestedCount} documents were not indexed.",
                "upsert-many",
                failed.Count,
                requestedIds.Length);
        }

        return PersonBulkIndexResult.Completed(indexed, failed);
    }

    public async Task<PersonIndexResult<IndexedPerson>> GetAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var response = await CallAsync(
            () => _client.GetAsync<JsonElement>(key, request => request.Index(_indexName), cancellationToken),
            cancellationToken);

        if (response is null)
            return PersonIndexResult<IndexedPerson>.Failure(InvalidResponse("get", cancellationToken));
        if (!response.IsValidResponse)
            return PersonIndexResult<IndexedPerson>.Failure(Classify("get", response, cancellationToken));
        if (!response.Found)
            return PersonIndexResult<IndexedPerson>.Failure(PersonIndexStatus.NotFound);

        return PersonDocument.TryRead(response.Source, out var person)
            ? PersonIndexResult<IndexedPerson>.Success(person)
            : PersonIndexResult<IndexedPerson>.Failure(IncompatibleDocument("get"));
    }

    public async Task<PersonIndexResult<IReadOnlyList<IndexedPerson>>> ListAsync(CancellationToken cancellationToken)
    {
        var response = await CallAsync(
            () => _client.SearchAsync<JsonElement>(search => search.Index(_indexName), cancellationToken),
            cancellationToken);

        if (response is null)
            return PersonIndexResult<IReadOnlyList<IndexedPerson>>.Failure(InvalidResponse("list", cancellationToken));
        if (!response.IsValidResponse)
            return PersonIndexResult<IReadOnlyList<IndexedPerson>>.Failure(Classify("list", response, cancellationToken));

        var persons = new List<IndexedPerson>(response.Documents.Count);
        foreach (var source in response.Documents)
        {
            if (!PersonDocument.TryRead(source, out var person))
                return PersonIndexResult<IReadOnlyList<IndexedPerson>>.Failure(IncompatibleDocument("list"));
            persons.Add(person);
        }

        return PersonIndexResult<IReadOnlyList<IndexedPerson>>.Success(persons);
    }

    public async Task<PersonIndexResult> RemoveAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var response = await CallAsync(() => _client.DeleteAsync(new DeleteRequest(_indexName, key), cancellationToken), cancellationToken);

        if (response is null)
            return PersonIndexResult.Failure(InvalidResponse("remove", cancellationToken));
        return response.IsValidResponse
            ? PersonIndexResult.Success()
            : PersonIndexResult.Failure(Classify("remove", response, cancellationToken));
    }

    public void Dispose() => ((IDisposable)_clientSettings).Dispose();

    /// <summary>
    /// Runs a client call. The client reports transport failures (including timeouts and cancellation) in the
    /// response, but throws when a response body cannot be read or the server is not Elasticsearch; those cases
    /// return null and are handled as an invalid response.
    /// </summary>
    private async Task<TResponse?> CallAsync<TResponse>(Func<Task<TResponse>> call, CancellationToken cancellationToken)
        where TResponse : ElasticsearchResponse
    {
        try
        {
            return await call();
        }
        catch (Exception error) when (error is TransportException or UnsupportedProductException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);

            _logger.LogWarning(
                "Person search received a response it could not read ({ErrorType}, inner {InnerErrorType}).",
                error.GetType().Name,
                error.InnerException?.GetType().Name ?? "none");
            return null;
        }
    }

    private PersonIndexStatus InvalidResponse(string operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Log(operation, PersonIndexStatus.Unavailable, httpStatus: null, errorType: "invalid-response");
        return PersonIndexStatus.Unavailable;
    }

    private PersonIndexStatus IncompatibleDocument(string operation)
    {
        Log(operation, PersonIndexStatus.IncompatibleDocument, httpStatus: 200, errorType: "incompatible-document");
        return PersonIndexStatus.IncompatibleDocument;
    }

    private PersonIndexStatus Classify(string operation, ElasticsearchResponse response, CancellationToken cancellationToken)
    {
        // The client turns a cancelled request into a failed response; the caller's cancellation wins over any status.
        cancellationToken.ThrowIfCancellationRequested();

        var httpStatus = response.ApiCallDetails.HttpStatusCode;
        var status = httpStatus switch
        {
            null => PersonIndexStatus.Unavailable, // no HTTP response: refused, unreachable or timed out
            404 => PersonIndexStatus.NotFound,
            429 or >= 500 => PersonIndexStatus.Unavailable,
            _ => PersonIndexStatus.Rejected
        };

        var errorType = response.ElasticsearchServerError?.Error?.Type
            ?? response.ApiCallDetails.OriginalException?.GetType().Name
            ?? "none";
        Log(operation, status, httpStatus, errorType);
        return status;
    }

    private void Log(string operation, PersonIndexStatus status, int? httpStatus, string errorType)
    {
        var level = status == PersonIndexStatus.NotFound ? LogLevel.Debug : LogLevel.Warning;
        _logger.Log(
            level,
            "Person search {Operation} ended with {Outcome} (HTTP status {HttpStatus}, error type {ErrorType}).",
            operation,
            status,
            httpStatus?.ToString() ?? "none",
            errorType);
    }
}
