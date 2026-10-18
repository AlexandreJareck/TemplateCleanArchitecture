namespace Template.Domain.Access;

/// <summary>Deterministic result of presenting a refresh token to its family.</summary>
public enum SessionRefreshOutcome
{
    /// <summary>The presented token was current; a new token replaced it (SEC-002).</summary>
    Rotated,

    /// <summary>The family passed its idle or absolute limit; nothing changed.</summary>
    Expired,

    /// <summary>The family was already revoked; nothing changed.</summary>
    Revoked,

    /// <summary>A replaced token was presented after the grace window; the family is now revoked (SEC-003).</summary>
    ReuseDetected,

    /// <summary>The immediate predecessor was presented inside the grace window; nothing changed (SEC-004).</summary>
    RefreshInProgress,

    /// <summary>The user's security stamp changed since the family started; the family is now revoked (SEC-006).</summary>
    SecurityStampChanged
}
