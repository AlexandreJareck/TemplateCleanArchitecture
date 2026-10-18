using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Template.Application.Interfaces.Access;
using Template.Functional.Tests.Common;
using Template.Infrastructure.Identity.Sessions;
using Template.Infrastructure.Identity.Settings;
using Template.WebApi.Infrastructure.Http;
using Template.WebApi.Infrastructure.Security;

namespace Template.Functional.Tests.Security;

/// <summary>Configured CORS (no wildcard), origin checks and the session cryptography adapter.</summary>
public sealed class CorsAndSessionComponentTests(CustomWebApplicationFactory<Program> factory)
    : IClassFixture<CustomWebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("/api/v2/auth/sessions/refresh")]
    [InlineData("/api/v2/product/get-paged-list")]
    [InlineData("/api/v1/product/get-paged-list")]
    public async Task Preflight_FromConfiguredOrigin_IsAllowed_WithoutCredentialsOrWildcard(string url)
    {
        using var response = await PreflightAsync(url, SessionClient.AllowedOrigin);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([SessionClient.AllowedOrigin]);
        response.Headers.Contains("Access-Control-Allow-Credentials").ShouldBeFalse();
        response.Headers.GetValues("Access-Control-Allow-Headers").Single().ShouldContain("X-XSRF-TOKEN", Case.Insensitive);
    }

    [Theory]
    [InlineData(SessionClient.ForeignOrigin)]
    [InlineData("http://spa.template.test")]
    [InlineData("https://spa.template.test.attacker.example")]
    public async Task Preflight_FromOtherOrigins_GetsNoCorsHeaders(string origin)
    {
        using var response = await PreflightAsync("/api/v2/product/get-paged-list", origin);

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Fact]
    public async Task SimpleRequest_NeverEchoesAWildcardOrigin()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v2/product/get-paged-list");
        request.Headers.Add("Origin", SessionClient.ForeignOrigin);
        using var response = await factory.CreateClient().SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Theory]
    [InlineData("*", false)]
    [InlineData("https://*.template.test", false)]
    [InlineData("https://spa.template.test/path", false)]
    [InlineData("ftp://spa.template.test", false)]
    [InlineData("https://user:secret@spa.template.test", false)]
    [InlineData("https://spa.template.test", true)]
    [InlineData("https://localhost:4200", true)]
    public void CorsSettingsValidator_AcceptsOnlyExplicitOrigins(string origin, bool valid)
    {
        var result = new CorsSettingsValidator().Validate(null, new CorsSettings { AllowedOrigins = [origin], AllowCredentials = true });

        result.Succeeded.ShouldBe(valid);
        if (!valid)
            result.FailureMessage!.ShouldNotContain("secret");
    }

    [Fact]
    public void CorsSettingsValidator_FailsSafelyWhenOriginsAreRequiredButAbsent()
    {
        var result = new CorsSettingsValidator().Validate(null, new CorsSettings { RequireAllowedOrigins = true });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Cors:AllowedOrigins");
        new CorsSettingsValidator().Validate(null, new CorsSettings()).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null, false, true)]
    [InlineData(null, true, false)]
    [InlineData("https://localhost", true, true)]
    [InlineData("https://spa.template.test", true, true)]
    [InlineData("https://SPA.template.test", true, true)]
    [InlineData("https://attacker.example", false, false)]
    [InlineData("null", false, false)]
    public void BrowserOriginValidator_AllowsSelfAndConfiguredOrigins(string? origin, bool required, bool expected)
    {
        var validator = new BrowserOriginValidator(Options.Create(new CorsSettings { AllowedOrigins = ["https://spa.template.test"] }));
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");
        if (origin is not null)
            context.Request.Headers.Origin = origin;

        validator.IsAllowed(context.Request, required).ShouldBe(expected);
    }

    [Fact]
    public void SessionTokenService_IssuesRandom256BitTokens_StoredOnlyAsSha256()
    {
        var service = factory.Services.GetRequiredService<ISessionTokenService>();

        var first = service.NewRefreshToken();
        var second = service.NewRefreshToken();

        (first.Token == second.Token).ShouldBeFalse();
        first.Token.Length.ShouldBe(43); // base64url of 32 bytes
        first.Hash.Length.ShouldBe(64);
        (service.HashRefreshToken(first.Token) == first.Hash).ShouldBeTrue();
        service.HashRefreshToken(first.Token + "x").ShouldBeNull();
        service.HashRefreshToken("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!").ShouldBeNull();
        service.HashRefreshToken(null).ShouldBeNull();
    }

    [Fact]
    public void SessionTokenService_CsrfIsBoundToTheFamilyAndToTheSigningKey()
    {
        var service = factory.Services.GetRequiredService<ISessionTokenService>();
        var family = Guid.NewGuid();
        var csrf = service.CreateCsrfToken(family);

        service.IsValidCsrfToken(family, csrf).ShouldBeTrue();
        service.IsValidCsrfToken(Guid.NewGuid(), csrf).ShouldBeFalse();
        service.IsValidCsrfToken(family, csrf[..^1] + (csrf[^1] == 'A' ? 'B' : 'A')).ShouldBeFalse();
        service.IsValidCsrfToken(family, null).ShouldBeFalse();
        service.IsValidCsrfToken(family, "short").ShouldBeFalse();

        var rotatedKey = new SessionTokenService(Options.Create(new JwtSettings { Key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)) }));
        rotatedKey.IsValidCsrfToken(family, csrf).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("\"7\"", 7L)]
    [InlineData("W/\"7\"", 0L)]
    [InlineData("*", 0L)]
    [InlineData("\"7\", \"8\"", 0L)]
    [InlineData("7", 0L)]
    [InlineData("\"abc\"", 0L)]
    public void IfMatch_AcceptsOnlyOneStrongVersionTag(string? header, long? expected)
    {
        var context = new DefaultHttpContext();
        if (header is not null)
            context.Request.Headers.IfMatch = header;

        VersionHeaders.ReadIfMatch(context.Request).ShouldBe(expected);
    }

    private async Task<HttpResponseMessage> PreflightAsync(string url, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, url);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "x-xsrf-token,content-type");
        return await factory.CreateClient().SendAsync(request);
    }
}
