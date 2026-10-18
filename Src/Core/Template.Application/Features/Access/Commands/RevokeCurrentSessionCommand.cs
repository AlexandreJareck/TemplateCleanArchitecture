#nullable enable
using MediatR;
using Microsoft.Extensions.Logging;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Results;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Access;
using Template.Application.Logging;
using Template.Application.Security;
using Template.Application.Services.Access;
using Template.Domain.Access;

namespace Template.Application.Features.Access.Commands;

/// <summary>
/// Logout (SEC-006): revokes the family of the presented refresh token. Missing, unknown or already revoked sessions
/// succeed without change so logout is repeatable; a present session still requires a valid CSRF token (SEC-005).
/// </summary>
public sealed record RevokeCurrentSessionCommand(string? RefreshToken, string? CsrfToken) : IRequest<Result>
{
    public override string ToString() => nameof(RevokeCurrentSessionCommand);
}

public sealed class RevokeCurrentSessionCommandHandler(
    ISessionTokenService tokens,
    ISessionFamilyRepository sessions,
    IUnitOfWork unitOfWork,
    SecurityAuditRecorder audit,
    IClock clock,
    ILogger<RevokeCurrentSessionCommandHandler> logger) : IRequestHandler<RevokeCurrentSessionCommand, Result>
{
    private const int MaxAttempts = 3;

    public async Task<Result> Handle(RevokeCurrentSessionCommand request, CancellationToken cancellationToken)
    {
        var presentedHash = tokens.HashRefreshToken(request.RefreshToken);
        if (presentedHash is null)
            return Result.Success();

        for (var attempt = 1; ; attempt++)
        {
            var family = await sessions.FindByTokenHashAsync(presentedHash, cancellationToken);
            if (family is null)
                return Result.Success();

            if (!tokens.IsValidCsrfToken(family.Id, request.CsrfToken))
            {
                audit.SessionEvent(SecurityAuditActions.CsrfRejected, family.UserId, family.Id);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                logger.LogWarning(LogEvents.CsrfRejected, "CSRF validation failed for session family {SessionFamilyId}", family.Id);
                return AccessErrors.CsrfInvalid;
            }

            if (!family.Revoke(SessionRevocationReason.Logout, clock.UtcNow))
                return Result.Success();

            audit.SessionEvent(SecurityAuditActions.SessionRevoked, family.UserId, family.Id,
                SessionReasons.Describe(SessionRevocationReason.Logout), "Revoked");
            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                logger.LogInformation(LogEvents.SessionRevoked, "Session family {SessionFamilyId} revoked by logout", family.Id);
                return Result.Success();
            }
            catch (ConcurrencyConflictException) when (attempt < MaxAttempts)
            {
                unitOfWork.DiscardChanges();
            }
        }
    }
}
