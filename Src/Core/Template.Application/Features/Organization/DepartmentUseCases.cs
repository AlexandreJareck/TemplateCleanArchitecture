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

/// <summary>Creates an active department (ORG-001, ORG-006). Permission <c>organization.manage</c>.</summary>
public sealed record CreateDepartmentCommand(string? Code, string? Name, Guid? HeadUserId) : IRequest<Result<DepartmentView>>;

/// <summary>Changes name and head; the code is immutable (ORG-001). Requires the expected version (If-Match).</summary>
public sealed record UpdateDepartmentCommand(Guid Id, long? ExpectedVersion, string? Name, Guid? HeadUserId) : IRequest<Result<DepartmentView>>;

/// <summary>ORG-003: reason required; only when every cost center of the department is inactive.</summary>
public sealed record DeactivateDepartmentCommand(Guid Id, long? ExpectedVersion, string? Reason) : IRequest<Result<DepartmentView>>;

public sealed record ReactivateDepartmentCommand(Guid Id, long? ExpectedVersion, string? Reason) : IRequest<Result<DepartmentView>>;

public sealed record GetDepartmentQuery(Guid Id) : IRequest<Result<DepartmentView>>;

public sealed record ListDepartmentsQuery(int? Page = null, int? PageSize = null, string? Sort = null, string? Q = null, bool? IsActive = null)
    : IRequest<Result<PagedResult<DepartmentView>>>;

public sealed class CreateDepartmentCommandValidator : AbstractValidator<CreateDepartmentCommand>
{
    public CreateDepartmentCommandValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(20);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(MasterDataRules.NameMaxLength);
    }
}

public sealed class UpdateDepartmentCommandValidator : AbstractValidator<UpdateDepartmentCommand>
{
    public UpdateDepartmentCommandValidator() =>
        RuleFor(command => command.Name).NotEmpty().MaximumLength(MasterDataRules.NameMaxLength);
}

