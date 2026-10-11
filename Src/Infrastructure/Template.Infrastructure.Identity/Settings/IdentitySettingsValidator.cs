using Microsoft.Extensions.Options;

namespace Template.Infrastructure.Identity.Settings;

/// <summary>Startup validation for <see cref="IdentitySettings"/>. Messages name keys only, never values.</summary>
public sealed class IdentitySettingsValidator : IValidateOptions<IdentitySettings>
{
    public ValidateOptionsResult Validate(string? name, IdentitySettings options) =>
        options.PasswordRequiredLength is >= IdentitySettings.MinimumPasswordLength and <= IdentitySettings.MaximumPasswordLength
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{IdentitySettings.SectionName}:PasswordRequiredLength must be between {IdentitySettings.MinimumPasswordLength} and {IdentitySettings.MaximumPasswordLength}.");
}
