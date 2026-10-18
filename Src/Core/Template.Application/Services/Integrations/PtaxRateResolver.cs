#nullable enable
using Template.Application.Interfaces.Integrations;
using Template.Domain.Common;

namespace Template.Application.Services.Integrations;

/// <summary>Provider names recorded in enrichment snapshots and rate provenance.</summary>
public static class EnrichmentProviders
{
    public const string BrasilApi = "BrasilAPI";
    public const string BcbPtax = "BcbPtax";

    public static IReadOnlyList<string> SupplierEnrichment { get; } = [BrasilApi];
}

/// <summary>Resolved PTAX selling rate: BRL per one unit of <see cref="Currency"/> (CALC-004 input).</summary>
public sealed record PtaxSellingRate(string Currency, decimal Rate, DateOnly QuoteDate, DateTime RetrievedAtUtc)
{
    public const string BaseCurrency = "BRL";
    public string Source => EnrichmentProviders.BcbPtax;
}

/// <summary>
/// Spike SP-5: the selling rate of the closing PTAX bulletin of the latest business day on or before the reference
/// local date, looking back at most <see cref="MaxLookbackDays"/> days in one provider request. No bulletin in the
/// window yields <see cref="EnrichmentOutcome.NotFound"/>; the caller then marks the comparison incomplete (SRC-013)
/// and never guesses a rate. Rates must be positive with at most 6 decimals (CALC-001).
/// </summary>
public sealed class PtaxRateResolver(IExchangeRateProvider provider)
{
    public const int MaxLookbackDays = 7;
    public const int MaxRateDecimals = 6;

    public async Task<EnrichmentResult<PtaxSellingRate>> ResolveSellingRateAsync(
        string currency, DateOnly referenceDate, CancellationToken cancellationToken)
    {
        var code = CurrencyCodes.Normalize(currency);
        if (code is null || code == PtaxSellingRate.BaseCurrency)
            return EnrichmentResult<PtaxSellingRate>.Failed(EnrichmentOutcome.Rejected, EnrichmentProviders.BcbPtax, "unsupported-currency");

        var bulletins = await provider.GetPtaxBulletinsAsync(code, referenceDate.AddDays(-MaxLookbackDays), referenceDate, cancellationToken);
        if (bulletins.Outcome != EnrichmentOutcome.Found)
            return EnrichmentResult<PtaxSellingRate>.Failed(bulletins.Outcome, bulletins.Provider, bulletins.Reason ?? "provider-failure");

        var closing = bulletins.Value!
            .Where(bulletin => bulletin.IsClosing && bulletin.Currency == code && bulletin.QuoteDate <= referenceDate)
            .OrderByDescending(bulletin => bulletin.QuoteDate)
            .FirstOrDefault();
        if (closing is null)
            return EnrichmentResult<PtaxSellingRate>.Failed(EnrichmentOutcome.NotFound, bulletins.Provider, "no-closing-bulletin");
        if (closing.SellRate <= 0 || DecimalPrecision.Places(closing.SellRate) > MaxRateDecimals)
            return EnrichmentResult<PtaxSellingRate>.Failed(EnrichmentOutcome.Invalid, bulletins.Provider, "rate-out-of-range");

        return EnrichmentResult<PtaxSellingRate>.Found(
            bulletins.Provider,
            bulletins.RetrievedAtUtc!.Value,
            new PtaxSellingRate(code, closing.SellRate, closing.QuoteDate, bulletins.RetrievedAtUtc.Value));
    }
}
