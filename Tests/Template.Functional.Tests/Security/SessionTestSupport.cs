using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Net.Http.Headers;
using Shouldly;
using Template.Application.Interfaces;
using Template.Functional.Tests.Common;
using Template.Infrastructure.Identity.Models;

namespace Template.Functional.Tests.Security;

/// <summary>
/// Browser-like client of the v2 session endpoints. Cookies are handled explicitly (no cookie container), so the
/// tests can assert every attribute; token and cookie values stay in memory and are never written to test output.
/// </summary>
internal sealed class SessionClient
{
    public const string AllowedOrigin = "https://spa.template.test";
    public const string SelfOrigin = "https://localhost";
    public const string ForeignOrigin = "https://attacker.example";
    public const string SessionsRoute = "/api/v2/auth/sessions";
    public const string RefreshRoute = "/api/v2/auth/sessions/refresh";
    public const string CurrentRoute = "/api/v2/auth/sessions/current";
    public const string MeRoute = "/api/v2/account/me";
    public const string AuditRoute = "/api/v2/audit-records";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, string> cookies = new(StringComparer.Ordinal);

    public SessionClient(HttpClient client)
    {
        Client = client;
    }

    public HttpClient Client { get; }
    public string? AccessToken { get; private set; }
    public string? RefreshCookie => cookies.GetValueOrDefault("__Secure-ph_refresh");
    public string? CsrfCookie => cookies.GetValueOrDefault("XSRF-TOKEN");

    public static SessionClient Create<TFactory>(TFactory factory) where TFactory : WebApplicationFactory<Program> =>
        new(factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(SelfOrigin), HandleCookies = false }));

    public async Task<HttpResponseMessage> LoginAsync(string userName, string password, string? origin = AllowedOrigin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, SessionsRoute) { Content = JsonContent.Create(new { userName, password }) };
        if (origin is not null)
            request.Headers.Add("Origin", origin);
        return await SendAndAbsorbAsync(request);
    }

    public async Task LoginOkAsync(string userName, string password = SecurityTestSupport.SyntheticPassword)
    {
        using var response = await LoginAsync(userName, password);
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
    }

    public async Task<HttpResponseMessage> RefreshAsync(string? csrfHeader = null, string? origin = AllowedOrigin, bool sendCsrfCookie = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, RefreshRoute);
        AddSessionHeaders(request, csrfHeader ?? CsrfCookie, origin, sendCsrfCookie);
        return await SendAndAbsorbAsync(request);
    }

    public async Task<HttpResponseMessage> LogoutAsync(string? csrfHeader = null, string? origin = AllowedOrigin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, CurrentRoute);
        AddSessionHeaders(request, csrfHeader ?? CsrfCookie, origin, sendCsrfCookie: true);
        return await SendAndAbsorbAsync(request);
    }

    public async Task<HttpResponseMessage> SendWithBearerAsync(HttpMethod method, string url, string? token = null)
    {
        using var request = new HttpRequestMessage(method, url);
        var bearer = token ?? AccessToken;
        if (bearer is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await SendAndAbsorbAsync(request);
    }

    /// <summary>Replaces the stored refresh cookie (to replay an older token).</summary>
    public void UseRefreshCookie(string value) => cookies["__Secure-ph_refresh"] = value;

    public void UseCsrfCookie(string value) => cookies["XSRF-TOKEN"] = value;

    public static IReadOnlyList<SetCookieHeaderValue> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? SetCookieHeaderValue.ParseList(values.ToList()).ToArray() : [];

    private void AddSessionHeaders(HttpRequestMessage request, string? csrfHeader, string? origin, bool sendCsrfCookie)
    {
        if (origin is not null)
            request.Headers.Add("Origin", origin);
        if (csrfHeader is not null)
            request.Headers.Add("X-XSRF-TOKEN", csrfHeader);
        var cookieHeader = string.Join("; ", cookies
            .Where(pair => sendCsrfCookie || pair.Key != "XSRF-TOKEN")
            .Select(pair => $"{pair.Key}={pair.Value}"));
        if (cookieHeader.Length > 0)
            request.Headers.Add("Cookie", cookieHeader);
    }

    private async Task<HttpResponseMessage> SendAndAbsorbAsync(HttpRequestMessage request)
    {
        var response = await Client.SendAsync(request);
        foreach (var cookie in SetCookies(response))
        {
            var name = cookie.Name.ToString();
            if (cookie.Expires is { } expires && expires <= DateTimeOffset.UtcNow || cookie.Value.Length == 0)
                cookies.Remove(name);
            else
                cookies[name] = cookie.Value.ToString();
        }

        if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "application/json"
            && request.RequestUri?.AbsolutePath is SessionsRoute or RefreshRoute)
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            AccessToken = document.RootElement.GetProperty("accessToken").GetString();
        }

        return response;
    }

    internal static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Json));
}

internal static class SessionUsers
{
    /// <summary>Creates a user with the synthetic password and the given roles; returns the user name.</summary>
    public static async Task<string> CreateAsync(IServiceProvider services, params string[] roles)
    {
        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var userName = "syn" + Guid.NewGuid().ToString("N")[..12];
        var user = new ApplicationUser { UserName = userName, Name = "Synthetic Person", Email = $"{userName}@example.invalid" };
        (await userManager.CreateAsync(user, SecurityTestSupport.SyntheticPassword)).Succeeded.ShouldBeTrue();
        foreach (var role in roles)
            (await userManager.AddToRoleAsync(user, role)).Succeeded.ShouldBeTrue();
        return userName;
    }
}

/// <summary>Refresh-token reuse is detected immediately (grace window zero).</summary>
public sealed class ZeroGraceWebApplicationFactory : CustomWebApplicationFactory<Program>
{
    protected override IReadOnlyDictionary<string, string?> AdditionalConfiguration { get; } =
        new Dictionary<string, string?> { ["SessionSettings:ReuseGraceWindow"] = "00:00:00" };
}

/// <summary>Policy-v1 rate limit (10 per minute) for SEC-007 tests.</summary>
public sealed class RateLimitedWebApplicationFactory : CustomWebApplicationFactory<Program>
{
    protected override IReadOnlyDictionary<string, string?> AdditionalConfiguration { get; } =
        new Dictionary<string, string?> { ["SessionSettings:RateLimitPermitLimit"] = "10" };
}

/// <summary>Host whose <see cref="IClock"/> can be advanced to cross idle/absolute session limits.</summary>
public sealed class MovableClockWebApplicationFactory : CustomWebApplicationFactory<Program>
{
    public MovableClock Clock { get; } = new();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<IClock>();
        services.AddSingleton<IClock>(Clock);
    }

    public sealed class MovableClock : IClock
    {
        public TimeSpan Offset { get; set; }
        public DateTime UtcNow => DateTime.UtcNow + Offset;
    }
}
