#nullable enable
using Template.Application.Common.Paging;
using Template.Domain.Organization;

namespace Template.Application.Interfaces.Organization;

/// <summary>Optional list filters; <see cref="Q"/> matches code or name case-insensitively.</summary>
public sealed record DepartmentFilter(string? Q = null, bool? IsActive = null);

public sealed record CostCenterFilter(string? Q = null, bool? IsActive = null, Guid? DepartmentId = null, Guid? ManagerUserId = null);

/// <summary>Aggregate repository of <see cref="Department"/> (Organization module).</summary>
public interface IDepartmentRepository
{
    /// <summary>Name of the unique index on the code; a violation means a concurrent duplicate (ORG-001).</summary>
    public const string CodeUniqueKey = "IX_OrganizationDepartments_Code";

    public static readonly IReadOnlyList<string> SortFields = ["code", "name", "createdAt", "updatedAt"];

    void Add(Department department);

    /// <summary>Tracked aggregate, or <c>null</c>.</summary>
    Task<Department?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken);

    Task<PagedResult<Department>> ListAsync(DepartmentFilter filter, PageRequest page, CancellationToken cancellationToken);
}

/// <summary>Aggregate repository of <see cref="CostCenter"/> (Organization module).</summary>
public interface ICostCenterRepository
{
    public const string CodeUniqueKey = "IX_OrganizationCostCenters_Code";

    public static readonly IReadOnlyList<string> SortFields = ["code", "name", "createdAt", "updatedAt"];

    void Add(CostCenter costCenter);

    Task<CostCenter?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken);

    /// <summary>ORG-003: a department with an active cost center cannot be deactivated.</summary>
    Task<bool> AnyActiveInDepartmentAsync(Guid departmentId, CancellationToken cancellationToken);

    Task<PagedResult<CostCenter>> ListAsync(CostCenterFilter filter, PageRequest page, CancellationToken cancellationToken);
}

/// <summary>The <see cref="OrganizationSettings"/> singleton (D23-1).</summary>
public interface IOrganizationSettingsRepository
{
    /// <summary>Tracked singleton, or <c>null</c> before the seed ran.</summary>
    Task<OrganizationSettings?> GetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// ORG-004 lock source: whether any budget allocation exists. Budgeting arrives in Phase 27; until then the
/// adapter answers from the data that exists (none), and Phase 27 replaces it with the allocation table query.
/// </summary>
public interface IOrganizationSettingsLock
{
    Task<bool> HasBudgetAllocationsAsync(CancellationToken cancellationToken);
}
