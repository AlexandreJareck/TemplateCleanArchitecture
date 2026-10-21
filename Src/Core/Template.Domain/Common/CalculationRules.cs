using System;

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
