using System;
using System.Collections.Generic;
using System.Linq;
using Template.Domain.Common;

namespace Template.Domain.Suppliers;

/// <summary>
/// Company that can be invited, selected and ordered from (docs/product/domain-model.md §3). The normalized tax ID is
/// unique per country (SUP-001, enforced by a unique index); activation needs complete data (SUP-002); only
/// <see cref="SupplierStatus.Active"/> suppliers are eligible for new commitments (SUP-003); suspension or
/// inactivation never touches snapshots held by other modules (SUP-004); suspend, inactivate and reactivate need a
/// reason (SUP-005). Every change increments <see cref="VersionedAggregateRoot.Version"/>, including child changes.
/// </summary>
public sealed class Supplier : VersionedAggregateRoot
{
    public const int LegalNameMaxLength = 200;
    public const int TradeNameMaxLength = 200;
    public const int MaxContacts = 20;
    public const int MaxAddresses = 10;
    public const int MaxCategories = 20;

    private readonly List<SupplierContact> contacts = [];
    private readonly List<SupplierAddress> addresses = [];
    private readonly List<SupplierCategory> categories = [];
    private readonly List<SupplierStatusChange> statusHistory = [];

    private Supplier()
    {
        LegalName = string.Empty;
        Country = string.Empty;
        TaxId = string.Empty;
    }

    public string LegalName { get; private set; }
    public string? TradeName { get; private set; }

    /// <summary>ISO 3166-1 alpha-2 country of the tax identifier.</summary>
    public string Country { get; private set; }

    /// <summary>Normalized tax identifier (<see cref="TaxIdentifier.Normalize"/>).</summary>
    public string TaxId { get; private set; }

    public SupplierStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public SupplierEnrichmentSnapshot? Enrichment { get; private set; }

    public IReadOnlyCollection<SupplierContact> Contacts => contacts;
    public IReadOnlyCollection<SupplierAddress> Addresses => addresses;
    public IReadOnlyCollection<SupplierCategory> Categories => categories;
    public IReadOnlyCollection<SupplierStatusChange> StatusHistory => statusHistory;

    /// <summary>SUP-003: only Active suppliers may be newly invited, awarded or ordered from.</summary>
    public bool IsEligibleForNewCommitments => Status == SupplierStatus.Active;

    /// <summary>Creates a <see cref="SupplierStatus.Draft"/> supplier with a normalized tax ID (SUP-001).</summary>
    public static (Supplier? Supplier, DomainError? Error) CreateDraft(
        Guid id, string? legalName, string? tradeName, string? country, string? taxId, Guid? createdBy, DateTime nowUtc)
    {
        MasterDataRules.EnsureId(id, nameof(id));
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        var normalizedCountry = TaxIdentifier.NormalizeCountry(country);
        if (normalizedCountry is null)
            return (null, SupplierErrors.CountryInvalid);
        var normalizedTaxId = TaxIdentifier.Normalize(normalizedCountry, taxId);
        if (normalizedTaxId is null)
            return (null, SupplierErrors.TaxIdInvalid);
        var names = NormalizeNames(legalName, tradeName);
        if (names.Error is not null)
            return (null, names.Error);

        return (new Supplier
        {
            Id = id,
            LegalName = names.LegalName!,
            TradeName = names.TradeName,
            Country = normalizedCountry,
            TaxId = normalizedTaxId,
            Status = SupplierStatus.Draft,
            CreatedAtUtc = nowUtc,
            CreatedBy = createdBy,
            UpdatedAtUtc = nowUtc
        }, null);
    }

    /// <summary>
    /// Changes names; the country and tax ID may change only while <see cref="SupplierStatus.Draft"/> (a typo fix
    /// before activation). Uniqueness of the new tax ID is checked by the use case and the unique index.
    /// </summary>
    public DomainError? UpdateDetails(string? legalName, string? tradeName, string? country, string? taxId, DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        var names = NormalizeNames(legalName, tradeName);
        if (names.Error is not null)
            return names.Error;
        var normalizedCountry = TaxIdentifier.NormalizeCountry(country);
        if (normalizedCountry is null)
            return SupplierErrors.CountryInvalid;
        var normalizedTaxId = TaxIdentifier.Normalize(normalizedCountry, taxId);
        if (normalizedTaxId is null)
            return SupplierErrors.TaxIdInvalid;

        var identityChanges = normalizedCountry != Country || normalizedTaxId != TaxId;
        if (identityChanges && Status != SupplierStatus.Draft)
            return DomainError.InvalidTransition(Status.ToString(), "The tax identifier can change only while the supplier is a draft.", "SUP-001");

        if (!identityChanges && names.LegalName == LegalName && names.TradeName == TradeName)
            return null;

        LegalName = names.LegalName!;
        TradeName = names.TradeName;
        Country = normalizedCountry;
        TaxId = normalizedTaxId;
        Touch(nowUtc);
        return null;
    }

