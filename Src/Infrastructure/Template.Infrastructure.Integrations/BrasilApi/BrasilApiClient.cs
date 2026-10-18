using System.Text.Json;
using Microsoft.Extensions.Logging;
using Template.Application.Interfaces.Integrations;
using Template.Application.Logging;
using Template.Application.Services.Integrations;
using Template.Infrastructure.Integrations.Http;

namespace Template.Infrastructure.Integrations.BrasilApi;

/// <summary>
/// Typed BrasilAPI client (https://brasilapi.com.br, docs at /docs): <c>GET cnpj/v1/{cnpj}</c> and
/// <c>GET cep/v2/{cep}</c>. Only the CNPJ or CEP is sent (NFR §9). Responses are validated and normalized before they
/// leave this adapter; contact data the provider may return (emails, phones, partners) is ignored on purpose.
/// BrasilAPI asks for human-like request volumes and has no published numeric limit, so nothing here loops or
/// prefetches; 429 is answered as <c>Unavailable</c> without retrying.
/// </summary>
internal sealed class BrasilApiClient(
    HttpClient client,
    ProviderHttpExecutorFactory executors,
    TimeProvider timeProvider,
    ILogger<BrasilApiClient> logger) : ISupplierRegistryLookup, IPostalCodeLookup
{
    private const int NameMaxLength = 200;

    private readonly ProviderHttpExecutor executor = executors.BrasilApi;

    Task<EnrichmentResult<CompanyRegistrationSuggestion>> ISupplierRegistryLookup.LookupAsync(string cnpj, CancellationToken cancellationToken) =>
        LookupCompanyAsync(cnpj, cancellationToken);

    Task<EnrichmentResult<PostalAddressSuggestion>> IPostalCodeLookup.LookupAsync(string postalCode, CancellationToken cancellationToken) =>
        LookupPostalCodeAsync(postalCode, cancellationToken);

    public async Task<EnrichmentResult<CompanyRegistrationSuggestion>> LookupCompanyAsync(string cnpj, CancellationToken cancellationToken)
    {
        var response = await executor.GetAsync(client, $"cnpj/v1/{Uri.EscapeDataString(cnpj)}", "company-registration", cancellationToken);
        if (Failure<CompanyRegistrationSuggestion>(response) is { } failure)
            return failure;
        if (!ProviderJson.TryParseObject(response.Body, out var document))
            return Invalid<CompanyRegistrationSuggestion>("company-registration");

        using (document)
        {
            var root = document!.RootElement;
            var valid = true;
            var returnedCnpj = ProviderJson.Text(root, "cnpj", 20, ref valid);
            var legalName = ProviderJson.Text(root, "razao_social", NameMaxLength, ref valid);
            var tradeName = ProviderJson.Text(root, "nome_fantasia", NameMaxLength, ref valid);
            var status = ProviderJson.Text(root, "descricao_situacao_cadastral", 100, ref valid);
            if (!valid || legalName is null || !string.Equals(Normalize(returnedCnpj), cnpj, StringComparison.Ordinal))
                return Invalid<CompanyRegistrationSuggestion>("company-registration");

            var address = ReadCompanyAddress(root);
            return EnrichmentResult<CompanyRegistrationSuggestion>.Found(
                EnrichmentProviders.BrasilApi,
                timeProvider.GetUtcNow().UtcDateTime,
                new CompanyRegistrationSuggestion(cnpj, legalName, tradeName, status, address));
        }
    }

    public async Task<EnrichmentResult<PostalAddressSuggestion>> LookupPostalCodeAsync(string postalCode, CancellationToken cancellationToken)
    {
        var response = await executor.GetAsync(client, $"cep/v2/{Uri.EscapeDataString(postalCode)}", "postal-code", cancellationToken);
        if (Failure<PostalAddressSuggestion>(response) is { } failure)
            return failure;
        if (!ProviderJson.TryParseObject(response.Body, out var document))
            return Invalid<PostalAddressSuggestion>("postal-code");

        using (document)
        {
            var root = document!.RootElement;
            var valid = true;
            var cep = ProviderJson.Digits(ProviderJson.Text(root, "cep", 10, ref valid));
            var state = ProviderJson.Text(root, "state", 2, ref valid)?.ToUpperInvariant();
            var city = ProviderJson.Text(root, "city", 100, ref valid);
            var district = ProviderJson.Text(root, "neighborhood", 100, ref valid);
            var street = ProviderJson.Text(root, "street", 200, ref valid);
            if (!valid || cep != postalCode || city is null || !IsState(state))
                return Invalid<PostalAddressSuggestion>("postal-code");

            return EnrichmentResult<PostalAddressSuggestion>.Found(
                EnrichmentProviders.BrasilApi,
                timeProvider.GetUtcNow().UtcDateTime,
                new PostalAddressSuggestion(cep, street, district, city, state!));
        }
    }

    /// <summary>Company address from the CNPJ record; omitted (not an error) when incomplete.</summary>
    private static PostalAddressSuggestion? ReadCompanyAddress(JsonElement root)
    {
        var valid = true;
        var cep = ProviderJson.Digits(ProviderJson.Text(root, "cep", 10, ref valid));
        var state = ProviderJson.Text(root, "uf", 2, ref valid)?.ToUpperInvariant();
        var city = ProviderJson.Text(root, "municipio", 100, ref valid);
        var district = ProviderJson.Text(root, "bairro", 100, ref valid);
        var streetType = ProviderJson.Text(root, "descricao_tipo_de_logradouro", 50, ref valid);
        var street = ProviderJson.Text(root, "logradouro", 150, ref valid);
        var number = ProviderJson.Text(root, "numero", 20, ref valid);
        var complement = ProviderJson.Text(root, "complemento", 100, ref valid);
        if (!valid || cep.Length != 8 || city is null || !IsState(state))
            return null;

        var fullStreet = streetType is not null && street is not null && !street.StartsWith(streetType, StringComparison.OrdinalIgnoreCase)
            ? $"{streetType} {street}"
            : street;
        return new PostalAddressSuggestion(cep, fullStreet, district, city, state!, number, complement);
    }

    private static string Normalize(string? cnpj) =>
        cnpj is null ? string.Empty : new string(cnpj.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static bool IsState(string? state) => state is { Length: 2 } && state.All(char.IsAsciiLetterUpper);

    private static EnrichmentResult<T>? Failure<T>(ProviderResponse response) where T : class => response.Status switch
    {
        ProviderCallStatus.Success => null,
        ProviderCallStatus.NotFound => EnrichmentResult<T>.Failed(EnrichmentOutcome.NotFound, EnrichmentProviders.BrasilApi, response.Reason),
        ProviderCallStatus.Rejected => EnrichmentResult<T>.Failed(EnrichmentOutcome.Rejected, EnrichmentProviders.BrasilApi, response.Reason),
        _ => EnrichmentResult<T>.Failed(EnrichmentOutcome.Unavailable, EnrichmentProviders.BrasilApi, response.Reason)
    };

    private EnrichmentResult<T> Invalid<T>(string operation) where T : class
    {
        logger.LogWarning(LogEvents.ProviderResponseInvalid, "Provider {Provider} {Operation} answered with an unexpected schema", EnrichmentProviders.BrasilApi, operation);
        return EnrichmentResult<T>.Failed(EnrichmentOutcome.Invalid, EnrichmentProviders.BrasilApi, "schema-mismatch");
    }
}
