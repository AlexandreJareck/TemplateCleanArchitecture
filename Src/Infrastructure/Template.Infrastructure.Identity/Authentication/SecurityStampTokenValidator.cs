using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Template.Infrastructure.Identity.Models;

namespace Template.Infrastructure.Identity.Authentication;

/// <summary>
/// Runs after signature, issuer, audience and lifetime validation. A token is rejected when it lacks the
/// subject or security-stamp claim, when its user no longer exists, or when its stamp is stale (after a
/// password change or a newer sign-in). Failure reasons stay on the server; clients only see 401.
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
            context.Fail("The token security stamp is not valid.");
    }
}
