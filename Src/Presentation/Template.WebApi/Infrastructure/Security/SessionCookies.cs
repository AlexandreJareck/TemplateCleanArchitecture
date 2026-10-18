namespace Template.WebApi.Infrastructure.Security;

/// <summary>
/// Cookie contract of the browser session (D23-11, docs/product/api-guidelines.md §7). Identical in every profile;
/// local development uses the HTTPS development certificate so <c>Secure</c> cookies work.
/// <list type="bullet">
/// <item><c>__Secure-ph_refresh</c>: HttpOnly, Secure, SameSite=Strict, Path=/api/v2/auth/sessions, no Domain,
/// expires at the family's absolute expiry.</item>
/// <item><c>XSRF-TOKEN</c>: readable by the SPA (not HttpOnly), Secure, SameSite=Strict, Path=/, same expiry; echoed in
/// <c>X-XSRF-TOKEN</c> (Angular's default names).</item>
/// </list>
/// Cookie values are never logged.
/// </summary>
public static class SessionCookies
{
    public const string RefreshCookieName = "__Secure-ph_refresh";
    public const string RefreshCookiePath = "/api/v2/auth/sessions";
    public const string CsrfCookieName = "XSRF-TOKEN";
    public const string CsrfCookiePath = "/";
    public const string CsrfHeaderName = "X-XSRF-TOKEN";

    public static void Write(HttpResponse response, string refreshToken, string csrfToken, DateTime expiresAtUtc)
    {
        var expires = new DateTimeOffset(DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc));
        response.Cookies.Append(RefreshCookieName, refreshToken, Options(RefreshCookiePath, httpOnly: true, expires));
        response.Cookies.Append(CsrfCookieName, csrfToken, Options(CsrfCookiePath, httpOnly: false, expires));
    }

    public static void Clear(HttpResponse response)
    {
        response.Cookies.Delete(RefreshCookieName, Options(RefreshCookiePath, httpOnly: true, expires: null));
        response.Cookies.Delete(CsrfCookieName, Options(CsrfCookiePath, httpOnly: false, expires: null));
    }

    public static string? ReadRefreshToken(HttpRequest request) =>
        request.Cookies.TryGetValue(RefreshCookieName, out var value) && !string.IsNullOrEmpty(value) ? value : null;

    /// <summary>
    /// Double-submit value: the header and the cookie must both be present and equal; the use case then verifies the
    /// HMAC binding to the session family. Returns <c>null</c> when the pair does not match.
    /// </summary>
    public static string? ReadDoubleSubmittedCsrfToken(HttpRequest request)
    {
        var header = request.Headers[CsrfHeaderName].ToString();
        return !string.IsNullOrEmpty(header)
            && request.Cookies.TryGetValue(CsrfCookieName, out var cookie)
            && string.Equals(header, cookie, StringComparison.Ordinal)
                ? header
                : null;
    }

    private static CookieOptions Options(string path, bool httpOnly, DateTimeOffset? expires) => new()
    {
        HttpOnly = httpOnly,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = path,
        Domain = null,
        Expires = expires,
        IsEssential = true
    };
}
