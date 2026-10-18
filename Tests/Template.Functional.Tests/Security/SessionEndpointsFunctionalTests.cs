using System.Net;
using System.Text.Json;
using Microsoft.Net.Http.Headers;
using Shouldly;
using Template.Application.Security;
using Template.Functional.Tests.Common;
using static Template.Functional.Tests.Security.SecurityTestSupport;

namespace Template.Functional.Tests.Security;

/// <summary>Browser session endpoints through the real pipeline: SEC-001..006 and SEC-009 (D23-11).</summary>
public sealed class SessionEndpointsFunctionalTests(CustomWebApplicationFactory<Program> factory)
    : IClassFixture<CustomWebApplicationFactory<Program>>
{
    [Fact]
    public async Task Login_ReturnsOnlyTheAccessToken_AndSetsHardenedCookies()
    {
        var userName = await SessionUsers.CreateAsync(factory.Services);
        var session = SessionClient.Create(factory);

        using var response = await session.LoginAsync(userName, SyntheticPassword);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await SessionClient.ReadJsonAsync(response);
        body.EnumerateObject().Select(property => property.Name).ShouldBe(["accessToken", "tokenType", "accessTokenExpiresAt", "sessionExpiresAt"]);
        body.GetProperty("tokenType").GetString().ShouldBe("Bearer");
        var text = body.GetRawText();
        text.Contains(session.RefreshCookie!, StringComparison.Ordinal).ShouldBeFalse();
        text.Contains(session.CsrfCookie!, StringComparison.Ordinal).ShouldBeFalse();
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);

        var cookies = SessionClient.SetCookies(response).ToDictionary(cookie => cookie.Name.ToString());
        var refresh = cookies["__Secure-ph_refresh"];
        refresh.HttpOnly.ShouldBeTrue();
        refresh.Secure.ShouldBeTrue();
        refresh.SameSite.ShouldBe(SameSiteMode.Strict);
        refresh.Path.ToString().ShouldBe("/api/v2/auth/sessions");
        refresh.Domain.HasValue.ShouldBeFalse();
        refresh.Expires!.Value.ShouldBe(DateTimeOffset.UtcNow.AddDays(7), TimeSpan.FromMinutes(1));
        var csrf = cookies["XSRF-TOKEN"];
        csrf.HttpOnly.ShouldBeFalse();
        csrf.Secure.ShouldBeTrue();
        csrf.SameSite.ShouldBe(SameSiteMode.Strict);
        csrf.Path.ToString().ShouldBe("/");
        csrf.Expires.ShouldBe(refresh.Expires);
    }

    [Fact]
    public async Task Login_UnknownUserAndWrongPassword_AreIndistinguishable401()
    {
        var userName = await SessionUsers.CreateAsync(factory.Services);
        var session = SessionClient.Create(factory);

        using var wrongPassword = await session.LoginAsync(userName, "Wrong@Pass1");
        using var unknownUser = await session.LoginAsync("missing" + Guid.NewGuid().ToString("N"), "Wrong@Pass1");

        var details = new List<string?>();
        foreach (var response in new[] { wrongPassword, unknownUser })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            var problem = await SessionClient.ReadJsonAsync(response);
            problem.GetProperty("code").GetString().ShouldBe("auth.invalid-credentials");
            problem.GetProperty("ruleId").GetString().ShouldBe("SEC-001");
            details.Add(problem.GetProperty("detail").GetString());
            SessionClient.SetCookies(response).ShouldBeEmpty();
        }

        details.Distinct().Count().ShouldBe(1);
    }

    [Fact]
    public async Task Login_MissingFields_Is400WithValidationCode()
    {
        using var response = await SessionClient.Create(factory).LoginAsync("", "");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await SessionClient.ReadJsonAsync(response);
        problem.GetProperty("code").GetString().ShouldBe("validation");
        problem.GetProperty("errors").EnumerateObject().Select(error => error.Name).ShouldBe(["UserName", "Password"], ignoreOrder: true);
    }

    [Fact]
    public async Task Login_FromForeignOrigin_Is403_AndNoSessionStarts()
    {
        var userName = await SessionUsers.CreateAsync(factory.Services);

        using var response = await SessionClient.Create(factory).LoginAsync(userName, SyntheticPassword, SessionClient.ForeignOrigin);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SessionClient.ReadJsonAsync(response)).GetProperty("code").GetString().ShouldBe("auth.csrf-invalid");
        SessionClient.SetCookies(response).ShouldBeEmpty();
    }

    [Fact]
    public async Task Me_ReturnsOnlyApprovedFields_WithRolesAndEffectivePermissions()
    {
        var userName = await SessionUsers.CreateAsync(factory.Services, SeededRoles.Auditor, SeededRoles.Receiver);
        var session = SessionClient.Create(factory);
        await session.LoginOkAsync(userName);

        using var response = await session.SendWithBearerAsync(HttpMethod.Get, SessionClient.MeRoute);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var me = await SessionClient.ReadJsonAsync(response);
        me.EnumerateObject().Select(property => property.Name).ShouldBe(["userId", "userName", "displayName", "roles", "permissions"]);
        me.GetProperty("userName").GetString().ShouldBe(userName);
        me.GetProperty("displayName").GetString().ShouldBe("Synthetic Person");
        me.GetProperty("roles").EnumerateArray().Select(role => role.GetString()).ShouldBe([SeededRoles.Auditor, SeededRoles.Receiver]);
        var expected = SeededRoles.PermissionsByRole[SeededRoles.Auditor].Union(SeededRoles.PermissionsByRole[SeededRoles.Receiver])
            .Order(StringComparer.Ordinal);
        me.GetProperty("permissions").EnumerateArray().Select(permission => permission.GetString()).ShouldBe(expected);
        me.GetRawText().ShouldNotContain("example.invalid"); // no email or other claim
    }

    [Fact]
    public async Task Me_WithLegacyToken_Works_AndWithoutToken_Is401WithCode()
    {
        var (_, legacyToken) = await CreateGhostAsync(factory.CreateClient());
        var session = SessionClient.Create(factory);

        using var legacy = await session.SendWithBearerAsync(HttpMethod.Get, SessionClient.MeRoute, legacyToken);
        using var anonymous = await session.Client.GetAsync(SessionClient.MeRoute);

        legacy.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SessionClient.ReadJsonAsync(legacy)).GetProperty("permissions").GetArrayLength().ShouldBe(0);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SessionClient.ReadJsonAsync(anonymous)).GetProperty("code").GetString().ShouldBe("auth.session-expired");
        anonymous.Headers.WwwAuthenticate.ShouldContain(challenge => challenge.Scheme == "Bearer");
    }

    [Fact]
    public async Task Refresh_RotatesBothTokens_AndTheNewAccessTokenWorks()
    {
        var session = await LoggedInAsync();
        var firstRefresh = session.RefreshCookie;

        using var response = await session.RefreshAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        // Boolean checks keep token values out of assertion messages.
        (session.RefreshCookie == firstRefresh).ShouldBeFalse();
        session.AccessToken.ShouldNotBeNullOrEmpty();
        using var me = await session.SendWithBearerAsync(HttpMethod.Get, SessionClient.MeRoute);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_ReplayInsideGrace_Is409_AndTheSessionSurvives()
    {
        var session = await LoggedInAsync();
        var replaced = session.RefreshCookie!;
        (await session.RefreshAsync()).StatusCode.ShouldBe(HttpStatusCode.OK);
        var current = session.RefreshCookie!;

        session.UseRefreshCookie(replaced);
        using var replay = await session.RefreshAsync();

        replay.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await SessionClient.ReadJsonAsync(replay)).GetProperty("code").GetString().ShouldBe("auth.refresh-in-progress");
        SessionClient.SetCookies(replay).ShouldBeEmpty();
        session.UseRefreshCookie(current);
        (await session.RefreshAsync()).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("missing-header")]
    [InlineData("header-differs-from-cookie")]
    [InlineData("cookie-missing")]
    [InlineData("token-of-another-family")]
    public async Task Refresh_WithInvalidCsrf_Is403_AndDoesNotRotate(string variant)
    {
        var session = await LoggedInAsync();
        var other = await LoggedInAsync();
        var refreshBefore = session.RefreshCookie;

        using var response = variant switch
        {
            "missing-header" => await session.RefreshAsync(csrfHeader: ""),
            "header-differs-from-cookie" => await session.RefreshAsync(csrfHeader: other.CsrfCookie),
            "cookie-missing" => await session.RefreshAsync(sendCsrfCookie: false),
            _ => await RefreshWithForeignPairAsync(session, other.CsrfCookie!)
        };

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SessionClient.ReadJsonAsync(response)).GetProperty("code").GetString().ShouldBe("auth.csrf-invalid");
        (session.RefreshCookie == refreshBefore).ShouldBeTrue();
        (await session.RefreshAsync()).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(SessionClient.ForeignOrigin)]
    [InlineData("null")]
    [InlineData(null)]
    public async Task Refresh_FromForeignOrMissingOrigin_Is403(string? origin)
    {
        var session = await LoggedInAsync();

        using var response = await session.RefreshAsync(origin: origin);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SessionClient.ReadJsonAsync(response)).GetProperty("code").GetString().ShouldBe("auth.csrf-invalid");
    }

    [Fact]
    public async Task Refresh_SameOriginRequest_IsAccepted()
    {
        var session = await LoggedInAsync();

        (await session.RefreshAsync(origin: SessionClient.SelfOrigin)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_WithoutCookie_Is401SessionExpired()
    {
        using var response = await SessionClient.Create(factory).RefreshAsync(csrfHeader: "anything");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SessionClient.ReadJsonAsync(response)).GetProperty("code").GetString().ShouldBe("auth.session-expired");
    }

    [Fact]
    public async Task Logout_RevokesFamily_ClearsCookies_AndKillsTheAccessTokenImmediately()
    {
        var session = await LoggedInAsync();
        var refresh = session.RefreshCookie!;
        var csrf = session.CsrfCookie!;

        using var logout = await session.LogoutAsync();

        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        SessionClient.SetCookies(logout).Select(cookie => cookie.Name.ToString())
            .ShouldBe(["__Secure-ph_refresh", "XSRF-TOKEN"], ignoreOrder: true);
        SessionClient.SetCookies(logout).ShouldAllBe(cookie => cookie.Expires < DateTimeOffset.UtcNow);
        (await session.SendWithBearerAsync(HttpMethod.Get, SessionClient.MeRoute)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        session.UseRefreshCookie(refresh);
        session.UseCsrfCookie(csrf);
        using var refreshAfter = await session.RefreshAsync();
        refreshAfter.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SessionClient.ReadJsonAsync(refreshAfter)).GetProperty("code").GetString().ShouldBe("auth.session-revoked");
        using var repeated = await SessionClient.Create(factory).LogoutAsync();
        repeated.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Logout_WithoutCsrf_Is403_AndTheSessionStaysUsable()
    {
        var session = await LoggedInAsync();

        using var response = await session.LogoutAsync(csrfHeader: "");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await session.RefreshAsync()).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RevokeAll_RevokesEverySessionOfTheUser()
    {
        var userName = await SessionUsers.CreateAsync(factory.Services);
        var first = SessionClient.Create(factory);
        var second = SessionClient.Create(factory);
        await first.LoginOkAsync(userName);
        await second.LoginOkAsync(userName);

        using var revoke = await first.SendWithBearerAsync(HttpMethod.Delete, SessionClient.SessionsRoute);

        revoke.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await second.SendWithBearerAsync(HttpMethod.Get, SessionClient.MeRoute)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var refresh = await second.RefreshAsync();
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SessionClient.ReadJsonAsync(refresh)).GetProperty("code").GetString().ShouldBe("auth.session-revoked");
        (await SessionClient.Create(factory).Client.DeleteAsync(SessionClient.SessionsRoute)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PasswordChange_RevokesEverySession()
    {
        var userName = await SessionUsers.CreateAsync(factory.Services);
        var session = SessionClient.Create(factory);
        await session.LoginOkAsync(userName);

        using var change = await session.Client.PutJsonWithTokenAsync(
            ApiRoutes.V2.Account.ChangePassword, new { password = NewSyntheticPassword, confirmPassword = NewSyntheticPassword }, session.AccessToken);
        change.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var refresh = await session.RefreshAsync();
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SessionClient.ReadJsonAsync(refresh)).GetProperty("code").GetString().ShouldBe("auth.session-revoked");
    }

    [Fact]
    public async Task LegacyAuthenticate_KeepsRotatingTheStamp_WhichRevokesSessionsOnRefresh()
    {
        var userName = await SessionUsers.CreateAsync(factory.Services);
        var session = SessionClient.Create(factory);
        await session.LoginOkAsync(userName);

        using var legacy = await session.Client.PostAsync(
            ApiRoutes.V2.Account.Authenticate,
            System.Net.Http.Json.JsonContent.Create(new { userName, password = SyntheticPassword }));
        legacy.StatusCode.ShouldBe(HttpStatusCode.OK);
        var legacyBody = await SessionClient.ReadJsonAsync(legacy);
        legacyBody.GetProperty("success").GetBoolean().ShouldBeTrue(); // legacy BaseResult shape unchanged

        (await session.SendWithBearerAsync(HttpMethod.Get, SessionClient.MeRoute)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var refresh = await session.RefreshAsync();
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SessionClient.ReadJsonAsync(refresh)).GetProperty("code").GetString().ShouldBe("auth.session-revoked");
    }

    [Fact]
    public async Task SessionLogin_DoesNotInvalidateLegacyTokensOrOtherSessions()
    {
        var userName = await SessionUsers.CreateAsync(factory.Services);
        var first = SessionClient.Create(factory);
        await first.LoginOkAsync(userName);

        var second = SessionClient.Create(factory);
        await second.LoginOkAsync(userName);

        (await first.SendWithBearerAsync(HttpMethod.Get, SessionClient.MeRoute)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await first.RefreshAsync()).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SecurityEvents_AreAudited_WithoutTokensOrCookies()
    {
        var userName = await SessionUsers.CreateAsync(factory.Services);
        var session = SessionClient.Create(factory);
        await session.LoginOkAsync(userName);
        var secrets = new[] { session.RefreshCookie!, session.CsrfCookie!, session.AccessToken!, SyntheticPassword };
        (await session.RefreshAsync()).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await session.LogoutAsync()).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var auditor = SessionClient.Create(factory);
        await auditor.LoginOkAsync(await SessionUsers.CreateAsync(factory.Services, SeededRoles.Auditor));
        using var response = await auditor.SendWithBearerAsync(HttpMethod.Get, $"{SessionClient.AuditRoute}?resourceType=session-family&pageSize=100");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await SessionClient.ReadJsonAsync(response);
        var actions = page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("action").GetString()).ToArray();
        actions.ShouldContain(SecurityAuditActions.SessionCreated);
        actions.ShouldContain(SecurityAuditActions.SessionRefreshed);
        actions.ShouldContain(SecurityAuditActions.SessionRevoked);
        foreach (var secret in secrets)
            page.GetRawText().Contains(secret, StringComparison.Ordinal).ShouldBeFalse();
    }

    private async Task<SessionClient> LoggedInAsync()
    {
        var session = SessionClient.Create(factory);
        await session.LoginOkAsync(await SessionUsers.CreateAsync(factory.Services));
        return session;
    }

    /// <summary>Header and cookie agree, but the pair belongs to another family: the HMAC binding fails.</summary>
    private static async Task<HttpResponseMessage> RefreshWithForeignPairAsync(SessionClient session, string otherCsrf)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, SessionClient.RefreshRoute);
        request.Headers.Add("Origin", SessionClient.AllowedOrigin);
        request.Headers.Add("X-XSRF-TOKEN", otherCsrf);
        request.Headers.Add("Cookie", $"__Secure-ph_refresh={session.RefreshCookie}; XSRF-TOKEN={otherCsrf}");
        return await session.Client.SendAsync(request);
    }
}
