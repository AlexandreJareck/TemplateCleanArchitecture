using System;
using Template.Domain.Common;

namespace Template.Domain.Organization;

/// <summary>
/// Organizational unit (docs/product/domain-model.md §3). The code is unique, immutable and upper-case (ORG-001);
/// deactivation keeps history and is allowed only when every cost center of the department is inactive (ORG-003).
/// Departments are never hard-deleted (ORG-005).
/// </summary>
public sealed class Department : VersionedAggregateRoot
{
    private Department()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public string Code { get; private set; }
    public string Name { get; private set; }
    public Guid? HeadUserId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Creates an active department. <paramref name="code"/> is normalized; an invalid code or name is a
    /// validation error. The head user must already be known to be active (ORG-006, checked by the use case).
    /// </summary>
    public static (Department? Department, DomainError? Error) Create(
        Guid id, string? code, string? name, Guid? headUserId, Guid? createdBy, DateTime nowUtc)
    {
        MasterDataRules.EnsureId(id, nameof(id));
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        var normalizedCode = MasterDataRules.NormalizeCode(code);
        if (!MasterDataRules.IsOrganizationCode(normalizedCode))
            return (null, OrganizationErrors.InvalidCode);
        var normalizedName = MasterDataRules.NormalizeName(name);
        if (normalizedName is null)
            return (null, OrganizationErrors.InvalidName);

        return (new Department
        {
            Id = id,
            Code = normalizedCode!,
            Name = normalizedName,
            HeadUserId = headUserId == Guid.Empty ? null : headUserId,
            IsActive = true,
            CreatedAtUtc = nowUtc,
            CreatedBy = createdBy,
            UpdatedAtUtc = nowUtc
        }, null);
    }

    /// <summary>Changes the name and head. The code never changes (ORG-001); an inactive department rejects a new head (ORG-003).</summary>
    public DomainError? Update(string? name, Guid? headUserId, DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        var normalizedName = MasterDataRules.NormalizeName(name);
        if (normalizedName is null)
            return OrganizationErrors.InvalidName;
        var head = headUserId == Guid.Empty ? null : headUserId;
        if (!IsActive && head is not null && head != HeadUserId)
            return OrganizationErrors.Inactive("department");

        if (normalizedName == Name && head == HeadUserId)
            return null;

        Name = normalizedName;
        HeadUserId = head;
        Touch(nowUtc);
        return null;
    }

    /// <summary>ORG-003: requires a reason and no active cost center in the department.</summary>
    public DomainError? Deactivate(string? reason, bool hasActiveCostCenters, DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (MasterDataRules.NormalizeReason(reason) is null)
            return DomainError.ReasonRequired("ORG-003", MasterDataRules.ReasonMinLength, MasterDataRules.ReasonMaxLength);
        if (!IsActive)
            return DomainError.InvalidTransition("Inactive", "The department is already inactive.", "ORG-003");
        if (hasActiveCostCenters)
            return OrganizationErrors.DepartmentHasActiveCostCenters;

        IsActive = false;
        Touch(nowUtc);
        return null;
    }

    public DomainError? Reactivate(DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (IsActive)
            return DomainError.InvalidTransition("Active", "The department is already active.", "ORG-003");

        IsActive = true;
        Touch(nowUtc);
        return null;
    }

    private void Touch(DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        IncrementVersion();
    }
}
