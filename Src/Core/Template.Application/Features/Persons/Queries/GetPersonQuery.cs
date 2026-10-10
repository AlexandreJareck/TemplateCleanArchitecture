#nullable enable
using MediatR;
using Template.Application.Interfaces.Search;
using Template.Application.Search.Persons;

namespace Template.Application.Features.Persons.Queries;

/// <summary>Reads one person from the search index by its document key.</summary>
public sealed record GetPersonQuery(string Key) : IRequest<PersonIndexResult<IndexedPerson>>;

public sealed class GetPersonQueryHandler(IPersonSearchIndex searchIndex)
    : IRequestHandler<GetPersonQuery, PersonIndexResult<IndexedPerson>>
{
    public Task<PersonIndexResult<IndexedPerson>> Handle(GetPersonQuery request, CancellationToken cancellationToken) =>
        searchIndex.GetAsync(request.Key, cancellationToken);
}
