using Microsoft.Extensions.Options;

namespace Template.WebApi.Infrastructure.Security;

/// <summary>
/// Browser origins allowed to call the API cross-origin, bound from the <c>Cors</c> section (replaces the Phase 0
/// "any origin" policy). The SPA topology is same-origin (docs/product/api-guidelines.md §7), so an empty list means
/// "no cross-origin access"; set <see cref="RequireAllowedOrigins"/> to make an empty list fail startup instead.
/// The same list (plus the API's own origin) is the allow-list of the <c>Origin</c> check on session endpoints.
/// </summary>
public sealed class CorsSettings
{
    public const string SectionName = "Cors";
    public const string PolicyName = "ConfiguredOrigins";

    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>Send <c>Access-Control-Allow-Credentials</c>. Never combined with a wildcard (wildcards are rejected).</summary>
    public bool AllowCredentials { get; set; }

    /// <summary>Fail startup when <see cref="AllowedOrigins"/> is empty (deployments whose browser client is cross-origin).</summary>
    public bool RequireAllowedOrigins { get; set; }

    public static readonly string[] AllowedMethods = ["GET", "POST", "PUT", "PATCH", "DELETE"];

    public static readonly string[] AllowedHeaders =
        ["Authorization", "Content-Type", "Accept", "Accept-Language", "If-Match", "Idempotency-Key", "X-XSRF-TOKEN"];

    public static readonly string[] ExposedHeaders = ["ETag", "Location", "Retry-After", "Idempotent-Replayed"];

    /// <summary>Normalized <c>scheme://host[:port]</c>, the exact form browsers send in <c>Origin</c>.</summary>
    public static string? Normalize(string? origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && string.IsNullOrEmpty(uri.UserInfo)
        && uri.AbsolutePath == "/"
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment)
        && origin!.TrimEnd('/').Length == uri.GetLeftPart(UriPartial.Authority).Length
            ? uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant()
            : null;
}

/// <summary>Startup validation of <see cref="CorsSettings"/>; messages name keys and positions only.</summary>
public sealed class CorsSettingsValidator : IValidateOptions<CorsSettings>
{
    public ValidateOptionsResult Validate(string? name, CorsSettings options)
    {
        var failures = new List<string>();
        var origins = options.AllowedOrigins ?? [];
        for (var index = 0; index < origins.Length; index++)
        {
            if (origins[index]?.Contains('*') == true)
                failures.Add($"{CorsSettings.SectionName}:AllowedOrigins:{index} must not contain a wildcard.");
            else if (CorsSettings.Normalize(origins[index]) is null)
                failures.Add($"{CorsSettings.SectionName}:AllowedOrigins:{index} must be an http(s) origin without path, query or credentials.");
        }

        if (options.RequireAllowedOrigins && origins.Length == 0)
            failures.Add($"{CorsSettings.SectionName}:AllowedOrigins is required when {CorsSettings.SectionName}:RequireAllowedOrigins is true.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
