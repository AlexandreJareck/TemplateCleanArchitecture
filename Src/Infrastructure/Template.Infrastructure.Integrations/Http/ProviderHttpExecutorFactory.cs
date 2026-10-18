using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Template.Infrastructure.Integrations.Http;

/// <summary>
/// Singleton owner of one <see cref="ProviderHttpExecutor"/> (and therefore one circuit breaker) per provider, so the
/// breaker state survives the short-lived typed <see cref="HttpClient"/> instances.
/// </summary>
internal sealed class ProviderHttpExecutorFactory(
    IOptions<BrasilApiSettings> brasilApi,
    IOptions<BcbPtaxSettings> bcbPtax,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory)
{
    private readonly Lazy<ProviderHttpExecutor> brasilApiExecutor = new(() => Create(Application.Services.Integrations.EnrichmentProviders.BrasilApi, brasilApi.Value, timeProvider, loggerFactory));
    private readonly Lazy<ProviderHttpExecutor> bcbPtaxExecutor = new(() => Create(Application.Services.Integrations.EnrichmentProviders.BcbPtax, bcbPtax.Value, timeProvider, loggerFactory));

    public ProviderHttpExecutor BrasilApi => brasilApiExecutor.Value;

    public ProviderHttpExecutor BcbPtax => bcbPtaxExecutor.Value;

    private static ProviderHttpExecutor Create(string provider, ProviderSettings settings, TimeProvider timeProvider, ILoggerFactory loggerFactory) =>
        new(provider,
            settings,
            new ProviderCircuitBreaker(settings.CircuitBreakerFailureThreshold, settings.CircuitBreakerBreakDuration, timeProvider),
            timeProvider,
            loggerFactory.CreateLogger($"Template.Infrastructure.Integrations.{provider}"));
}
