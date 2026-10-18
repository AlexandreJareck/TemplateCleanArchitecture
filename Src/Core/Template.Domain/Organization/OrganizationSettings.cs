using System;
using System.Collections.Generic;
using System.Linq;
using Template.Domain.Common;

namespace Template.Domain.Organization;

/// <summary>
/// Installation-wide singleton (D23-1, ADR-0006): display name, base currency (BRL), organization time zone
/// (<c>America/Sao_Paulo</c>, B23-5) and supported currencies. Base currency and time zone are locked once the first
/// budget allocation exists (ORG-004).
/// </summary>
public sealed class OrganizationSettings : VersionedAggregateRoot
{
    /// <summary>Fixed identifier of the singleton row.</summary>
    public static readonly Guid SingletonId = Guid.Parse("6f0d6b0e-25a0-4c25-9d0e-000000000001");

    public const string DefaultDisplayName = "Procurement Hub";
    public const string DefaultBaseCurrency = "BRL";
    public const string DefaultTimeZone = "America/Sao_Paulo";
    public const int TimeZoneMaxLength = 64;

    private OrganizationSettings()
    {
        DisplayName = string.Empty;
        BaseCurrency = string.Empty;
        TimeZone = string.Empty;
        SupportedCurrencies = [];
    }

    public string DisplayName { get; private set; }
    public string BaseCurrency { get; private set; }
    public string TimeZone { get; private set; }
    public string[] SupportedCurrencies { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Seed values of policy version 1 (BRL, America/Sao_Paulo, BRL/USD/EUR).</summary>
    public static OrganizationSettings CreateDefault(DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        return new OrganizationSettings
        {
            Id = SingletonId,
            DisplayName = DefaultDisplayName,
            BaseCurrency = DefaultBaseCurrency,
            TimeZone = DefaultTimeZone,
            SupportedCurrencies = ["BRL", "EUR", "USD"],
            UpdatedAtUtc = nowUtc
        };
    }

    /// <summary>
    /// Applies new settings. <paramref name="isTimeZoneKnown"/> is the result of resolving the IANA identifier
    /// (done by the caller, which owns the platform lookup); <paramref name="hasBudgetAllocations"/> locks base
    /// currency and time zone (ORG-004).
    /// </summary>
    public DomainError? Change(
        string? displayName,
        string? baseCurrency,
        string? timeZone,
        IEnumerable<string>? supportedCurrencies,
        bool isTimeZoneKnown,
        bool hasBudgetAllocations,
        DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        var name = MasterDataRules.NormalizeName(displayName);
        if (name is null)
            return new DomainError("validation", DomainErrorKind.Validation, "The display name must have 1 to 200 characters.", Field: "displayName");
        var currency = CurrencyCodes.Normalize(baseCurrency);
        if (currency is null)
            return new DomainError("validation", DomainErrorKind.Validation, "The base currency must be a 3-letter ISO 4217 code.", Field: "baseCurrency");
        var zone = timeZone?.Trim();
        if (string.IsNullOrEmpty(zone) || zone.Length > TimeZoneMaxLength || !isTimeZoneKnown)
            return new DomainError("validation", DomainErrorKind.Validation, "The time zone must be a known IANA identifier.", Field: "timeZone");

        var currencies = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var value in supportedCurrencies ?? [])
        {
            var normalized = CurrencyCodes.Normalize(value);
            if (normalized is null)
                return new DomainError("validation", DomainErrorKind.Validation, "Every supported currency must be a 3-letter ISO 4217 code.", Field: "supportedCurrencies");
            currencies.Add(normalized);
        }

        if (!currencies.Contains(currency))
            return new DomainError("validation", DomainErrorKind.Validation, "The supported currencies must include the base currency.", Field: "supportedCurrencies");

        var lockedFieldChanges = currency != BaseCurrency || !string.Equals(zone, TimeZone, StringComparison.Ordinal);
        if (lockedFieldChanges && hasBudgetAllocations)
            return OrganizationErrors.SettingsLocked;

        var newCurrencies = currencies.ToArray();
        if (name == DisplayName && !lockedFieldChanges && newCurrencies.SequenceEqual(SupportedCurrencies))
            return null;

        DisplayName = name;
        BaseCurrency = currency;
        TimeZone = zone;
        SupportedCurrencies = newCurrencies;
        UpdatedAtUtc = nowUtc;
        IncrementVersion();
        return null;
    }
}
