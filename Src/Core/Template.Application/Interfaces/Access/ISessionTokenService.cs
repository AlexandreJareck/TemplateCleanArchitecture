#nullable enable
namespace Template.Application.Interfaces.Access;

/// <summary>A new refresh token and the verifier hash that is the only form ever stored.</summary>
public sealed record GeneratedRefreshToken(string Token, string Hash);

/// <summary>
/// Cryptographic primitives of the browser session (SEC-002, SEC-005): 256-bit random refresh tokens, their SHA-256
/// verifier hashes, and CSRF tokens bound to a session family by HMAC. Implemented in Infrastructure.Identity so the
/// key material never reaches Application.
/// </summary>
public interface ISessionTokenService
{
    GeneratedRefreshToken NewRefreshToken();

    /// <summary>Hash of a presented token, or <c>null</c> when the value is not a well-formed refresh token.</summary>
    string? HashRefreshToken(string? token);

    string CreateCsrfToken(Guid sessionFamilyId);

    /// <summary>Constant-time comparison of a presented CSRF token with the value bound to the family.</summary>
    bool IsValidCsrfToken(Guid sessionFamilyId, string? csrfToken);
}
