using System;
using System.Collections.Generic;

namespace Template.Domain.Common;

/// <summary>
/// Shared calculation boundaries (docs/product/business-rules.md CALC-001..003, Phase 27). Monetary results are rounded
/// to the currency minor units with <see cref="MidpointRounding.AwayFromZero"/> only at the boundaries named here;
/// unit prices keep up to 4 decimals and quantities up to 3. The server always recomputes totals (CALC-002).
/// </summary>
public static class CalculationRules
{
    public const int MaxQuantityDecimals = 3;
    public const int MaxUnitPriceDecimals = 4;
    public const int MaxExchangeRateDecimals = 6;

    /// <summary>Largest monetary amount stored by the product (precision 18, scale 4 columns).</summary>
    public const decimal MaxAmount = 99_999_999_999_999m;

    /// <summary>CALC-001: minor units of the seeded currencies (BRL, USD, EUR: 2); unknown ISO codes default to 2.</summary>
    public static int MinorUnits(string currency) => 2;

    /// <summary>CALC-001: rounds a monetary result to the currency minor units, midpoint away from zero.</summary>
    public static decimal RoundMoney(decimal amount, string currency) =>
        Math.Round(amount, MinorUnits(currency), MidpointRounding.AwayFromZero);

    /// <summary>CALC-002: line total = round(quantity × unit price).</summary>
    public static Money LineTotal(decimal quantity, Money unitPrice) =>
        new(RoundMoney(quantity * unitPrice.Amount, unitPrice.Currency), unitPrice.Currency);

    /// <summary>Whether a monetary amount has at most the currency minor units (an allocation or reservation amount).</summary>
    public static bool HasMinorUnitPrecision(decimal amount, string currency) =>
        DecimalPrecision.Places(amount) <= MinorUnits(currency);

    /// <summary>CALC-001: an exchange rate is positive with at most 6 decimals.</summary>
    public static bool IsValidExchangeRate(decimal rate) =>
        rate > 0 && rate <= MaxAmount && DecimalPrecision.Places(rate) <= MaxExchangeRateDecimals;

    /// <summary>
    /// CALC-004 (Phase 29): normalized = round(amount × rate) in <paramref name="baseCurrency"/>, where the rate is base
    /// units per foreign unit from a persisted snapshot. The same currency uses rate 1 and needs no snapshot.
    /// </summary>
    public static Money Normalize(Money amount, decimal rate, string baseCurrency)
    {
        if (string.Equals(amount.Currency, baseCurrency, StringComparison.Ordinal))
            return amount;
        if (!IsValidExchangeRate(rate))
            throw new ArgumentOutOfRangeException(nameof(rate), "Exchange rates are positive with at most 6 decimals.");
        return new Money(RoundMoney(amount.Amount * rate, baseCurrency), baseCurrency);
    }

    /// <summary>
    /// CALC-007 (Phase 29): award amount in the requisition currency. Equal to the awarded total when the currencies
    /// match; otherwise round(round(awarded × rate quote→base) ÷ rate requisition→base), base→base being 1.
    /// </summary>
    public static Money ToRequisitionCurrency(Money awarded, decimal quoteToBaseRate, string requisitionCurrency, decimal requisitionToBaseRate, string baseCurrency)
    {
        if (string.Equals(awarded.Currency, requisitionCurrency, StringComparison.Ordinal))
            return awarded;
        var inBase = Normalize(awarded, quoteToBaseRate, baseCurrency);
        if (string.Equals(requisitionCurrency, baseCurrency, StringComparison.Ordinal))
            return inBase;
        if (!IsValidExchangeRate(requisitionToBaseRate))
            throw new ArgumentOutOfRangeException(nameof(requisitionToBaseRate), "Exchange rates are positive with at most 6 decimals.");
        return new Money(RoundMoney(inBase.Amount / requisitionToBaseRate, requisitionCurrency), requisitionCurrency);
    }

    /// <summary>
    /// CALC-009 (Phase 32): receipt cap per line = ordered × (1 + tolerance % ÷ 100), truncated to the unit's allowed
    /// decimals (at most 3). Tolerance 0 % (<c>ReceivingPolicy</c> v1, D23-18) makes the cap the ordered quantity.
    /// </summary>
    public static decimal ReceiptCap(decimal ordered, decimal tolerancePercent, int unitDecimals)
    {
        if (ordered <= 0)
            throw new ArgumentOutOfRangeException(nameof(ordered), "The ordered quantity is positive.");
        if (tolerancePercent < 0)
            throw new ArgumentOutOfRangeException(nameof(tolerancePercent), "The tolerance is not negative.");
        var decimals = Math.Clamp(unitDecimals, 0, MaxQuantityDecimals);
        var factor = (decimal)Math.Pow(10, decimals);
        return Math.Truncate(ordered * (1 + tolerancePercent / 100m) * factor) / factor;
    }

    /// <summary>
    /// CALC-008 (Phase 32): short-close release = round(Σ (ordered − accepted) × unit price) in the PO currency, converted
    /// to the requisition currency with the award rates (CALC-007) and capped at the committed amount.
    /// </summary>
    public static Money ShortCloseRelease(
        IEnumerable<(decimal Ordered, decimal Accepted, decimal UnitPrice)> lines, string orderCurrency, decimal quoteToBaseRate,
        string requisitionCurrency, decimal requisitionToBaseRate, string baseCurrency, decimal committedAmount)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var unreceived = 0m;
        foreach (var (ordered, accepted, unitPrice) in lines)
            unreceived += Math.Max(0m, ordered - accepted) * unitPrice;
        var inOrderCurrency = new Money(RoundMoney(unreceived, orderCurrency), orderCurrency);
        var converted = ToRequisitionCurrency(inOrderCurrency, quoteToBaseRate, requisitionCurrency, requisitionToBaseRate, baseCurrency);
        return converted.Amount > committedAmount ? new Money(committedAmount, requisitionCurrency) : converted;
    }
}

/// <summary>
/// Amount in one currency (domain-model.md §2). Arithmetic and comparison require the same currency (CALC-001); a
/// mismatch inside Domain is a programming error and throws.
/// </summary>
public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money Zero(string currency) => new(0m, currency);

    public Money Add(Money other) => new(Amount + SameCurrency(other).Amount, Currency);

    public Money Subtract(Money other) => new(Amount - SameCurrency(other).Amount, Currency);

    public int CompareTo(Money other) => Amount.CompareTo(SameCurrency(other).Amount);

    private Money SameCurrency(Money other) =>
        string.Equals(Currency, other.Currency, StringComparison.Ordinal)
            ? other
            : throw new InvalidOperationException($"Money operations require equal currency ({Currency} vs {other.Currency}).");
}
