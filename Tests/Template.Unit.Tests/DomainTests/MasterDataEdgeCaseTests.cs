using Shouldly;
using Template.Domain.Catalog;
using Template.Domain.Common;
using Template.Domain.Organization;
using Template.Domain.Suppliers;

namespace Template.Unit.Tests.DomainTests;

/// <summary>Rejection paths and edge cases of the Phase 25 aggregates (every behavior method's guard), without mocks.</summary>
public sealed class MasterDataEdgeCaseTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
    private const string Reason = "Synthetic reason for the edge cases";
    private static readonly string LongText = new('x', 201);

    [Theory]
    [InlineData("FIN-01", true)]
    [InlineData("-A", true)]
    [InlineData("F", false)]
    [InlineData("fin", false)] // normalization happens before the check
    [InlineData(null, false)]
    public void OrganizationCode_Format(string? code, bool valid) => MasterDataRules.IsOrganizationCode(code).ShouldBe(valid);

    [Theory]
    [InlineData("A", true)]
    [InlineData("UN-2", true)]
    [InlineData("", false)]
    [InlineData("KG.", false)]
    [InlineData(null, false)]
    public void CatalogCode_Format(string? code, bool valid) => MasterDataRules.IsCatalogCode(code).ShouldBe(valid);

    [Theory]
    [InlineData("SKU.1_A-B", true)]
    [InlineData("1", true)]
    [InlineData(".SKU", false)]
    [InlineData("SKU 1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CatalogItemCode_Format(string? code, bool valid) => MasterDataRules.IsCatalogItemCode(code).ShouldBe(valid);

    [Fact]
    public void CatalogItemCode_LongerThanForty_IsRejected() => MasterDataRules.IsCatalogItemCode(new string('A', 41)).ShouldBeFalse();

    [Fact]
    public void Names_AndUtcAndIds_AreGuarded()
    {
        Department.Create(Guid.NewGuid(), "FIN", LongText, null, null, Now).Error!.Field.ShouldBe("name");
        CostCenter.Create(Guid.NewGuid(), "C", "Name", NewDepartment(), null, null, Now).Error!.RuleId.ShouldBe("ORG-001");
        CostCenter.Create(Guid.NewGuid(), "CC", " ", NewDepartment(), null, null, Now).Error!.Field.ShouldBe("name");
        Category.Create(Guid.NewGuid(), "C", "", null, Now).Error!.Field.ShouldBe("name");
        UnitOfMeasure.Create(Guid.NewGuid(), "U U", "Unit", 0, null, Now).Error!.RuleId.ShouldBe("CAT-001");
        UnitOfMeasure.Create(Guid.NewGuid(), "UN", "", 0, null, Now).Error!.Field.ShouldBe("name");
        CatalogItem.Create(Guid.NewGuid(), ".BAD", "Desc", CatalogItemKind.Good, NewCategory(), NewUnit(), null, null, null, Now).Error!.RuleId.ShouldBe("CAT-001");
        CatalogItem.Create(Guid.NewGuid(), "SKU", "", CatalogItemKind.Good, NewCategory(), NewUnit(), null, null, null, Now).Error!.Field.ShouldBe("description");
        Should.Throw<ArgumentException>(() => Category.Create(Guid.Empty, "C", "N", null, Now));
        Should.Throw<ArgumentException>(() => UnitOfMeasure.Create(Guid.NewGuid(), "U", "N", 0, null, DateTime.SpecifyKind(Now, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void Department_UpdateAndReactivationGuards()
    {
        var department = NewDepartment();

        department.Update("", null, Now)!.Field.ShouldBe("name");
        department.Reactivate(Now)!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        department.Deactivate(Reason, false, Now).ShouldBeNull();
        department.Update("Finance renamed", null, Now).ShouldBeNull(); // renaming an inactive department is allowed
        department.Name.ShouldBe("Finance renamed");
    }

    [Fact]
    public void CostCenter_UpdateGuards_AndForeignDepartment()
    {
        var department = NewDepartment();
        var costCenter = CostCenter.Create(Guid.NewGuid(), "CC-1", "Treasury", department, null, null, Now).CostCenter!;

        costCenter.Update(LongText, null, Now)!.Field.ShouldBe("name");
        costCenter.Update("Treasury", null, Now).ShouldBeNull();
        costCenter.Version.ShouldBe(1);
        costCenter.Update("Treasury", Guid.NewGuid(), Now).ShouldBeNull();
        costCenter.Version.ShouldBe(2);
        costCenter.Deactivate(null, Now)!.Code.ShouldBe(DomainError.ReasonRequiredCode);
        costCenter.Reactivate(department, Now)!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        Should.Throw<ArgumentException>(() => costCenter.Reactivate(NewDepartment(), Now));
    }

    [Fact]
    public void Category_And_Unit_RenameAndTransitions()
    {
        var category = NewCategory();
        var unit = NewUnit();

        category.Rename(" ", Now)!.Field.ShouldBe("name");
        category.Rename("Office supplies", Now).ShouldBeNull();
        category.Version.ShouldBe(1);
        category.Rename("Stationery", Now).ShouldBeNull();
        category.Version.ShouldBe(2);
        category.Deactivate("short", Now)!.Code.ShouldBe(DomainError.ReasonRequiredCode);
        category.Reactivate(Now)!.Details!["currentState"].ShouldBe("Active");
        category.Deactivate(Reason, Now).ShouldBeNull();
        category.Deactivate(Reason, Now)!.Details!["currentState"].ShouldBe("Inactive");

        unit.Rename(LongText, Now)!.Field.ShouldBe("name");
        unit.Rename("Unit", Now).ShouldBeNull();
        unit.Rename("Piece", Now).ShouldBeNull();
        unit.Version.ShouldBe(2);
        unit.Reactivate(Now)!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        unit.Deactivate(null, Now)!.Code.ShouldBe(DomainError.ReasonRequiredCode);
        unit.Deactivate(Reason, Now).ShouldBeNull();
        unit.Deactivate(Reason, Now)!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        unit.EnsureUsable()!.Code.ShouldBe(CatalogErrors.InactiveCode);
        unit.Reactivate(Now).ShouldBeNull();
        unit.EnsureUsable().ShouldBeNull();
    }

    [Fact]
    public void CatalogItem_TransitionGuards_AndUnchangedUpdate()
    {
        var category = NewCategory();
        var unit = NewUnit();
        var item = CatalogItem.Create(Guid.NewGuid(), "SKU-1", "Desk", CatalogItemKind.Good, category, unit, null, null, null, Now).Item!;

        item.Update("Desk", CatalogItemKind.Good, category, unit, null, null, Now).ShouldBeNull();
        item.Version.ShouldBe(1);
        item.Update("Desk", CatalogItemKind.Service, category, unit, 10m, "usd", Now).ShouldBeNull();
        (item.Kind, item.ReferencePriceCurrency, item.Version).ShouldBe((CatalogItemKind.Service, "USD", 2L));
        item.Update("", CatalogItemKind.Good, category, unit, null, null, Now)!.Field.ShouldBe("description");
        item.EnsureUsable().ShouldBeNull();
        item.Deactivate(Reason, Now).ShouldBeNull();
        item.Deactivate(Reason, Now)!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        Should.Throw<ArgumentException>(() => item.Reactivate(NewCategory(), unit, Now));
    }

    [Fact]
    public void Settings_RejectInvalidSupportedCurrencies_AndIgnoreUnchanged()
    {
        var settings = OrganizationSettings.CreateDefault(Now);

        settings.Change("Procurement Hub", "BRL", "America/Sao_Paulo", ["BRL", "US"], true, false, Now)!.Field.ShouldBe("supportedCurrencies");
        settings.Change("Procurement Hub", "BRL", "America/Sao_Paulo", ["USD", "EUR", "BRL"], true, false, Now).ShouldBeNull();
        settings.Version.ShouldBe(1);
        settings.Change("Procurement Hub", "BRL", null, ["BRL"], true, false, Now)!.Field.ShouldBe("timeZone");
    }

    [Fact]
    public void CurrencyCodes_AreThreeAsciiLetters()
    {
        CurrencyCodes.Normalize(" usd ").ShouldBe("USD");
        CurrencyCodes.Normalize("U$D").ShouldBeNull();
        CurrencyCodes.Normalize("ÇÃO").ShouldBeNull();
        CurrencyCodes.Normalize(null).ShouldBeNull();
    }

    [Theory]
    [InlineData(null, "11222333000181")]
    [InlineData("BRA", "11222333000181")]
    [InlineData("BR", null)]
    [InlineData("US", "")]
    [InlineData("US", "1234567890123456789012345678901")] // 31 characters
    public void TaxIdentifier_RejectsMissingOrMalformedInput(string? country, string? taxId) =>
        TaxIdentifier.Normalize(country, taxId).ShouldBeNull();

    [Fact]
    public void TaxIdentifier_FormatsOnlyBrazilianCnpjs()
    {
        TaxIdentifier.Format("BR", "11222333000181").ShouldBe("11.222.333/0001-81");
        TaxIdentifier.Format("US", "123456789").ShouldBe("123456789");
        TaxIdentifier.IsValidCnpj(null).ShouldBeFalse();
    }

    [Fact]
    public void Supplier_DetailGuards()
    {
        var supplier = NewSupplier();

        supplier.UpdateDetails("", null, "BR", "11222333000181", Now)!.Field.ShouldBe("legalName");
        supplier.UpdateDetails("Acme", LongText, "BR", "11222333000181", Now)!.Field.ShouldBe("tradeName");
        supplier.UpdateDetails("Acme", null, "Brazil", "11222333000181", Now)!.Field.ShouldBe("country");
        supplier.UpdateDetails("Acme", null, "BR", "11222333000182", Now)!.Code.ShouldBe(SupplierErrors.TaxIdInvalidCode);
        supplier.UpdateDetails("Acme Comércio Ltda", "Acme", "BR", "11.222.333/0001-81", Now).ShouldBeNull();
        supplier.Version.ShouldBe(1);
    }

    [Fact]
    public void Supplier_ContactGuards()
    {
        var supplier = NewSupplier();
        var tooMany = Enumerable.Range(0, Supplier.MaxContacts + 1).Select(index => new ContactInput(null, $"C{index}", null, null, null)).ToArray();

        supplier.ReplaceContacts(tooMany, Now)!.Field.ShouldBe("contacts");
        supplier.ReplaceContacts([new ContactInput(null, "", null, null, null)], Now)!.Field.ShouldBe("contacts[0].name");
        supplier.ReplaceContacts([new ContactInput(null, "Name", null, new string('9', 31), null)], Now)!.Field.ShouldBe("contacts[0].phone");
        supplier.ReplaceContacts([new ContactInput(null, "Name", null, null, LongText)], Now)!.Field.ShouldBe("contacts[0].role");
        supplier.ReplaceContacts([new ContactInput(null, "Name", "a@b", null, null)], Now)!.Field.ShouldBe("contacts[0].email");
        supplier.ReplaceContacts([new ContactInput(null, "Name", "a b@example.invalid", null, null)], Now)!.Field.ShouldBe("contacts[0].email");

        supplier.ReplaceContacts([new ContactInput(null, "Name", "name@example.invalid", null, null)], Now).ShouldBeNull();
        var id = supplier.Contacts.Single().Id;
        supplier.ReplaceContacts([new ContactInput(id, "Name", "name@example.invalid", null, null), new ContactInput(id, "Again", null, null, null)], Now)!
            .Message.ShouldContain("more than once");
        var version = supplier.Version;
        supplier.ReplaceContacts([new ContactInput(id, " Name ", "NAME@example.invalid", null, null)], Now).ShouldBeNull();
        supplier.Version.ShouldBe(version); // unchanged after normalization
    }

    [Fact]
    public void Supplier_AddressGuards()
    {
        var supplier = NewSupplier();
        var tooMany = Enumerable.Range(0, Supplier.MaxAddresses + 1).Select(_ => new AddressInput(null, Address())).ToArray();

        supplier.ReplaceAddresses(tooMany, Now)!.Field.ShouldBe("addresses");
        supplier.ReplaceAddresses([new AddressInput(Guid.NewGuid(), Address())], Now)!.Kind.ShouldBe(DomainErrorKind.NotFound);
        supplier.ReplaceAddresses([new AddressInput(null, null)], Now)!.Field.ShouldBe("addresses[0].address");
        foreach (var (values, field) in new (AddressValues, string)[]
        {
            (Address() with { Type = (SupplierAddressType)9 }, "address.type"),
            (Address() with { Country = "Brasil" }, "address.country"),
            (Address() with { Street = "" }, "address.street"),
            (Address() with { City = LongText }, "address.city"),
            (Address() with { Number = new string('1', 21) }, "address.number"),
            (Address() with { Complement = LongText }, "address.complement"),
            (Address() with { District = LongText }, "address.district"),
            (Address() with { PostalCode = "" }, "address.postalCode")
        })
            supplier.ReplaceAddresses([new AddressInput(null, values)], Now)!.Field.ShouldBe($"addresses[0].{field}");

        supplier.ReplaceAddresses([new AddressInput(null, Address())], Now).ShouldBeNull();
        var id = supplier.Addresses.Single().Id;
        supplier.ReplaceAddresses([new AddressInput(id, Address()), new AddressInput(id, Address())], Now)!.Message.ShouldContain("more than once");
        supplier.ReplaceAddresses([new AddressInput(id, Address() with { Number = "2000" })], Now).ShouldBeNull();
        supplier.Addresses.Single().Number.ShouldBe("2000");

        var (_, addError) = supplier.AddAddress(Guid.NewGuid(), Address() with { Street = "" }, Now);
        addError!.Field.ShouldBe("address.street");
    }

    [Fact]
    public void Supplier_AddAddress_IsBounded()
    {
        var supplier = NewSupplier();
        for (var index = 0; index < Supplier.MaxAddresses; index++)
            supplier.AddAddress(Guid.NewGuid(), Address(), Now).Error.ShouldBeNull();

        supplier.AddAddress(Guid.NewGuid(), Address(), Now).Error!.Field.ShouldBe("addresses");
    }

    [Fact]
    public void ForeignPostalCodes_AreUpperCasedAndBounded()
    {
        Supplier.NormalizePostalCode("PT", " 1000-001 ").ShouldBe("1000-001");
        Supplier.NormalizePostalCode("GB", "sw1a 1aa").ShouldBe("SW1A 1AA");
        Supplier.NormalizePostalCode("US", new string('1', 21)).ShouldBeNull();
        Supplier.NormalizePostalCode("BR", "  ").ShouldBeNull();
    }

    [Fact]
    public void Supplier_CategoryAndEnrichmentGuards()
    {
        var supplier = NewSupplier();
        var many = Enumerable.Range(0, Supplier.MaxCategories + 1).Select(_ => new MasterDataReference(Guid.NewGuid(), true)).ToArray();

        supplier.SetCategories(many, Now)!.Field.ShouldBe("categoryIds");
        supplier.RecordEnrichment("", Now, ["legalName"], null, null, null, Now)!.Field.ShouldBe("provider");
        supplier.RecordEnrichment("BrasilAPI", Now, ["legalName"], new string('S', 150), null, null, Now).ShouldBeNull();
        supplier.Enrichment!.RegistrationStatus!.Length.ShouldBe(SupplierEnrichmentSnapshot.RegistrationStatusMaxLength);
        supplier.RecordEnrichment("BrasilAPI", Now, ["legalName"], "  ", null, null, Now).ShouldBeNull();
        supplier.Enrichment!.RegistrationStatus.ShouldBeNull();
    }

    private static Department NewDepartment() => Department.Create(Guid.NewGuid(), "FIN", "Finance", null, null, Now).Department!;

    private static Category NewCategory() => Category.Create(Guid.NewGuid(), "OFFICE", "Office supplies", null, Now).Category!;

    private static UnitOfMeasure NewUnit() => UnitOfMeasure.Create(Guid.NewGuid(), "UN", "Unit", 0, null, Now).Unit!;

    private static Supplier NewSupplier() => Supplier.CreateDraft(Guid.NewGuid(), "Acme Comércio Ltda", "Acme", "BR", "11222333000181", null, Now).Supplier!;

    private static AddressValues Address() => new(
        SupplierAddressType.Billing, "01310100", "Avenida Paulista", "1000", "Sala 1", "Bela Vista", "São Paulo", "SP", "BR");
}
