#nullable enable
using Template.Application.Common.Concurrency;
using Template.Application.Common.Results;
using Template.Application.Wrappers;
using Template.Domain.Organization;

namespace Template.Application.Features.Organization;

/// <summary>Representation of a department; <see cref="Version"/> equals the strong ETag value.</summary>
public sealed record DepartmentView(
    Guid Id, string Code, string Name, Guid? HeadUserId, bool IsActive, DateTime CreatedAt, DateTime UpdatedAt, string Version)
{
    public static DepartmentView From(Department department) => new(
        department.Id, department.Code, department.Name, department.HeadUserId, department.IsActive,
        department.CreatedAtUtc, department.UpdatedAtUtc, VersionPrecondition.Format(department.Version));
}

public sealed record CostCenterView(
    Guid Id, string Code, string Name, Guid DepartmentId, Guid? ManagerUserId, bool IsActive, DateTime CreatedAt, DateTime UpdatedAt, string Version)
{
    public static CostCenterView From(CostCenter costCenter) => new(
        costCenter.Id, costCenter.Code, costCenter.Name, costCenter.DepartmentId, costCenter.ManagerUserId, costCenter.IsActive,
        costCenter.CreatedAtUtc, costCenter.UpdatedAtUtc, VersionPrecondition.Format(costCenter.Version));
}

public sealed record OrganizationSettingsView(
    string DisplayName, string BaseCurrency, string TimeZone, IReadOnlyList<string> SupportedCurrencies, DateTime UpdatedAt, string Version)
{
    public static OrganizationSettingsView From(OrganizationSettings settings) => new(
        settings.DisplayName, settings.BaseCurrency, settings.TimeZone, settings.SupportedCurrencies, settings.UpdatedAtUtc,
        VersionPrecondition.Format(settings.Version));
}

/// <summary>Audit actions and failures of the Organization module (ORG-*).</summary>
public static class OrganizationAudit
{
    public const string DepartmentResource = "department";
    public const string CostCenterResource = "cost-center";
    public const string SettingsResource = "organization-settings";

    public const string DepartmentCreated = "organization.department.created";
    public const string DepartmentUpdated = "organization.department.updated";
    public const string DepartmentDeactivated = "organization.department.deactivated";
    public const string DepartmentReactivated = "organization.department.reactivated";
    public const string CostCenterCreated = "organization.cost-center.created";
    public const string CostCenterUpdated = "organization.cost-center.updated";
    public const string CostCenterDeactivated = "organization.cost-center.deactivated";
    public const string CostCenterReactivated = "organization.cost-center.reactivated";
    public const string SettingsChanged = "organization.settings.changed";

    public static readonly ApplicationError CodeDuplicate = new(
        ProblemCodes.OrganizationCodeDuplicate, ErrorCategory.Conflict, "Another record already uses this code.", "ORG-001");

    public static ApplicationError NotFound(string resource) =>
        new(ProblemCodes.NotFound, ErrorCategory.NotFound, $"The {resource} was not found.");

    /// <summary>ORG-006: managers and heads must be existing, active users.</summary>
    public static ApplicationError InactiveUser(string field) =>
        new(ProblemCodes.Validation, ErrorCategory.Validation, "The user does not exist or is not active.", "ORG-006",
            FieldErrors: new Dictionary<string, string[]> { [field] = ["The user does not exist or is not active."] });

    public static string State(bool isActive) => isActive ? "Active" : "Inactive";
}
