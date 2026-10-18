using System;
using Template.Domain.Common;

namespace Template.Domain.Catalog;

public enum CatalogItemKind
{
    Good,
    Service
}

/// <summary>
/// Reusable description of a good or service. Code unique and immutable (CAT-001); new or changed references must
/// point to an active category and unit (CAT-002); deactivation keeps history and blocks new requisition lines.
/// <see cref="ReferencePriceAmount"/> is advisory only (CAT-004): it pre-fills a draft line and is never authoritative.
/// </summary>
public sealed class CatalogItem : VersionedAggregateRoot
{
    public const int DescriptionMaxLength = 500;
    public const int ReferencePriceMaxDecimals = 4;

    private CatalogItem()
    {
        Code = string.Empty;
        Description = string.Empty;
    }

    public string Code { get; private set; }
    public string Description { get; private set; }
    public CatalogItemKind Kind { get; private set; }
    public Guid CategoryId { get; private set; }
    public Guid UnitOfMeasureId { get; private set; }
    public decimal? ReferencePriceAmount { get; private set; }
    public string? ReferencePriceCurrency { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static (CatalogItem? Item, DomainError? Error) Create(
        Guid id,
        string? code,
        string? description,
        CatalogItemKind kind,
        Category category,
        UnitOfMeasure unit,
        decimal? referencePriceAmount,
        string? referencePriceCurrency,
        Guid? createdBy,
        DateTime nowUtc)
    {
        MasterDataRules.EnsureId(id, nameof(id));
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(unit);
        var normalizedCode = MasterDataRules.NormalizeCode(code);
        if (!MasterDataRules.IsCatalogItemCode(normalizedCode))
            return (null, CatalogErrors.InvalidCode(MasterDataRules.CatalogItemCodePattern));

        var item = new CatalogItem
        {
            Id = id,
            Code = normalizedCode!,
            IsActive = true,
            CreatedAtUtc = nowUtc,
            CreatedBy = createdBy,
            UpdatedAtUtc = nowUtc
        };
        var error = item.Apply(description, kind, category, unit, referencePriceAmount, referencePriceCurrency);
        return error is null ? (item, null) : (null, error);
    }

    /// <summary>Updates every mutable field; the code never changes (CAT-001).</summary>
    public DomainError? Update(
        string? description,
        CatalogItemKind kind,
        Category category,
        UnitOfMeasure unit,
        decimal? referencePriceAmount,
        string? referencePriceCurrency,
        DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(unit);
        var before = (Description, Kind, CategoryId, UnitOfMeasureId, ReferencePriceAmount, ReferencePriceCurrency);
        // An unchanged reference to a category or unit that became inactive is kept (history); only a new reference must be active.
        var error = Validate(description, kind, category, unit, referencePriceAmount, referencePriceCurrency,
            requireActiveCategory: category.Id != CategoryId, requireActiveUnit: unit.Id != UnitOfMeasureId);
        if (error is not null)
            return error;

        Assign(description!, kind, category, unit, referencePriceAmount, referencePriceCurrency);
        if (before != (Description, Kind, CategoryId, UnitOfMeasureId, ReferencePriceAmount, ReferencePriceCurrency))
            Touch(nowUtc);
        return null;
    }

    public DomainError? Deactivate(string? reason, DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (MasterDataRules.NormalizeReason(reason) is null)
            return DomainError.ReasonRequired("CAT-002", MasterDataRules.ReasonMinLength, MasterDataRules.ReasonMaxLength);
        if (!IsActive)
            return DomainError.InvalidTransition("Inactive", "The catalog item is already inactive.", "CAT-002");

        IsActive = false;
        Touch(nowUtc);
        return null;
    }

    /// <summary>Reactivation requires the current category and unit to be active (CAT-002).</summary>
    public DomainError? Reactivate(Category category, UnitOfMeasure unit, DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (category.Id != CategoryId || unit.Id != UnitOfMeasureId)
            throw new ArgumentException("The category and unit must be the item's current references.");
        if (IsActive)
            return DomainError.InvalidTransition("Active", "The catalog item is already active.", "CAT-002");
        if ((category.EnsureUsable() ?? unit.EnsureUsable()) is { } inactive)
            return inactive;

        IsActive = true;
        Touch(nowUtc);
        return null;
    }

    /// <summary>CAT-002 guard for new requisition lines (Phase 27).</summary>
    public DomainError? EnsureUsable() => IsActive ? null : CatalogErrors.Inactive("catalog item");

    private DomainError? Apply(
        string? description, CatalogItemKind kind, Category category, UnitOfMeasure unit, decimal? amount, string? currency)
    {
        var error = Validate(description, kind, category, unit, amount, currency, requireActiveCategory: true, requireActiveUnit: true);
        if (error is null)
            Assign(description!, kind, category, unit, amount, currency);
        return error;
    }

    private static DomainError? Validate(
        string? description,
        CatalogItemKind kind,
        Category category,
        UnitOfMeasure unit,
        decimal? amount,
        string? currency,
        bool requireActiveCategory,
        bool requireActiveUnit)
    {
        if (MasterDataRules.NormalizeName(description, DescriptionMaxLength) is null)
            return new DomainError("validation", DomainErrorKind.Validation, $"The description must have 1 to {DescriptionMaxLength} characters.", Field: "description");
        if (!Enum.IsDefined(kind))
            return new DomainError("validation", DomainErrorKind.Validation, "The kind must be Good or Service.", Field: "kind");
        if (amount is not null || currency is not null)
        {
            if (amount is null || amount <= 0 || DecimalPrecision.Places(amount.Value) > ReferencePriceMaxDecimals)
                return new DomainError("validation", DomainErrorKind.Validation, "The reference price must be positive with at most 4 decimals.", "CAT-004", "referencePrice.amount");
            if (CurrencyCodes.Normalize(currency) is null)
                return new DomainError("validation", DomainErrorKind.Validation, "The reference price currency must be a 3-letter ISO 4217 code.", "CAT-004", "referencePrice.currency");
        }

        if (requireActiveCategory && category.EnsureUsable() is { } inactiveCategory)
            return inactiveCategory;
        if (requireActiveUnit && unit.EnsureUsable() is { } inactiveUnit)
            return inactiveUnit;
        return null;
    }

    private void Assign(string description, CatalogItemKind kind, Category category, UnitOfMeasure unit, decimal? amount, string? currency)
    {
        Description = MasterDataRules.NormalizeName(description, DescriptionMaxLength)!;
        Kind = kind;
        CategoryId = category.Id;
        UnitOfMeasureId = unit.Id;
        ReferencePriceAmount = amount;
        ReferencePriceCurrency = amount is null ? null : CurrencyCodes.Normalize(currency);
    }

    private void Touch(DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        IncrementVersion();
    }
}
