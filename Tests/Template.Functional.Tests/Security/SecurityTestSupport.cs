using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Template.Application.Interfaces;
using Template.Functional.Tests.Common;
using Template.Infrastructure.Identity.Models;
using Template.Infrastructure.Identity.Settings;

namespace Template.Functional.Tests.Security;

/// <summary>
/// Helpers for authentication tests. Tokens, stamps and passwords stay in memory and are never written to
/// test output or assertion messages.
/// </summary>
internal static class SecurityTestSupport
{
    /// <summary>Synthetic password scoped to this test fixture; it satisfies the Test password policy.</summary>
    internal const string SyntheticPassword = "Syn@Test2026";
    internal const string NewSyntheticPassword = "Syn@Test2027";

    internal const string AuthenticatedProbe = "/_tests/forbidden"; // 403 when authenticated, 401 otherwise
    internal const string AdminProbe = "/_tests/admin";

    internal static JwtSettings JwtSettings(IServiceProvider services) =>
        services.GetRequiredService<IOptions<JwtSettings>>().Value;

    internal static async Task<IReadOnlyList<Claim>> CurrentClaimsAsync(IServiceProvider services, string userName)
    {
        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var claimsFactory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(userName) ?? throw new InvalidOperationException("Test user not found.");
        return [.. (await claimsFactory.CreateAsync(user)).Claims];
    }

    internal static string CreateToken(
        JwtSettings settings,
        IEnumerable<Claim> claims,
        DateTime? notBefore = null,
        DateTime? expires = null,
        string? issuer = null,
        string? audience = null,
        string? signingKey = null)
    {
        var now = DateTime.UtcNow;
        var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(signingKey ?? settings.Key));
        var token = new JwtSecurityToken(
            issuer ?? settings.Issuer,
            audience ?? settings.Audience,
            claims,
            notBefore ?? now.AddMinutes(-1),
            expires ?? now.AddMinutes(10),
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    internal static async Task<HttpResponseMessage> GetWithTokenAsync(this HttpClient client, string url, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    internal static async Task<HttpResponseMessage> PutJsonWithTokenAsync(this HttpClient client, string url, object body, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) };
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    internal static async Task<(string UserName, string Token)> CreateGhostAsync(HttpClient client)
    {
        var ghost = await client.GetGhostAccount();
        return (ghost.UserName, ghost.JwToken);
    }

    internal sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }
}
