using System;
using Template.Domain.Common;

namespace Template.Domain.Catalog;

/// <summary>
/// Unit in which quantities are expressed (for example <c>UN</c>, <c>KG</c>, <c>H</c>). Code unique and immutable
/// (CAT-001). <see cref="AllowedDecimals"/> (0–3) bounds every quantity in this unit (CAT-003) and is fixed at
/// creation so quantities already recorded stay valid.
/// </summary>
public sealed class UnitOfMeasure : VersionedAggregateRoot
{
    public const int MaxAllowedDecimals = 3;

    private UnitOfMeasure()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public string Code { get; private set; }
    public string Name { get; private set; }
    public int AllowedDecimals { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static (UnitOfMeasure? Unit, DomainError? Error) Create(
        Guid id, string? code, string? name, int allowedDecimals, Guid? createdBy, DateTime nowUtc)
    {
        MasterDataRules.EnsureId(id, nameof(id));
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        var normalizedCode = MasterDataRules.NormalizeCode(code);
        if (!MasterDataRules.IsCatalogCode(normalizedCode))
            return (null, CatalogErrors.InvalidCode(MasterDataRules.CatalogCodePattern));
        var normalizedName = MasterDataRules.NormalizeName(name);
        if (normalizedName is null)
            return (null, CatalogErrors.InvalidName);
        if (allowedDecimals is < 0 or > MaxAllowedDecimals)
            return (null, new DomainError("validation", DomainErrorKind.Validation, "Allowed decimals must be between 0 and 3.", "CAT-003", "allowedDecimals"));

        return (new UnitOfMeasure
        {
            Id = id,
            Code = normalizedCode!,
            Name = normalizedName,
            AllowedDecimals = allowedDecimals,
            IsActive = true,
            CreatedAtUtc = nowUtc,
            CreatedBy = createdBy,
            UpdatedAtUtc = nowUtc
        }, null);
    }

    /// <summary>CAT-003: a quantity is valid when positive and within the unit's allowed decimals.</summary>
    public bool Accepts(decimal quantity) => quantity > 0 && DecimalPrecision.Places(quantity) <= AllowedDecimals;

    public DomainError? Rename(string? name, DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        var normalizedName = MasterDataRules.NormalizeName(name);
        if (normalizedName is null)
            return CatalogErrors.InvalidName;
        if (normalizedName == Name)
            return null;

        Name = normalizedName;
        Touch(nowUtc);
        return null;
    }

    public DomainError? Deactivate(string? reason, DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (MasterDataRules.NormalizeReason(reason) is null)
            return DomainError.ReasonRequired("CAT-002", MasterDataRules.ReasonMinLength, MasterDataRules.ReasonMaxLength);
        if (!IsActive)
            return DomainError.InvalidTransition("Inactive", "The unit is already inactive.", "CAT-002");

        IsActive = false;
        Touch(nowUtc);
        return null;
    }

    public DomainError? Reactivate(DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (IsActive)
            return DomainError.InvalidTransition("Active", "The unit is already active.", "CAT-002");

        IsActive = true;
        Touch(nowUtc);
        return null;
    }

    public DomainError? EnsureUsable() => IsActive ? null : CatalogErrors.Inactive("unit of measure");

    private void Touch(DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        IncrementVersion();
    }
}
