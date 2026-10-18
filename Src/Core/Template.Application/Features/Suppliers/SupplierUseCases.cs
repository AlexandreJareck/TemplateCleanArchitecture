#nullable enable
using FluentValidation;
using MediatR;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Interfaces.Catalog;
using Template.Application.Interfaces.Suppliers;
using Template.Application.Services.MasterData;
using Template.Domain.Common;
using Template.Domain.Suppliers;

namespace Template.Application.Features.Suppliers;

/// <summary>Address input of the supplier endpoints; <see cref="Id"/> identifies an existing address on replace.</summary>
public sealed record SupplierAddressInput(
    Guid? Id, SupplierAddressType? Type, string? PostalCode, string? Street, string? Number, string? Complement,
    string? District, string? City, string? State, string? Country)
{
    public AddressValues ToValues() => new(
        Type ?? SupplierAddressType.Headquarters, PostalCode ?? string.Empty, Street ?? string.Empty, Number, Complement,
        District, City ?? string.Empty, State ?? string.Empty, Country ?? string.Empty);
}

public sealed record SupplierContactInput(Guid? Id, string? Name, string? Email, string? Phone, string? Role);

/// <summary>Creates a <c>Draft</c> supplier (SUP-001). Permission <c>suppliers.manage</c>.</summary>
public sealed record CreateSupplierCommand(string? LegalName, string? TradeName, string? Country, string? TaxId) : IRequest<Result<SupplierView>>;

/// <summary>Changes names; country and tax ID only while <c>Draft</c>.</summary>
public sealed record UpdateSupplierCommand(Guid Id, long? ExpectedVersion, string? LegalName, string? TradeName, string? Country, string? TaxId)
    : IRequest<Result<SupplierView>>;

public sealed record ReplaceSupplierContactsCommand(Guid Id, long? ExpectedVersion, IReadOnlyList<SupplierContactInput>? Contacts)
    : IRequest<Result<SupplierView>>;

public sealed record ReplaceSupplierAddressesCommand(Guid Id, long? ExpectedVersion, IReadOnlyList<SupplierAddressInput>? Addresses)
    : IRequest<Result<SupplierView>>;

public sealed record SetSupplierCategoriesCommand(Guid Id, long? ExpectedVersion, IReadOnlyList<Guid>? CategoryIds) : IRequest<Result<SupplierView>>;

public enum SupplierStatusTransition
{
    Activate,
    Suspend,
    Inactivate,
    Reactivate
}

/// <summary>Status transitions (SUP-002..005). Permission <c>suppliers.status.change</c>.</summary>
public sealed record ChangeSupplierStatusCommand(Guid Id, long? ExpectedVersion, SupplierStatusTransition Transition, string? Reason)
    : IRequest<Result<SupplierView>>;

public sealed record GetSupplierQuery(Guid Id) : IRequest<Result<SupplierView>>;

/// <summary><see cref="Status"/> is a comma-separated list of statuses, for example <c>Active,Suspended</c>.</summary>
public sealed record ListSuppliersQuery(
    int? Page = null, int? PageSize = null, string? Sort = null, string? Q = null, string? Status = null,
    string? Country = null, string? TaxId = null, Guid? CategoryId = null) : IRequest<Result<PagedResult<SupplierSummaryView>>>;

public sealed class CreateSupplierCommandValidator : AbstractValidator<CreateSupplierCommand>
{
    public CreateSupplierCommandValidator()
    {
        RuleFor(command => command.LegalName).NotEmpty().MaximumLength(Supplier.LegalNameMaxLength);
        RuleFor(command => command.TradeName).MaximumLength(Supplier.TradeNameMaxLength);
        RuleFor(command => command.Country).NotEmpty().Length(2);
        RuleFor(command => command.TaxId).NotEmpty().MaximumLength(40);
    }
}

