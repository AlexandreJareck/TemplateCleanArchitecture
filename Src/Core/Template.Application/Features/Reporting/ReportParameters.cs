#nullable enable
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Template.Domain.Reporting;

namespace Template.Application.Features.Reporting;

/// <summary>
/// Validated parameter snapshot of a report job (RPT-001, RPT-003). User-supplied filters plus server-pinned source
/// versions (<see cref="RequisitionVersion"/>, <see cref="ComparisonSequence"/>) captured at request time, so every
/// attempt renders the same content. Serialized canonically (ordinal key order, invariant formats, unset values
/// omitted); the fingerprint is the SHA-256 of the type and that JSON.
/// </summary>
public sealed record ReportParameters(
    Guid? RequisitionId = null,
    long? RequisitionVersion = null,
    Guid? RfqId = null,
    int? ComparisonSequence = null,
    Guid? FiscalPeriodId = null,
    Guid? CostCenterId = null,
    string? Currency = null,
    DateOnly? From = null,
    DateOnly? To = null,
    Guid? SupplierId = null,
    Guid? PurchaseOrderId = null)
{
    /// <summary>Longest local-date window of the supplier quotes report.</summary>
    public const int MaxWindowDays = 366;

    public string ToCanonicalJson()
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        Add(values, "comparisonSequence", ComparisonSequence?.ToString(CultureInfo.InvariantCulture));
        Add(values, "costCenterId", CostCenterId?.ToString("D"));
        Add(values, "currency", Currency);
        Add(values, "fiscalPeriodId", FiscalPeriodId?.ToString("D"));
        Add(values, "from", From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(values, "purchaseOrderId", PurchaseOrderId?.ToString("D"));
        Add(values, "requisitionId", RequisitionId?.ToString("D"));
        Add(values, "requisitionVersion", RequisitionVersion?.ToString(CultureInfo.InvariantCulture));
        Add(values, "rfqId", RfqId?.ToString("D"));
        Add(values, "supplierId", SupplierId?.ToString("D"));
        Add(values, "to", To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return JsonSerializer.Serialize(values);
    }

    /// <summary>Non-secret content fingerprint (no key material), computed like the FIL-002 file digests.</summary>
    public static string Fingerprint(ReportType type, string canonicalJson)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{type}\n{canonicalJson}"));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>Reads a stored snapshot back; unknown keys are rejected (the snapshot is server-written).</summary>
    public static ReportParameters Parse(string canonicalJson)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(canonicalJson)
            ?? throw new JsonException("The report parameter snapshot is empty.");
        var parameters = new ReportParameters();
        foreach (var (key, value) in values)
        {
            parameters = key switch
            {
                "comparisonSequence" => parameters with { ComparisonSequence = int.Parse(value, CultureInfo.InvariantCulture) },
                "costCenterId" => parameters with { CostCenterId = Guid.Parse(value) },
                "currency" => parameters with { Currency = value },
                "fiscalPeriodId" => parameters with { FiscalPeriodId = Guid.Parse(value) },
                "from" => parameters with { From = DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture) },
                "purchaseOrderId" => parameters with { PurchaseOrderId = Guid.Parse(value) },
                "requisitionId" => parameters with { RequisitionId = Guid.Parse(value) },
                "requisitionVersion" => parameters with { RequisitionVersion = long.Parse(value, CultureInfo.InvariantCulture) },
                "rfqId" => parameters with { RfqId = Guid.Parse(value) },
                "supplierId" => parameters with { SupplierId = Guid.Parse(value) },
                "to" => parameters with { To = DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture) },
                _ => throw new JsonException("The report parameter snapshot has an unknown key.")
            };
        }

        return parameters;
    }

    private static void Add(SortedDictionary<string, string> values, string key, string? value)
    {
        if (value is not null)
            values[key] = value;
    }
}
