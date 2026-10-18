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

public sealed record CreateCategoryCommand(string? Code, string? Name) : IRequest<Result<CategoryView>>;

public sealed record UpdateCategoryCommand(Guid Id, long? ExpectedVersion, string? Name) : IRequest<Result<CategoryView>>;

public sealed record DeactivateCategoryCommand(Guid Id, long? ExpectedVersion, string? Reason) : IRequest<Result<CategoryView>>;

public sealed record ReactivateCategoryCommand(Guid Id, long? ExpectedVersion, string? Reason) : IRequest<Result<CategoryView>>;

public sealed record GetCategoryQuery(Guid Id) : IRequest<Result<CategoryView>>;

public sealed record ListCategoriesQuery(int? Page = null, int? PageSize = null, string? Sort = null, string? Q = null, bool? IsActive = null)
    : IRequest<Result<PagedResult<CategoryView>>>;

public sealed class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(20);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(MasterDataRules.NameMaxLength);
    }
}

public sealed class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator() =>
        RuleFor(command => command.Name).NotEmpty().MaximumLength(MasterDataRules.NameMaxLength);
}

public sealed class CategoryCommandHandlers(ICategoryRepository categories, MasterDataWriter writer) :
    IRequestHandler<CreateCategoryCommand, Result<CategoryView>>,
    IRequestHandler<UpdateCategoryCommand, Result<CategoryView>>,
    IRequestHandler<DeactivateCategoryCommand, Result<CategoryView>>,
    IRequestHandler<ReactivateCategoryCommand, Result<CategoryView>>
{
    public async Task<Result<CategoryView>> Handle(CreateCategoryCommand request, CancellationToken cancellationToken)
    {
        var (category, error) = Category.Create(Guid.NewGuid(), request.Code, request.Name, writer.ActorId, writer.UtcNow);
        if (error is not null)
            return error.ToApplicationError();
        if (await categories.CodeExistsAsync(category!.Code, cancellationToken))
            return CatalogAudit.CodeDuplicate;

        categories.Add(category);
        writer.Audit(CatalogAudit.CategoryCreated, CatalogAudit.CategoryResource, category.Id, toState: "Active",
            ruleIds: ["CAT-001"], details: new Dictionary<string, string> { ["code"] = category.Code });
        var commit = await writer.CommitAsync(
            _ => Task.FromResult<long?>(null),
            constraint => constraint == ICategoryRepository.CodeUniqueKey ? CatalogAudit.CodeDuplicate : null,
            cancellationToken);
        return commit is null ? CategoryView.From(category) : commit;
    }

    public Task<Result<CategoryView>> Handle(UpdateCategoryCommand request, CancellationToken cancellationToken) =>
        ChangeAsync(request.Id, request.ExpectedVersion, category => category.Rename(request.Name, writer.UtcNow),
            CatalogAudit.CategoryUpdated, null, null, null, cancellationToken);

    public Task<Result<CategoryView>> Handle(DeactivateCategoryCommand request, CancellationToken cancellationToken) =>
        ChangeAsync(request.Id, request.ExpectedVersion, category => category.Deactivate(request.Reason, writer.UtcNow),
            CatalogAudit.CategoryDeactivated, "Active", "Inactive", request.Reason, cancellationToken);

    public Task<Result<CategoryView>> Handle(ReactivateCategoryCommand request, CancellationToken cancellationToken) =>
        ChangeAsync(request.Id, request.ExpectedVersion, category => category.Reactivate(writer.UtcNow),
            CatalogAudit.CategoryReactivated, "Inactive", "Active", request.Reason, cancellationToken);

    private async Task<Result<CategoryView>> ChangeAsync(
        Guid id,
        long? expectedVersion,
        Func<Category, DomainError?> change,
        string action,
        string? fromState,
        string? toState,
        string? reason,
        CancellationToken cancellationToken)
    {
        var category = await categories.FindAsync(id, cancellationToken);
        if (category is null)
            return CatalogAudit.NotFound("category");
        if (VersionPrecondition.Check(expectedVersion, category.Version) is { } precondition)
            return precondition;

        var version = category.Version;
        if (change(category) is { } error)
            return error.ToApplicationError();
        if (category.Version == version)
            return CategoryView.From(category);

        writer.Audit(action, CatalogAudit.CategoryResource, category.Id, fromState, toState, MasterDataRules.NormalizeReason(reason),
            fromState is null ? null : ["CAT-002"]);
        var commit = await writer.CommitAsync(token => categories.GetVersionAsync(category.Id, token), null, cancellationToken);
        return commit is null ? CategoryView.From(category) : commit;
    }
}

public sealed class CategoryQueryHandlers(ICategoryRepository categories) :
    IRequestHandler<GetCategoryQuery, Result<CategoryView>>,
    IRequestHandler<ListCategoriesQuery, Result<PagedResult<CategoryView>>>
{
    public const string DefaultSort = "code";

    public async Task<Result<CategoryView>> Handle(GetCategoryQuery request, CancellationToken cancellationToken) =>
        await categories.FindAsync(request.Id, cancellationToken) is { } category ? CategoryView.From(category) : CatalogAudit.NotFound("category");

    public async Task<Result<PagedResult<CategoryView>>> Handle(ListCategoriesQuery request, CancellationToken cancellationToken)
    {
        var page = PageRequest.Create(request.Page, request.PageSize, request.Sort, ICategoryRepository.SortFields, DefaultSort);
        if (!page.IsSuccess)
            return page.Error!;
        if (MasterDataQueries.InvalidQ(request.Q) is { } invalidQ)
            return invalidQ;

        var result = await categories.ListAsync(new CategoryFilter(MasterDataQueries.Trim(request.Q), request.IsActive), page.Value, cancellationToken);
        return MasterDataQueries.Map(result, CategoryView.From);
    }
}
