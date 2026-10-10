using Template.Application.Interfaces.Search;
using Template.Application.Search.Persons;

namespace Template.Infrastructure.Search.Persons;

/// <summary>
/// Used when search is disabled by configuration or no search engine is composed (Test environment). It never
/// contacts Elasticsearch and never reports success or a missing person.
/// </summary>
public sealed class DisabledPersonSearchIndex : IPersonSearchIndex
{
    public Task<PersonIndexResult> EnsureIndexAsync(string indexName, CancellationToken cancellationToken) => Unavailable(cancellationToken);

    public Task<PersonIndexResult> UpsertAsync(IndexedPerson person, CancellationToken cancellationToken) => Unavailable(cancellationToken);

    public Task<PersonBulkIndexResult> UpsertManyAsync(IReadOnlyCollection<IndexedPerson> persons, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(persons);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(PersonBulkIndexResult.Failure(
            PersonIndexStatus.Unavailable, persons.Select(person => person.Id).ToArray()));
    }

    public Task<PersonIndexResult<IndexedPerson>> GetAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(PersonIndexResult<IndexedPerson>.Failure(PersonIndexStatus.Unavailable));
    }

    public Task<PersonIndexResult<IReadOnlyList<IndexedPerson>>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(PersonIndexResult<IReadOnlyList<IndexedPerson>>.Failure(PersonIndexStatus.Unavailable));
    }

    public Task<PersonIndexResult> RemoveAsync(string key, CancellationToken cancellationToken) => Unavailable(cancellationToken);

    private static Task<PersonIndexResult> Unavailable(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(PersonIndexResult.Failure(PersonIndexStatus.Unavailable));
    }
}
