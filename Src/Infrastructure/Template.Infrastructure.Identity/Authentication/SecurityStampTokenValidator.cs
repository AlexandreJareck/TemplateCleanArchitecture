using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Access;
using Template.Infrastructure.Identity.Models;
using Template.Infrastructure.Identity.Sessions;

namespace Template.Infrastructure.Identity.Authentication;

/// <summary>
/// Runs after signature, issuer, audience and lifetime validation. A token is rejected when it lacks the
/// subject or security-stamp claim, when its user no longer exists, or when its stamp is stale (after a
/// password change or a newer sign-in). Failure reasons stay on the server; clients only see 401.
/// A token issued by a v2 session (Phase 24) also carries the session family ID and is rejected as soon as that family
/// is revoked or expired (logout, revoke-all, reuse detection); legacy tokens have no such claim and are unaffected.
/// </summary>
public static class SecurityStampTokenValidator
{
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var services = context.HttpContext.RequestServices;
        var claimTypes = services.GetRequiredService<IOptions<IdentityOptions>>().Value.ClaimsIdentity;

        if (context.Principal is not { Identity: ClaimsIdentity { IsAuthenticated: true } identity } principal
            || string.IsNullOrWhiteSpace(identity.FindFirst(claimTypes.UserIdClaimType)?.Value))
        {
            context.Fail("The token has no subject.");
            return;
        }

        if (string.IsNullOrWhiteSpace(identity.FindFirst(claimTypes.SecurityStampClaimType)?.Value))
        {
            context.Fail("The token has no security stamp.");
            return;
        }

        // Identity's stamp validation has no CancellationToken overload; stop early if the request is gone.
        context.HttpContext.RequestAborted.ThrowIfCancellationRequested();

        var signInManager = services.GetRequiredService<SignInManager<ApplicationUser>>();
        if (await signInManager.ValidateSecurityStampAsync(principal) is null)
        {
            context.Fail("The token security stamp is not valid.");
            return;
        }

        // JwtBearer maps the registered "sid" claim to ClaimTypes.Sid on inbound tokens.
        var sessionClaim = identity.FindFirst(ClaimTypes.Sid) ?? identity.FindFirst(SessionClaimTypes.SessionFamilyId);
        if (sessionClaim is null)
            return;

        if (!Guid.TryParse(sessionClaim.Value, out var sessionFamilyId)
            || !Guid.TryParse(identity.FindFirst(claimTypes.UserIdClaimType)!.Value, out var userId)
            || !await services.GetRequiredService<ISessionFamilyRepository>().IsActiveAsync(
                sessionFamilyId, userId, services.GetRequiredService<IClock>().UtcNow, context.HttpContext.RequestAborted))
        {
            context.Fail("The token session is not active.");
        }
    }
}
