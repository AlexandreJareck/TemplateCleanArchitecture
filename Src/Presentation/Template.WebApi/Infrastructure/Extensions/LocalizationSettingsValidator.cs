using System.Globalization;
using Microsoft.Extensions.Options;
using Template.Application.Settings;

namespace Template.WebApi.Infrastructure.Extensions;

/// <summary>
/// Validates <c>LocalizationSettings</c> at startup. Failures name the configuration key only: the default culture
/// must be a predefined culture and belong to the supported set; supported cultures must be present, predefined and
/// unique (case-insensitive), so duplicates fail deterministically instead of being silently merged.
/// </summary>
public sealed class LocalizationSettingsValidator : IValidateOptions<LocalizationSettings>
{
    public const string SectionName = nameof(LocalizationSettings);

    public ValidateOptionsResult Validate(string? name, LocalizationSettings options)
    {
        var failures = new List<string>();
        var defaultCulture = options.DefaultRequestCulture;
        var supported = options.SupportedCultures ?? [];

        if (!IsPredefinedCulture(defaultCulture))
            failures.Add($"{SectionName}:DefaultRequestCulture must be a predefined culture name.");

        if (supported.Count == 0)
            failures.Add($"{SectionName}:SupportedCultures must contain at least one culture.");
        if (supported.Any(culture => !IsPredefinedCulture(culture)))
            failures.Add($"{SectionName}:SupportedCultures must contain only predefined culture names.");
        if (supported.Count != supported.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            failures.Add($"{SectionName}:SupportedCultures must not contain duplicates.");

        if (IsPredefinedCulture(defaultCulture)
            && !supported.Contains(defaultCulture, StringComparer.OrdinalIgnoreCase))
            failures.Add($"{SectionName}:DefaultRequestCulture must be one of {SectionName}:SupportedCultures.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsPredefinedCulture(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        try
        {
            return CultureInfo.GetCultureInfo(name, predefinedOnly: true).Name.Length > 0;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}
