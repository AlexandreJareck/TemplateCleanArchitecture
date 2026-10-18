using System;

namespace Template.Domain.Common;

/// <summary>Decimal-precision helper shared by quantity (CAT-003), price (CAT-004) and rate (CALC-001) checks.</summary>
public static class DecimalPrecision
{
    /// <summary>Significant decimal places of a value (trailing zeros ignored).</summary>
    public static int Places(decimal value)
    {
        value = Math.Abs(value);
        var places = 0;
        while (value != decimal.Truncate(value))
        {
            value *= 10;
            places++;
        }

        return places;
    }
}
