using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Template.Application.Interfaces.Access;
using Template.Infrastructure.Identity.Settings;

namespace Template.Infrastructure.Identity.Sessions;

/// <summary>
/// Refresh and CSRF token primitives (SEC-002, SEC-005).
/// <list type="bullet">
/// <item>Refresh tokens are 32 random bytes (256 bits) in base64url; only their SHA-256 (lowercase hex) is stored.</item>
/// <item>CSRF tokens are HMAC-SHA256(family ID) under a key derived with HKDF-SHA256 from <c>JwtSettings:Key</c>
/// (decision D24-3: no new secret; rotating the signing key also rotates CSRF bindings).</item>
/// </list>
/// No value produced here is logged.
/// </summary>
public sealed class SessionTokenService(IOptions<JwtSettings> jwtSettings) : ISessionTokenService
{
    public const int RefreshTokenBytes = 32;
    private static readonly byte[] CsrfKeyInfo = Encoding.ASCII.GetBytes("template.session.csrf.v1");
    private readonly Lazy<byte[]> csrfKey = new(() =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(jwtSettings.Value.Key), 32, salt: [], info: CsrfKeyInfo));

    public GeneratedRefreshToken NewRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(RefreshTokenBytes);
        return new GeneratedRefreshToken(Base64Url.EncodeToString(bytes), Hash(bytes));
    }

    public string? HashRefreshToken(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length != Base64Url.GetEncodedLength(RefreshTokenBytes) || !IsBase64Url(token))
            return null;

        var bytes = new byte[RefreshTokenBytes];
        return TryDecode(token, bytes) ? Hash(bytes) : null;
    }

    public string CreateCsrfToken(Guid sessionFamilyId) =>
        Base64Url.EncodeToString(HMACSHA256.HashData(csrfKey.Value, sessionFamilyId.ToByteArray()));

    public bool IsValidCsrfToken(Guid sessionFamilyId, string? csrfToken)
    {
        if (string.IsNullOrEmpty(csrfToken) || csrfToken.Length != Base64Url.GetEncodedLength(HMACSHA256.HashSizeInBytes) || !IsBase64Url(csrfToken))
            return false;

        var presented = new byte[HMACSHA256.HashSizeInBytes];
        if (!TryDecode(csrfToken, presented))
            return false;

        var expected = HMACSHA256.HashData(csrfKey.Value, sessionFamilyId.ToByteArray());
        return CryptographicOperations.FixedTimeEquals(expected, presented);
    }

    // Untrusted cookie/header values: reject anything outside the base64url alphabet before decoding.
    private static bool IsBase64Url(string value) =>
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    // Non-canonical trailing bits make the decoder throw; malformed input is simply "not a token".
    private static bool TryDecode(string value, Span<byte> destination)
    {
        try
        {
            return Base64Url.TryDecodeFromChars(value, destination, out var written) && written == destination.Length;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Lowercase hexadecimal SHA-256, the only stored form of a refresh token or security stamp.</summary>
    public static string Hash(ReadOnlySpan<byte> value) => Convert.ToHexStringLower(SHA256.HashData(value));
}
