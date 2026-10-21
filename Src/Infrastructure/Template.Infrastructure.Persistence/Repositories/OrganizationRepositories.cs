using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Template.Application.Common.Paging;
using Template.Application.Interfaces.Organization;
using Template.Domain.Organization;
using Template.Infrastructure.Persistence.Contexts;

namespace Template.Infrastructure.Persistence.Repositories;

public sealed class DepartmentRepository(ApplicationDbContext context) : IDepartmentRepository
{
    private static readonly Dictionary<string, Expression<Func<Department, object>>> SortKeys = new(StringComparer.Ordinal)
    {
        ["code"] = department => department.Code,
        ["name"] = department => department.Name,
        ["createdAt"] = department => department.CreatedAtUtc,
        ["updatedAt"] = department => department.UpdatedAtUtc
    };

    public void Add(Department department) => context.Add(department);

    public Task<Department?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<Department>().SingleOrDefaultAsync(department => department.Id == id, cancellationToken);

    public Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<Department>().AsNoTracking().Where(department => department.Id == id)
            .Select(department => (long?)department.Version).SingleOrDefaultAsync(cancellationToken);

    public Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken) =>
        context.Set<Department>().AsNoTracking().AnyAsync(department => department.Code == normalizedCode, cancellationToken);

    public Task<PagedResult<Department>> ListAsync(DepartmentFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = context.Set<Department>().AsNoTracking();
        if (MasterDataQueryExtensions.SearchTerm(filter.Q) is { } term)
            query = query.Where(department => department.Code.Contains(term) || department.Name.ToUpper().Contains(term));
        if (filter.IsActive is { } isActive)
            query = query.Where(department => department.IsActive == isActive);
        return query.ToPagedAsync(page, SortKeys, department => department.Id, cancellationToken);
    }
}

public sealed class CostCenterRepository(ApplicationDbContext context) : ICostCenterRepository
{
    private static readonly Dictionary<string, Expression<Func<CostCenter, object>>> SortKeys = new(StringComparer.Ordinal)
    {
        ["code"] = costCenter => costCenter.Code,
        ["name"] = costCenter => costCenter.Name,
        ["createdAt"] = costCenter => costCenter.CreatedAtUtc,
        ["updatedAt"] = costCenter => costCenter.UpdatedAtUtc
    };

    public void Add(CostCenter costCenter) => context.Add(costCenter);

    public Task<CostCenter?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<CostCenter>().SingleOrDefaultAsync(costCenter => costCenter.Id == id, cancellationToken);

    public Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<CostCenter>().AsNoTracking().Where(costCenter => costCenter.Id == id)
            .Select(costCenter => (long?)costCenter.Version).SingleOrDefaultAsync(cancellationToken);

    public Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken) =>
        context.Set<CostCenter>().AsNoTracking().AnyAsync(costCenter => costCenter.Code == normalizedCode, cancellationToken);

    public Task<bool> AnyActiveInDepartmentAsync(Guid departmentId, CancellationToken cancellationToken) =>
        context.Set<CostCenter>().AsNoTracking().AnyAsync(costCenter => costCenter.DepartmentId == departmentId && costCenter.IsActive, cancellationToken);

    public Task<PagedResult<CostCenter>> ListAsync(CostCenterFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = context.Set<CostCenter>().AsNoTracking();
        if (MasterDataQueryExtensions.SearchTerm(filter.Q) is { } term)
            query = query.Where(costCenter => costCenter.Code.Contains(term) || costCenter.Name.ToUpper().Contains(term));
        if (filter.IsActive is { } isActive)
            query = query.Where(costCenter => costCenter.IsActive == isActive);
        if (filter.DepartmentId is { } departmentId)
            query = query.Where(costCenter => costCenter.DepartmentId == departmentId);
        if (filter.ManagerUserId is { } managerUserId)
            query = query.Where(costCenter => costCenter.ManagerUserId == managerUserId);
        return query.ToPagedAsync(page, SortKeys, costCenter => costCenter.Id, cancellationToken);
    }
}

public sealed class OrganizationSettingsRepository(ApplicationDbContext context) : IOrganizationSettingsRepository
{
    public Task<OrganizationSettings?> GetAsync(CancellationToken cancellationToken) =>
        context.Set<OrganizationSettings>().SingleOrDefaultAsync(settings => settings.Id == OrganizationSettings.SingletonId, cancellationToken);
}
