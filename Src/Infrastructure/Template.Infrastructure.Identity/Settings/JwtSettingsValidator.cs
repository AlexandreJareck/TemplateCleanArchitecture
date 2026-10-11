using System.Text;
using Microsoft.Extensions.Options;

namespace Template.Infrastructure.Identity.Settings;

/// <summary>Startup validation for <see cref="JwtSettings"/>. Messages name keys only, never values.</summary>
public sealed class JwtSettingsValidator : IValidateOptions<JwtSettings>
{
    public ValidateOptionsResult Validate(string? name, JwtSettings options)
    {
        var failures = new List<string>();
        const string section = JwtSettings.SectionName;

        if (string.IsNullOrWhiteSpace(options.Key))
            failures.Add($"{section}:Key is required.");
        else if (Encoding.UTF8.GetByteCount(options.Key) < JwtSettings.MinimumKeyBytes)
            failures.Add($"{section}:Key must be at least {JwtSettings.MinimumKeyBytes} bytes for HMAC-SHA256.");

        if (string.IsNullOrWhiteSpace(options.Issuer))
            failures.Add($"{section}:Issuer is required.");

        if (string.IsNullOrWhiteSpace(options.Audience))
            failures.Add($"{section}:Audience is required.");

        if (!(options.DurationInMinutes > 0 && options.DurationInMinutes <= JwtSettings.MaximumDurationInMinutes))
            failures.Add($"{section}:DurationInMinutes must be greater than 0 and at most {JwtSettings.MaximumDurationInMinutes}.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
