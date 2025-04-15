#nullable disable
using FluentValidation;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using System.Globalization;
using Template.Application.Settings;

namespace Template.WebApi.Infrastructure.Extensions;

public static class LocalizationExtensions
{
    public static IServiceCollection AddCustomLocalization(this IServiceCollection services)
    {
        var serviceProvider = services.BuildServiceProvider();
        var localizationSettings = serviceProvider.GetService<IOptions<LocalizationSettings>>().Value;
        var supportedCultures = localizationSettings.SupportedCultures.Select(p => new CultureInfo(p)).ToArray();

        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.DefaultRequestCulture = new RequestCulture(localizationSettings.DefaultRequestCulture);
            options.SupportedCultures = supportedCultures;
            options.SupportedUICultures = supportedCultures;
        });

        ValidatorOptions.Global.LanguageManager.Culture = new CultureInfo(localizationSettings.DefaultRequestCulture);

        return services;
    }
    public static IApplicationBuilder UseCustomLocalization(this IApplicationBuilder app)
    {
        app.UseRequestLocalization(app.ApplicationServices.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value);

        return app;
    }

}
