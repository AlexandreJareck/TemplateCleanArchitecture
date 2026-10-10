#nullable enable
using Template.Application.Search.Persons;

namespace Template.Application.Interfaces.Search;

/// <summary>
/// The person search index used by the v1 person endpoints. Expected outcomes (missing person, search engine
/// unavailable or disabled, request refused, unreadable document) are returned as a <see cref="PersonIndexStatus"/>;
/// caller cancellation throws <see cref="OperationCanceledException"/> and programming errors are not caught.
/// </summary>
public interface IPersonSearchIndex
{
    /// <summary>Creates the named index unless it already exists (idempotent).</summary>
    Task<PersonIndexResult> EnsureIndexAsync(string indexName, CancellationToken cancellationToken);

    /// <summary>Creates or replaces the person document and waits until it is visible to searches.</summary>
    Task<PersonIndexResult> UpsertAsync(IndexedPerson person, CancellationToken cancellationToken);

    /// <summary>Creates or merges several person documents in one request; it can partially succeed.</summary>
    Task<PersonBulkIndexResult> UpsertManyAsync(IReadOnlyCollection<IndexedPerson> persons, CancellationToken cancellationToken);

    /// <summary>Reads one person by its document key.</summary>
    Task<PersonIndexResult<IndexedPerson>> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Lists persons with the search engine's default page (the first 10 hits, unordered); this is the existing v1
    /// "get all" behavior, not an exhaustive scan.
    /// </summary>
    Task<PersonIndexResult<IReadOnlyList<IndexedPerson>>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Removes one person by its document key.</summary>
    Task<PersonIndexResult> RemoveAsync(string key, CancellationToken cancellationToken);
}
