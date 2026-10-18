using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Template.Application.Interfaces.Integrations;
using Template.Infrastructure.Integrations.Bcb;
using Template.Infrastructure.Integrations.BrasilApi;
using Template.Infrastructure.Integrations.Http;

namespace Template.Infrastructure.Integrations;

public static class ServiceRegistration
{
    /// <summary>Largest provider response accepted (the documented payloads are a few kilobytes).</summary>
    public const long MaxResponseBytes = 1024 * 1024;

    /// <summary>
    /// Registers the optional enrichment adapters (ADR-0007). With <paramref name="useExternalProviders"/> and the
    /// provider's <c>Enabled</c> flag, calls go to the configured base URL; otherwise every lookup answers
    /// <c>Unavailable</c> (<c>disabled</c>) without network access, so manual entry is never blocked. Building the
    /// container never contacts a provider. The typed clients have the default HttpClient request logging removed
    /// because request URLs contain the CNPJ or CEP.
    /// </summary>
    public static IServiceCollection AddIntegrationsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool useExternalProviders)
    {
        services.AddOptions<BrasilApiSettings>()
            .Bind(configuration.GetSection(BrasilApiSettings.SectionName))
            .Validate(ProviderSettings.IsValid, string.Format(System.Globalization.CultureInfo.InvariantCulture, ProviderSettings.ValidationMessage, BrasilApiSettings.SectionName))
            .ValidateOnStart();
        services.AddOptions<BcbPtaxSettings>()
            .Bind(configuration.GetSection(BcbPtaxSettings.SectionName))
            .Validate(ProviderSettings.IsValid, string.Format(System.Globalization.CultureInfo.InvariantCulture, ProviderSettings.ValidationMessage, BcbPtaxSettings.SectionName))
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<DisabledEnrichmentProviders>();

        if (!useExternalProviders)
        {
            services.AddSingleton<ISupplierRegistryLookup>(provider => provider.GetRequiredService<DisabledEnrichmentProviders>());
            services.AddSingleton<IPostalCodeLookup>(provider => provider.GetRequiredService<DisabledEnrichmentProviders>());
            services.AddSingleton<IExchangeRateProvider>(provider => provider.GetRequiredService<DisabledEnrichmentProviders>());
            return services;
        }

        services.AddSingleton<ProviderHttpExecutorFactory>();
        services.AddHttpClient<BrasilApiClient>(ConfigureClient<BrasilApiSettings>).RemoveAllLoggers();
        services.AddHttpClient<BcbPtaxClient>(ConfigureClient<BcbPtaxSettings>).RemoveAllLoggers();

        services.AddTransient<ISupplierRegistryLookup>(provider =>
            provider.GetRequiredService<IOptions<BrasilApiSettings>>().Value.Enabled
                ? provider.GetRequiredService<BrasilApiClient>()
                : provider.GetRequiredService<DisabledEnrichmentProviders>());
        services.AddTransient<IPostalCodeLookup>(provider =>
            provider.GetRequiredService<IOptions<BrasilApiSettings>>().Value.Enabled
                ? provider.GetRequiredService<BrasilApiClient>()
                : provider.GetRequiredService<DisabledEnrichmentProviders>());
        services.AddTransient<IExchangeRateProvider>(provider =>
            provider.GetRequiredService<IOptions<BcbPtaxSettings>>().Value.Enabled
                ? provider.GetRequiredService<BcbPtaxClient>()
                : provider.GetRequiredService<DisabledEnrichmentProviders>());
        return services;
    }

    private static void ConfigureClient<TSettings>(IServiceProvider provider, HttpClient client) where TSettings : ProviderSettings
    {
        var settings = provider.GetRequiredService<IOptions<TSettings>>().Value;
        if (settings.Enabled)
            client.BaseAddress = new Uri(settings.BaseUrl, UriKind.Absolute);
        // Attempts are bounded by ProviderHttpExecutor (per-attempt timeout); the client-wide timeout only caps the worst case.
        client.Timeout = settings.Timeout * (settings.MaxRetries + 1) + settings.RetryDelay * (settings.MaxRetries * (settings.MaxRetries + 1) / 2) + TimeSpan.FromSeconds(1);
        client.MaxResponseContentBufferSize = MaxResponseBytes;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ProcurementHub/1.0 (+academic template)");
    }
}
