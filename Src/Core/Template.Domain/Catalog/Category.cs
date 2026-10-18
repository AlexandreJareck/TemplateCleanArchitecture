using System;
using Template.Domain.Common;

namespace Template.Domain.Catalog;

/// <summary>
/// Controlled classification of catalog items and suppliers. Code unique and immutable (CAT-001); deactivation keeps
/// history and blocks new references (CAT-002); never hard-deleted (ORG-005).
/// </summary>
public sealed class Category : VersionedAggregateRoot
{
    private Category()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public string Code { get; private set; }
    public string Name { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static (Category? Category, DomainError? Error) Create(Guid id, string? code, string? name, Guid? createdBy, DateTime nowUtc)
    {
        MasterDataRules.EnsureId(id, nameof(id));
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        var normalizedCode = MasterDataRules.NormalizeCode(code);
        if (!MasterDataRules.IsCatalogCode(normalizedCode))
            return (null, CatalogErrors.InvalidCode(MasterDataRules.CatalogCodePattern));
        var normalizedName = MasterDataRules.NormalizeName(name);
        if (normalizedName is null)
            return (null, CatalogErrors.InvalidName);

        return (new Category
        {
            Id = id,
            Code = normalizedCode!,
            Name = normalizedName,
            IsActive = true,
            CreatedAtUtc = nowUtc,
            CreatedBy = createdBy,
            UpdatedAtUtc = nowUtc
        }, null);
    }

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

    public DomainError? Deactivate(string? reason, DateTime nowUtc) => SetActive(false, reason, nowUtc);

    public DomainError? Reactivate(DateTime nowUtc) => SetActive(true, "reactivation", nowUtc);

    /// <summary>CAT-002 guard for new references.</summary>
    public DomainError? EnsureUsable() => IsActive ? null : CatalogErrors.Inactive("category");

    private DomainError? SetActive(bool active, string? reason, DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (!active && MasterDataRules.NormalizeReason(reason) is null)
            return DomainError.ReasonRequired("CAT-002", MasterDataRules.ReasonMinLength, MasterDataRules.ReasonMaxLength);
        if (IsActive == active)
            return DomainError.InvalidTransition(IsActive ? "Active" : "Inactive", "The category is already in that state.", "CAT-002");

        IsActive = active;
        Touch(nowUtc);
        return null;
    }

    private void Touch(DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        IncrementVersion();
    }
}