public sealed class DepartmentCommandHandlers(
    IDepartmentRepository departments,
    ICostCenterRepository costCenters,
    IAccountDirectory accounts,
    MasterDataWriter writer) :
    IRequestHandler<CreateDepartmentCommand, Result<DepartmentView>>,
    IRequestHandler<UpdateDepartmentCommand, Result<DepartmentView>>,
    IRequestHandler<DeactivateDepartmentCommand, Result<DepartmentView>>,
    IRequestHandler<ReactivateDepartmentCommand, Result<DepartmentView>>
{
    public async Task<Result<DepartmentView>> Handle(CreateDepartmentCommand request, CancellationToken cancellationToken)
    {
        if (request.HeadUserId is { } head && head != Guid.Empty && !await accounts.IsActiveUserAsync(head, cancellationToken))
            return OrganizationAudit.InactiveUser("headUserId");

        var (department, error) = Department.Create(Guid.NewGuid(), request.Code, request.Name, request.HeadUserId, writer.ActorId, writer.UtcNow);
        if (error is not null)
            return error.ToApplicationError();
        if (await departments.CodeExistsAsync(department!.Code, cancellationToken))
            return OrganizationAudit.CodeDuplicate;

        departments.Add(department);
        writer.Audit(OrganizationAudit.DepartmentCreated, OrganizationAudit.DepartmentResource, department.Id,
            toState: "Active", ruleIds: ["ORG-001"], details: new Dictionary<string, string> { ["code"] = department.Code });
        var commit = await writer.CommitAsync(_ => Task.FromResult<long?>(null), DuplicateCode, cancellationToken);
        return commit is null ? DepartmentView.From(department) : commit;
    }

    public async Task<Result<DepartmentView>> Handle(UpdateDepartmentCommand request, CancellationToken cancellationToken)
    {
        var department = await departments.FindAsync(request.Id, cancellationToken);
        if (department is null)
            return OrganizationAudit.NotFound("department");
        if (VersionPrecondition.Check(request.ExpectedVersion, department.Version) is { } precondition)
            return precondition;

        var head = request.HeadUserId == Guid.Empty ? null : request.HeadUserId;
        if (head is not null && head != department.HeadUserId && !await accounts.IsActiveUserAsync(head.Value, cancellationToken))
            return OrganizationAudit.InactiveUser("headUserId");

        var version = department.Version;
        if (department.Update(request.Name, head, writer.UtcNow) is { } error)
            return error.ToApplicationError();
        if (department.Version == version)
            return DepartmentView.From(department);

        writer.Audit(OrganizationAudit.DepartmentUpdated, OrganizationAudit.DepartmentResource, department.Id,
            details: new Dictionary<string, string> { ["fields"] = "name,headUserId" });
        return await CommitAsync(department, cancellationToken);
    }

    public async Task<Result<DepartmentView>> Handle(DeactivateDepartmentCommand request, CancellationToken cancellationToken)
    {
        var department = await departments.FindAsync(request.Id, cancellationToken);
        if (department is null)
            return OrganizationAudit.NotFound("department");
        if (VersionPrecondition.Check(request.ExpectedVersion, department.Version) is { } precondition)
            return precondition;

        var hasActiveCostCenters = await costCenters.AnyActiveInDepartmentAsync(department.Id, cancellationToken);
        if (department.Deactivate(request.Reason, hasActiveCostCenters, writer.UtcNow) is { } error)
            return error.ToApplicationError();

        writer.Audit(OrganizationAudit.DepartmentDeactivated, OrganizationAudit.DepartmentResource, department.Id,
            "Active", "Inactive", MasterDataRules.NormalizeReason(request.Reason), ["ORG-003"]);
        return await CommitAsync(department, cancellationToken);
    }

    public async Task<Result<DepartmentView>> Handle(ReactivateDepartmentCommand request, CancellationToken cancellationToken)
    {
        var department = await departments.FindAsync(request.Id, cancellationToken);
        if (department is null)
            return OrganizationAudit.NotFound("department");
        if (VersionPrecondition.Check(request.ExpectedVersion, department.Version) is { } precondition)
            return precondition;
        if (department.Reactivate(writer.UtcNow) is { } error)
            return error.ToApplicationError();

        writer.Audit(OrganizationAudit.DepartmentReactivated, OrganizationAudit.DepartmentResource, department.Id,
            "Inactive", "Active", MasterDataRules.NormalizeReason(request.Reason), ["ORG-003"]);
        return await CommitAsync(department, cancellationToken);
    }

    private async Task<Result<DepartmentView>> CommitAsync(Department department, CancellationToken cancellationToken)
    {
        var commit = await writer.CommitAsync(token => departments.GetVersionAsync(department.Id, token), null, cancellationToken);
        return commit is null ? DepartmentView.From(department) : commit;
    }

    private static ApplicationError? DuplicateCode(string constraint) =>
        constraint == IDepartmentRepository.CodeUniqueKey ? OrganizationAudit.CodeDuplicate : null;
}

public sealed class DepartmentQueryHandlers(IDepartmentRepository departments) :
    IRequestHandler<GetDepartmentQuery, Result<DepartmentView>>,
    IRequestHandler<ListDepartmentsQuery, Result<PagedResult<DepartmentView>>>
{
    public const string DefaultSort = "code";

    public async Task<Result<DepartmentView>> Handle(GetDepartmentQuery request, CancellationToken cancellationToken) =>
        await departments.FindAsync(request.Id, cancellationToken) is { } department
            ? DepartmentView.From(department)
            : OrganizationAudit.NotFound("department");

    public async Task<Result<PagedResult<DepartmentView>>> Handle(ListDepartmentsQuery request, CancellationToken cancellationToken)
    {
        var page = PageRequest.Create(request.Page, request.PageSize, request.Sort, IDepartmentRepository.SortFields, DefaultSort);
        if (!page.IsSuccess)
            return page.Error!;
        if (MasterDataQueries.InvalidQ(request.Q) is { } invalidQ)
            return invalidQ;

        var result = await departments.ListAsync(new DepartmentFilter(MasterDataQueries.Trim(request.Q), request.IsActive), page.Value, cancellationToken);
        return MasterDataQueries.Map(result, DepartmentView.From);
    }
}