public sealed class UpdateSupplierCommandValidator : AbstractValidator<UpdateSupplierCommand>
{
    public UpdateSupplierCommandValidator()
    {
        RuleFor(command => command.LegalName).NotEmpty().MaximumLength(Supplier.LegalNameMaxLength);
        RuleFor(command => command.TradeName).MaximumLength(Supplier.TradeNameMaxLength);
        RuleFor(command => command.Country).NotEmpty().Length(2);
        RuleFor(command => command.TaxId).NotEmpty().MaximumLength(40);
    }
}

public sealed class ReplaceSupplierContactsCommandValidator : AbstractValidator<ReplaceSupplierContactsCommand>
{
    public ReplaceSupplierContactsCommandValidator()
    {
        RuleFor(command => command.Contacts).NotNull();
        RuleForEach(command => command.Contacts).ChildRules(contact =>
        {
            contact.RuleFor(value => value.Name).NotEmpty().MaximumLength(SupplierContact.NameMaxLength);
            contact.RuleFor(value => value.Email).EmailAddress().MaximumLength(SupplierContact.EmailMaxLength);
            contact.RuleFor(value => value.Phone).MaximumLength(SupplierContact.PhoneMaxLength);
            contact.RuleFor(value => value.Role).MaximumLength(SupplierContact.RoleMaxLength);
        });
    }
}

public sealed class ReplaceSupplierAddressesCommandValidator : AbstractValidator<ReplaceSupplierAddressesCommand>
{
    public ReplaceSupplierAddressesCommandValidator() => RuleFor(command => command.Addresses).NotNull();
}

public sealed class SetSupplierCategoriesCommandValidator : AbstractValidator<SetSupplierCategoriesCommand>
{
    public SetSupplierCategoriesCommandValidator()
    {
        RuleFor(command => command.CategoryIds).NotNull();
        RuleForEach(command => command.CategoryIds).NotEmpty();
    }
}

public sealed class ChangeSupplierStatusCommandValidator : AbstractValidator<ChangeSupplierStatusCommand>
{
    public ChangeSupplierStatusCommandValidator() => RuleFor(command => command.Transition).IsInEnum();
}

