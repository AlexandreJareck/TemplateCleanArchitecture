#nullable enable
using MediatR;
using Template.Application.Interfaces.Search;
using Template.Application.Search.Persons;

namespace Template.Application.Features.Persons.Commands;

/// <summary>Creates the named search index unless it already exists.</summary>
public sealed record EnsurePersonIndexCommand(string IndexName) : IRequest<PersonIndexResult>;

public sealed class EnsurePersonIndexCommandHandler(IPersonSearchIndex searchIndex)
    : IRequestHandler<EnsurePersonIndexCommand, PersonIndexResult>
{
    public Task<PersonIndexResult> Handle(EnsurePersonIndexCommand request, CancellationToken cancellationToken) =>
        searchIndex.EnsureIndexAsync(request.IndexName, cancellationToken);
}
