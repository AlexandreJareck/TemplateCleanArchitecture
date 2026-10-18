#nullable enable
using Microsoft.Extensions.Options;
using Template.Domain.Access;

namespace Template.Application.Settings;

/// <summary>
/// Browser-session policy bound from the <c>SessionSettings</c> section (SEC-002/003/004/007/010). Defaults are policy
/// version 1; none of these values is secret. The access-token lifetime stays <c>JwtSettings:DurationInMinutes</c>.
/// </summary>
public sealed class SessionSettings
{
    public const string SectionName = nameof(SessionSettings);

    public TimeSpan AbsoluteLifetime { get; set; } = SessionPolicy.Version1.AbsoluteLifetime;
    public TimeSpan IdleTimeout { get; set; } = SessionPolicy.Version1.IdleTimeout;
    public TimeSpan ReuseGraceWindow { get; set; } = SessionPolicy.Version1.ReuseGraceWindow;
    public int MaxActiveFamilies { get; set; } = SessionPolicy.Version1.MaxActiveFamilies;

    /// <summary>Requests allowed per partition (IP + user name for login, IP for refresh) per window (SEC-007).</summary>
    public int RateLimitPermitLimit { get; set; } = 10;
    public TimeSpan RateLimitWindow { get; set; } = TimeSpan.FromMinutes(1);

    public SessionPolicy ToPolicy() => new(AbsoluteLifetime, IdleTimeout, ReuseGraceWindow, MaxActiveFamilies);
}

/// <summary>Startup validation for <see cref="SessionSettings"/>. Messages name keys only.</summary>
public sealed class SessionSettingsValidator : IValidateOptions<SessionSettings>
{
    public ValidateOptionsResult Validate(string? name, SessionSettings options)
    {
        var failures = new List<string>();
        const string section = SessionSettings.SectionName;

        if (options.AbsoluteLifetime <= TimeSpan.Zero || options.AbsoluteLifetime > TimeSpan.FromDays(90))
            failures.Add($"{section}:AbsoluteLifetime must be greater than zero and at most 90 days.");
        if (options.IdleTimeout <= TimeSpan.Zero || options.IdleTimeout > options.AbsoluteLifetime)
            failures.Add($"{section}:IdleTimeout must be greater than zero and at most AbsoluteLifetime.");
        if (options.ReuseGraceWindow < TimeSpan.Zero || options.ReuseGraceWindow > TimeSpan.FromMinutes(1))
            failures.Add($"{section}:ReuseGraceWindow must be between 0 and 1 minute.");
        if (options.MaxActiveFamilies is < 1 or > 100)
            failures.Add($"{section}:MaxActiveFamilies must be between 1 and 100.");
        if (options.RateLimitPermitLimit < 1)
            failures.Add($"{section}:RateLimitPermitLimit must be at least 1.");
        if (options.RateLimitWindow <= TimeSpan.Zero || options.RateLimitWindow > TimeSpan.FromHours(1))
            failures.Add($"{section}:RateLimitWindow must be greater than zero and at most 1 hour.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