    /// <summary>
    /// Replaces the contact list atomically: entries with an existing <see cref="ContactInput.Id"/> are updated, entries
    /// without one are added, missing contacts are removed. Nothing changes when any entry is invalid or when a
    /// non-draft supplier would lose its last contact with email (SUP-002).
    /// </summary>
    public DomainError? ReplaceContacts(IReadOnlyList<ContactInput> inputs, DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count > MaxContacts)
            return SupplierErrors.Validation("contacts", $"A supplier has at most {MaxContacts} contacts.");

        var normalized = new List<(Guid? Id, string Name, string? Email, string? Phone, string? Role)>();
        for (var index = 0; index < inputs.Count; index++)
        {
            var input = inputs[index];
            if (input.Id is { } existingId && contacts.All(contact => contact.Id != existingId))
                return SupplierErrors.NotFound("contact");
            var values = NormalizeContact(input.Name, input.Email, input.Phone, input.Role);
            if (values.Error is not null)
                return values.Error with { Field = $"contacts[{index}].{values.Error.Field}" };
            normalized.Add((input.Id, values.Name!, values.Email, values.Phone, values.Role));
        }

        if (normalized.Where(value => value.Id is not null).GroupBy(value => value.Id).Any(group => group.Count() > 1))
            return SupplierErrors.Validation("contacts", "A contact appears more than once.");
        if (Status != SupplierStatus.Draft && !normalized.Any(value => value.Email is not null))
            return SupplierErrors.ActivationIncomplete("contactWithEmail");

        var before = contacts.Select(contact => (contact.Id, contact.Name, contact.Email, contact.Phone, contact.Role)).ToArray();
        var keep = normalized.Where(value => value.Id is not null).Select(value => value.Id!.Value).ToHashSet();
        contacts.RemoveAll(contact => !keep.Contains(contact.Id));
        foreach (var value in normalized)
        {
            if (value.Id is { } id)
                contacts.Single(contact => contact.Id == id).Change(value.Name, value.Email, value.Phone, value.Role);
            else
                contacts.Add(new SupplierContact(Guid.NewGuid(), Id, value.Name, value.Email, value.Phone, value.Role));
        }

