using System;

namespace Template.Domain.Suppliers;

/// <summary>Supplier lifecycle (docs/product/workflows.md §7).</summary>
public enum SupplierStatus
{
    Draft,
    Active,
    Suspended,
    Inactive
}

public enum SupplierAddressType
{
    Headquarters,
    Billing,
    Shipping,
    Other
}

/// <summary>Commercial contact. Personal data: readable only with <c>suppliers.read</c> and never logged (SUP-006).</summary>
public sealed class SupplierContact
{
    public const int NameMaxLength = 200;
    public const int EmailMaxLength = 254;
    public const int PhoneMaxLength = 30;
    public const int RoleMaxLength = 100;

    private SupplierContact()
    {
        Name = string.Empty;
    }

    internal SupplierContact(Guid id, Guid supplierId, string name, string? email, string? phone, string? role)
    {
        Id = id;
        SupplierId = supplierId;
        Name = name;
        Email = email;
        Phone = phone;
        Role = role;
    }

    public Guid Id { get; private set; }
    public Guid SupplierId { get; private set; }
    public string Name { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? Role { get; private set; }

    internal void Change(string name, string? email, string? phone, string? role)
    {
        Name = name;
        Email = email;
        Phone = phone;
        Role = role;
    }
}

/// <summary>Postal address. Full addresses are never written to logs or audit details (AUD-003).</summary>
public sealed class SupplierAddress
{
    public const int PostalCodeMaxLength = 20;
    public const int StreetMaxLength = 200;
    public const int NumberMaxLength = 20;
    public const int ComplementMaxLength = 100;
    public const int DistrictMaxLength = 100;
    public const int CityMaxLength = 100;
    public const int StateMaxLength = 50;

    private SupplierAddress()
    {
        PostalCode = string.Empty;
        Street = string.Empty;
        City = string.Empty;
        State = string.Empty;
        Country = string.Empty;
    }

    internal SupplierAddress(Guid id, Guid supplierId, AddressValues values)
        : this()
    {
        Id = id;
        SupplierId = supplierId;
        Change(values);
    }

    public Guid Id { get; private set; }
    public Guid SupplierId { get; private set; }
    public SupplierAddressType Type { get; private set; }
    public string PostalCode { get; private set; }
    public string Street { get; private set; }
    public string? Number { get; private set; }
    public string? Complement { get; private set; }
    public string? District { get; private set; }
    public string City { get; private set; }
    public string State { get; private set; }
    public string Country { get; private set; }

    internal void Change(AddressValues values)
    {
        Type = values.Type;
        PostalCode = values.PostalCode;
        Street = values.Street;
        Number = values.Number;
        Complement = values.Complement;
        District = values.District;
        City = values.City;
        State = values.State;
        Country = values.Country;
    }

    internal AddressValues Values => new(Type, PostalCode, Street, Number, Complement, District, City, State, Country);
}

/// <summary>Normalized address fields (see <see cref="Supplier.NormalizeAddress"/>).</summary>
public sealed record AddressValues(
    SupplierAddressType Type,
    string PostalCode,
    string Street,
    string? Number,
    string? Complement,
    string? District,
    string City,
    string State,
    string Country);

/// <summary>Link to a catalog <c>Category</c> by ID only (ADR-0001 module boundary).</summary>
public sealed class SupplierCategory
{
    private SupplierCategory()
    {
    }

    internal SupplierCategory(Guid supplierId, Guid categoryId)
    {
        SupplierId = supplierId;
        CategoryId = categoryId;
    }

    public Guid SupplierId { get; private set; }
    public Guid CategoryId { get; private set; }
}

/// <summary>Appended on every status transition; never edited (domain-model.md §2 <c>StateChange</c>).</summary>
public sealed class SupplierStatusChange
{
    private SupplierStatusChange()
    {
    }

    internal SupplierStatusChange(Guid id, Guid supplierId, int sequence, SupplierStatus from, SupplierStatus to, Guid? actorId, DateTime atUtc, string? reason)
    {
        Id = id;
        SupplierId = supplierId;
        Sequence = sequence;
        From = from;
        To = to;
        ActorId = actorId;
        AtUtc = atUtc;
        Reason = reason;
    }

    public Guid Id { get; private set; }
    public Guid SupplierId { get; private set; }
    public int Sequence { get; private set; }
    public SupplierStatus From { get; private set; }
    public SupplierStatus To { get; private set; }
    public Guid? ActorId { get; private set; }
    public DateTime AtUtc { get; private set; }
    public string? Reason { get; private set; }
}

/// <summary>
/// Accepted external enrichment (INT-002): provider, retrieval instant, accepting user and the values the user
/// accepted, possibly after editing them. Nothing is read live from the provider afterwards. This is advisory
/// provenance, never a government verification (ADR-0007).
/// </summary>
public sealed class SupplierEnrichmentSnapshot
{
    public const int ProviderMaxLength = 50;
    public const int RegistrationStatusMaxLength = 100;
    public const int AcceptedFieldsMaxLength = 200;

    private SupplierEnrichmentSnapshot()
    {
        Provider = string.Empty;
        AcceptedFields = string.Empty;
    }

    internal SupplierEnrichmentSnapshot(
        string provider, DateTime retrievedAtUtc, DateTime acceptedAtUtc, Guid? acceptedBy, string acceptedFields,
        string? legalName, string? tradeName, string? registrationStatus, string? postalCode)
    {
        Provider = provider;
        RetrievedAtUtc = retrievedAtUtc;
        AcceptedAtUtc = acceptedAtUtc;
        AcceptedBy = acceptedBy;
        AcceptedFields = acceptedFields;
        LegalName = legalName;
        TradeName = tradeName;
        RegistrationStatus = registrationStatus;
        PostalCode = postalCode;
    }

    public string Provider { get; private set; }
    public DateTime RetrievedAtUtc { get; private set; }
    public DateTime AcceptedAtUtc { get; private set; }
    public Guid? AcceptedBy { get; private set; }

    /// <summary>Comma-separated names of the accepted fields, for example <c>legalName,tradeName,address</c>.</summary>
    public string AcceptedFields { get; private set; }

    public string? LegalName { get; private set; }
    public string? TradeName { get; private set; }

    /// <summary>Registration status text reported by the provider (informational only).</summary>
    public string? RegistrationStatus { get; private set; }

    public string? PostalCode { get; private set; }
}

/// <summary>Contact entry of <see cref="Supplier.ReplaceContacts"/>; <see cref="Id"/> is <c>null</c> for a new contact.</summary>
public sealed record ContactInput(Guid? Id, string? Name, string? Email, string? Phone, string? Role);

/// <summary>Address entry of <see cref="Supplier.ReplaceAddresses"/>; <see cref="Id"/> is <c>null</c> for a new address.</summary>
public sealed record AddressInput(Guid? Id, AddressValues? Values);
