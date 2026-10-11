using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Template.Application.DTOs.Account.Response;
using Template.Application.Wrappers;
using Template.Functional.Tests.Common;
using Template.Infrastructure.Identity.Models;
using static Template.Functional.Tests.Security.SecurityTestSupport;

namespace Template.Functional.Tests.Security;

/// <summary>JWT validation, security stamp, password change and 401/403 behavior through the real pipeline.</summary>
public sealed class JwtAuthenticationFunctionalTests(CustomWebApplicationFactory<Program> factory)
    : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private const string SecurityStampClaimType = "AspNet.Identity.SecurityStamp";
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task IssuedToken_IsAuthenticated()
    {
        var (_, token) = await CreateGhostAsync(client);

        using var response = await client.GetWithTokenAsync(AuthenticatedProbe, token);

        await AssertForbiddenAsync(response); // authenticated, lacks the probe role
    }

    [Fact]
    public async Task ValidCraftedToken_IsAuthenticated()
    {
        var (userName, _) = await CreateGhostAsync(client);
        var token = CreateToken(Settings, await CurrentClaimsAsync(factory.Services, userName));

        using var response = await client.GetWithTokenAsync(AuthenticatedProbe, token);

        await AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task ExpiredToken_Returns401()
    {
        var (userName, _) = await CreateGhostAsync(client);
        var now = DateTime.UtcNow;
        var token = CreateToken(Settings, await CurrentClaimsAsync(factory.Services, userName), now.AddMinutes(-20), now.AddMinutes(-10));

        await AssertUnauthorizedAsync(token);
    }

    [Fact]
    public async Task NotYetValidToken_Returns401()
    {
        var (userName, _) = await CreateGhostAsync(client);
        var now = DateTime.UtcNow;
        var token = CreateToken(Settings, await CurrentClaimsAsync(factory.Services, userName), now.AddMinutes(10), now.AddMinutes(20));

        await AssertUnauthorizedAsync(token);
    }

    [Fact]
    public async Task TokenSignedWithAnotherKey_Returns401()
    {
        var (userName, _) = await CreateGhostAsync(client);
        var token = CreateToken(
            Settings,
            await CurrentClaimsAsync(factory.Services, userName),
            signingKey: "synthetic-other-signing-key-0123456789abcdef");

        await AssertUnauthorizedAsync(token);
    }

    [Fact]
    public async Task TokenWithWrongIssuer_Returns401()
    {
        var (userName, _) = await CreateGhostAsync(client);
        var token = CreateToken(Settings, await CurrentClaimsAsync(factory.Services, userName), issuer: "synthetic-other-issuer");

        await AssertUnauthorizedAsync(token);
    }

    [Fact]
    public async Task TokenWithWrongAudience_Returns401()
    {
        var (userName, _) = await CreateGhostAsync(client);
        var token = CreateToken(Settings, await CurrentClaimsAsync(factory.Services, userName), audience: "synthetic-other-audience");

        await AssertUnauthorizedAsync(token);
    }

    [Fact]
    public async Task TokenWithoutSecurityStamp_Returns401()
    {
        var (userName, _) = await CreateGhostAsync(client);
        var claims = (await CurrentClaimsAsync(factory.Services, userName)).Where(claim => claim.Type != SecurityStampClaimType);

        await AssertUnauthorizedAsync(CreateToken(Settings, claims));
    }

    [Fact]
    public async Task TokenWithoutSubject_Returns401()
    {
        var (userName, _) = await CreateGhostAsync(client);
        var claims = (await CurrentClaimsAsync(factory.Services, userName)).Where(claim => claim.Type != ClaimTypes.NameIdentifier);

        await AssertUnauthorizedAsync(CreateToken(Settings, claims));
    }

    [Fact]
    public async Task TokenWithStaleSecurityStamp_Returns401()
    {
        var (userName, _) = await CreateGhostAsync(client);
        var claims = (await CurrentClaimsAsync(factory.Services, userName))
            .Select(claim => claim.Type == SecurityStampClaimType ? new Claim(claim.Type, Guid.NewGuid().ToString("N")) : claim);

        await AssertUnauthorizedAsync(CreateToken(Settings, claims));
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    [InlineData("eyJhbGciOiJub25lIn0.eyJzdWIiOiJ4In0.")]
    public async Task MalformedOrUnsignedToken_Returns401(string token) =>
        await AssertUnauthorizedAsync(token);

    [Fact]
    public async Task MissingToken_Returns401NotForbidden() =>
        await AssertUnauthorizedAsync(null);

    [Fact]
    public async Task TokenOfDeletedUser_Returns401()
    {
        var (userName, token) = await CreateGhostAsync(client);
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            (await userManager.DeleteAsync((await userManager.FindByNameAsync(userName))!)).Succeeded.ShouldBeTrue();
        }

        await AssertUnauthorizedAsync(token);
    }

    [Fact]
    public async Task NewSignIn_InvalidatesPreviouslyIssuedToken()
    {
        var (userName, firstToken) = await CreateGhostAsync(client);
        (await ChangePasswordAsync(ApiRoutes.V2.Account.ChangePassword, firstToken, SyntheticPassword)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var secondToken = await AuthenticateAsync(userName, SyntheticPassword);
        var thirdToken = await AuthenticateAsync(userName, SyntheticPassword);

        await AssertUnauthorizedAsync(secondToken);
        await AssertForbiddenAsync(await client.GetWithTokenAsync(AuthenticatedProbe, thirdToken));
    }

    [Theory]
    [InlineData(ApiRoutes.V1.Account.ChangePassword)]
    [InlineData(ApiRoutes.V2.Account.ChangePassword)]
    public async Task PasswordChange_InvalidatesOldToken_AndNewTokenIsAccepted(string changePasswordRoute)
    {
        var (userName, oldToken) = await CreateGhostAsync(client);

        using var change = await ChangePasswordAsync(changePasswordRoute, oldToken, SyntheticPassword);
        change.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await change.Content.ReadAsStringAsync()).ShouldNotContain(SyntheticPassword);

        await AssertUnauthorizedAsync(oldToken);
        using (var reuse = await ChangePasswordAsync(changePasswordRoute, oldToken, NewSyntheticPassword))
            reuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var newToken = await AuthenticateAsync(userName, SyntheticPassword);
        await AssertForbiddenAsync(await client.GetWithTokenAsync(AuthenticatedProbe, newToken));
    }

    [Fact]
    public async Task PasswordChange_WithInvalidNewPassword_Returns400_AndKeepsToken()
    {
        var (_, token) = await CreateGhostAsync(client);

        using var response = await ChangePasswordAsync(ApiRoutes.V2.Account.ChangePassword, token, "weak");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        await AssertForbiddenAsync(await client.GetWithTokenAsync(AuthenticatedProbe, token));
    }

    [Fact]
    public async Task PasswordChange_WithMismatchedConfirmation_Returns400()
    {
        var (_, token) = await CreateGhostAsync(client);

        using var response = await client.PutJsonWithTokenAsync(
            ApiRoutes.V2.Account.ChangePassword,
            new { password = SyntheticPassword, confirmPassword = NewSyntheticPassword },
            token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Authenticate_WithWrongPassword_Returns400_WithoutToken()
    {
        var (userName, token) = await CreateGhostAsync(client);
        (await ChangePasswordAsync(ApiRoutes.V2.Account.ChangePassword, token, SyntheticPassword)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var response = await client.PostAsJsonAsync(
            ApiRoutes.V2.Account.Authenticate,
            new { userName, password = NewSyntheticPassword });
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        text.ShouldNotContain("jwToken", Case.Insensitive);
        text.ShouldNotContain(NewSyntheticPassword);
    }

    [Fact]
    public async Task UserWithRequiredRole_IsAllowed_AndUserWithoutRole_Gets403()
    {
        var (userName, token) = await CreateGhostAsync(client);
        (await ChangePasswordAsync(ApiRoutes.V2.Account.ChangePassword, token, SyntheticPassword)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var withoutRole = await AuthenticateAsync(userName, SyntheticPassword);

        using (var forbidden = await client.GetWithTokenAsync(AdminProbe, withoutRole))
        {
            await AssertForbiddenAsync(forbidden);
            (await forbidden.Content.ReadAsStringAsync()).ShouldNotContain("role", Case.Insensitive);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            (await userManager.AddToRoleAsync((await userManager.FindByNameAsync(userName))!, "Admin")).Succeeded.ShouldBeTrue();
        }

        var withRole = await AuthenticateAsync(userName, SyntheticPassword);
        using var allowed = await client.GetWithTokenAsync(AdminProbe, withRole);

        allowed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private Infrastructure.Identity.Settings.JwtSettings Settings => JwtSettings(factory.Services);

    private Task<HttpResponseMessage> ChangePasswordAsync(string route, string token, string password) =>
        client.PutJsonWithTokenAsync(route, new { password, confirmPassword = password }, token);

    private async Task<string> AuthenticateAsync(string userName, string password)
    {
        using var response = await client.PostAsJsonAsync(ApiRoutes.V2.Account.Authenticate, new { userName, password });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<BaseResult<AuthenticationResponse>>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return result!.Data.JwToken;
    }

    private async Task AssertUnauthorizedAsync(string? token)
    {
        using var response = await client.GetWithTokenAsync(AuthenticatedProbe, token);
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        response.Headers.WwwAuthenticate.Count.ShouldBe(1);
        response.Headers.WwwAuthenticate.Single().Scheme.ShouldBe("Bearer");
        response.Headers.WwwAuthenticate.Single().Parameter.ShouldBeNull(); // no error_description leaks
        AssertProblemShape(text, 401);
        if (token is not null)
            text.Contains(token, StringComparison.Ordinal).ShouldBeFalse();
        text.ShouldNotContain("stamp", Case.Insensitive);
        text.ShouldNotContain("signature", Case.Insensitive);
        text.ShouldNotContain("expired", Case.Insensitive);
    }

    private static async Task AssertForbiddenAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        response.Headers.WwwAuthenticate.ShouldBeEmpty();
        AssertProblemShape(await response.Content.ReadAsStringAsync(), 403);
    }

    private static void AssertProblemShape(string text, int status)
    {
        using var body = JsonDocument.Parse(text);
        body.RootElement.GetProperty("status").GetInt32().ShouldBe(status);
        body.RootElement.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
        body.RootElement.GetProperty("type").GetString().ShouldNotBeNullOrWhiteSpace();
        body.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }
}
