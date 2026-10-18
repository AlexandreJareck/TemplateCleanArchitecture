namespace Template.Domain.Access;

/// <summary>Why a session family stopped being usable (SEC-003, SEC-006, SEC-010). Stored as text.</summary>
public enum SessionRevocationReason
{
    Logout,
    RevokeAll,
    ReuseDetected,
    SecurityStampChanged,
    PasswordChanged,
    FamilyLimitExceeded
}
