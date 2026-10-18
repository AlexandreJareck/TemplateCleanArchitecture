#nullable enable
using FluentValidation;
using MediatR;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Interfaces.Access;
using Template.Application.Interfaces.Organization;
using Template.Application.Services.MasterData;
using Template.Domain.Common;
using Template.Domain.Organization;

namespace Template.Application.Features.Organization;

/// <summary>Creates an active cost center in an active department (ORG-001..003, ORG-006).</summary>
public sealed record CreateCostCenterCommand(string? Code, string? Name, Guid DepartmentId, Guid? ManagerUserId) : IRequest<Result<CostCenterView>>;

/// <summary>Changes name and manager; code and department are fixed (ORG-001, ORG-002).</summary>
public sealed record UpdateCostCenterCommand(Guid Id, long? ExpectedVersion, string? Name, Guid? ManagerUserId) : IRequest<Result<CostCenterView>>;

public sealed record DeactivateCostCenterCommand(Guid Id, long? ExpectedVersion, string? Reason) : IRequest<Result<CostCenterView>>;

public sealed record ReactivateCostCenterCommand(Guid Id, long? ExpectedVersion, string? Reason) : IRequest<Result<CostCenterView>>;

public sealed record GetCostCenterQuery(Guid Id) : IRequest<Result<CostCenterView>>;

public sealed record ListCostCentersQuery(
    int? Page = null, int? PageSize = null, string? Sort = null, string? Q = null, bool? IsActive = null,
    Guid? DepartmentId = null, Guid? ManagerUserId = null) : IRequest<Result<PagedResult<CostCenterView>>>;

public sealed class CreateCostCenterCommandValidator : AbstractValidator<CreateCostCenterCommand>
{
    public CreateCostCenterCommandValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(20);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(MasterDataRules.NameMaxLength);
        RuleFor(command => command.DepartmentId).NotEmpty();
    }
}

public sealed class UpdateCostCenterCommandValidator : AbstractValidator<UpdateCostCenterCommand>
{
    public UpdateCostCenterCommandValidator() =>
        RuleFor(command => command.Name).NotEmpty().MaximumLength(MasterDataRules.NameMaxLength);
}

