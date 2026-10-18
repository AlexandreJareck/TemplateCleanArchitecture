#nullable enable
using Template.Application.Common.Concurrency;
using Template.Application.Common.Results;
using Template.Application.Wrappers;
using Template.Domain.Suppliers;

namespace Template.Application.Features.Suppliers;

/// <summary>List representation: no contacts or addresses (SUP-006 data stays on the detail representation).</summary>
public sealed record SupplierSummaryView(
    Guid Id,
    string LegalName,
    string? TradeName,
    string Country,
    string TaxId,
    string FormattedTaxId,
    string Status,
    bool IsEligibleForNewCommitments,
    IReadOnlyList<Guid> CategoryIds,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string Version)
{
    public static SupplierSummaryView From(Supplier supplier) => new(
        supplier.Id, supplier.LegalName, supplier.TradeName, supplier.Country, supplier.TaxId,
        TaxIdentifier.Format(supplier.Country, supplier.TaxId), supplier.Status.ToString(), supplier.IsEligibleForNewCommitments,
        supplier.Categories.Select(link => link.CategoryId).Order().ToArray(), supplier.CreatedAtUtc, supplier.UpdatedAtUtc,
        VersionPrecondition.Format(supplier.Version));
}

public sealed record SupplierContactView(Guid Id, string Name, string? Email, string? Phone, string? Role);

public sealed record SupplierAddressView(
    Guid Id, string Type, string PostalCode, string Street, string? Number, string? Complement, string? District,
    string City, string State, string Country);

public sealed record SupplierStatusChangeView(int Sequence, string From, string To, Guid? ActorId, DateTime At, string? Reason);

/// <summary>Accepted enrichment provenance (INT-002); advisory, never a government verification.</summary>
public sealed record SupplierEnrichmentView(
    string Provider, DateTime RetrievedAt, DateTime AcceptedAt, Guid? AcceptedBy, IReadOnlyList<string> AcceptedFields,
    string? RegistrationStatus);

/// <summary>Detail representation, readable only with <c>suppliers.read</c> (SUP-006).</summary>
public sealed record SupplierView(
    Guid Id,
    string LegalName,
    string? TradeName,
    string Country,
    string TaxId,
    string FormattedTaxId,
    string Status,
    bool IsEligibleForNewCommitments,
    IReadOnlyList<Guid> CategoryIds,
    IReadOnlyList<SupplierContactView> Contacts,
    IReadOnlyList<SupplierAddressView> Addresses,
    IReadOnlyList<SupplierStatusChangeView> StatusHistory,
    SupplierEnrichmentView? Enrichment,
    string? MissingForActivation,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string Version)
{
    public static SupplierView From(Supplier supplier) => new(
        supplier.Id,
        supplier.LegalName,
        supplier.TradeName,
        supplier.Country,
        supplier.TaxId,
        TaxIdentifier.Format(supplier.Country, supplier.TaxId),
        supplier.Status.ToString(),
        supplier.IsEligibleForNewCommitments,
        supplier.Categories.Select(link => link.CategoryId).Order().ToArray(),
        supplier.Contacts.Select(contact => new SupplierContactView(contact.Id, contact.Name, contact.Email, contact.Phone, contact.Role)).ToArray(),
        supplier.Addresses.Select(address => new SupplierAddressView(
            address.Id, address.Type.ToString(), address.PostalCode, address.Street, address.Number, address.Complement,
            address.District, address.City, address.State, address.Country)).ToArray(),
        supplier.StatusHistory.OrderBy(change => change.Sequence).Select(change => new SupplierStatusChangeView(
            change.Sequence, change.From.ToString(), change.To.ToString(), change.ActorId, change.AtUtc, change.Reason)).ToArray(),
        supplier.Enrichment is { } enrichment
            ? new SupplierEnrichmentView(enrichment.Provider, enrichment.RetrievedAtUtc, enrichment.AcceptedAtUtc, enrichment.AcceptedBy,
                enrichment.AcceptedFields.Split(',', StringSplitOptions.RemoveEmptyEntries), enrichment.RegistrationStatus)
            : null,
        supplier.MissingRequiredData(),
        supplier.CreatedAtUtc,
        supplier.UpdatedAtUtc,
        VersionPrecondition.Format(supplier.Version));
}

/// <summary>Audit actions and failures of the Suppliers module (SUP-*).</summary>
public static class SupplierAudit
{
    public const string Resource = "supplier";

    public const string Created = "supplier.created";
    public const string Updated = "supplier.updated";
    public const string ContactsChanged = "supplier.contacts-changed";
    public const string AddressesChanged = "supplier.addresses-changed";
    public const string CategoriesChanged = "supplier.categories-changed";
    public const string EnrichmentAccepted = "supplier.enrichment-accepted";
    public const string Activated = "supplier.activated";
    public const string Suspended = "supplier.suspended";
    public const string Inactivated = "supplier.inactivated";
    public const string Reactivated = "supplier.reactivated";

    public static readonly ApplicationError TaxIdDuplicate = new(
        ProblemCodes.SupplierTaxIdDuplicate, ErrorCategory.Conflict, "Another supplier already uses this tax identifier in the country.", "SUP-001");

    public static readonly ApplicationError NotFound = new(ProblemCodes.NotFound, ErrorCategory.NotFound, "The supplier was not found.");

    public static ApplicationError? DuplicateTaxId(string constraint) =>
        constraint == Interfaces.Suppliers.ISupplierRepository.TaxIdUniqueKey ? TaxIdDuplicate : null;
}