public sealed class SupplierCommandHandlers(
    ISupplierRepository suppliers,
    ICategoryRepository categories,
    MasterDataWriter writer) :
    IRequestHandler<CreateSupplierCommand, Result<SupplierView>>,
    IRequestHandler<UpdateSupplierCommand, Result<SupplierView>>,
    IRequestHandler<ReplaceSupplierContactsCommand, Result<SupplierView>>,
    IRequestHandler<ReplaceSupplierAddressesCommand, Result<SupplierView>>,
    IRequestHandler<SetSupplierCategoriesCommand, Result<SupplierView>>,
    IRequestHandler<ChangeSupplierStatusCommand, Result<SupplierView>>
{
    public async Task<Result<SupplierView>> Handle(CreateSupplierCommand request, CancellationToken cancellationToken)
    {
        var (supplier, error) = Supplier.CreateDraft(
            Guid.NewGuid(), request.LegalName, request.TradeName, request.Country, request.TaxId, writer.ActorId, writer.UtcNow);
        if (error is not null)
            return error.ToApplicationError();
        if (await suppliers.TaxIdExistsAsync(supplier!.Country, supplier.TaxId, null, cancellationToken))
            return SupplierAudit.TaxIdDuplicate;

        suppliers.Add(supplier);
        // The CNPJ is a public business identifier (NFR §9) but it is still kept out of audit details; the ID suffices.
        writer.Audit(SupplierAudit.Created, SupplierAudit.Resource, supplier.Id, toState: nameof(SupplierStatus.Draft), ruleIds: ["SUP-001"]);
        var commit = await writer.CommitAsync(_ => Task.FromResult<long?>(null), SupplierAudit.DuplicateTaxId, cancellationToken);
        return commit is null ? SupplierView.From(supplier) : commit;
    }

    public async Task<Result<SupplierView>> Handle(UpdateSupplierCommand request, CancellationToken cancellationToken) =>
        await ChangeAsync(request.Id, request.ExpectedVersion, async supplier =>
        {
            var normalizedCountry = TaxIdentifier.NormalizeCountry(request.Country);
            var normalizedTaxId = TaxIdentifier.Normalize(normalizedCountry, request.TaxId);
            if (normalizedCountry is not null && normalizedTaxId is not null
                && (normalizedCountry != supplier.Country || normalizedTaxId != supplier.TaxId)
                && await suppliers.TaxIdExistsAsync(normalizedCountry, normalizedTaxId, supplier.Id, cancellationToken))
                return SupplierAudit.TaxIdDuplicate;

            return supplier.UpdateDetails(request.LegalName, request.TradeName, request.Country, request.TaxId, writer.UtcNow)?.ToApplicationError();
        }, SupplierAudit.Updated, "legalName,tradeName,country,taxId", cancellationToken);

    public async Task<Result<SupplierView>> Handle(ReplaceSupplierContactsCommand request, CancellationToken cancellationToken) =>
        await ChangeAsync(request.Id, request.ExpectedVersion, supplier => Task.FromResult(supplier.ReplaceContacts(
                (request.Contacts ?? []).Select(contact => new ContactInput(contact.Id, contact.Name, contact.Email, contact.Phone, contact.Role)).ToArray(),
                writer.UtcNow)?.ToApplicationError()),
            SupplierAudit.ContactsChanged, "contacts", cancellationToken);

    public async Task<Result<SupplierView>> Handle(ReplaceSupplierAddressesCommand request, CancellationToken cancellationToken) =>
        await ChangeAsync(request.Id, request.ExpectedVersion, supplier => Task.FromResult(supplier.ReplaceAddresses(
                (request.Addresses ?? []).Select(address => new AddressInput(address.Id, address.ToValues())).ToArray(),
                writer.UtcNow)?.ToApplicationError()),
            SupplierAudit.AddressesChanged, "addresses", cancellationToken);

    public async Task<Result<SupplierView>> Handle(SetSupplierCategoriesCommand request, CancellationToken cancellationToken) =>
        await ChangeAsync(request.Id, request.ExpectedVersion, async supplier =>
        {
            var ids = (request.CategoryIds ?? []).Distinct().ToArray();
            var found = await categories.FindManyAsync(ids, cancellationToken);
            if (found.Count != ids.Length)
                return ApplicationError.Validation("categoryIds", "Every category must exist.");

            return supplier.SetCategories(
                found.Select(category => new MasterDataReference(category.Id, category.IsActive)).ToArray(), writer.UtcNow)?.ToApplicationError();
        }, SupplierAudit.CategoriesChanged, "categoryIds", cancellationToken);

    public async Task<Result<SupplierView>> Handle(ChangeSupplierStatusCommand request, CancellationToken cancellationToken)
    {
        var supplier = await suppliers.FindAsync(request.Id, cancellationToken);
        if (supplier is null)
            return SupplierAudit.NotFound;
        if (VersionPrecondition.Check(request.ExpectedVersion, supplier.Version) is { } precondition)
            return precondition;

        var from = supplier.Status;
        var actor = writer.ActorId;
        var now = writer.UtcNow;
        var (error, action, rules) = request.Transition switch
        {
            SupplierStatusTransition.Activate => (supplier.Activate(actor, now), SupplierAudit.Activated, new[] { "SUP-002" }),
            SupplierStatusTransition.Suspend => (supplier.Suspend(request.Reason, actor, now), SupplierAudit.Suspended, ["SUP-004", "SUP-005"]),
            SupplierStatusTransition.Inactivate => (supplier.Inactivate(request.Reason, actor, now), SupplierAudit.Inactivated, ["SUP-004", "SUP-005"]),
            _ => (supplier.Reactivate(request.Reason, actor, now), SupplierAudit.Reactivated, ["SUP-002", "SUP-005"])
        };
        if (error is not null)
            return error.ToApplicationError();

        writer.Audit(action, SupplierAudit.Resource, supplier.Id, from.ToString(), supplier.Status.ToString(),
            MasterDataRules.NormalizeReason(request.Reason), rules, occurredAtUtc: now);
        var commit = await writer.CommitAsync(token => suppliers.GetVersionAsync(supplier.Id, token), null, cancellationToken);
        return commit is null ? SupplierView.From(supplier) : commit;
    }

    private async Task<Result<SupplierView>> ChangeAsync(
        Guid id,
        long? expectedVersion,
        Func<Supplier, Task<ApplicationError?>> change,
        string action,
        string fields,
        CancellationToken cancellationToken)
    {
        var supplier = await suppliers.FindAsync(id, cancellationToken);
        if (supplier is null)
            return SupplierAudit.NotFound;
        if (VersionPrecondition.Check(expectedVersion, supplier.Version) is { } precondition)
            return precondition;

        var version = supplier.Version;
        if (await change(supplier) is { } error)
            return error;
        if (supplier.Version == version)
            return SupplierView.From(supplier);

        writer.Audit(action, SupplierAudit.Resource, supplier.Id, details: new Dictionary<string, string> { ["fields"] = fields });
        var commit = await writer.CommitAsync(token => suppliers.GetVersionAsync(supplier.Id, token), SupplierAudit.DuplicateTaxId, cancellationToken);
        return commit is null ? SupplierView.From(supplier) : commit;
    }
}

