#nullable enable
using MediatR;
using Template.Application.Common.Results;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Access;

namespace Template.Application.Features.Access.Queries;

/// <summary>
/// <c>GET /api/v2/account/me</c> (SEC-009): user ID, user name, display name, roles and effective permissions only.
/// The SPA uses it to hide unavailable actions; the API stays authoritative.
/// </summary>
public sealed record CurrentUserDto(
    Guid UserId,
    string UserName,
    string? DisplayName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public sealed record GetCurrentUserQuery : IRequest<Result<CurrentUserDto>>;

public sealed class GetCurrentUserQueryHandler(
    IAuthenticatedUserService currentUser,
    IAccountDirectory accounts,
    IPermissionReader permissions) : IRequestHandler<GetCurrentUserQuery, Result<CurrentUserDto>>
{
    public async Task<Result<CurrentUserDto>> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(currentUser.UserId, out var userId))
            return AccessErrors.NotAuthenticated;

        var profile = await accounts.GetProfileAsync(userId, cancellationToken);
        if (profile is null)
            return AccessErrors.NotAuthenticated;

        var effective = await permissions.GetEffectivePermissionsAsync(userId, cancellationToken);
        return new CurrentUserDto(
            profile.UserId,
            profile.UserName,
            profile.DisplayName,
            profile.Roles.Order(StringComparer.Ordinal).ToArray(),
            effective.Order(StringComparer.Ordinal).ToArray());
    }
}
