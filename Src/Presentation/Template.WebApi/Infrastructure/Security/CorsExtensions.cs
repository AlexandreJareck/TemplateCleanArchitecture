using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace Template.WebApi.Infrastructure.Security;

public static class CorsExtensions
{
    /// <summary>
    /// Registers the validated <see cref="CorsSettings"/> policy: explicit origins, methods and headers, credentials
    /// only when configured, never a wildcard origin.
    /// </summary>
    public static IServiceCollection AddConfiguredCors(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<CorsSettings>, CorsSettingsValidator>();
        services.AddOptions<CorsSettings>().Bind(configuration.GetSection(CorsSettings.SectionName)).ValidateOnStart();
        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IOptions<CorsSettings>>((options, settings) =>
            options.AddPolicy(CorsSettings.PolicyName, policy =>
            {
                var origins = settings.Value.AllowedOrigins.Select(CorsSettings.Normalize).OfType<string>().ToArray();
                policy.WithOrigins(origins)
                    .WithMethods(CorsSettings.AllowedMethods)
                    .WithHeaders(CorsSettings.AllowedHeaders)
                    .WithExposedHeaders(CorsSettings.ExposedHeaders);
                if (settings.Value.AllowCredentials)
                    policy.AllowCredentials();
                else
                    policy.DisallowCredentials();
            }));
        services.AddSingleton<BrowserOriginValidator>();
        return services;
    }

    public static IApplicationBuilder UseConfiguredCors(this IApplicationBuilder app) => app.UseCors(CorsSettings.PolicyName);
}
