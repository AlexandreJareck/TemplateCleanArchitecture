#nullable enable
namespace Template.Application.Interfaces.Access;

/// <summary>A user whose credentials were verified; the stamp is exposed only as a SHA-256 fingerprint.</summary>
public sealed record VerifiedAccount(Guid UserId, string SecurityStampFingerprint);

/// <summary>Approved current-user fields (SEC-009). No email, phone or other claim is exposed.</summary>
public sealed record AccountProfile(Guid UserId, string UserName, string? DisplayName, IReadOnlyList<string> Roles);

/// <summary>
/// Application-owned view of the user store. The Identity adapter implements it with ASP.NET Core Identity; no
/// Identity, claims or HTTP type crosses this port.
/// </summary>
public interface IAccountDirectory
{
    /// <summary>
    /// Verifies a user name and password without signing in and without rotating the security stamp. Returns
    /// <c>null</c> for an unknown user or a wrong password alike (no account enumeration).
    /// </summary>
    Task<VerifiedAccount?> VerifyCredentialsAsync(string userName, string password, CancellationToken cancellationToken);

    /// <summary>Current security-stamp fingerprint, or <c>null</c> when the user no longer exists.</summary>
    Task<string?> GetSecurityStampFingerprintAsync(Guid userId, CancellationToken cancellationToken);

    Task<AccountProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken);
}
