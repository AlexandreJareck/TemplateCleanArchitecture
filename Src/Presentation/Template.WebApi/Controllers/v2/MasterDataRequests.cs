using System.Text.Json.Serialization;
using Template.Application.Features.Catalog;
using Template.Application.Features.Suppliers;
using Template.Domain.Catalog;
using Template.Domain.Suppliers;

namespace Template.WebApi.Controllers.v2;

/// <summary>Body of the deactivate/reactivate/suspend/inactivate actions; the reason is 10–500 characters when required.</summary>
public sealed record ReasonRequest(string? Reason);

public sealed record CreateDepartmentRequest(string? Code, string? Name, Guid? HeadUserId);

public sealed record UpdateDepartmentRequest(string? Name, Guid? HeadUserId);

public sealed record CreateCostCenterRequest(string? Code, string? Name, Guid DepartmentId, Guid? ManagerUserId);

public sealed record UpdateCostCenterRequest(string? Name, Guid? ManagerUserId);

public sealed record UpdateOrganizationSettingsRequest(string? DisplayName, string? BaseCurrency, string? TimeZone, IReadOnlyList<string>? SupportedCurrencies);

public sealed record CreateCategoryRequest(string? Code, string? Name);

public sealed record UpdateNameRequest(string? Name);

public sealed record CreateUnitOfMeasureRequest(string? Code, string? Name, int AllowedDecimals);

/// <summary>Catalog item body; <c>kind</c> is <c>Good</c> or <c>Service</c>; money amounts are strings.</summary>
public sealed record CatalogItemRequest(
    string? Code,
    string? Description,
    [property: JsonConverter(typeof(JsonStringEnumConverter<CatalogItemKind>))] CatalogItemKind? Kind,
    Guid CategoryId,
    Guid UnitOfMeasureId,
    MoneyInput? ReferencePrice);

public sealed record CreateSupplierRequest(string? LegalName, string? TradeName, string? Country, string? TaxId);

public sealed record SupplierContactsRequest(IReadOnlyList<SupplierContactInput>? Contacts);

/// <summary>Address entry; <c>type</c> is <c>Headquarters</c>, <c>Billing</c>, <c>Shipping</c> or <c>Other</c>.</summary>
public sealed record SupplierAddressRequest(
    Guid? Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter<SupplierAddressType>))] SupplierAddressType? Type,
    string? PostalCode,
    string? Street,
    string? Number,
    string? Complement,
    string? District,
    string? City,
    string? State,
    string? Country)
{
    public SupplierAddressInput ToInput() => new(Id, Type, PostalCode, Street, Number, Complement, District, City, State, Country);
}

public sealed record SupplierAddressesRequest(IReadOnlyList<SupplierAddressRequest>? Addresses);

public sealed record SupplierCategoriesRequest(IReadOnlyList<Guid>? CategoryIds);

/// <summary>
/// Accepted (possibly edited) enrichment values from a proposal returned by the enrichment endpoints. Only the
/// fields present are applied; <c>provider</c> and <c>retrievedAt</c> come from that proposal (INT-002).
/// </summary>
public sealed record AcceptEnrichmentRequest(
    string? Provider,
    DateTime? RetrievedAt,
    string? LegalName,
    string? TradeName,
    string? RegistrationStatus,
    SupplierAddressRequest? Address);
