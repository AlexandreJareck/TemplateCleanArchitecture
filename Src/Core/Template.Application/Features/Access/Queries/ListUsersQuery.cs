#nullable enable
using MediatR;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Interfaces.Access;
using Template.Application.Services.MasterData;

namespace Template.Application.Features.Access.Queries;

/// <summary>
/// User lookup for cost-center manager and department head assignment (ORG-006, permission
/// <c>access.users.manage</c>). Returns ID, user name, display name and active flag only.
/// </summary>
public sealed record ListUsersQuery(int? Page = null, int? PageSize = null, string? Q = null) : IRequest<Result<PagedResult<UserSummary>>>;

public sealed class ListUsersQueryHandler(IAccountDirectory accounts) : IRequestHandler<ListUsersQuery, Result<PagedResult<UserSummary>>>
{
    public async Task<Result<PagedResult<UserSummary>>> Handle(ListUsersQuery request, CancellationToken cancellationToken)
    {
        var page = PageRequest.Create(request.Page, request.PageSize, null, ["userName"], "userName");
        if (!page.IsSuccess)
            return page.Error!;
        if (MasterDataQueries.InvalidQ(request.Q) is { } invalidQ)
            return invalidQ;

        return await accounts.SearchUsersAsync(MasterDataQueries.Trim(request.Q), page.Value, cancellationToken);
    }
}
