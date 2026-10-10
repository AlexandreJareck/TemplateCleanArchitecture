#nullable enable
using MediatR;
using Template.Application.Interfaces.Search;
using Template.Application.Search.Persons;

namespace Template.Application.Features.Persons.Queries;

/// <summary>Lists persons with the search engine's default page (see <see cref="IPersonSearchIndex.ListAsync"/>).</summary>
public sealed record ListPersonsQuery : IRequest<PersonIndexResult<IReadOnlyList<IndexedPerson>>>;

public sealed class ListPersonsQueryHandler(IPersonSearchIndex searchIndex)
    : IRequestHandler<ListPersonsQuery, PersonIndexResult<IReadOnlyList<IndexedPerson>>>
{
    public Task<PersonIndexResult<IReadOnlyList<IndexedPerson>>> Handle(ListPersonsQuery request, CancellationToken cancellationToken) =>
        searchIndex.ListAsync(cancellationToken);
}
