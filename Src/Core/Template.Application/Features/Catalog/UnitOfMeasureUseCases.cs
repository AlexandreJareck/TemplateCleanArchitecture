#nullable enable
using FluentValidation;
using MediatR;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Interfaces.Catalog;
using Template.Application.Services.MasterData;
using Template.Domain.Catalog;
using Template.Domain.Common;

namespace Template.Application.Features.Catalog;

/// <summary>Creates a unit; <see cref="AllowedDecimals"/> (0–3, CAT-003) is fixed afterwards.</summary>
public sealed record CreateUnitOfMeasureCommand(string? Code, string? Name, int AllowedDecimals) : IRequest<Result<UnitOfMeasureView>>;

public sealed record UpdateUnitOfMeasureCommand(Guid Id, long? ExpectedVersion, string? Name) : IRequest<Result<UnitOfMeasureView>>;

public sealed record DeactivateUnitOfMeasureCommand(Guid Id, long? ExpectedVersion, string? Reason) : IRequest<Result<UnitOfMeasureView>>;

public sealed record ReactivateUnitOfMeasureCommand(Guid Id, long? ExpectedVersion, string? Reason) : IRequest<Result<UnitOfMeasureView>>;

public sealed record GetUnitOfMeasureQuery(Guid Id) : IRequest<Result<UnitOfMeasureView>>;

public sealed record ListUnitsOfMeasureQuery(int? Page = null, int? PageSize = null, string? Sort = null, string? Q = null, bool? IsActive = null)
    : IRequest<Result<PagedResult<UnitOfMeasureView>>>;

public sealed class CreateUnitOfMeasureCommandValidator : AbstractValidator<CreateUnitOfMeasureCommand>
{
    public CreateUnitOfMeasureCommandValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(20);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(MasterDataRules.NameMaxLength);
        RuleFor(command => command.AllowedDecimals).InclusiveBetween(0, UnitOfMeasure.MaxAllowedDecimals);
    }
}

public sealed class UpdateUnitOfMeasureCommandValidator : AbstractValidator<UpdateUnitOfMeasureCommand>
{
    public UpdateUnitOfMeasureCommandValidator() =>
        RuleFor(command => command.Name).NotEmpty().MaximumLength(MasterDataRules.NameMaxLength);
}

public sealed class UnitOfMeasureCommandHandlers(IUnitOfMeasureRepository units, MasterDataWriter writer) :
    IRequestHandler<CreateUnitOfMeasureCommand, Result<UnitOfMeasureView>>,
    IRequestHandler<UpdateUnitOfMeasureCommand, Result<UnitOfMeasureView>>,
    IRequestHandler<DeactivateUnitOfMeasureCommand, Result<UnitOfMeasureView>>,
    IRequestHandler<ReactivateUnitOfMeasureCommand, Result<UnitOfMeasureView>>
{
    public async Task<Result<UnitOfMeasureView>> Handle(CreateUnitOfMeasureCommand request, CancellationToken cancellationToken)
    {
        var (unit, error) = UnitOfMeasure.Create(Guid.NewGuid(), request.Code, request.Name, request.AllowedDecimals, writer.ActorId, writer.UtcNow);
        if (error is not null)
            return error.ToApplicationError();
        if (await units.CodeExistsAsync(unit!.Code, cancellationToken))
            return CatalogAudit.CodeDuplicate;

        units.Add(unit);
        writer.Audit(CatalogAudit.UnitCreated, CatalogAudit.UnitResource, unit.Id, toState: "Active",
            ruleIds: ["CAT-001", "CAT-003"], details: new Dictionary<string, string> { ["code"] = unit.Code });
        var commit = await writer.CommitAsync(
            _ => Task.FromResult<long?>(null),
            constraint => constraint == IUnitOfMeasureRepository.CodeUniqueKey ? CatalogAudit.CodeDuplicate : null,
            cancellationToken);
        return commit is null ? UnitOfMeasureView.From(unit) : commit;
    }

    public Task<Result<UnitOfMeasureView>> Handle(UpdateUnitOfMeasureCommand request, CancellationToken cancellationToken) =>
        ChangeAsync(request.Id, request.ExpectedVersion, unit => unit.Rename(request.Name, writer.UtcNow),
            CatalogAudit.UnitUpdated, null, null, null, cancellationToken);

    public Task<Result<UnitOfMeasureView>> Handle(DeactivateUnitOfMeasureCommand request, CancellationToken cancellationToken) =>
        ChangeAsync(request.Id, request.ExpectedVersion, unit => unit.Deactivate(request.Reason, writer.UtcNow),
            CatalogAudit.UnitDeactivated, "Active", "Inactive", request.Reason, cancellationToken);

    public Task<Result<UnitOfMeasureView>> Handle(ReactivateUnitOfMeasureCommand request, CancellationToken cancellationToken) =>
        ChangeAsync(request.Id, request.ExpectedVersion, unit => unit.Reactivate(writer.UtcNow),
            CatalogAudit.UnitReactivated, "Inactive", "Active", request.Reason, cancellationToken);

    private async Task<Result<UnitOfMeasureView>> ChangeAsync(
        Guid id,
        long? expectedVersion,
        Func<UnitOfMeasure, DomainError?> change,
        string action,
        string? fromState,
        string? toState,
        string? reason,
        CancellationToken cancellationToken)
    {
        var unit = await units.FindAsync(id, cancellationToken);
        if (unit is null)
            return CatalogAudit.NotFound("unit of measure");
        if (VersionPrecondition.Check(expectedVersion, unit.Version) is { } precondition)
            return precondition;

        var version = unit.Version;
        if (change(unit) is { } error)
            return error.ToApplicationError();
        if (unit.Version == version)
            return UnitOfMeasureView.From(unit);

        writer.Audit(action, CatalogAudit.UnitResource, unit.Id, fromState, toState, MasterDataRules.NormalizeReason(reason),
            fromState is null ? null : ["CAT-002"]);
        var commit = await writer.CommitAsync(token => units.GetVersionAsync(unit.Id, token), null, cancellationToken);
        return commit is null ? UnitOfMeasureView.From(unit) : commit;
    }
}

public sealed class UnitOfMeasureQueryHandlers(IUnitOfMeasureRepository units) :
    IRequestHandler<GetUnitOfMeasureQuery, Result<UnitOfMeasureView>>,
    IRequestHandler<ListUnitsOfMeasureQuery, Result<PagedResult<UnitOfMeasureView>>>
{
    public const string DefaultSort = "code";

    public async Task<Result<UnitOfMeasureView>> Handle(GetUnitOfMeasureQuery request, CancellationToken cancellationToken) =>
        await units.FindAsync(request.Id, cancellationToken) is { } unit ? UnitOfMeasureView.From(unit) : CatalogAudit.NotFound("unit of measure");

    public async Task<Result<PagedResult<UnitOfMeasureView>>> Handle(ListUnitsOfMeasureQuery request, CancellationToken cancellationToken)
    {
        var page = PageRequest.Create(request.Page, request.PageSize, request.Sort, IUnitOfMeasureRepository.SortFields, DefaultSort);
        if (!page.IsSuccess)
            return page.Error!;
        if (MasterDataQueries.InvalidQ(request.Q) is { } invalidQ)
            return invalidQ;

        var result = await units.ListAsync(new UnitOfMeasureFilter(MasterDataQueries.Trim(request.Q), request.IsActive), page.Value, cancellationToken);
        return MasterDataQueries.Map(result, UnitOfMeasureView.From);
    }
}
