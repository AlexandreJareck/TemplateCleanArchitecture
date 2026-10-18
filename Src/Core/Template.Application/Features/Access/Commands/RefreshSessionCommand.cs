#nullable enable
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Results;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Access;
using Template.Application.Logging;
using Template.Application.Security;
using Template.Application.Services.Access;
using Template.Application.Settings;
using Template.Domain.Access;

namespace Template.Application.Features.Access.Commands;

/// <summary>
/// Refresh (SEC-002..006): validates the CSRF token bound to the family, then rotates the refresh token atomically.
/// Every outcome is deterministic: 401 expired/revoked, 403 CSRF, 409 when a concurrent refresh won the race.
/// </summary>
public sealed record RefreshSessionCommand(string? RefreshToken, string? CsrfToken) : IRequest<Result<SessionIssued>>
{
    public override string ToString() => nameof(RefreshSessionCommand);
}

public sealed class RefreshSessionCommandHandler(
    IAccountDirectory accounts,
    ISessionTokenService tokens,
    ISessionFamilyRepository sessions,
    IAccessTokenIssuer accessTokens,
    IUnitOfWork unitOfWork,
    SecurityAuditRecorder audit,
    IClock clock,
    IOptions<SessionSettings> settings,
    ILogger<RefreshSessionCommandHandler> logger) : IRequestHandler<RefreshSessionCommand, Result<SessionIssued>>
{
    private const int MaxAttempts = 3;

    public async Task<Result<SessionIssued>> Handle(RefreshSessionCommand request, CancellationToken cancellationToken)
    {
        var presentedHash = tokens.HashRefreshToken(request.RefreshToken);
        if (presentedHash is null)
            return AccessErrors.SessionExpired;

        for (var attempt = 1; ; attempt++)
        {
            var family = await sessions.FindByTokenHashAsync(presentedHash, cancellationToken);
            if (family is null)
                return AccessErrors.SessionExpired;

            if (!tokens.IsValidCsrfToken(family.Id, request.CsrfToken))
            {
                audit.SessionEvent(SecurityAuditActions.CsrfRejected, family.UserId, family.Id);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                logger.LogWarning(LogEvents.CsrfRejected, "CSRF validation failed for session family {SessionFamilyId}", family.Id);
                return AccessErrors.CsrfInvalid;
            }

            // A deleted user has no stamp; an empty fingerprint never matches, so the family is revoked below.
            var fingerprint = await accounts.GetSecurityStampFingerprintAsync(family.UserId, cancellationToken) ?? string.Empty;
            var newToken = tokens.NewRefreshToken();
            var now = clock.UtcNow;
            var outcome = family.Refresh(presentedHash, newToken.Hash, fingerprint, now, settings.Value.ToPolicy());

            switch (outcome)
            {
                case SessionRefreshOutcome.Expired:
                    return AccessErrors.SessionExpired;
                case SessionRefreshOutcome.Revoked:
                    return AccessErrors.SessionRevoked;
                case SessionRefreshOutcome.RefreshInProgress:
                    return AccessErrors.RefreshInProgress;
                case SessionRefreshOutcome.ReuseDetected:
                    audit.SessionEvent(SecurityAuditActions.SessionReuseDetected, family.UserId, family.Id,
                        SessionReasons.Describe(SessionRevocationReason.ReuseDetected), "Revoked");
                    break;
                case SessionRefreshOutcome.SecurityStampChanged:
                    audit.SessionEvent(SecurityAuditActions.SessionRevoked, family.UserId, family.Id,
                        SessionReasons.Describe(SessionRevocationReason.SecurityStampChanged), "Revoked");
                    break;
                case SessionRefreshOutcome.Rotated:
                    audit.SessionEvent(SecurityAuditActions.SessionRefreshed, family.UserId, family.Id);
                    break;
                default:
                    throw new InvalidOperationException($"Unhandled refresh outcome {outcome}.");
            }

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (ConcurrencyConflictException)
            {
                unitOfWork.DiscardChanges();
                // SEC-004: the other request rotated first; never return a second token for the same presented one.
                if (outcome == SessionRefreshOutcome.Rotated)
                    return AccessErrors.RefreshInProgress;
                if (attempt >= MaxAttempts)
                    throw;
                continue; // a revocation lost a race; re-evaluate against the current state
            }

            if (outcome == SessionRefreshOutcome.ReuseDetected)
            {
                logger.LogWarning(LogEvents.SessionReuseDetected, "Refresh-token reuse revoked session family {SessionFamilyId}", family.Id);
                return AccessErrors.SessionRevoked;
            }

            if (outcome == SessionRefreshOutcome.SecurityStampChanged)
            {
                logger.LogInformation(LogEvents.SessionRevoked, "Session family {SessionFamilyId} revoked after a security-stamp change", family.Id);
                return AccessErrors.SessionRevoked;
            }

            var accessToken = await accessTokens.IssueAsync(family.UserId, family.Id, cancellationToken);
            return new SessionIssued(
                family.Id,
                accessToken.Token,
                accessToken.ExpiresAtUtc,
                newToken.Token,
                tokens.CreateCsrfToken(family.Id),
                family.CookieExpiresAtUtc);
        }
    }
}
