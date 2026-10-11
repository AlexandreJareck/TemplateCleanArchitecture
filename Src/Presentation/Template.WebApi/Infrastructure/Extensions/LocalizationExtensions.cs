using System.Globalization;
using FluentValidation;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using Template.Application.Settings;

namespace Template.WebApi.Infrastructure.Extensions;

public static class LocalizationExtensions
{
    /// <summary>
    /// Binds <c>LocalizationSettings</c> through the options pipeline, validates it at startup
    /// (<see cref="LocalizationSettingsValidator"/>) and derives <see cref="RequestLocalizationOptions"/> from the
    /// validated value. No intermediate service provider is built.
    /// </summary>
    public static IServiceCollection AddCustomLocalization(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<LocalizationSettings>, LocalizationSettingsValidator>();
        services.AddOptions<LocalizationSettings>()
            .Bind(configuration.GetSection(LocalizationSettingsValidator.SectionName))
            .ValidateOnStart();

        services.AddOptions<RequestLocalizationOptions>()
            .Configure<IOptions<LocalizationSettings>>((options, settings) =>
            {
                var supportedCultures = settings.Value.SupportedCultures.Select(CultureInfo.GetCultureInfo).ToArray();
                options.DefaultRequestCulture = new RequestCulture(settings.Value.DefaultRequestCulture);
                options.SupportedCultures = supportedCultures;
                options.SupportedUICultures = supportedCultures;
            });

        return services;
    }

    /// <summary>
    /// Establishes the request culture (query string, cookie, then <c>Accept-Language</c>; unsupported cultures fall
    /// back to the default) before any localized behavior runs. FluentValidation's built-in message language is
    /// process-global, so it is set once here, from the validated default culture, and never per request.
    /// </summary>
    public static IApplicationBuilder UseCustomLocalization(this IApplicationBuilder app)
    {
        var settings = app.ApplicationServices.GetRequiredService<IOptions<LocalizationSettings>>().Value;
        ValidatorOptions.Global.LanguageManager.Culture = CultureInfo.GetCultureInfo(settings.DefaultRequestCulture);

        app.UseRequestLocalization(app.ApplicationServices.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value);

        return app;
    }
}
