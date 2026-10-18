using Shouldly;
using Template.Domain.Catalog;
using Template.Domain.Common;

namespace Template.Unit.Tests.DomainTests.Catalog;

/// <summary>CAT-001..004 rules of categories, units and catalog items, without mocks.</summary>
public sealed class CatalogTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
    private const string Reason = "Replaced by a consolidated entry";

    [Fact]
    public void T_CAT_001_CodesAreNormalizedAndImmutable()
    {
        var category = Category.Create(Guid.NewGuid(), " office ", "Office supplies", null, Now).Category!;
        var unit = UnitOfMeasure.Create(Guid.NewGuid(), "kg", "Kilogram", 3, null, Now).Unit!;
        var item = CatalogItem.Create(Guid.NewGuid(), "sku-001.a_b", "Paper A4", CatalogItemKind.Good, category, unit, null, null, null, Now).Item!;

        category.Code.ShouldBe("OFFICE");
        unit.Code.ShouldBe("KG");
        item.Code.ShouldBe("SKU-001.A_B");
        foreach (var type in new[] { typeof(Category), typeof(UnitOfMeasure), typeof(CatalogItem) })
            type.GetProperty("Code")!.SetMethod!.IsPublic.ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("A B")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU")]
    public void T_CAT_001_InvalidCategoryCodes_AreRejected(string code) =>
        Category.Create(Guid.NewGuid(), code, "Name", null, Now).Error!.RuleId.ShouldBe("CAT-001");

    [Fact]
    public void T_CAT_002_a_NewItemsNeedAnActiveCategoryAndUnit()
    {
        var category = NewCategory();
        var unit = NewUnit(0);
        category.Deactivate(Reason, Now).ShouldBeNull();

        var (item, error) = CatalogItem.Create(Guid.NewGuid(), "SKU-1", "Chair", CatalogItemKind.Good, category, unit, null, null, null, Now);

        item.ShouldBeNull();
        error!.Code.ShouldBe(CatalogErrors.InactiveCode);
        error.RuleId.ShouldBe("CAT-002");

        unit.Deactivate(Reason, Now).ShouldBeNull();
        category.Reactivate(Now).ShouldBeNull();
        CatalogItem.Create(Guid.NewGuid(), "SKU-1", "Chair", CatalogItemKind.Good, category, unit, null, null, null, Now).Error!.Code
            .ShouldBe(CatalogErrors.InactiveCode);
    }

    [Fact]
    public void T_CAT_002_a_DeactivatedReferencesStayOnExistingItems_ButNewReferencesMustBeActive()
    {
        var category = NewCategory();
        var other = Category.Create(Guid.NewGuid(), "IT", "Information technology", null, Now).Category!;
        var unit = NewUnit(0);
        var item = CatalogItem.Create(Guid.NewGuid(), "SKU-1", "Chair", CatalogItemKind.Good, category, unit, null, null, null, Now).Item!;

        category.Deactivate(Reason, Now).ShouldBeNull();
        item.Update("Office chair", CatalogItemKind.Good, category, unit, null, null, Now).ShouldBeNull();
        item.Description.ShouldBe("Office chair");

        other.Deactivate(Reason, Now).ShouldBeNull();
        item.Update("Office chair", CatalogItemKind.Good, other, unit, null, null, Now)!.Code.ShouldBe(CatalogErrors.InactiveCode);
        item.CategoryId.ShouldBe(category.Id);
    }

    [Fact]
    public void T_CAT_002_a_DeactivationKeepsHistory_AndReactivationNeedsActiveReferences()
    {
        var category = NewCategory();
        var unit = NewUnit(2);
        var item = CatalogItem.Create(Guid.NewGuid(), "SKU-1", "Chair", CatalogItemKind.Good, category, unit, null, null, null, Now).Item!;

        item.Deactivate("short", Now)!.Code.ShouldBe(DomainError.ReasonRequiredCode);
        item.Deactivate(Reason, Now).ShouldBeNull();
        item.EnsureUsable()!.Code.ShouldBe(CatalogErrors.InactiveCode);
        item.Code.ShouldBe("SKU-1");

        category.Deactivate(Reason, Now).ShouldBeNull();
        item.Reactivate(category, unit, Now)!.Code.ShouldBe(CatalogErrors.InactiveCode);
        category.Reactivate(Now).ShouldBeNull();
        item.Reactivate(category, unit, Now).ShouldBeNull();
        item.Reactivate(category, unit, Now)!.Code.ShouldBe(DomainError.InvalidTransitionCode);
    }

    [Theory]
    [InlineData(0, "1", true)]
    [InlineData(0, "1.5", false)]
    [InlineData(2, "1.25", true)]
    [InlineData(2, "1.250", true)]
    [InlineData(2, "1.255", false)]
    [InlineData(3, "0.001", true)]
    [InlineData(3, "0", false)]
    [InlineData(3, "-1", false)]
    public void T_CAT_003_a_AllowedDecimalsBoundQuantities(int allowedDecimals, string quantity, bool accepted) =>
        NewUnit(allowedDecimals).Accepts(decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(accepted);

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void T_CAT_003_a_AllowedDecimalsAreZeroToThree(int allowedDecimals) =>
        UnitOfMeasure.Create(Guid.NewGuid(), "UN", "Unit", allowedDecimals, null, Now).Error!.RuleId.ShouldBe("CAT-003");

    [Fact]
    public void T_CAT_004_ReferencePrice_IsOptionalPositiveWithAtMostFourDecimals()
    {
        var category = NewCategory();
        var unit = NewUnit(0);

        var item = CatalogItem.Create(Guid.NewGuid(), "SKU-1", "Chair", CatalogItemKind.Good, category, unit, 12.3456m, "brl", null, Now).Item!;
        item.ReferencePriceAmount.ShouldBe(12.3456m);
        item.ReferencePriceCurrency.ShouldBe("BRL");

        CatalogItem.Create(Guid.NewGuid(), "SKU-2", "Chair", CatalogItemKind.Good, category, unit, 1.23456m, "BRL", null, Now).Error!.RuleId.ShouldBe("CAT-004");
        CatalogItem.Create(Guid.NewGuid(), "SKU-3", "Chair", CatalogItemKind.Good, category, unit, 0m, "BRL", null, Now).Error!.Field.ShouldBe("referencePrice.amount");
        CatalogItem.Create(Guid.NewGuid(), "SKU-4", "Chair", CatalogItemKind.Good, category, unit, 10m, "REAL", null, Now).Error!.Field.ShouldBe("referencePrice.currency");
        CatalogItem.Create(Guid.NewGuid(), "SKU-5", "Chair", CatalogItemKind.Good, category, unit, null, "BRL", null, Now).Error.ShouldNotBeNull();
    }

    [Fact]
    public void Kind_MustBeDefined() =>
        CatalogItem.Create(Guid.NewGuid(), "SKU-1", "Chair", (CatalogItemKind)9, NewCategory(), NewUnit(0), null, null, null, Now).Error!.Field.ShouldBe("kind");

    [Fact]
    public void UnitAllowedDecimals_HasNoPublicMutator() =>
        typeof(UnitOfMeasure).GetProperty(nameof(UnitOfMeasure.AllowedDecimals))!.SetMethod!.IsPublic.ShouldBeFalse();

    private static Category NewCategory() => Category.Create(Guid.NewGuid(), "OFFICE", "Office supplies", null, Now).Category!;

    private static UnitOfMeasure NewUnit(int decimals) => UnitOfMeasure.Create(Guid.NewGuid(), "UN", "Unit", decimals, null, Now).Unit!;
}