public sealed class SupplierQueryHandlers(ISupplierRepository suppliers) :
    IRequestHandler<GetSupplierQuery, Result<SupplierView>>,
    IRequestHandler<ListSuppliersQuery, Result<PagedResult<SupplierSummaryView>>>
{
    public const string DefaultSort = "legalName";

    public async Task<Result<SupplierView>> Handle(GetSupplierQuery request, CancellationToken cancellationToken) =>
        await suppliers.FindAsync(request.Id, cancellationToken) is { } supplier ? SupplierView.From(supplier) : SupplierAudit.NotFound;

    public async Task<Result<PagedResult<SupplierSummaryView>>> Handle(ListSuppliersQuery request, CancellationToken cancellationToken)
    {
        var page = PageRequest.Create(request.Page, request.PageSize, request.Sort, ISupplierRepository.SortFields, DefaultSort);
        if (!page.IsSuccess)
            return page.Error!;
        if (MasterDataQueries.InvalidQ(request.Q) is { } invalidQ)
            return invalidQ;

        var statuses = new List<SupplierStatus>();
        foreach (var part in (request.Status ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<SupplierStatus>(part, ignoreCase: true, out var status) || !Enum.IsDefined(status))
                return ApplicationError.Validation("status", "status accepts a comma-separated list of: Draft, Active, Suspended, Inactive.");
            statuses.Add(status);
        }

        string? country = null;
        if (!string.IsNullOrWhiteSpace(request.Country))
        {
            country = TaxIdentifier.NormalizeCountry(request.Country);
            if (country is null)
                return ApplicationError.Validation("country", "country must be an ISO 3166-1 alpha-2 code.");
        }

        string? taxId = null;
        if (!string.IsNullOrWhiteSpace(request.TaxId))
        {
            taxId = TaxIdentifier.Normalize(country ?? TaxIdentifier.Brazil, request.TaxId);
            if (taxId is null)
                return ApplicationError.Validation("taxId", "taxId is not a structurally valid identifier for the country.");
            country ??= TaxIdentifier.Brazil;
        }

        var filter = new SupplierFilter(MasterDataQueries.Trim(request.Q), statuses.Count == 0 ? null : statuses, country, taxId, request.CategoryId);
        return MasterDataQueries.Map(await suppliers.ListAsync(filter, page.Value, cancellationToken), SupplierSummaryView.From);
    }
}
