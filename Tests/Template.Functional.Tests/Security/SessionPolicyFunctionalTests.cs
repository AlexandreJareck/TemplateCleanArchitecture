using System.Net;
using Shouldly;
using Template.Application.Security;
using Template.Functional.Tests.Common;

namespace Template.Functional.Tests.Security;

/// <summary>SEC-003 reuse detection with a zero grace window.</summary>
public sealed class SessionReuseFunctionalTests(ZeroGraceWebApplicationFactory factory) : IClassFixture<ZeroGraceWebApplicationFactory>
{
    [Fact]
    public async Task ReplayingARotatedToken_RevokesTheWholeFamily()
    {
        var session = SessionClient.Create(factory);
        await session.LoginOkAsync(await SessionUsers.CreateAsync(factory.Services));
        var stolen = session.RefreshCookie!;
        var csrf = session.CsrfCookie!; // bound to the family, so it survives rotation
        (await session.RefreshAsync()).StatusCode.ShouldBe(HttpStatusCode.OK);
        var legitimate = session.RefreshCookie!;
        await Task.Delay(TimeSpan.FromMilliseconds(5));

        session.UseRefreshCookie(stolen);
        using var replay = await session.RefreshAsync();
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SessionClient.ReadJsonAsync(replay)).GetProperty("code").GetString().ShouldBe("auth.session-revoked");
        SessionClient.SetCookies(replay).ShouldAllBe(cookie => cookie.Expires < DateTimeOffset.UtcNow);

        session.UseRefreshCookie(legitimate);
        session.UseCsrfCookie(csrf);
        using var afterReuse = await session.RefreshAsync();
        afterReuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SessionClient.ReadJsonAsync(afterReuse)).GetProperty("code").GetString().ShouldBe("auth.session-revoked");
        (await session.SendWithBearerAsync(HttpMethod.Get, SessionClient.MeRoute)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}

/// <summary>SEC-002 idle and absolute expiry, with a movable application clock.</summary>
public sealed class SessionExpiryFunctionalTests(MovableClockWebApplicationFactory factory) : IClassFixture<MovableClockWebApplicationFactory>
{
    [Fact]
    public async Task IdleAndAbsoluteLimits_ExpireTheSession()
    {
        factory.Clock.Offset = TimeSpan.Zero;
        var idle = SessionClient.Create(factory);
        await idle.LoginOkAsync(await SessionUsers.CreateAsync(factory.Services));
        var absolute = SessionClient.Create(factory);
        await absolute.LoginOkAsync(await SessionUsers.CreateAsync(factory.Services));

        try
        {
            // Absolute: keep refreshing inside the idle window until the 7-day limit passes.
            for (var hours = 11; hours < 7 * 24; hours += 11)
            {
                factory.Clock.Offset = TimeSpan.FromHours(hours);
                (await absolute.RefreshAsync()).StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            factory.Clock.Offset = TimeSpan.FromDays(7);
            using var absoluteExpired = await absolute.RefreshAsync();
            absoluteExpired.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await SessionClient.ReadJsonAsync(absoluteExpired)).GetProperty("code").GetString().ShouldBe("auth.session-expired");

            // Idle: never refreshed for 12 hours (the clock is already past it).
            using var idleExpired = await idle.RefreshAsync();
            idleExpired.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await SessionClient.ReadJsonAsync(idleExpired)).GetProperty("code").GetString().ShouldBe("auth.session-expired");
        }
        finally
        {
            factory.Clock.Offset = TimeSpan.Zero;
        }
    }
}

/// <summary>SEC-007: authentication endpoints are rate-limited at policy v1 (10 per minute); others are not.</summary>
public sealed class AuthenticationRateLimitFunctionalTests(RateLimitedWebApplicationFactory factory) : IClassFixture<RateLimitedWebApplicationFactory>
{
    [Fact]
    public async Task EleventhLoginForTheSameUserName_Is429_WithRetryAfter_AndOtherEndpointsStayOpen()
    {
        var userName = await SessionUsers.CreateAsync(factory.Services);
        var session = SessionClient.Create(factory);
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            using var allowed = await session.LoginAsync(userName, "Wrong@Pass1");
            allowed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var limited = await session.LoginAsync(userName, SecurityTestSupport.SyntheticPassword);

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter!.Delta!.Value.TotalSeconds.ShouldBeInRange(1, 60);
        var problem = await SessionClient.ReadJsonAsync(limited);
        problem.GetProperty("code").GetString().ShouldBe("auth.rate-limited");
        problem.GetProperty("type").GetString().ShouldBe("https://www.rfc-editor.org/rfc/rfc6585#section-4");

        // Another user name from the same address has its own partition; unrelated endpoints are not limited.
        using var otherUser = await session.LoginAsync(await SessionUsers.CreateAsync(factory.Services), SecurityTestSupport.SyntheticPassword);
        otherUser.StatusCode.ShouldBe(HttpStatusCode.OK);
        for (var attempt = 0; attempt < 12; attempt++)
            (await session.Client.GetAsync(ApiRoutes.V2.Product.GetPagedListProduct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EleventhRefreshFromTheSameAddress_Is429_AndIsAuditedOnce()
    {
        var session = SessionClient.Create(factory);
        await session.LoginOkAsync(await SessionUsers.CreateAsync(factory.Services));
        HttpStatusCode last = HttpStatusCode.OK;
        for (var attempt = 0; attempt < 12 && last != HttpStatusCode.TooManyRequests; attempt++)
        {
            using var response = await session.RefreshAsync();
            last = response.StatusCode;
        }

        last.ShouldBe(HttpStatusCode.TooManyRequests);
        (await session.RefreshAsync()).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests); // sampled: no second audit record
        var auditor = SessionClient.Create(factory);
        await auditor.LoginOkAsync(await SessionUsers.CreateAsync(factory.Services, SeededRoles.Auditor));
        using var audit = await auditor.SendWithBearerAsync(HttpMethod.Get, $"{SessionClient.AuditRoute}?action={SecurityAuditActions.RateLimited}&pageSize=100");
        var items = (await SessionClient.ReadJsonAsync(audit)).GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("details").GetProperty("policy").GetString() == "Refresh")
            .ToArray();
        items.Length.ShouldBe(1);
        items[0].GetProperty("details").EnumerateObject().Select(property => property.Name).ShouldBe(["policy"]);
    }
}
