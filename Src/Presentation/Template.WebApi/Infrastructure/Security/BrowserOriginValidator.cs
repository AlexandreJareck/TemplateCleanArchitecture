using Microsoft.Extensions.Options;

namespace Template.WebApi.Infrastructure.Security;

/// <summary>
/// <c>Origin</c> check of cookie-authenticated session endpoints (SEC-005): the request's own origin or one of
/// <see cref="CorsSettings.AllowedOrigins"/>. Browsers send <c>Origin</c> on every non-GET fetch, so a missing header
/// is rejected where the check is required.
/// </summary>
public sealed class BrowserOriginValidator(IOptions<CorsSettings> settings)
{
    public bool IsAllowed(HttpRequest request, bool required)
    {
        var origin = request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
            return !required;

        var normalized = CorsSettings.Normalize(origin);
        if (normalized is null)
            return false;

        var self = $"{request.Scheme}://{request.Host.Value}".ToLowerInvariant();
        return normalized == self
            || settings.Value.AllowedOrigins.Any(allowed => CorsSettings.Normalize(allowed) == normalized);
    }
}
