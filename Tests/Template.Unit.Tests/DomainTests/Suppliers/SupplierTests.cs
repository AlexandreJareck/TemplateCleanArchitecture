using Shouldly;
using Template.Domain.Common;
using Template.Domain.Suppliers;

namespace Template.Unit.Tests.DomainTests.Suppliers;

/// <summary>SUP-001..005 rules of <see cref="Supplier"/> and <see cref="TaxIdentifier"/>, without mocks.</summary>
public sealed class SupplierTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
    private const string Reason = "Quality incident under investigation";
    private const string ValidCnpj = "11.222.333/0001-81";

    [Theory]
    [InlineData("11.222.333/0001-81", "11222333000181")]
    [InlineData("11222333000181", "11222333000181")]
    [InlineData(" 11 222 333 0001 81 ", "11222333000181")]
    [InlineData("12.ABC.345/01DE-35", "12ABC34501DE35")] // alphanumeric CNPJ (IN RFB 2.229/2024), official example
    [InlineData("12.abc.345/01de-35", "12ABC34501DE35")]
    public void T_SUP_001_a_CnpjIsNormalized_AndCheckDigitsValidated(string input, string expected) =>
        TaxIdentifier.Normalize("BR", input).ShouldBe(expected);

    [Theory]
    [InlineData("11.222.333/0001-82")] // wrong second check digit
    [InlineData("11.222.333/0001-91")] // wrong first check digit
    [InlineData("12.ABC.345/01DE-36")]
    [InlineData("12.ABC.345/01DE-3A")] // check digits are numeric
    [InlineData("00000000000000")] // repeated digits are never issued
    [InlineData("11111111111111")]
    [InlineData("1122233300018")] // 13 characters
    [InlineData("112223330001810")]
    [InlineData("11.222.333/0001#81")]
    [InlineData("")]
    public void T_SUP_001_a_StructurallyInvalidCnpjs_AreRejected(string input) =>
        TaxIdentifier.Normalize("BR", input).ShouldBeNull();

    [Theory]
    [InlineData("US", "12-3456789", "123456789")]
    [InlineData("pt", "PT 501 964 843", "PT501964843")]
    public void T_SUP_001_a_ForeignTaxIds_UseTheFreeFormat(string country, string input, string expected) =>
        TaxIdentifier.Normalize(country, input).ShouldBe(expected);

    [Fact]
    public void T_SUP_001_a_CreateDraft_NormalizesAndFormats()
    {
        var supplier = NewDraft();

        supplier.Status.ShouldBe(SupplierStatus.Draft);
        supplier.Country.ShouldBe("BR");
        supplier.TaxId.ShouldBe("11222333000181");
        TaxIdentifier.Format(supplier.Country, supplier.TaxId).ShouldBe(ValidCnpj);
        supplier.IsEligibleForNewCommitments.ShouldBeFalse();
        supplier.MissingRequiredData().ShouldBe("contactWithEmail");

        Supplier.CreateDraft(Guid.NewGuid(), "Acme", null, "BR", "11.222.333/0001-82", null, Now).Error!.Code.ShouldBe(SupplierErrors.TaxIdInvalidCode);
        Supplier.CreateDraft(Guid.NewGuid(), "Acme", null, "Brazil", ValidCnpj, null, Now).Error!.Field.ShouldBe("country");
        Supplier.CreateDraft(Guid.NewGuid(), " ", null, "BR", ValidCnpj, null, Now).Error!.Field.ShouldBe("legalName");
    }

    [Theory]
    [InlineData("contactWithEmail")]
    [InlineData("address")]
    [InlineData("category")]
    public void T_SUP_002_a_ActivationRequiresEachElement(string missing)
    {
        var supplier = NewDraft();
        if (missing != "contactWithEmail")
            supplier.ReplaceContacts([new ContactInput(null, "Maria Synthetic", "maria@example.invalid", null, "Sales")], Now).ShouldBeNull();
        if (missing != "address")
            supplier.ReplaceAddresses([new AddressInput(null, Address())], Now).ShouldBeNull();
        if (missing != "category")
            supplier.SetCategories([new MasterDataReference(Guid.NewGuid(), true)], Now).ShouldBeNull();

        var error = supplier.Activate(Guid.NewGuid(), Now);

        error!.Code.ShouldBe(SupplierErrors.ActivationIncompleteCode);
        error.RuleId.ShouldBe("SUP-002");
        error.Details!["missing"].ShouldBe(missing);
        supplier.Status.ShouldBe(SupplierStatus.Draft);
    }

    [Fact]
    public void T_SUP_002_a_ContactWithoutEmail_DoesNotCount()
    {
        var supplier = NewDraft();
        supplier.ReplaceContacts([new ContactInput(null, "Phone only", null, "+55 11 0000-0000", null)], Now).ShouldBeNull();
        supplier.ReplaceAddresses([new AddressInput(null, Address())], Now).ShouldBeNull();
        supplier.SetCategories([new MasterDataReference(Guid.NewGuid(), true)], Now).ShouldBeNull();

        supplier.Activate(null, Now)!.Details!["missing"].ShouldBe("contactWithEmail");
    }

    [Fact]
    public void T_SUP_002_a_CompleteDraft_Activates_AndRecordsHistory()
    {
        var supplier = CompleteDraft();
        var actor = Guid.NewGuid();
        var version = supplier.Version;

        supplier.Activate(actor, Now).ShouldBeNull();

        supplier.Status.ShouldBe(SupplierStatus.Active);
        supplier.IsEligibleForNewCommitments.ShouldBeTrue();
        supplier.Version.ShouldBe(version + 1);
        var change = supplier.StatusHistory.Single();
        (change.Sequence, change.From, change.To, change.ActorId, change.AtUtc, change.Reason)
            .ShouldBe((1, SupplierStatus.Draft, SupplierStatus.Active, actor, Now, null));
    }

    [Theory]
    [InlineData(SupplierStatus.Draft, false)]
    [InlineData(SupplierStatus.Active, true)]
    [InlineData(SupplierStatus.Suspended, false)]
    [InlineData(SupplierStatus.Inactive, false)]
    public void T_SUP_003_a_OnlyActiveSuppliersAreEligible(SupplierStatus status, bool eligible)
    {
        var supplier = InStatus(status);

        supplier.IsEligibleForNewCommitments.ShouldBe(eligible);
        if (eligible)
            supplier.EnsureEligibleForNewCommitment().ShouldBeNull();
        else
            supplier.EnsureEligibleForNewCommitment()!.Code.ShouldBe(SupplierErrors.NotEligibleCode);
    }

    [Fact]
    public void Lifecycle_FollowsTheWorkflowTable()
    {
        var supplier = InStatus(SupplierStatus.Active);

        supplier.Suspend(Reason, null, Now).ShouldBeNull();
        supplier.Suspend(Reason, null, Now)!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        supplier.Reactivate(Reason, null, Now).ShouldBeNull();
        supplier.Inactivate(Reason, null, Now).ShouldBeNull();
        supplier.Suspend(Reason, null, Now)!.Details!["currentState"].ShouldBe("Inactive");
        supplier.Activate(null, Now)!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        supplier.Reactivate(Reason, null, Now).ShouldBeNull();
        supplier.Suspend(Reason, null, Now).ShouldBeNull();
        supplier.Inactivate(Reason, null, Now).ShouldBeNull();

        supplier.StatusHistory.Select(change => change.To).ShouldBe([
            SupplierStatus.Active, SupplierStatus.Suspended, SupplierStatus.Active, SupplierStatus.Inactive,
            SupplierStatus.Active, SupplierStatus.Suspended, SupplierStatus.Inactive]);
        supplier.StatusHistory.Select(change => change.Sequence).ShouldBe([1, 2, 3, 4, 5, 6, 7]);
        supplier.StatusHistory.Skip(1).ShouldAllBe(change => change.Reason == Reason);
    }

    [Fact]
    public void DraftCannotBeSuspendedOrInactivated()
    {
        var supplier = NewDraft();

        supplier.Suspend(Reason, null, Now)!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        supplier.Inactivate(Reason, null, Now)!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        supplier.Reactivate(Reason, null, Now)!.Code.ShouldBe(DomainError.InvalidTransitionCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too short")]
    public void T_SUP_005_SuspendInactivateAndReactivate_RequireAReason(string? reason)
    {
        var supplier = InStatus(SupplierStatus.Active);

        supplier.Suspend(reason, null, Now)!.Code.ShouldBe(DomainError.ReasonRequiredCode);
        supplier.Inactivate(reason, null, Now)!.Code.ShouldBe(DomainError.ReasonRequiredCode);
        supplier.Suspend(Reason, null, Now).ShouldBeNull();
        supplier.Reactivate(reason, null, Now)!.RuleId.ShouldBe("SUP-005");
        supplier.Status.ShouldBe(SupplierStatus.Suspended);
    }

    [Fact]
    public void T_SUP_002_ActiveSupplier_CannotLoseItsRequiredData()
    {
        var supplier = InStatus(SupplierStatus.Active);
        var version = supplier.Version;

        supplier.ReplaceContacts([new ContactInput(null, "No email", null, null, null)], Now)!.Code.ShouldBe(SupplierErrors.ActivationIncompleteCode);
        supplier.ReplaceAddresses([], Now)!.Code.ShouldBe(SupplierErrors.ActivationIncompleteCode);
        supplier.SetCategories([], Now)!.Code.ShouldBe(SupplierErrors.ActivationIncompleteCode);

        supplier.Version.ShouldBe(version);
        supplier.Contacts.Count.ShouldBe(1);
        supplier.Addresses.Count.ShouldBe(1);
        supplier.Categories.Count.ShouldBe(1);
    }

    [Fact]
    public void ReplaceContacts_UpdatesAddsAndRemoves_Atomically()
    {
        var supplier = NewDraft();
        supplier.ReplaceContacts([
            new ContactInput(null, "First", "first@example.invalid", null, null),
            new ContactInput(null, "Second", "second@example.invalid", null, null)], Now).ShouldBeNull();
        var first = supplier.Contacts.Single(contact => contact.Name == "First");

        supplier.ReplaceContacts([
            new ContactInput(first.Id, "First Renamed", "FIRST@example.invalid", "+55 11 1111-1111", "Sales"),
            new ContactInput(null, "Third", null, null, null)], Now).ShouldBeNull();

        supplier.Contacts.Select(contact => contact.Name).Order().ShouldBe(["First Renamed", "Third"]);
        supplier.Contacts.Single(contact => contact.Id == first.Id).Email.ShouldBe("first@example.invalid");

        var before = supplier.Contacts.Select(contact => contact.Name).ToArray();
        var error = supplier.ReplaceContacts([
            new ContactInput(null, "Valid", "valid@example.invalid", null, null),
            new ContactInput(null, "Invalid", "not-an-email", null, null)], Now);
        error!.Field.ShouldBe("contacts[1].email");
        supplier.Contacts.Select(contact => contact.Name).ShouldBe(before);

        supplier.ReplaceContacts([new ContactInput(Guid.NewGuid(), "Unknown", null, null, null)], Now)!.Kind.ShouldBe(DomainErrorKind.NotFound);
    }

    [Fact]
    public void Addresses_AreNormalized_ForBrazil()
    {
        var supplier = NewDraft();

        supplier.ReplaceAddresses([new AddressInput(null, Address() with { PostalCode = "01310-100", State = "sp" })], Now).ShouldBeNull();

        var address = supplier.Addresses.Single();
        (address.PostalCode, address.State, address.Country).ShouldBe(("01310100", "SP", "BR"));
        supplier.ReplaceAddresses([new AddressInput(null, Address() with { PostalCode = "0131-010" })], Now)!.Field.ShouldBe("addresses[0].address.postalCode");
        supplier.ReplaceAddresses([new AddressInput(null, Address() with { State = "SPX" })], Now)!.Field.ShouldBe("addresses[0].address.state");
    }

    [Fact]
    public void Categories_NewLinksMustBeActive_ExistingInactiveLinksMayStay()
    {
        var supplier = NewDraft();
        var kept = new MasterDataReference(Guid.NewGuid(), true);
        supplier.SetCategories([kept], Now).ShouldBeNull();

        supplier.SetCategories([kept with { IsActive = false }], Now).ShouldBeNull();
        supplier.SetCategories([kept with { IsActive = false }, new MasterDataReference(Guid.NewGuid(), false)], Now)!.Code.ShouldBe("catalog.inactive");
        supplier.Categories.Select(link => link.CategoryId).ShouldBe([kept.Id]);
    }

    [Fact]
    public void TaxId_ChangesOnlyWhileDraft()
    {
        var supplier = NewDraft();
        supplier.UpdateDetails("Acme Ltda", "Acme", "BR", "12.ABC.345/01DE-35", Now).ShouldBeNull();
        supplier.TaxId.ShouldBe("12ABC34501DE35");

        var active = InStatus(SupplierStatus.Active);
        active.UpdateDetails("Acme Ltda", null, "BR", "12.ABC.345/01DE-35", Now)!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        active.UpdateDetails("Acme Comércio Ltda", "Acme", "BR", ValidCnpj, Now).ShouldBeNull();
        active.LegalName.ShouldBe("Acme Comércio Ltda");
    }

    [Fact]
    public void T_INT_002_EnrichmentSnapshot_RecordsProvenance()
    {
        var supplier = NewDraft();
        var actor = Guid.NewGuid();

        supplier.RecordEnrichment("BrasilAPI", Now.AddMinutes(-5), ["tradeName", "legalName", "legalName"], " ATIVA ", "01310100", actor, Now).ShouldBeNull();

        var snapshot = supplier.Enrichment!;
        (snapshot.Provider, snapshot.RetrievedAtUtc, snapshot.AcceptedAtUtc, snapshot.AcceptedBy, snapshot.AcceptedFields, snapshot.RegistrationStatus)
            .ShouldBe(("BrasilAPI", Now.AddMinutes(-5), Now, (Guid?)actor, "legalName,tradeName", "ATIVA"));
        supplier.RecordEnrichment("BrasilAPI", Now.AddMinutes(1), ["legalName"], null, null, actor, Now)!.Field.ShouldBe("retrievedAt");
        supplier.RecordEnrichment("BrasilAPI", Now, [], null, null, actor, Now)!.Field.ShouldBe("acceptedFields");
    }

    [Fact]
    public void T_SUP_004_StatusChangesNeverTouchOtherData()
    {
        var supplier = InStatus(SupplierStatus.Active);
        var snapshot = (supplier.LegalName, supplier.TaxId, Contacts: supplier.Contacts.Count, Addresses: supplier.Addresses.Count);

        supplier.Suspend(Reason, null, Now).ShouldBeNull();
        supplier.Inactivate(Reason, null, Now).ShouldBeNull();

        (supplier.LegalName, supplier.TaxId, supplier.Contacts.Count, supplier.Addresses.Count).ShouldBe(snapshot);
    }

    private static Supplier NewDraft() => Supplier.CreateDraft(Guid.NewGuid(), "Acme Comércio Ltda", "Acme", "br", ValidCnpj, null, Now).Supplier!;

    private static Supplier CompleteDraft()
    {
        var supplier = NewDraft();
        supplier.ReplaceContacts([new ContactInput(null, "Maria Synthetic", "maria@example.invalid", null, "Sales")], Now).ShouldBeNull();
        supplier.ReplaceAddresses([new AddressInput(null, Address())], Now).ShouldBeNull();
        supplier.SetCategories([new MasterDataReference(Guid.NewGuid(), true)], Now).ShouldBeNull();
        return supplier;
    }

    private static Supplier InStatus(SupplierStatus status)
    {
        var supplier = status == SupplierStatus.Draft ? NewDraft() : CompleteDraft();
        if (status != SupplierStatus.Draft)
            supplier.Activate(null, Now).ShouldBeNull();
        if (status == SupplierStatus.Suspended)
            supplier.Suspend(Reason, null, Now).ShouldBeNull();
        if (status == SupplierStatus.Inactive)
            supplier.Inactivate(Reason, null, Now).ShouldBeNull();
        return supplier;
    }

    private static AddressValues Address() => new(
        SupplierAddressType.Headquarters, "01310100", "Avenida Paulista", "1000", null, "Bela Vista", "São Paulo", "SP", "BR");
}
