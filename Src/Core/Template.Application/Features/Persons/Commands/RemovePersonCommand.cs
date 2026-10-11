#nullable enable
using MediatR;
using Template.Application.Interfaces.Search;
using Template.Application.Search.Persons;

namespace Template.Application.Features.Persons.Commands;

/// <summary>Removes a person from the search index by its document key.</summary>
public sealed record RemovePersonCommand(string Key) : IRequest<PersonIndexResult>;

public sealed class RemovePersonCommandHandler(IPersonSearchIndex searchIndex)
    : IRequestHandler<RemovePersonCommand, PersonIndexResult>
{
    public Task<PersonIndexResult> Handle(RemovePersonCommand request, CancellationToken cancellationToken) =>
        searchIndex.RemoveAsync(request.Key, cancellationToken);
}