        var after = contacts.Select(contact => (contact.Id, contact.Name, contact.Email, contact.Phone, contact.Role)).ToArray();
        if (!before.SequenceEqual(after))
            Touch(nowUtc);
        return null;
    }

    /// <summary>Replaces the address list atomically, with the same rules as <see cref="ReplaceContacts"/>.</summary>
    public DomainError? ReplaceAddresses(IReadOnlyList<AddressInput> inputs, DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count > MaxAddresses)
            return SupplierErrors.Validation("addresses", $"A supplier has at most {MaxAddresses} addresses.");

        var normalized = new List<(Guid? Id, AddressValues Values)>();
        for (var index = 0; index < inputs.Count; index++)
        {
            var input = inputs[index];
            if (input.Id is { } existingId && addresses.All(address => address.Id != existingId))
                return SupplierErrors.NotFound("address");
            var values = NormalizeAddress(input.Values);
            if (values.Error is not null)
                return values.Error with { Field = $"addresses[{index}].{values.Error.Field}" };
            normalized.Add((input.Id, values.Values!));
        }

        if (normalized.Where(value => value.Id is not null).GroupBy(value => value.Id).Any(group => group.Count() > 1))
            return SupplierErrors.Validation("addresses", "An address appears more than once.");
        if (Status != SupplierStatus.Draft && normalized.Count == 0)
            return SupplierErrors.ActivationIncomplete("address");

        var before = addresses.Select(address => (address.Id, address.Values)).ToArray();
        var keep = normalized.Where(value => value.Id is not null).Select(value => value.Id!.Value).ToHashSet();
        addresses.RemoveAll(address => !keep.Contains(address.Id));
        foreach (var value in normalized)
        {
            if (value.Id is { } id)
                addresses.Single(address => address.Id == id).Change(value.Values);
            else
                addresses.Add(new SupplierAddress(Guid.NewGuid(), Id, value.Values));
        }

        var after = addresses.Select(address => (address.Id, address.Values)).ToArray();
        if (!before.SequenceEqual(after))
            Touch(nowUtc);
        return null;
    }

    public (SupplierAddress? Address, DomainError? Error) AddAddress(Guid addressId, AddressValues? values, DateTime nowUtc)
    {
        MasterDataRules.EnsureId(addressId, nameof(addressId));
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (addresses.Count >= MaxAddresses)
            return (null, SupplierErrors.Validation("addresses", $"A supplier has at most {MaxAddresses} addresses."));
        var normalized = NormalizeAddress(values);
        if (normalized.Error is not null)
            return (null, normalized.Error);

        var address = new SupplierAddress(addressId, Id, normalized.Values!);
        addresses.Add(address);
        Touch(nowUtc);
        return (address, null);
    }

    /// <summary>
    /// Replaces the category set. Categories belong to the Catalog module and are passed by ID and state only
    /// (ADR-0001). Newly added categories must be active (CAT-002); already linked categories that became inactive
    /// may stay (history).
    /// </summary>
    public DomainError? SetCategories(IReadOnlyCollection<MasterDataReference> newCategories, DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentNullException.ThrowIfNull(newCategories);
        var ids = newCategories.Select(category => category.Id).Distinct().ToArray();
        if (ids.Length > MaxCategories)
            return SupplierErrors.Validation("categoryIds", $"A supplier has at most {MaxCategories} categories.");
        var current = categories.Select(link => link.CategoryId).ToHashSet();
        foreach (var category in newCategories.Where(category => !current.Contains(category.Id)))
        {
            if (!category.IsActive)
                return new DomainError(MasterDataReference.InactiveCatalogCode, DomainErrorKind.BusinessRule, "The category is inactive.", "CAT-002");
        }

        if (ids.Length == 0 && Status != SupplierStatus.Draft)
            return SupplierErrors.ActivationIncomplete("category");
        if (current.SetEquals(ids))
            return null;

        categories.RemoveAll(link => !ids.Contains(link.CategoryId));
        foreach (var id in ids.Where(id => !current.Contains(id)))
            categories.Add(new SupplierCategory(Id, id));
        Touch(nowUtc);
        return null;
    }

    /// <summary>
    /// Records accepted enrichment provenance (INT-002). The accepted values themselves are applied by the caller
    /// through the ordinary behavior methods, so every invariant still applies to them.
    /// </summary>
    public DomainError? RecordEnrichment(
        string? provider,
        DateTime retrievedAtUtc,
        IReadOnlyCollection<string> acceptedFields,
        string? registrationStatus,
        string? postalCode,
        Guid? acceptedBy,
        DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        MasterDataRules.EnsureUtc(retrievedAtUtc, nameof(retrievedAtUtc));
        var providerName = MasterDataRules.NormalizeName(provider, SupplierEnrichmentSnapshot.ProviderMaxLength);
        if (providerName is null)
            return SupplierErrors.Validation("provider", "The enrichment provider is required.", "INT-002");
        if (retrievedAtUtc > nowUtc)
            return SupplierErrors.Validation("retrievedAt", "The retrieval instant cannot be in the future.", "INT-002");
        var fields = string.Join(',', acceptedFields.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        if (fields.Length is 0 or > SupplierEnrichmentSnapshot.AcceptedFieldsMaxLength)
            return SupplierErrors.Validation("acceptedFields", "At least one accepted field is required.", "INT-002");
        var status = registrationStatus?.Trim();
        if (status is { Length: > SupplierEnrichmentSnapshot.RegistrationStatusMaxLength })
            status = status[..SupplierEnrichmentSnapshot.RegistrationStatusMaxLength];

        Enrichment = new SupplierEnrichmentSnapshot(
            providerName, retrievedAtUtc, nowUtc, acceptedBy, fields, LegalName, TradeName,
            string.IsNullOrEmpty(status) ? null : status, postalCode);
        Touch(nowUtc);
        return null;
    }

    /// <summary><c>Draft → Active</c> (SUP-002).</summary>
    public DomainError? Activate(Guid? actorId, DateTime nowUtc) =>
        Status != SupplierStatus.Draft
            ? DomainError.InvalidTransition(Status.ToString(), "Only a draft supplier can be activated; use reactivate otherwise.", "SUP-002")
            : MissingRequiredData() is { } missing
                ? SupplierErrors.ActivationIncomplete(missing)
                : Transition(SupplierStatus.Active, actorId, reason: null, nowUtc);

    /// <summary><c>Active → Suspended</c> with reason (SUP-004, SUP-005).</summary>
    public DomainError? Suspend(string? reason, Guid? actorId, DateTime nowUtc) =>
        RequireReason(reason) is { } error
            ? error
            : Status != SupplierStatus.Active
                ? DomainError.InvalidTransition(Status.ToString(), "Only an active supplier can be suspended.", "SUP-005")
                : Transition(SupplierStatus.Suspended, actorId, reason, nowUtc);

    /// <summary><c>Active/Suspended → Inactive</c> with reason (SUP-004, SUP-005).</summary>
    public DomainError? Inactivate(string? reason, Guid? actorId, DateTime nowUtc) =>
        RequireReason(reason) is { } error
            ? error
            : Status is not (SupplierStatus.Active or SupplierStatus.Suspended)
                ? DomainError.InvalidTransition(Status.ToString(), "Only an active or suspended supplier can be inactivated.", "SUP-005")
                : Transition(SupplierStatus.Inactive, actorId, reason, nowUtc);

    /// <summary><c>Suspended/Inactive → Active</c> with reason; the data must still be complete (SUP-002, SUP-005).</summary>
    public DomainError? Reactivate(string? reason, Guid? actorId, DateTime nowUtc) =>
        RequireReason(reason) is { } error
            ? error
            : Status is not (SupplierStatus.Suspended or SupplierStatus.Inactive)
                ? DomainError.InvalidTransition(Status.ToString(), "Only a suspended or inactive supplier can be reactivated.", "SUP-005")
                : MissingRequiredData() is { } missing
                    ? SupplierErrors.ActivationIncomplete(missing)
                    : Transition(SupplierStatus.Active, actorId, reason, nowUtc);

    /// <summary>SUP-003 guard used by Sourcing (Phase 29) and Purchasing (Phase 32).</summary>
    public DomainError? EnsureEligibleForNewCommitment() => IsEligibleForNewCommitments ? null : SupplierErrors.NotEligible;

    /// <summary>Normalizes address input; Brazilian postal codes (CEP) must have 8 digits and states 2 letters.</summary>
    public static (AddressValues? Values, DomainError? Error) NormalizeAddress(AddressValues? values)
    {
        if (values is null)
            return (null, SupplierErrors.Validation("address", "The address is required."));
        if (!Enum.IsDefined(values.Type))
            return (null, SupplierErrors.Validation("address.type", "The address type is invalid."));
        var country = TaxIdentifier.NormalizeCountry(values.Country);
        if (country is null)
            return (null, SupplierErrors.Validation("address.country", "The country must be an ISO 3166-1 alpha-2 code."));
        var postalCode = NormalizePostalCode(country, values.PostalCode);
        if (postalCode is null)
            return (null, SupplierErrors.Validation("address.postalCode", "The postal code is invalid for the country."));
        var street = MasterDataRules.NormalizeName(values.Street, SupplierAddress.StreetMaxLength);
        var city = MasterDataRules.NormalizeName(values.City, SupplierAddress.CityMaxLength);
        var state = MasterDataRules.NormalizeName(values.State, SupplierAddress.StateMaxLength)?.ToUpperInvariant();
        if (street is null)
            return (null, SupplierErrors.Validation("address.street", "The street is required (at most 200 characters)."));
        if (city is null)
            return (null, SupplierErrors.Validation("address.city", "The city is required (at most 100 characters)."));
        if (state is null || country == TaxIdentifier.Brazil && (state.Length != 2 || !state.All(char.IsAsciiLetterUpper)))
            return (null, SupplierErrors.Validation("address.state", "The state is required (two letters for Brazil)."));
        if (!Optional(values.Number, SupplierAddress.NumberMaxLength, out var number))
            return (null, SupplierErrors.Validation("address.number", "The number has at most 20 characters."));
        if (!Optional(values.Complement, SupplierAddress.ComplementMaxLength, out var complement))
            return (null, SupplierErrors.Validation("address.complement", "The complement has at most 100 characters."));
        if (!Optional(values.District, SupplierAddress.DistrictMaxLength, out var district))
            return (null, SupplierErrors.Validation("address.district", "The district has at most 100 characters."));

        return (new AddressValues(values.Type, postalCode, street, number, complement, district, city, state, country), null);
    }

    /// <summary>Digits only for Brazil (8 digits); otherwise trimmed and upper-cased, 1–20 characters.</summary>
    public static string? NormalizePostalCode(string? country, string? postalCode)
    {
        var trimmed = postalCode?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        if (country == TaxIdentifier.Brazil)
        {
            var digits = new string(trimmed.Where(character => character != '-' && character != '.' && character != ' ').ToArray());
            return digits.Length == 8 && digits.All(char.IsAsciiDigit) ? digits : null;
        }

        var normalized = trimmed.ToUpperInvariant();
        return normalized.Length <= SupplierAddress.PostalCodeMaxLength ? normalized : null;
    }

    /// <summary>
    /// SUP-002 completeness; <c>null</c> when complete, otherwise the first missing element. Legal name and tax ID are
    /// guaranteed by construction (<see cref="CreateDraft"/> and <see cref="UpdateDetails"/> reject them empty).
    /// </summary>
    public string? MissingRequiredData()
    {
        if (!contacts.Any(contact => contact.Email is not null))
            return "contactWithEmail";
        if (addresses.Count == 0)
            return "address";
        if (categories.Count == 0)
            return "category";
        return null;
    }

    private DomainError? Transition(SupplierStatus to, Guid? actorId, string? reason, DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        statusHistory.Add(new SupplierStatusChange(
            Guid.NewGuid(), Id, statusHistory.Count + 1, Status, to, actorId == Guid.Empty ? null : actorId, nowUtc,
            reason is null ? null : MasterDataRules.NormalizeReason(reason)));
        Status = to;
        Touch(nowUtc);
        return null;
    }

    private static DomainError? RequireReason(string? reason) =>
        MasterDataRules.NormalizeReason(reason) is null
            ? DomainError.ReasonRequired("SUP-005", MasterDataRules.ReasonMinLength, MasterDataRules.ReasonMaxLength)
            : null;

    private static (string? LegalName, string? TradeName, DomainError? Error) NormalizeNames(string? legalName, string? tradeName)
    {
        var legal = MasterDataRules.NormalizeName(legalName, LegalNameMaxLength);
        if (legal is null)
            return (null, null, SupplierErrors.Validation("legalName", "The legal name must have 1 to 200 characters.", "SUP-002"));
        if (!Optional(tradeName, TradeNameMaxLength, out var trade))
            return (null, null, SupplierErrors.Validation("tradeName", "The trade name has at most 200 characters."));
        return (legal, trade, null);
    }

    private static (string? Name, string? Email, string? Phone, string? Role, DomainError? Error) NormalizeContact(
        string? name, string? email, string? phone, string? role)
    {
        var normalizedName = MasterDataRules.NormalizeName(name, SupplierContact.NameMaxLength);
        if (normalizedName is null)
            return (null, null, null, null, SupplierErrors.Validation("name", "The contact name must have 1 to 200 characters."));
        if (!Optional(email, SupplierContact.EmailMaxLength, out var normalizedEmail) || normalizedEmail is not null && !LooksLikeEmail(normalizedEmail))
            return (null, null, null, null, SupplierErrors.Validation("email", "The email address is invalid.", "SUP-002"));
        if (!Optional(phone, SupplierContact.PhoneMaxLength, out var normalizedPhone))
            return (null, null, null, null, SupplierErrors.Validation("phone", "The phone has at most 30 characters."));
        if (!Optional(role, SupplierContact.RoleMaxLength, out var normalizedRole))
            return (null, null, null, null, SupplierErrors.Validation("role", "The role has at most 100 characters."));
        return (normalizedName, normalizedEmail?.ToLowerInvariant(), normalizedPhone, normalizedRole, null);
    }

    private static bool LooksLikeEmail(string value)
    {
        var at = value.IndexOf('@');
        return at > 0 && at == value.LastIndexOf('@') && at < value.Length - 3
            && value.IndexOf('.', at) > at + 1 && !value.EndsWith('.') && !value.Any(char.IsWhiteSpace);
    }

    private static bool Optional(string? value, int maxLength, out string? normalized)
    {
        normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is null || normalized.Length <= maxLength;
    }

    private void Touch(DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        IncrementVersion();
    }
}
