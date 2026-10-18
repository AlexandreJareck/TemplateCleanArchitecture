using Template.Application.Interfaces.Integrations;
using Template.Application.Services.Integrations;

namespace Template.Infrastructure.Integrations;

/// <summary>
/// Adapter used when a provider is disabled (default) or in the Test environment: every lookup answers
/// <see cref="EnrichmentOutcome.Unavailable"/> with reason <c>disabled</c> and never touches the network.
/// </summary>
internal sealed class DisabledEnrichmentProviders : ISupplierRegistryLookup, IPostalCodeLookup, IExchangeRateProvider
{
    public const string Reason = "disabled";

    Task<EnrichmentResult<CompanyRegistrationSuggestion>> ISupplierRegistryLookup.LookupAsync(string cnpj, CancellationToken cancellationToken) =>
        Task.FromResult(EnrichmentResult<CompanyRegistrationSuggestion>.Failed(EnrichmentOutcome.Unavailable, EnrichmentProviders.BrasilApi, Reason));

    Task<EnrichmentResult<PostalAddressSuggestion>> IPostalCodeLookup.LookupAsync(string postalCode, CancellationToken cancellationToken) =>
        Task.FromResult(EnrichmentResult<PostalAddressSuggestion>.Failed(EnrichmentOutcome.Unavailable, EnrichmentProviders.BrasilApi, Reason));

    public Task<EnrichmentResult<IReadOnlyList<ExchangeRateBulletin>>> GetPtaxBulletinsAsync(
        string currency, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
        Task.FromResult(EnrichmentResult<IReadOnlyList<ExchangeRateBulletin>>.Failed(EnrichmentOutcome.Unavailable, EnrichmentProviders.BcbPtax, Reason));
}
