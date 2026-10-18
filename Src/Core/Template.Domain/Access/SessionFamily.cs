using System;
using System.Collections.Generic;
using System.Linq;
using Template.Domain.Common;

namespace Template.Domain.Access;

/// <summary>
/// A browser session started by one login (SEC-001). It owns a chain of rotating refresh tokens, stored as hashes
/// only (SEC-002), expires by idle and absolute limits, detects reuse of replaced tokens (SEC-003), tolerates a
/// refresh race inside the grace window (SEC-004) and is bound to the user's security stamp (SEC-006).
/// </summary>
public sealed class SessionFamily : VersionedAggregateRoot
{
    /// <summary>Lowercase hexadecimal SHA-256 length used for token hashes and stamp fingerprints.</summary>
    public const int HashLength = 64;

    private readonly List<RefreshTokenRecord> tokens = [];

    private SessionFamily()
    {
        SecurityStampFingerprint = string.Empty;
    }

    public Guid UserId { get; private set; }

    /// <summary>Hash of the user's security stamp when the family started; a different current value revokes the family.</summary>
    public string SecurityStampFingerprint { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime LastRefreshedAtUtc { get; private set; }
    public DateTime IdleExpiresAtUtc { get; private set; }
    public DateTime AbsoluteExpiresAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public SessionRevocationReason? RevocationReason { get; private set; }

    public IReadOnlyCollection<RefreshTokenRecord> Tokens => tokens;

    public bool IsRevoked => RevokedAtUtc is not null;

    public static SessionFamily Start(
        Guid id,
        Guid userId,
        string securityStampFingerprint,
        string tokenHash,
        DateTime nowUtc,
        SessionPolicy policy)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("A session family needs an identifier.", nameof(id));
        if (userId == Guid.Empty)
            throw new ArgumentException("A session family needs a user.", nameof(userId));
        EnsureHash(securityStampFingerprint, nameof(securityStampFingerprint));
        EnsureHash(tokenHash, nameof(tokenHash));
        EnsureUtc(nowUtc);
        ArgumentNullException.ThrowIfNull(policy);

        var family = new SessionFamily
        {
            Id = id,
            UserId = userId,
            SecurityStampFingerprint = securityStampFingerprint,
            CreatedAtUtc = nowUtc,
            LastRefreshedAtUtc = nowUtc,
            AbsoluteExpiresAtUtc = nowUtc + policy.AbsoluteLifetime,
            IdleExpiresAtUtc = nowUtc + policy.IdleTimeout
        };
        family.tokens.Add(new RefreshTokenRecord(Guid.NewGuid(), id, tokenHash, 1, nowUtc));
        return family;
    }

    public bool IsExpired(DateTime nowUtc) => nowUtc >= AbsoluteExpiresAtUtc || nowUtc >= IdleExpiresAtUtc;

    public bool IsActive(DateTime nowUtc) => !IsRevoked && !IsExpired(nowUtc);

    /// <summary>Expiry the refresh cookie should carry: the absolute limit of the family.</summary>
    public DateTime CookieExpiresAtUtc => AbsoluteExpiresAtUtc;

    public bool Contains(string tokenHash) => tokens.Any(token => token.TokenHash == tokenHash);

    /// <summary>
    /// Presents <paramref name="presentedTokenHash"/> (which must belong to this family) and, when it is the current
    /// token of an active family bound to <paramref name="currentStampFingerprint"/>, rotates it to
    /// <paramref name="newTokenHash"/>. Every other case is answered deterministically without rotating.
    /// </summary>
    public SessionRefreshOutcome Refresh(
        string presentedTokenHash,
        string newTokenHash,
        string currentStampFingerprint,
        DateTime nowUtc,
        SessionPolicy policy)
    {
        EnsureHash(newTokenHash, nameof(newTokenHash));
        EnsureUtc(nowUtc);
        ArgumentNullException.ThrowIfNull(policy);
        var presented = tokens.SingleOrDefault(token => token.TokenHash == presentedTokenHash)
            ?? throw new ArgumentException("The presented token does not belong to this session family.", nameof(presentedTokenHash));

        if (IsRevoked)
            return SessionRefreshOutcome.Revoked;

        if (IsExpired(nowUtc))
            return SessionRefreshOutcome.Expired;

        if (!presented.IsCurrent)
        {
            var current = CurrentToken();
            var isImmediatePredecessor = presented.Sequence == current.Sequence - 1;
            if (isImmediatePredecessor && nowUtc - presented.ReplacedAtUtc!.Value <= policy.ReuseGraceWindow)
                return SessionRefreshOutcome.RefreshInProgress;

            Revoke(SessionRevocationReason.ReuseDetected, nowUtc);
            return SessionRefreshOutcome.ReuseDetected;
        }

        if (!string.Equals(SecurityStampFingerprint, currentStampFingerprint, StringComparison.Ordinal))
        {
            Revoke(SessionRevocationReason.SecurityStampChanged, nowUtc);
            return SessionRefreshOutcome.SecurityStampChanged;
        }

        if (Contains(newTokenHash))
            throw new ArgumentException("The new token hash is already used by this family.", nameof(newTokenHash));

        presented.MarkReplaced(nowUtc);
        tokens.Add(new RefreshTokenRecord(Guid.NewGuid(), Id, newTokenHash, presented.Sequence + 1, nowUtc));
        LastRefreshedAtUtc = nowUtc;
        var idleExpiry = nowUtc + policy.IdleTimeout;
        IdleExpiresAtUtc = idleExpiry < AbsoluteExpiresAtUtc ? idleExpiry : AbsoluteExpiresAtUtc;
        IncrementVersion();
        return SessionRefreshOutcome.Rotated;
    }

    /// <summary>Revokes the family. Returns <c>false</c> (and changes nothing) when it was already revoked.</summary>
    public bool Revoke(SessionRevocationReason reason, DateTime nowUtc)
    {
        EnsureUtc(nowUtc);
        if (IsRevoked)
            return false;

        RevokedAtUtc = nowUtc;
        RevocationReason = reason;
        IncrementVersion();
        return true;
    }

    private RefreshTokenRecord CurrentToken() => tokens.Single(token => token.IsCurrent);

    private static void EnsureHash(string value, string parameterName)
    {
        if (value is null || value.Length != HashLength || !value.All(character => char.IsAsciiHexDigitLower(character) || char.IsAsciiDigit(character)))
            throw new ArgumentException("Expected a lowercase hexadecimal SHA-256 value.", parameterName);
    }

    private static void EnsureUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Session instants must be UTC.", nameof(value));
    }
}
