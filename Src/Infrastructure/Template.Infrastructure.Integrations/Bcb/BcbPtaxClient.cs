using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Template.Application.Interfaces.Integrations;
using Template.Application.Logging;
using Template.Application.Services.Integrations;
using Template.Infrastructure.Integrations.Http;

namespace Template.Infrastructure.Integrations.Bcb;

/// <summary>
/// Typed client of the Banco Central do Brasil PTAX OData service (Olinda, <c>servico/PTAX/versao/v1/odata/</c>),
/// resource <c>CotacaoMoedaPeriodo(moeda, dataInicial, dataFinalCotacao)</c> with dates as <c>'MM-dd-yyyy'</c> and
/// <c>$format=json</c>. Each row has <c>cotacaoCompra</c>, <c>cotacaoVenda</c>, <c>dataHoraCotacao</c>
/// (<c>yyyy-MM-dd HH:mm:ss.fff</c>, Brasília time) and <c>tipoBoletim</c> (<c>Abertura</c>, <c>Intermediário</c>,
/// <c>Fechamento</c>; the closing bulletin is the one used by SP-5). Rates are BRL per foreign unit.
/// </summary>
internal sealed class BcbPtaxClient(
    HttpClient client,
    ProviderHttpExecutorFactory executors,
    TimeProvider timeProvider,
    ILogger<BcbPtaxClient> logger) : IExchangeRateProvider
{
    private const string Operation = "ptax-period";
    private readonly ProviderHttpExecutor executor = executors.BcbPtax;

    public async Task<EnrichmentResult<IReadOnlyList<ExchangeRateBulletin>>> GetPtaxBulletinsAsync(
        string currency, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
    {
        if (currency is not { Length: 3 } || !currency.All(char.IsAsciiLetterUpper) || fromDate > toDate)
            return EnrichmentResult<IReadOnlyList<ExchangeRateBulletin>>.Failed(EnrichmentOutcome.Rejected, EnrichmentProviders.BcbPtax, "invalid-request");

        var response = await executor.GetAsync(client, BuildUrl(currency, fromDate, toDate), Operation, cancellationToken);
        switch (response.Status)
        {
            case ProviderCallStatus.Success:
                break;
            case ProviderCallStatus.NotFound:
                return EnrichmentResult<IReadOnlyList<ExchangeRateBulletin>>.Failed(EnrichmentOutcome.NotFound, EnrichmentProviders.BcbPtax, response.Reason);
            case ProviderCallStatus.Rejected:
                return EnrichmentResult<IReadOnlyList<ExchangeRateBulletin>>.Failed(EnrichmentOutcome.Rejected, EnrichmentProviders.BcbPtax, response.Reason);
            default:
                return EnrichmentResult<IReadOnlyList<ExchangeRateBulletin>>.Failed(EnrichmentOutcome.Unavailable, EnrichmentProviders.BcbPtax, response.Reason);
        }

        var bulletins = Parse(response.Body, currency, fromDate, toDate);
        if (bulletins is null)
        {
            logger.LogWarning(LogEvents.ProviderResponseInvalid, "Provider {Provider} {Operation} answered with an unexpected schema", EnrichmentProviders.BcbPtax, Operation);
            return EnrichmentResult<IReadOnlyList<ExchangeRateBulletin>>.Failed(EnrichmentOutcome.Invalid, EnrichmentProviders.BcbPtax, "schema-mismatch");
        }

        return EnrichmentResult<IReadOnlyList<ExchangeRateBulletin>>.Found(EnrichmentProviders.BcbPtax, timeProvider.GetUtcNow().UtcDateTime, bulletins);
    }

    internal static string BuildUrl(string currency, DateOnly fromDate, DateOnly toDate)
    {
        var from = fromDate.ToString("MM-dd-yyyy", CultureInfo.InvariantCulture);
        var to = toDate.ToString("MM-dd-yyyy", CultureInfo.InvariantCulture);
        return "CotacaoMoedaPeriodo(moeda=@moeda,dataInicial=@dataInicial,dataFinalCotacao=@dataFinalCotacao)"
            + $"?@moeda='{currency}'&@dataInicial='{from}'&@dataFinalCotacao='{to}'"
            + "&$format=json&$select=cotacaoCompra,cotacaoVenda,dataHoraCotacao,tipoBoletim";
    }

    /// <summary>Validated bulletins, or <c>null</c> when the body does not match the documented schema.</summary>
    internal static IReadOnlyList<ExchangeRateBulletin>? Parse(string? body, string currency, DateOnly fromDate, DateOnly toDate)
    {
        if (!ProviderJson.TryParseObject(body, out var document))
            return null;

        using (document)
        {
            if (!document!.RootElement.TryGetProperty("value", out var rows) || rows.ValueKind != JsonValueKind.Array)
                return null;

            var bulletins = new List<ExchangeRateBulletin>();
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object
                    || !TryRate(row, "cotacaoCompra", out var buy)
                    || !TryRate(row, "cotacaoVenda", out var sell)
                    || !row.TryGetProperty("dataHoraCotacao", out var instant) || instant.ValueKind != JsonValueKind.String
                    || !DateTime.TryParseExact(instant.GetString(), ["yyyy-MM-dd HH:mm:ss.FFF", "yyyy-MM-dd HH:mm:ss"],
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var quotedAt))
                    return null;

                var bulletinType = row.TryGetProperty("tipoBoletim", out var type) && type.ValueKind == JsonValueKind.String
                    ? type.GetString()?.Trim()
                    : null;
                if (string.IsNullOrEmpty(bulletinType) || bulletinType.Length > 40)
                    return null;

                var quoteDate = DateOnly.FromDateTime(quotedAt);
                if (quoteDate < fromDate || quoteDate > toDate)
                    return null;

                bulletins.Add(new ExchangeRateBulletin(
                    currency, quoteDate, buy, sell, bulletinType,
                    bulletinType.StartsWith("Fechamento", StringComparison.OrdinalIgnoreCase)));
            }

            return bulletins;
        }
    }

    private static bool TryRate(JsonElement row, string property, out decimal rate)
    {
        rate = 0;
        return row.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDecimal(out rate)
            && rate > 0;
    }
}
