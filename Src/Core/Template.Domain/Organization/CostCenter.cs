using System;
using Template.Domain.Common;

namespace Template.Domain.Organization;

/// <summary>
/// Accounting unit owned by exactly one department, fixed at creation (ORG-002). The code is unique and immutable
/// (ORG-001). An inactive cost center keeps history and rejects new requisitions, allocations and manager
/// assignments (ORG-003). Never hard-deleted (ORG-005).
/// </summary>
public sealed class CostCenter : VersionedAggregateRoot
{
    private CostCenter()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public string Code { get; private set; }
    public string Name { get; private set; }

    /// <summary>ORG-002: there is no setter path after creation.</summary>
    public Guid DepartmentId { get; private set; }

    public Guid? ManagerUserId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Creates an active cost center inside an active department (ORG-002, ORG-003).</summary>
    public static (CostCenter? CostCenter, DomainError? Error) Create(
        Guid id, string? code, string? name, Department department, Guid? managerUserId, Guid? createdBy, DateTime nowUtc)
    {
        MasterDataRules.EnsureId(id, nameof(id));
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentNullException.ThrowIfNull(department);
        var normalizedCode = MasterDataRules.NormalizeCode(code);
        if (!MasterDataRules.IsOrganizationCode(normalizedCode))
            return (null, OrganizationErrors.InvalidCode);
        var normalizedName = MasterDataRules.NormalizeName(name);
        if (normalizedName is null)
            return (null, OrganizationErrors.InvalidName);
        if (!department.IsActive)
            return (null, OrganizationErrors.Inactive("department"));

        return (new CostCenter
        {
            Id = id,
            Code = normalizedCode!,
            Name = normalizedName,
            DepartmentId = department.Id,
            ManagerUserId = managerUserId == Guid.Empty ? null : managerUserId,
            IsActive = true,
            CreatedAtUtc = nowUtc,
            CreatedBy = createdBy,
            UpdatedAtUtc = nowUtc
        }, null);
    }

    /// <summary>Changes the name and manager; an inactive cost center rejects a new manager (ORG-003).</summary>
    public DomainError? Update(string? name, Guid? managerUserId, DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        var normalizedName = MasterDataRules.NormalizeName(name);
        if (normalizedName is null)
            return OrganizationErrors.InvalidName;
        var manager = managerUserId == Guid.Empty ? null : managerUserId;
        if (!IsActive && manager is not null && manager != ManagerUserId)
            return OrganizationErrors.Inactive("cost center");

        if (normalizedName == Name && manager == ManagerUserId)
            return null;

        Name = normalizedName;
        ManagerUserId = manager;
        Touch(nowUtc);
        return null;
    }

    public DomainError? Deactivate(string? reason, DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (MasterDataRules.NormalizeReason(reason) is null)
            return DomainError.ReasonRequired("ORG-003", MasterDataRules.ReasonMinLength, MasterDataRules.ReasonMaxLength);
        if (!IsActive)
            return DomainError.InvalidTransition("Inactive", "The cost center is already inactive.", "ORG-003");

        IsActive = false;
        Touch(nowUtc);
        return null;
    }

    /// <summary>Reactivation requires the owning department to be active (ORG-003).</summary>
    public DomainError? Reactivate(Department department, DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentNullException.ThrowIfNull(department);
        if (department.Id != DepartmentId)
            throw new ArgumentException("The department does not own this cost center.", nameof(department));
        if (IsActive)
            return DomainError.InvalidTransition("Active", "The cost center is already active.", "ORG-003");
        if (!department.IsActive)
            return OrganizationErrors.Inactive("department");

        IsActive = true;
        Touch(nowUtc);
        return null;
    }

    /// <summary>ORG-003 guard for later consumers (requisitions, allocations).</summary>
    public DomainError? EnsureUsable() => IsActive ? null : OrganizationErrors.Inactive("cost center");

    private void Touch(DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        IncrementVersion();
    }
}
