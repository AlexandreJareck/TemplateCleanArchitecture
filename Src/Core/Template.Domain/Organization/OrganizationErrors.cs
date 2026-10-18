using Template.Domain.Common;

namespace Template.Domain.Organization;

/// <summary>Stable rejections of the Organization module (ORG-*).</summary>
public static class OrganizationErrors
{
    public const string CodeDuplicateCode = "organization.code-duplicate";
    public const string InactiveCode = "organization.inactive";
    public const string DepartmentHasActiveCostCentersCode = "organization.department-has-active-cost-centers";
    public const string SettingsLockedCode = "organization.settings-locked";

    public static readonly DomainError InvalidCode = new(
        "validation", DomainErrorKind.Validation, "The code must match ^[A-Z0-9-]{2,20}$.", "ORG-001", "code");

    public static readonly DomainError InvalidName = new(
        "validation", DomainErrorKind.Validation, $"The name must have 1 to {MasterDataRules.NameMaxLength} characters.", Field: "name");

    public static readonly DomainError DepartmentHasActiveCostCenters = new(
        DepartmentHasActiveCostCentersCode, DomainErrorKind.Conflict,
        "The department still has active cost centers; deactivate them first.", "ORG-003");

    public static readonly DomainError SettingsLocked = new(
        SettingsLockedCode, DomainErrorKind.Conflict,
        "Base currency and time zone cannot change after the first budget allocation exists.", "ORG-004");

    public static DomainError Inactive(string resource) => new(
        InactiveCode, DomainErrorKind.BusinessRule, $"The {resource} is inactive.", "ORG-003");
}
