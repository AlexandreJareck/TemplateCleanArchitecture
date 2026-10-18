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

/// <summary>Wire money input: <see cref="Amount"/> is a decimal string (api-guidelines §2).</summary>
public sealed record MoneyInput(string? Amount, string? Currency);

public sealed record CreateCatalogItemCommand(
    string? Code, string? Description, CatalogItemKind? Kind, Guid CategoryId, Guid UnitOfMeasureId, MoneyInput? ReferencePrice)
    : IRequest<Result<CatalogItemView>>;

public sealed record UpdateCatalogItemCommand(
    Guid Id, long? ExpectedVersion, string? Description, CatalogItemKind? Kind, Guid CategoryId, Guid UnitOfMeasureId, MoneyInput? ReferencePrice)
    : IRequest<Result<CatalogItemView>>;

public sealed record DeactivateCatalogItemCommand(Guid Id, long? ExpectedVersion, string? Reason) : IRequest<Result<CatalogItemView>>;

public sealed record ReactivateCatalogItemCommand(Guid Id, long? ExpectedVersion, string? Reason) : IRequest<Result<CatalogItemView>>;

public sealed record GetCatalogItemQuery(Guid Id) : IRequest<Result<CatalogItemView>>;

public sealed record ListCatalogItemsQuery(
    int? Page = null, int? PageSize = null, string? Sort = null, string? Q = null, bool? IsActive = null,
    CatalogItemKind? Kind = null, Guid? CategoryId = null, Guid? UnitOfMeasureId = null) : IRequest<Result<PagedResult<CatalogItemView>>>;

public sealed class CreateCatalogItemCommandValidator : AbstractValidator<CreateCatalogItemCommand>
{
    public CreateCatalogItemCommandValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(40);
        RuleFor(command => command.Description).NotEmpty().MaximumLength(CatalogItem.DescriptionMaxLength);
        RuleFor(command => command.Kind).NotNull().IsInEnum();
        RuleFor(command => command.CategoryId).NotEmpty();
        RuleFor(command => command.UnitOfMeasureId).NotEmpty();
    }
}

public sealed class UpdateCatalogItemCommandValidator : AbstractValidator<UpdateCatalogItemCommand>
{
    public UpdateCatalogItemCommandValidator()
    {
        RuleFor(command => command.Description).NotEmpty().MaximumLength(CatalogItem.DescriptionMaxLength);
        RuleFor(command => command.Kind).NotNull().IsInEnum();
        RuleFor(command => command.CategoryId).NotEmpty();
        RuleFor(command => command.UnitOfMeasureId).NotEmpty();
    }
}

