#nullable disable
using FluentValidation;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace Template.WebApi.Infrastructure.Extensions;

public static class LocalizationExtensions
{
    public static IServiceCollection AddCustomLocalization(this IServiceCollection services, IConfiguration configuration)
    {
        var supportedCultures = configuration.GetSection("Localization:SupportedCultures")
            .Get<List<string>>().Select(p => new CultureInfo(p)).ToArray();

        var defaultCulture = configuration["Localization:DefaultRequestCulture"];

        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.DefaultRequestCulture = new RequestCulture(configuration["Localization:DefaultRequestCulture"]);
            options.SupportedCultures = supportedCultures;
            options.SupportedUICultures = supportedCultures;
        });

        ValidatorOptions.Global.LanguageManager.Culture = new CultureInfo(defaultCulture);

        return services;
    }
    public static IApplicationBuilder UseCustomLocalization(this IApplicationBuilder app)
    {
        app.UseRequestLocalization(app.ApplicationServices.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value);

        return app;
    }

}