public sealed class CostCenterCommandHandlers(
    ICostCenterRepository costCenters,
    IDepartmentRepository departments,
    IAccountDirectory accounts,
    MasterDataWriter writer) :
    IRequestHandler<CreateCostCenterCommand, Result<CostCenterView>>,
    IRequestHandler<UpdateCostCenterCommand, Result<CostCenterView>>,
    IRequestHandler<DeactivateCostCenterCommand, Result<CostCenterView>>,
    IRequestHandler<ReactivateCostCenterCommand, Result<CostCenterView>>
{
    public async Task<Result<CostCenterView>> Handle(CreateCostCenterCommand request, CancellationToken cancellationToken)
    {
        var department = await departments.FindAsync(request.DepartmentId, cancellationToken);
        if (department is null)
            return ApplicationError.Validation("departmentId", "The department does not exist.");
        if (request.ManagerUserId is { } manager && manager != Guid.Empty && !await accounts.IsActiveUserAsync(manager, cancellationToken))
            return OrganizationAudit.InactiveUser("managerUserId");

        var (costCenter, error) = CostCenter.Create(
            Guid.NewGuid(), request.Code, request.Name, department, request.ManagerUserId, writer.ActorId, writer.UtcNow);
        if (error is not null)
            return error.ToApplicationError();
        if (await costCenters.CodeExistsAsync(costCenter!.Code, cancellationToken))
            return OrganizationAudit.CodeDuplicate;

        costCenters.Add(costCenter);
        writer.Audit(OrganizationAudit.CostCenterCreated, OrganizationAudit.CostCenterResource, costCenter.Id,
            toState: "Active", ruleIds: ["ORG-001", "ORG-002"],
            details: new Dictionary<string, string> { ["code"] = costCenter.Code, ["departmentId"] = department.Id.ToString() });
        var commit = await writer.CommitAsync(
            _ => Task.FromResult<long?>(null),
            constraint => constraint == ICostCenterRepository.CodeUniqueKey ? OrganizationAudit.CodeDuplicate : null,
            cancellationToken);
        return commit is null ? CostCenterView.From(costCenter) : commit;
    }

    public async Task<Result<CostCenterView>> Handle(UpdateCostCenterCommand request, CancellationToken cancellationToken)
    {
        var costCenter = await costCenters.FindAsync(request.Id, cancellationToken);
        if (costCenter is null)
            return OrganizationAudit.NotFound("cost center");
        if (VersionPrecondition.Check(request.ExpectedVersion, costCenter.Version) is { } precondition)
            return precondition;

        var manager = request.ManagerUserId == Guid.Empty ? null : request.ManagerUserId;
        if (manager is not null && manager != costCenter.ManagerUserId && !await accounts.IsActiveUserAsync(manager.Value, cancellationToken))
            return OrganizationAudit.InactiveUser("managerUserId");

        var version = costCenter.Version;
        if (costCenter.Update(request.Name, manager, writer.UtcNow) is { } error)
            return error.ToApplicationError();
        if (costCenter.Version == version)
            return CostCenterView.From(costCenter);

        writer.Audit(OrganizationAudit.CostCenterUpdated, OrganizationAudit.CostCenterResource, costCenter.Id,
            details: new Dictionary<string, string> { ["fields"] = "name,managerUserId" });
        return await CommitAsync(costCenter, cancellationToken);
    }

    public async Task<Result<CostCenterView>> Handle(DeactivateCostCenterCommand request, CancellationToken cancellationToken)
    {
        var costCenter = await costCenters.FindAsync(request.Id, cancellationToken);
        if (costCenter is null)
            return OrganizationAudit.NotFound("cost center");
        if (VersionPrecondition.Check(request.ExpectedVersion, costCenter.Version) is { } precondition)
            return precondition;
        if (costCenter.Deactivate(request.Reason, writer.UtcNow) is { } error)
            return error.ToApplicationError();

        writer.Audit(OrganizationAudit.CostCenterDeactivated, OrganizationAudit.CostCenterResource, costCenter.Id,
            "Active", "Inactive", MasterDataRules.NormalizeReason(request.Reason), ["ORG-003"]);
        return await CommitAsync(costCenter, cancellationToken);
    }

    public async Task<Result<CostCenterView>> Handle(ReactivateCostCenterCommand request, CancellationToken cancellationToken)
    {
        var costCenter = await costCenters.FindAsync(request.Id, cancellationToken);
        if (costCenter is null)
            return OrganizationAudit.NotFound("cost center");
        if (VersionPrecondition.Check(request.ExpectedVersion, costCenter.Version) is { } precondition)
            return precondition;

        var department = await departments.FindAsync(costCenter.DepartmentId, cancellationToken)
            ?? throw new InvalidOperationException("A cost center references a missing department.");
        if (costCenter.Reactivate(department, writer.UtcNow) is { } error)
            return error.ToApplicationError();

        writer.Audit(OrganizationAudit.CostCenterReactivated, OrganizationAudit.CostCenterResource, costCenter.Id,
            "Inactive", "Active", MasterDataRules.NormalizeReason(request.Reason), ["ORG-003"]);
        return await CommitAsync(costCenter, cancellationToken);
    }

    private async Task<Result<CostCenterView>> CommitAsync(CostCenter costCenter, CancellationToken cancellationToken)
    {
        var commit = await writer.CommitAsync(token => costCenters.GetVersionAsync(costCenter.Id, token), null, cancellationToken);
        return commit is null ? CostCenterView.From(costCenter) : commit;
    }
}

public sealed class CostCenterQueryHandlers(ICostCenterRepository costCenters) :
    IRequestHandler<GetCostCenterQuery, Result<CostCenterView>>,
    IRequestHandler<ListCostCentersQuery, Result<PagedResult<CostCenterView>>>
{
    public const string DefaultSort = "code";

    public async Task<Result<CostCenterView>> Handle(GetCostCenterQuery request, CancellationToken cancellationToken) =>
        await costCenters.FindAsync(request.Id, cancellationToken) is { } costCenter
            ? CostCenterView.From(costCenter)
            : OrganizationAudit.NotFound("cost center");

    public async Task<Result<PagedResult<CostCenterView>>> Handle(ListCostCentersQuery request, CancellationToken cancellationToken)
    {
        var page = PageRequest.Create(request.Page, request.PageSize, request.Sort, ICostCenterRepository.SortFields, DefaultSort);
        if (!page.IsSuccess)
            return page.Error!;
        if (MasterDataQueries.InvalidQ(request.Q) is { } invalidQ)
            return invalidQ;

        var filter = new CostCenterFilter(MasterDataQueries.Trim(request.Q), request.IsActive, request.DepartmentId, request.ManagerUserId);
        return MasterDataQueries.Map(await costCenters.ListAsync(filter, page.Value, cancellationToken), CostCenterView.From);
    }
}