public sealed class CatalogItemCommandHandlers(
    ICatalogItemRepository items,
    ICategoryRepository categories,
    IUnitOfMeasureRepository units,
    MasterDataWriter writer) :
    IRequestHandler<CreateCatalogItemCommand, Result<CatalogItemView>>,
    IRequestHandler<UpdateCatalogItemCommand, Result<CatalogItemView>>,
    IRequestHandler<DeactivateCatalogItemCommand, Result<CatalogItemView>>,
    IRequestHandler<ReactivateCatalogItemCommand, Result<CatalogItemView>>
{
    public async Task<Result<CatalogItemView>> Handle(CreateCatalogItemCommand request, CancellationToken cancellationToken)
    {
        var references = await LoadReferencesAsync(request.CategoryId, request.UnitOfMeasureId, request.ReferencePrice, cancellationToken);
        if (references.Error is not null)
            return references.Error;

        var (item, error) = CatalogItem.Create(
            Guid.NewGuid(), request.Code, request.Description, request.Kind!.Value, references.Category!, references.Unit!,
            references.Amount, request.ReferencePrice?.Currency, writer.ActorId, writer.UtcNow);
        if (error is not null)
            return error.ToApplicationError();
        if (await items.CodeExistsAsync(item!.Code, cancellationToken))
            return CatalogAudit.CodeDuplicate;

        items.Add(item);
        writer.Audit(CatalogAudit.ItemCreated, CatalogAudit.ItemResource, item.Id, toState: "Active", ruleIds: ["CAT-001", "CAT-002"],
            details: new Dictionary<string, string>
            {
                ["code"] = item.Code,
                ["categoryId"] = item.CategoryId.ToString(),
                ["unitOfMeasureId"] = item.UnitOfMeasureId.ToString()
            });
        var commit = await writer.CommitAsync(
            _ => Task.FromResult<long?>(null),
            constraint => constraint == ICatalogItemRepository.CodeUniqueKey ? CatalogAudit.CodeDuplicate : null,
            cancellationToken);
        return commit is null ? CatalogItemView.From(item) : commit;
    }

    public async Task<Result<CatalogItemView>> Handle(UpdateCatalogItemCommand request, CancellationToken cancellationToken)
    {
        var item = await items.FindAsync(request.Id, cancellationToken);
        if (item is null)
            return CatalogAudit.NotFound("catalog item");
        if (VersionPrecondition.Check(request.ExpectedVersion, item.Version) is { } precondition)
            return precondition;

        var references = await LoadReferencesAsync(request.CategoryId, request.UnitOfMeasureId, request.ReferencePrice, cancellationToken);
        if (references.Error is not null)
            return references.Error;

        var version = item.Version;
        var error = item.Update(request.Description, request.Kind!.Value, references.Category!, references.Unit!,
            references.Amount, request.ReferencePrice?.Currency, writer.UtcNow);
        if (error is not null)
            return error.ToApplicationError();
        if (item.Version == version)
            return CatalogItemView.From(item);

        writer.Audit(CatalogAudit.ItemUpdated, CatalogAudit.ItemResource, item.Id, ruleIds: ["CAT-002", "CAT-004"],
            details: new Dictionary<string, string>
            {
                ["categoryId"] = item.CategoryId.ToString(),
                ["unitOfMeasureId"] = item.UnitOfMeasureId.ToString()
            });
        return await CommitAsync(item, cancellationToken);
    }

    public async Task<Result<CatalogItemView>> Handle(DeactivateCatalogItemCommand request, CancellationToken cancellationToken)
    {
        var item = await items.FindAsync(request.Id, cancellationToken);
        if (item is null)
            return CatalogAudit.NotFound("catalog item");
        if (VersionPrecondition.Check(request.ExpectedVersion, item.Version) is { } precondition)
            return precondition;
        if (item.Deactivate(request.Reason, writer.UtcNow) is { } error)
            return error.ToApplicationError();

        writer.Audit(CatalogAudit.ItemDeactivated, CatalogAudit.ItemResource, item.Id, "Active", "Inactive",
            MasterDataRules.NormalizeReason(request.Reason), ["CAT-002"]);
        return await CommitAsync(item, cancellationToken);
    }

    public async Task<Result<CatalogItemView>> Handle(ReactivateCatalogItemCommand request, CancellationToken cancellationToken)
    {
        var item = await items.FindAsync(request.Id, cancellationToken);
        if (item is null)
            return CatalogAudit.NotFound("catalog item");
        if (VersionPrecondition.Check(request.ExpectedVersion, item.Version) is { } precondition)
            return precondition;

        var category = await categories.FindAsync(item.CategoryId, cancellationToken)
            ?? throw new InvalidOperationException("A catalog item references a missing category.");
        var unit = await units.FindAsync(item.UnitOfMeasureId, cancellationToken)
            ?? throw new InvalidOperationException("A catalog item references a missing unit.");
        if (item.Reactivate(category, unit, writer.UtcNow) is { } error)
            return error.ToApplicationError();

        writer.Audit(CatalogAudit.ItemReactivated, CatalogAudit.ItemResource, item.Id, "Inactive", "Active",
            MasterDataRules.NormalizeReason(request.Reason), ["CAT-002"]);
        return await CommitAsync(item, cancellationToken);
    }

    private async Task<(Category? Category, UnitOfMeasure? Unit, decimal? Amount, ApplicationError? Error)> LoadReferencesAsync(
        Guid categoryId, Guid unitId, MoneyInput? price, CancellationToken cancellationToken)
    {
        if (!CatalogAudit.TryParseAmount(price?.Amount, out var amount))
            return (null, null, null, ApplicationError.Validation("referencePrice.amount", "The amount must be a decimal string such as \"12.50\"."));
        if (price is not null && price.Amount is null)
            return (null, null, null, ApplicationError.Validation("referencePrice.amount", "The amount is required when a reference price is given."));

        var category = await categories.FindAsync(categoryId, cancellationToken);
        if (category is null)
            return (null, null, null, ApplicationError.Validation("categoryId", "The category does not exist."));
        var unit = await units.FindAsync(unitId, cancellationToken);
        if (unit is null)
            return (null, null, null, ApplicationError.Validation("unitOfMeasureId", "The unit of measure does not exist."));
        return (category, unit, amount, null);
    }

    private async Task<Result<CatalogItemView>> CommitAsync(CatalogItem item, CancellationToken cancellationToken)
    {
        var commit = await writer.CommitAsync(token => items.GetVersionAsync(item.Id, token), null, cancellationToken);
        return commit is null ? CatalogItemView.From(item) : commit;
    }
}

public sealed class CatalogItemQueryHandlers(ICatalogItemRepository items) :
    IRequestHandler<GetCatalogItemQuery, Result<CatalogItemView>>,
    IRequestHandler<ListCatalogItemsQuery, Result<PagedResult<CatalogItemView>>>
{
    public const string DefaultSort = "code";

    public async Task<Result<CatalogItemView>> Handle(GetCatalogItemQuery request, CancellationToken cancellationToken) =>
        await items.FindAsync(request.Id, cancellationToken) is { } item ? CatalogItemView.From(item) : CatalogAudit.NotFound("catalog item");

    public async Task<Result<PagedResult<CatalogItemView>>> Handle(ListCatalogItemsQuery request, CancellationToken cancellationToken)
    {
        var page = PageRequest.Create(request.Page, request.PageSize, request.Sort, ICatalogItemRepository.SortFields, DefaultSort);
        if (!page.IsSuccess)
            return page.Error!;
        if (MasterDataQueries.InvalidQ(request.Q) is { } invalidQ)
            return invalidQ;

        var filter = new CatalogItemFilter(MasterDataQueries.Trim(request.Q), request.IsActive, request.Kind, request.CategoryId, request.UnitOfMeasureId);
        return MasterDataQueries.Map(await items.ListAsync(filter, page.Value, cancellationToken), CatalogItemView.From);
    }
}
