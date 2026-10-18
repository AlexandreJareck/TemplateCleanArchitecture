#nullable enable
using MediatR;
using Microsoft.Extensions.Logging;
using Template.Application.Common.Results;
using Template.Application.Interfaces;
using Template.Application.Logging;
using Template.Application.Services.Access;
using Template.Domain.Access;

namespace Template.Application.Features.Access.Commands;

/// <summary>Revoke all (SEC-006): revokes every active session family of the authenticated user.</summary>
public sealed record RevokeAllSessionsCommand : IRequest<Result<int>>;

public sealed class RevokeAllSessionsCommandHandler(
    IAuthenticatedUserService currentUser,
    ISessionRevocationService revocation,
    ILogger<RevokeAllSessionsCommandHandler> logger) : IRequestHandler<RevokeAllSessionsCommand, Result<int>>
{
    public async Task<Result<int>> Handle(RevokeAllSessionsCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(currentUser.UserId, out var userId))
            return AccessErrors.NotAuthenticated;

        var revoked = await revocation.RevokeAllAsync(userId, SessionRevocationReason.RevokeAll, cancellationToken);
        logger.LogInformation(LogEvents.SessionRevoked, "{RevokedCount} session families revoked by revoke-all", revoked);
        return revoked;
    }
}
