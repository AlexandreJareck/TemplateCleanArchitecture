#nullable enable
using MediatR;
using Template.Application.Interfaces.Search;
using Template.Application.Search.Persons;

namespace Template.Application.Features.Persons.Commands;

/// <summary>
/// Creates or replaces a person in the search index. Adding and updating share these upsert semantics, as the v1
/// add-person and update-person endpoints always did.
/// </summary>
public sealed record IndexPersonCommand(int Id, string? FirstName, string? LastName) : IRequest<PersonIndexResult>;

public sealed class IndexPersonCommandHandler(IPersonSearchIndex searchIndex)
    : IRequestHandler<IndexPersonCommand, PersonIndexResult>
{
    public Task<PersonIndexResult> Handle(IndexPersonCommand request, CancellationToken cancellationToken) =>
        searchIndex.UpsertAsync(new IndexedPerson(request.Id, request.FirstName, request.LastName), cancellationToken);
}
