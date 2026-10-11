#nullable enable
namespace Template.Application.Search.Persons;

/// <summary>
/// Result of indexing several persons in one request. A bulk request can partially succeed: <see cref="IsComplete"/>
/// is true only when the request itself succeeded and every person was indexed.
/// </summary>
public sealed class PersonBulkIndexResult
{
    private PersonBulkIndexResult(PersonIndexStatus status, IReadOnlyList<int> indexedIds, IReadOnlyList<int> failedIds)
    {
        Status = status;
        IndexedIds = indexedIds;
        FailedIds = failedIds;
    }

    /// <summary>Status of the request as a whole. <see cref="PersonIndexStatus.Succeeded"/> does not imply every item succeeded.</summary>
    public PersonIndexStatus Status { get; }

    public IReadOnlyList<int> IndexedIds { get; }

    public IReadOnlyList<int> FailedIds { get; }

    public bool IsComplete => Status == PersonIndexStatus.Succeeded && FailedIds.Count == 0;

    public bool IsPartial => Status == PersonIndexStatus.Succeeded && FailedIds.Count > 0;

    /// <summary>The request reached the search engine; <paramref name="failedIds"/> lists the persons it refused.</summary>
    public static PersonBulkIndexResult Completed(IReadOnlyList<int> indexedIds, IReadOnlyList<int> failedIds) =>
        new(PersonIndexStatus.Succeeded,
            indexedIds ?? throw new ArgumentNullException(nameof(indexedIds)),
            failedIds ?? throw new ArgumentNullException(nameof(failedIds)));

    /// <summary>The request as a whole failed; no person is known to be indexed.</summary>
    public static PersonBulkIndexResult Failure(PersonIndexStatus status, IReadOnlyList<int> requestedIds) =>
        status == PersonIndexStatus.Succeeded
            ? throw new ArgumentOutOfRangeException(nameof(status), status, "A failure needs a non-success status.")
            : new PersonBulkIndexResult(status, [], requestedIds ?? throw new ArgumentNullException(nameof(requestedIds)));
}
