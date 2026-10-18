using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Access;
using Template.Infrastructure.Identity.Authentication;
using Template.Infrastructure.Identity.Models;
using Template.Infrastructure.Identity.Settings;

namespace Template.Infrastructure.Identity.Sessions;

/// <summary>
/// Issues the short-lived access token of a session (D23-11): the same claims as the legacy token (subject, name,
/// roles, security stamp) plus <see cref="SessionClaimTypes.SessionFamilyId"/>, which lets the bearer validator reject
/// tokens of a revoked family immediately. The security stamp is not rotated.
/// </summary>
public sealed class JwtAccessTokenIssuer(
    UserManager<ApplicationUser> userManager,
    IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
    JwtTokenFactory tokenFactory,
    IOptions<JwtSettings> jwtSettings,
    IClock clock) : IAccessTokenIssuer
{
    public async Task<IssuedAccessToken> IssueAsync(Guid userId, Guid sessionFamilyId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("The session user no longer exists.");

        cancellationToken.ThrowIfCancellationRequested();
        var principal = await claimsFactory.CreateAsync(user);
        var claims = principal.Claims.Append(new Claim(SessionClaimTypes.SessionFamilyId, sessionFamilyId.ToString()));

        // CreateToken stamps expiry from the same clock, so this value matches the token's exp claim.
        var expiresAt = clock.UtcNow.AddMinutes(jwtSettings.Value.DurationInMinutes);
        return new IssuedAccessToken(tokenFactory.CreateToken(claims), expiresAt);
    }
}

public static class SessionClaimTypes
{
    /// <summary>Registered JWT claim "sid" (session ID), carrying the session family ID.</summary>
    public const string SessionFamilyId = "sid";
}
