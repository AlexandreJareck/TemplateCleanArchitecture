using Shouldly;
using Template.Application.Common.Results;
using Template.Application.Features.Catalog;
using Template.Application.Interfaces.Catalog;
using Template.Application.Interfaces.Integrations;
using Template.Application.Services.Integrations;
using Template.Application.Wrappers;
using Template.Domain.Catalog;

namespace Template.Unit.Tests.ApplicationTests.Features.MasterData;

/// <summary>Catalog use cases (CAT-001..004) and the SP-5 PTAX resolver against port fakes.</summary>
public sealed class CatalogHandlerTests
{
    private const string Reason = "Discontinued by the manufacturer";
    private readonly MasterDataTestDoubles doubles = new();

    private CategoryCommandHandlers Categories => new(doubles.Categories, doubles.Writer);
    private UnitOfMeasureCommandHandlers Units => new(doubles.Units, doubles.Writer);
    private CatalogItemCommandHandlers Items => new(doubles.Items, doubles.Categories, doubles.Units, doubles.Writer);

    [Fact]
    public async Task T_CAT_001_DuplicateCodes_AreRejected_PerKind()
    {
        (await Categories.Handle(new CreateCategoryCommand("office", "Office"), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await Units.Handle(new CreateUnitOfMeasureCommand("office", "Office unit", 0), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        var duplicate = await Categories.Handle(new CreateCategoryCommand("OFFICE", "Again"), CancellationToken.None);

        duplicate.Error!.Code.ShouldBe(ProblemCodes.CatalogCodeDuplicate);
        duplicate.Error.RuleId.ShouldBe("CAT-001");
        var (category, unit) = await ReferencesAsync();
        doubles.UnitOfWork.UniqueViolation = ICatalogItemRepository.CodeUniqueKey;
        (await Items.Handle(new CreateCatalogItemCommand("SKU-1", "Chair", CatalogItemKind.Good, category, unit, null), CancellationToken.None))
            .Error!.Code.ShouldBe(ProblemCodes.CatalogCodeDuplicate);
    }

    [Fact]
    public async Task T_CAT_002_b_CreateItem_WithInactiveCategory_Is422()
    {
        var (category, unit) = await ReferencesAsync();
        await Categories.Handle(new DeactivateCategoryCommand(category, 1, Reason), CancellationToken.None);

        var result = await Items.Handle(new CreateCatalogItemCommand("SKU-1", "Chair", CatalogItemKind.Good, category, unit, null), CancellationToken.None);

        result.Error!.Code.ShouldBe(ProblemCodes.CatalogInactive);
        result.Error.Category.ShouldBe(ErrorCategory.BusinessRule);
        doubles.Audit.Records.Last().Action.ShouldBe(CatalogAudit.CategoryDeactivated);
    }

    [Fact]
    public async Task CreateItem_ParsesMoneyStrings_AndAudits()
    {
        var (category, unit) = await ReferencesAsync();

        var result = await Items.Handle(new CreateCatalogItemCommand("sku-1", "Chair", CatalogItemKind.Good, category, unit, new MoneyInput("1234.5000", "brl")), CancellationToken.None);

        result.Value.Code.ShouldBe("SKU-1");
        result.Value.Kind.ShouldBe("Good");
        result.Value.ReferencePrice.ShouldBe(new MoneyView("1234.5000", "BRL"));
        var audit = doubles.Audit.Records.Last();
        audit.Action.ShouldBe(CatalogAudit.ItemCreated);
        audit.Details.Keys.ShouldBe(["code", "categoryId", "unitOfMeasureId"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("12,50")]
    [InlineData("abc")]
    [InlineData("1e3")]
    public async Task CreateItem_RejectsNonDecimalAmounts(string amount)
    {
        var (category, unit) = await ReferencesAsync();

        var result = await Items.Handle(new CreateCatalogItemCommand("SKU-1", "Chair", CatalogItemKind.Good, category, unit, new MoneyInput(amount, "BRL")), CancellationToken.None);

        result.Error!.FieldErrors!.Keys.ShouldBe(["referencePrice.amount"]);
    }

    [Fact]
    public async Task CreateItem_UnknownReferences_AreValidationErrors()
    {
        (await Items.Handle(new CreateCatalogItemCommand("SKU-1", "Chair", CatalogItemKind.Good, Guid.NewGuid(), Guid.NewGuid(), null), CancellationToken.None))
            .Error!.FieldErrors!.Keys.ShouldBe(["categoryId"]);
    }

    [Fact]
    public async Task T_CAT_002_DeactivateAndReactivateItem_WithVersioning()
    {
        var (category, unit) = await ReferencesAsync();
        var item = (await Items.Handle(new CreateCatalogItemCommand("SKU-1", "Chair", CatalogItemKind.Good, category, unit, null), CancellationToken.None)).Value;

        var deactivated = await Items.Handle(new DeactivateCatalogItemCommand(item.Id, 1, Reason), CancellationToken.None);
        var stale = await Items.Handle(new ReactivateCatalogItemCommand(item.Id, 1, null), CancellationToken.None);
        var reactivated = await Items.Handle(new ReactivateCatalogItemCommand(item.Id, 2, null), CancellationToken.None);

        deactivated.Value.IsActive.ShouldBeFalse();
        stale.Error!.Code.ShouldBe(ProblemCodes.VersionMismatch);
        reactivated.Value.IsActive.ShouldBeTrue();
        reactivated.Value.Version.ShouldBe("3");
    }

    [Fact]
    public async Task Units_AllowedDecimalsAreValidated()
    {
        new CreateUnitOfMeasureCommandValidator().Validate(new CreateUnitOfMeasureCommand("UN", "Unit", 4)).IsValid.ShouldBeFalse();
        (await Units.Handle(new CreateUnitOfMeasureCommand("UN", "Unit", 4), CancellationToken.None)).Error!.RuleId.ShouldBe("CAT-003");
    }

    [Fact]
    public async Task List_PassesKindAndCategoryFilters()
    {
        var category = Guid.NewGuid();

        (await new CatalogItemQueryHandlers(doubles.Items).Handle(new ListCatalogItemsQuery(Kind: CatalogItemKind.Service, CategoryId: category, Sort: "-kind,code"), CancellationToken.None))
            .IsSuccess.ShouldBeTrue();

        doubles.Items.LastFilter.ShouldBe(new CatalogItemFilter(null, null, CatalogItemKind.Service, category, null));
    }

    [Fact]
    public async Task SP5_Ptax_UsesTheLatestClosingBulletinOnOrBeforeTheDate()
    {
        var provider = new FakeRates(EnrichmentResult<IReadOnlyList<ExchangeRateBulletin>>.Found("BcbPtax", MasterDataTestDoubles.Now,
        [
            new ExchangeRateBulletin("USD", new DateOnly(2026, 10, 15), 5.40m, 5.41m, "Fechamento", true),
            new ExchangeRateBulletin("USD", new DateOnly(2026, 10, 16), 5.42m, 5.4321m, "Fechamento", true),
            new ExchangeRateBulletin("USD", new DateOnly(2026, 10, 16), 5.50m, 5.51m, "Intermediário", false)
        ]));

        var result = await new PtaxRateResolver(provider).ResolveSellingRateAsync("usd", new DateOnly(2026, 10, 18), CancellationToken.None);

        result.Outcome.ShouldBe(EnrichmentOutcome.Found);
        result.Value!.Rate.ShouldBe(5.4321m);
        result.Value.QuoteDate.ShouldBe(new DateOnly(2026, 10, 16));
        result.Value.Source.ShouldBe("BcbPtax");
        provider.Requested.ShouldBe(("USD", new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 18)));
    }

    [Fact]
    public async Task SP5_Ptax_NoClosingBulletinInSevenDays_IsNotFound_AndFailuresPassThrough()
    {
        var empty = new FakeRates(EnrichmentResult<IReadOnlyList<ExchangeRateBulletin>>.Found("BcbPtax", MasterDataTestDoubles.Now, []));
        var down = new FakeRates(EnrichmentResult<IReadOnlyList<ExchangeRateBulletin>>.Failed(EnrichmentOutcome.Unavailable, "BcbPtax", "timeout"));
        var date = new DateOnly(2026, 10, 18);

        (await new PtaxRateResolver(empty).ResolveSellingRateAsync("USD", date, CancellationToken.None)).Outcome.ShouldBe(EnrichmentOutcome.NotFound);
        var unavailable = await new PtaxRateResolver(down).ResolveSellingRateAsync("USD", date, CancellationToken.None);
        (unavailable.Outcome, unavailable.Reason).ShouldBe((EnrichmentOutcome.Unavailable, "timeout"));
        (await new PtaxRateResolver(down).ResolveSellingRateAsync("BRL", date, CancellationToken.None)).Outcome.ShouldBe(EnrichmentOutcome.Rejected);
    }

    [Fact]
    public async Task SP5_Ptax_RatesWithMoreThanSixDecimals_AreInvalid()
    {
        var provider = new FakeRates(EnrichmentResult<IReadOnlyList<ExchangeRateBulletin>>.Found("BcbPtax", MasterDataTestDoubles.Now,
            [new ExchangeRateBulletin("EUR", new DateOnly(2026, 10, 16), 6m, 6.1234567m, "Fechamento", true)]));

        (await new PtaxRateResolver(provider).ResolveSellingRateAsync("EUR", new DateOnly(2026, 10, 16), CancellationToken.None))
            .Outcome.ShouldBe(EnrichmentOutcome.Invalid);
    }

    private async Task<(Guid Category, Guid Unit)> ReferencesAsync()
    {
        var category = await Categories.Handle(new CreateCategoryCommand("FURN", "Furniture"), CancellationToken.None);
        var unit = await Units.Handle(new CreateUnitOfMeasureCommand("UN", "Unit", 0), CancellationToken.None);
        return (category.Value.Id, unit.Value.Id);
    }

    private sealed class FakeRates(EnrichmentResult<IReadOnlyList<ExchangeRateBulletin>> result) : IExchangeRateProvider
    {
        public (string, DateOnly, DateOnly)? Requested { get; private set; }

        public Task<EnrichmentResult<IReadOnlyList<ExchangeRateBulletin>>> GetPtaxBulletinsAsync(
            string currency, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
        {
            Requested = (currency, fromDate, toDate);
            return Task.FromResult(result);
        }
    }
}
