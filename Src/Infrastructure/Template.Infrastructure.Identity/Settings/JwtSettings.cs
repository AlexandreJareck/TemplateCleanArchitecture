namespace Template.Infrastructure.Identity.Settings;

/// <summary>
/// JWT issuance and validation settings bound from the <c>JwtSettings</c> section.
/// Validated at startup by <see cref="JwtSettingsValidator"/>; the signing key, issuer and audience
/// have no code defaults.
/// </summary>
public sealed class JwtSettings
{
    public const string SectionName = nameof(JwtSettings);

    /// <summary>HMAC-SHA256 requires a key of at least 256 bits (32 UTF-8 bytes).</summary>
    public const int MinimumKeyBytes = 32;
    public const double MaximumDurationInMinutes = 1440;

    /// <summary>
    /// Clock skew applied to <c>exp</c>/<c>nbf</c>. Zero is intentional: the same host issues and validates
    /// tokens, so no tolerance is granted beyond the configured lifetime.
    /// </summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.Zero;

    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public double DurationInMinutes { get; set; }
}
