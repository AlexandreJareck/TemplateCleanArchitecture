using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Template.Application.Interfaces;
using Template.Infrastructure.Identity.Settings;

namespace Template.Infrastructure.Identity.Authentication;

/// <summary>
/// Builds signed access tokens. Issuance and validation share <see cref="CreateSigningKey"/> and
/// <see cref="CreateValidationParameters"/> so both sides agree on key, issuer, audience and algorithm.
/// </summary>
public sealed class JwtTokenFactory(IOptions<JwtSettings> options, IClock clock)
{
    public const string SigningAlgorithm = SecurityAlgorithms.HmacSha256;

    public string CreateToken(IEnumerable<Claim> claims)
    {
        var settings = options.Value;
        var now = clock.UtcNow;

        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(settings.DurationInMinutes),
            signingCredentials: new SigningCredentials(CreateSigningKey(settings), SigningAlgorithm));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static SymmetricSecurityKey CreateSigningKey(JwtSettings settings) =>
        new(Encoding.UTF8.GetBytes(settings.Key));

    public static TokenValidationParameters CreateValidationParameters(JwtSettings settings) => new()
    {
        ValidateIssuerSigningKey = true,
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ClockSkew = JwtSettings.ClockSkew,
        ValidIssuer = settings.Issuer,
        ValidAudience = settings.Audience,
        ValidAlgorithms = [SigningAlgorithm],
        IssuerSigningKey = CreateSigningKey(settings)
    };
}
