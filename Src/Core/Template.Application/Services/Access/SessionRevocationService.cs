#nullable enable
using Template.Application.Common.Concurrency;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Access;
using Template.Application.Security;
using Template.Domain.Access;

namespace Template.Application.Services.Access;

/// <summary>Revokes every active session family of a user (SEC-006), auditing each one in the same commit.</summary>
public interface ISessionRevocationService
{
    /// <returns>The number of families revoked.</returns>
    Task<int> RevokeAllAsync(Guid userId, SessionRevocationReason reason, CancellationToken cancellationToken);
}

public sealed class SessionRevocationService(
    ISessionFamilyRepository sessions,
    IUnitOfWork unitOfWork,
    SecurityAuditRecorder audit,
    IClock clock) : ISessionRevocationService
{
    private const int MaxAttempts = 3;

    public async Task<int> RevokeAllAsync(Guid userId, SessionRevocationReason reason, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var now = clock.UtcNow;
            var revoked = 0;
            foreach (var family in await sessions.ListActiveAsync(userId, now, cancellationToken))
            {
                if (!family.Revoke(reason, now))
                    continue;

                audit.SessionEvent(SecurityAuditActions.SessionRevoked, userId, family.Id, SessionReasons.Describe(reason), "Revoked");
                revoked++;
            }

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return revoked;
            }
            catch (ConcurrencyConflictException) when (attempt < MaxAttempts)
            {
                // A family rotated concurrently; reload current state and revoke again.
                unitOfWork.DiscardChanges();
            }
        }
    }
}

/// <summary>Stable kebab-case audit reasons for <see cref="SessionRevocationReason"/>.</summary>
public static class SessionReasons
{
    public static string Describe(SessionRevocationReason reason) => reason switch
    {
        SessionRevocationReason.Logout => "logout",
        SessionRevocationReason.RevokeAll => "revoke-all",
        SessionRevocationReason.ReuseDetected => "reuse-detected",
        SessionRevocationReason.SecurityStampChanged => "security-stamp-changed",
        SessionRevocationReason.PasswordChanged => "password-changed",
        SessionRevocationReason.FamilyLimitExceeded => "family-limit-exceeded",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
    };
}
