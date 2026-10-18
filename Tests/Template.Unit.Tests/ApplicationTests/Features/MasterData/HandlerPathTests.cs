using FluentValidation;
using Shouldly;
using Template.Application.Common.Results;
using Template.Application.Features.Access.Queries;
using Template.Application.Features.Catalog;
using Template.Application.Features.Organization;
using Template.Application.Features.Suppliers;
using Template.Application.Interfaces.Integrations;
using Template.Application.Interfaces.Organization;
using Template.Application.Interfaces.Catalog;
using Template.Application.Wrappers;
using Template.Domain.Catalog;
using Template.Domain.Common;
using Template.Domain.Organization;
using Template.Domain.Suppliers;

namespace Template.Unit.Tests.ApplicationTests.Features.MasterData;

/// <summary>
/// Every failure and no-op path of the master-data handlers: unknown IDs (404), missing/stale versions (428/412),
/// domain rejections, unchanged updates (no commit, no audit), commit conflicts and the validators.
/// </summary>
public sealed class HandlerPathTests
{
    private const string Reason = "Synthetic reason for handler paths";
    private readonly MasterDataTestDoubles doubles = new();

    private DepartmentCommandHandlers Departments => new(doubles.Departments, doubles.CostCenters, doubles.Accounts, doubles.Writer);
    private CostCenterCommandHandlers CostCenters => new(doubles.CostCenters, doubles.Departments, doubles.Accounts, doubles.Writer);
    private CategoryCommandHandlers Categories => new(doubles.Categories, doubles.Writer);
    private UnitOfMeasureCommandHandlers Units => new(doubles.Units, doubles.Writer);
    private CatalogItemCommandHandlers Items => new(doubles.Items, doubles.Categories, doubles.Units, doubles.Writer);
    private SupplierCommandHandlers Suppliers => new(doubles.Suppliers, doubles.Categories, doubles.Writer);
    private SupplierEnrichmentHandlers Enrichment => new(doubles.Registry, doubles.PostalCodes, doubles.Suppliers, doubles.Writer, MasterDataTestDoubles.Logger<SupplierEnrichmentHandlers>());
    private OrganizationSettingsHandlers Settings => new(doubles.Settings, doubles.SettingsLock, doubles.Writer);

    public static TheoryData<string> Commands => new()
    {
        "department.update", "department.deactivate", "department.reactivate",
        "costCenter.update", "costCenter.deactivate", "costCenter.reactivate",
        "category.update", "category.deactivate", "category.reactivate",
        "unit.update", "unit.deactivate", "unit.reactivate",
        "item.update", "item.deactivate", "item.reactivate",
        "supplier.update", "supplier.contacts", "supplier.addresses", "supplier.categories", "supplier.status", "supplier.enrichment"
    };

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task UnknownId_IsNotFound(string command)
    {
        var result = await RunAsync(command, Guid.NewGuid(), 1);

        result!.Code.ShouldBe(ProblemCodes.NotFound);
        result.Category.ShouldBe(ErrorCategory.NotFound);
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task MissingAndStaleVersions_Are428And412(string command)
    {
        var id = await ExistingAsync(command);

        (await RunAsync(command, id, null))!.Code.ShouldBe(ProblemCodes.PreconditionRequired);
        (await RunAsync(command, id, 99))!.Code.ShouldBe(ProblemCodes.VersionMismatch);
        doubles.Audit.Records.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("department.update")]
    [InlineData("costCenter.update")]
    [InlineData("category.update")]
    [InlineData("unit.update")]
    [InlineData("item.update")]
    [InlineData("supplier.update")]
    public async Task UnchangedUpdate_ReturnsTheCurrentRepresentation_WithoutCommitOrAudit(string command)
    {
        var id = await ExistingAsync(command);
        var saves = doubles.UnitOfWork.Saves;

        var result = await RunAsync(command, id, 1, unchanged: true);

        result.ShouldBeNull();
        doubles.UnitOfWork.Saves.ShouldBe(saves);
        doubles.Audit.Records.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("department.deactivate")]
    [InlineData("costCenter.update")]
    [InlineData("category.deactivate")]
    [InlineData("unit.deactivate")]
    [InlineData("item.deactivate")]
    [InlineData("supplier.contacts")]
    public async Task CommitConflict_Is412WithTheCurrentVersion(string command)
    {
        var id = await ExistingAsync(command);
        doubles.UnitOfWork.ThrowConflict = true;

        var result = await RunAsync(command, id, 1);

        result!.Code.ShouldBe(ProblemCodes.VersionMismatch);
    }

    [Fact]
    public async Task ConflictForAVanishedResource_IsStill412()
    {
        var id = await ExistingAsync("category.update");
        doubles.UnitOfWork.BeforeSave = () => doubles.Categories.Items.Clear();
        doubles.UnitOfWork.ThrowConflict = true;

        var result = await Categories.Handle(new UpdateCategoryCommand(id, 1, "Renamed"), CancellationToken.None);

        result.Error!.Code.ShouldBe(ProblemCodes.VersionMismatch);
        result.Error.Extensions.ShouldBeNull();
    }

    [Fact]
    public async Task DomainRejections_AreMappedPerHandler()
    {
        var department = await ExistingAsync("department.update");
        var costCenter = await ExistingAsync("costCenter.update");
        var category = await ExistingAsync("category.update");
        var unit = await ExistingAsync("unit.update");
        var item = await ExistingAsync("item.update");

        (await Departments.Handle(new UpdateDepartmentCommand(department, 1, "", null), CancellationToken.None)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await Departments.Handle(new UpdateDepartmentCommand(department, 1, "Finance", Guid.NewGuid()), CancellationToken.None)).Error!.RuleId.ShouldBe("ORG-006");
        (await Departments.Handle(new ReactivateDepartmentCommand(department, 1, null), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.InvalidTransition);
        (await CostCenters.Handle(new UpdateCostCenterCommand(costCenter, 1, "", null), CancellationToken.None)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await CostCenters.Handle(new UpdateCostCenterCommand(costCenter, 1, "Name", Guid.NewGuid()), CancellationToken.None)).Error!.RuleId.ShouldBe("ORG-006");
        (await CostCenters.Handle(new DeactivateCostCenterCommand(costCenter, 1, "short"), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.ReasonRequired);
        (await CostCenters.Handle(new ReactivateCostCenterCommand(costCenter, 1, null), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.InvalidTransition);
        (await Categories.Handle(new UpdateCategoryCommand(category, 1, ""), CancellationToken.None)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await Categories.Handle(new ReactivateCategoryCommand(category, 1, null), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.InvalidTransition);
        (await Units.Handle(new UpdateUnitOfMeasureCommand(unit, 1, ""), CancellationToken.None)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await Units.Handle(new ReactivateUnitOfMeasureCommand(unit, 1, null), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.InvalidTransition);
        (await Items.Handle(new UpdateCatalogItemCommand(item, 1, "", CatalogItemKind.Good, Category(item), Unit(item), null), CancellationToken.None))
            .Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await Items.Handle(new UpdateCatalogItemCommand(item, 1, "Desk", CatalogItemKind.Good, Guid.NewGuid(), Unit(item), null), CancellationToken.None))
            .Error!.FieldErrors!.Keys.ShouldBe(["categoryId"]);
        (await Items.Handle(new UpdateCatalogItemCommand(item, 1, "Desk", CatalogItemKind.Good, Category(item), Guid.NewGuid(), null), CancellationToken.None))
            .Error!.FieldErrors!.Keys.ShouldBe(["unitOfMeasureId"]);
        (await Items.Handle(new UpdateCatalogItemCommand(item, 1, "Desk", CatalogItemKind.Good, Category(item), Unit(item), new MoneyInput(null, "BRL")), CancellationToken.None))
            .Error!.FieldErrors!.Keys.ShouldBe(["referencePrice.amount"]);
        (await Items.Handle(new DeactivateCatalogItemCommand(item, 1, null), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.ReasonRequired);
        (await Items.Handle(new ReactivateCatalogItemCommand(item, 1, null), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.InvalidTransition);
    }

    [Fact]
    public async Task SuccessfulTransitions_AreAuditedWithStatesAndRules()
    {
        var department = await ExistingAsync("department.update");
        var costCenter = await ExistingAsync("costCenter.update");
        var category = await ExistingAsync("category.update");
        var unit = await ExistingAsync("unit.update");
        var item = await ExistingAsync("item.update");
        var manager = Guid.NewGuid();
        doubles.Accounts.ActiveUsers.Add(manager);

        (await CostCenters.Handle(new UpdateCostCenterCommand(costCenter, 1, "Treasury", manager), CancellationToken.None)).Value.ManagerUserId.ShouldBe(manager);
        (await CostCenters.Handle(new DeactivateCostCenterCommand(costCenter, 2, Reason), CancellationToken.None)).Value.IsActive.ShouldBeFalse();
        (await CostCenters.Handle(new ReactivateCostCenterCommand(costCenter, 3, Reason), CancellationToken.None)).Value.IsActive.ShouldBeTrue();
        (await Departments.Handle(new UpdateDepartmentCommand(department, 1, "Finance", manager), CancellationToken.None)).Value.HeadUserId.ShouldBe(manager);
        (await Units.Handle(new UpdateUnitOfMeasureCommand(unit, 1, "Piece"), CancellationToken.None)).Value.Name.ShouldBe("Piece");
        (await Units.Handle(new DeactivateUnitOfMeasureCommand(unit, 2, Reason), CancellationToken.None)).Value.IsActive.ShouldBeFalse();
        (await Units.Handle(new ReactivateUnitOfMeasureCommand(unit, 3, null), CancellationToken.None)).Value.IsActive.ShouldBeTrue();
        (await Categories.Handle(new UpdateCategoryCommand(category, 1, "Furniture & fittings"), CancellationToken.None)).Value.Version.ShouldBe("2");
        (await Categories.Handle(new DeactivateCategoryCommand(category, 2, Reason), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await Categories.Handle(new ReactivateCategoryCommand(category, 3, Reason), CancellationToken.None)).Value.IsActive.ShouldBeTrue();
        (await Items.Handle(new UpdateCatalogItemCommand(item, 1, "Standing desk", CatalogItemKind.Good, Category(item), Unit(item), new MoneyInput("10", "EUR")), CancellationToken.None))
            .Value.ReferencePrice.ShouldBe(new MoneyView("10", "EUR"));

        doubles.Audit.Records.Select(record => record.Action).ShouldBe([
            OrganizationAudit.CostCenterUpdated, OrganizationAudit.CostCenterDeactivated, OrganizationAudit.CostCenterReactivated,
            OrganizationAudit.DepartmentUpdated, CatalogAudit.UnitUpdated, CatalogAudit.UnitDeactivated, CatalogAudit.UnitReactivated,
            CatalogAudit.CategoryUpdated, CatalogAudit.CategoryDeactivated, CatalogAudit.CategoryReactivated, CatalogAudit.ItemUpdated]);
        doubles.Audit.Records.Single(record => record.Action == CatalogAudit.CategoryReactivated).Reason.ShouldBe(Reason);
    }

    [Fact]
    public async Task CreateFailures_AreMapped()
    {
        await ExistingAsync("costCenter.update");
        await ExistingAsync("item.update");

        (await CostCenters.Handle(new CreateCostCenterCommand("X", "Name", doubles.Departments.Items[0].Id, null), CancellationToken.None)).Error!.RuleId.ShouldBe("ORG-001");
        (await CostCenters.Handle(new CreateCostCenterCommand(doubles.CostCenters.Items[0].Code, "Name", doubles.Departments.Items[0].Id, null), CancellationToken.None))
            .Error!.Code.ShouldBe(ProblemCodes.OrganizationCodeDuplicate);
        (await Departments.Handle(new CreateDepartmentCommand("x", "Name", null), CancellationToken.None)).Error!.RuleId.ShouldBe("ORG-001");
        (await Categories.Handle(new CreateCategoryCommand("A B", "Name"), CancellationToken.None)).Error!.RuleId.ShouldBe("CAT-001");
        (await Units.Handle(new CreateUnitOfMeasureCommand(doubles.Units.Items[0].Code, "Again", 0), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.CatalogCodeDuplicate);
        (await Items.Handle(new CreateCatalogItemCommand(".BAD", "Desk", CatalogItemKind.Good, doubles.Categories.Items[0].Id, doubles.Units.Items[0].Id, null), CancellationToken.None))
            .Error!.RuleId.ShouldBe("CAT-001");
        (await Items.Handle(new CreateCatalogItemCommand(doubles.Items.Items[0].Code, "Desk", CatalogItemKind.Good, doubles.Categories.Items[0].Id, doubles.Units.Items[0].Id, null), CancellationToken.None))
            .Error!.Code.ShouldBe(ProblemCodes.CatalogCodeDuplicate);
        (await Items.Handle(new CreateCatalogItemCommand("SKU-9", "Desk", CatalogItemKind.Good, doubles.Categories.Items[0].Id, Guid.NewGuid(), null), CancellationToken.None))
            .Error!.FieldErrors!.Keys.ShouldBe(["unitOfMeasureId"]);

        doubles.UnitOfWork.UniqueViolation = ICostCenterRepository.CodeUniqueKey;
        (await CostCenters.Handle(new CreateCostCenterCommand("CC-RACE", "Name", doubles.Departments.Items[0].Id, null), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.OrganizationCodeDuplicate);
        doubles.UnitOfWork.UniqueViolation = ICategoryRepository.CodeUniqueKey;
        (await Categories.Handle(new CreateCategoryCommand("RACE", "Name"), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.CatalogCodeDuplicate);
        doubles.UnitOfWork.UniqueViolation = IUnitOfMeasureRepository.CodeUniqueKey;
        (await Units.Handle(new CreateUnitOfMeasureCommand("RACE", "Name", 1), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.CatalogCodeDuplicate);
    }

    [Fact]
    public async Task Queries_MapEntities_AndValidatePaging()
    {
        await ExistingAsync("item.update");
        await ExistingAsync("costCenter.update");
        await ExistingAsync("supplier.update");

        (await new CostCenterQueryHandlers(doubles.CostCenters).Handle(new GetCostCenterQuery(doubles.CostCenters.Items[0].Id), CancellationToken.None)).Value.Code.ShouldBe("CC-1");
        (await new CostCenterQueryHandlers(doubles.CostCenters).Handle(new GetCostCenterQuery(Guid.NewGuid()), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.NotFound);
        (await new CostCenterQueryHandlers(doubles.CostCenters).Handle(new ListCostCentersQuery(DepartmentId: doubles.Departments.Items[0].Id), CancellationToken.None)).Value.TotalItems.ShouldBe(1);
        (await new CostCenterQueryHandlers(doubles.CostCenters).Handle(new ListCostCentersQuery(Q: new string('q', 101)), CancellationToken.None)).Error!.FieldErrors!.Keys.ShouldBe(["q"]);
        (await new CostCenterQueryHandlers(doubles.CostCenters).Handle(new ListCostCentersQuery(PageSize: 0), CancellationToken.None)).Error!.FieldErrors!.Keys.ShouldBe(["pageSize"]);
        (await new DepartmentQueryHandlers(doubles.Departments).Handle(new GetDepartmentQuery(doubles.Departments.Items[0].Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        var categories = new CategoryQueryHandlers(doubles.Categories);
        (await categories.Handle(new GetCategoryQuery(doubles.Categories.Items[0].Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await categories.Handle(new GetCategoryQuery(Guid.NewGuid()), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.NotFound);
        (await categories.Handle(new ListCategoriesQuery(Sort: "-name"), CancellationToken.None)).Value.Items.Count.ShouldBe(1);
        (await categories.Handle(new ListCategoriesQuery(Sort: "secret"), CancellationToken.None)).Error!.FieldErrors!.Keys.ShouldBe(["sort"]);
        (await categories.Handle(new ListCategoriesQuery(Q: new string('q', 101)), CancellationToken.None)).Error!.FieldErrors!.Keys.ShouldBe(["q"]);

        var units = new UnitOfMeasureQueryHandlers(doubles.Units);
        (await units.Handle(new GetUnitOfMeasureQuery(doubles.Units.Items[0].Id), CancellationToken.None)).Value.AllowedDecimals.ShouldBe(0);
        (await units.Handle(new GetUnitOfMeasureQuery(Guid.NewGuid()), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.NotFound);
        (await units.Handle(new ListUnitsOfMeasureQuery(IsActive: true), CancellationToken.None)).Value.TotalItems.ShouldBe(1);
        (await units.Handle(new ListUnitsOfMeasureQuery(Page: 0), CancellationToken.None)).Error!.FieldErrors!.Keys.ShouldBe(["page"]);
        (await units.Handle(new ListUnitsOfMeasureQuery(Q: new string('q', 101)), CancellationToken.None)).Error!.FieldErrors!.Keys.ShouldBe(["q"]);

        var items = new CatalogItemQueryHandlers(doubles.Items);
        (await items.Handle(new GetCatalogItemQuery(doubles.Items.Items[0].Id), CancellationToken.None)).Value.ReferencePrice.ShouldBeNull();
        (await items.Handle(new GetCatalogItemQuery(Guid.NewGuid()), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.NotFound);
        (await items.Handle(new ListCatalogItemsQuery(Sort: "price"), CancellationToken.None)).Error!.FieldErrors!.Keys.ShouldBe(["sort"]);
        (await items.Handle(new ListCatalogItemsQuery(Q: new string('q', 101)), CancellationToken.None)).Error!.FieldErrors!.Keys.ShouldBe(["q"]);

        var suppliers = new SupplierQueryHandlers(doubles.Suppliers);
        (await suppliers.Handle(new GetSupplierQuery(doubles.Suppliers.Items[0].Id), CancellationToken.None)).Value.Status.ShouldBe("Draft");
        (await suppliers.Handle(new GetSupplierQuery(Guid.NewGuid()), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.NotFound);
        (await suppliers.Handle(new ListSuppliersQuery(Country: "pt", Q: "acme"), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        doubles.Suppliers.LastFilter!.Country.ShouldBe("PT");
        (await suppliers.Handle(new ListSuppliersQuery(Sort: "taxId"), CancellationToken.None)).Error!.FieldErrors!.Keys.ShouldBe(["sort"]);
        (await suppliers.Handle(new ListSuppliersQuery(Q: new string('q', 101)), CancellationToken.None)).Error!.FieldErrors!.Keys.ShouldBe(["q"]);
        (await suppliers.Handle(new ListSuppliersQuery(Status: "7"), CancellationToken.None)).Error!.FieldErrors!.Keys.ShouldBe(["status"]);
    }

    [Fact]
    public async Task SupplierStatus_InactivateAndReactivate_AndReplacePathsWithErrors()
    {
        var id = await ExistingAsync("supplier.status");
        var supplier = doubles.Suppliers.Items.Single();
        var category = Template.Domain.Catalog.Category.Create(Guid.NewGuid(), "SUP", "Supplies", null, MasterDataTestDoubles.Now).Category!;
        doubles.Categories.Add(category);
        supplier.ReplaceContacts([new ContactInput(null, "Maria Synthetic", "maria@example.invalid", null, null)], MasterDataTestDoubles.Now);
        supplier.ReplaceAddresses([new AddressInput(null, new AddressValues(SupplierAddressType.Billing, "01310100", "Rua A", null, null, null, "São Paulo", "SP", "BR"))], MasterDataTestDoubles.Now);
        supplier.SetCategories([new MasterDataReference(category.Id, true)], MasterDataTestDoubles.Now);
        supplier.Activate(null, MasterDataTestDoubles.Now);
        var version = supplier.Version;

        (await Suppliers.Handle(new ChangeSupplierStatusCommand(id, version, SupplierStatusTransition.Inactivate, Reason), CancellationToken.None)).Value.Status.ShouldBe("Inactive");
        (await Suppliers.Handle(new ChangeSupplierStatusCommand(id, version + 1, SupplierStatusTransition.Reactivate, Reason), CancellationToken.None)).Value.Status.ShouldBe("Active");
        (await Suppliers.Handle(new ReplaceSupplierAddressesCommand(id, version + 2, []), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.SupplierActivationIncomplete);
        (await Suppliers.Handle(new ReplaceSupplierContactsCommand(id, version + 2, null), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.SupplierActivationIncomplete);
        (await Suppliers.Handle(new SetSupplierCategoriesCommand(id, version + 2, null), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.SupplierActivationIncomplete);
        (await Suppliers.Handle(new UpdateSupplierCommand(id, version + 2, "Acme", null, "BR", "11.222.333/0001-80"), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.SupplierTaxIdInvalid);
        (await Suppliers.Handle(new UpdateSupplierCommand(id, version + 2, "Acme renamed", "Acme", "BR", "11222333000181"), CancellationToken.None)).Value.LegalName.ShouldBe("Acme renamed");
        doubles.Audit.Records.Select(record => record.Action).ShouldBe([SupplierAudit.Inactivated, SupplierAudit.Reactivated, SupplierAudit.Updated]);
    }

    [Fact]
    public async Task Enrichment_RemainingPaths()
    {
        var id = await ExistingAsync("supplier.enrichment");
        doubles.PostalCodes.Result = EnrichmentResult<PostalAddressSuggestion>.Found("BrasilAPI", MasterDataTestDoubles.Now, new PostalAddressSuggestion("01310100", "Avenida Paulista", "Bela Vista", "São Paulo", "SP"));
        doubles.Registry.Result = EnrichmentResult<CompanyRegistrationSuggestion>.Found("BrasilAPI", MasterDataTestDoubles.Now, new CompanyRegistrationSuggestion("11222333000181", "ACME", null, null, null));

        var postal = await Enrichment.Handle(new LookupPostalCodeQuery("01310-100"), CancellationToken.None);
        postal.Value.City.ShouldBe("São Paulo");
        postal.Value.Country.ShouldBe("BR");
        (await Enrichment.Handle(new LookupCompanyRegistrationQuery("11222333000181"), CancellationToken.None)).Value.Address.ShouldBeNull();

        var badAddress = new SupplierAddressInput(null, SupplierAddressType.Headquarters, "123", "Rua", null, null, null, "São Paulo", "SP", "BR");
        (await Enrichment.Handle(new AcceptSupplierEnrichmentCommand(id, 1, "BrasilAPI", MasterDataTestDoubles.Now, null, null, null, badAddress), CancellationToken.None))
            .Error!.FieldErrors!.Keys.ShouldBe(["address.postalCode"]);
        (await Enrichment.Handle(new AcceptSupplierEnrichmentCommand(id, 1, "BrasilAPI", MasterDataTestDoubles.Now, "", null, null, null), CancellationToken.None))
            .Error!.FieldErrors!.Keys.ShouldBe(["legalName"]);
        var local = DateTime.SpecifyKind(MasterDataTestDoubles.Now.AddMinutes(-1), DateTimeKind.Unspecified);
        (await Enrichment.Handle(new AcceptSupplierEnrichmentCommand(id, 1, "BrasilAPI", local, null, "Acme", null, null), CancellationToken.None))
            .Value.Enrichment!.AcceptedFields.ShouldBe(["tradeName"]);
        (await Enrichment.Handle(new AcceptSupplierEnrichmentCommand(id, 2, "BrasilAPI", MasterDataTestDoubles.Now.ToLocalTime(), "Acme Ltda", null, null, null), CancellationToken.None))
            .Value.Enrichment!.AcceptedFields.ShouldBe(["legalName"]);
    }

    [Fact]
    public async Task Settings_RemainingPaths()
    {
        (await Settings.Handle(new UpdateOrganizationSettingsCommand(null, "A", "BRL", "America/Sao_Paulo", ["BRL"]), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.PreconditionRequired);
        (await Settings.Handle(new UpdateOrganizationSettingsCommand(1, "Procurement Hub", "BRL", "America/Sao_Paulo", ["BRL", "EUR", "USD"]), CancellationToken.None)).Value.Version.ShouldBe("1");
        (await Settings.Handle(new UpdateOrganizationSettingsCommand(1, "Procurement Hub", "BRL", "", ["BRL"]), CancellationToken.None)).Error!.FieldErrors!.Keys.ShouldBe(["timeZone"]);
        doubles.UnitOfWork.ThrowConflict = true;
        (await Settings.Handle(new UpdateOrganizationSettingsCommand(1, "Renamed", "BRL", "America/Sao_Paulo", ["BRL"]), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.VersionMismatch);

        doubles.Settings.Current = null;
        (await Settings.Handle(new GetOrganizationSettingsQuery(), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.NotFound);
        (await Settings.Handle(new UpdateOrganizationSettingsCommand(1, "A", "BRL", "America/Sao_Paulo", ["BRL"]), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.NotFound);
    }

    [Fact]
    public async Task UserLookup_ValidatesAndDelegates()
    {
        var handler = new ListUsersQueryHandler(doubles.Accounts);

        (await handler.Handle(new ListUsersQuery(Q: "syn"), CancellationToken.None)).Value.Items.Single().UserName.ShouldBe("synthetic-user");
        (await handler.Handle(new ListUsersQuery(PageSize: 500), CancellationToken.None)).Error!.FieldErrors!.Keys.ShouldBe(["pageSize"]);
        (await handler.Handle(new ListUsersQuery(Q: new string('q', 101)), CancellationToken.None)).Error!.FieldErrors!.Keys.ShouldBe(["q"]);
    }

    [Fact]
    public void Validators_AcceptValidAndRejectInvalidCommands()
    {
        var id = Guid.NewGuid();
        Valid(new UpdateDepartmentCommandValidator(), new UpdateDepartmentCommand(id, 1, "Name", null), new UpdateDepartmentCommand(id, 1, "", null));
        Valid(new UpdateCostCenterCommandValidator(), new UpdateCostCenterCommand(id, 1, "Name", null), new UpdateCostCenterCommand(id, 1, null, null));
        Valid(new CreateCategoryCommandValidator(), new CreateCategoryCommand("C", "Name"), new CreateCategoryCommand(null, "Name"));
        Valid(new UpdateCategoryCommandValidator(), new UpdateCategoryCommand(id, 1, "Name"), new UpdateCategoryCommand(id, 1, new string('n', 201)));
        Valid(new CreateUnitOfMeasureCommandValidator(), new CreateUnitOfMeasureCommand("UN", "Unit", 2), new CreateUnitOfMeasureCommand("UN", "", 2));
        Valid(new UpdateUnitOfMeasureCommandValidator(), new UpdateUnitOfMeasureCommand(id, 1, "Unit"), new UpdateUnitOfMeasureCommand(id, 1, ""));
        Valid(new CreateCatalogItemCommandValidator(), new CreateCatalogItemCommand("SKU", "Desk", CatalogItemKind.Good, id, id, null),
            new CreateCatalogItemCommand("SKU", "Desk", null, id, id, null));
        Valid(new UpdateCatalogItemCommandValidator(), new UpdateCatalogItemCommand(id, 1, "Desk", CatalogItemKind.Service, id, id, null),
            new UpdateCatalogItemCommand(id, 1, "Desk", (CatalogItemKind)7, Guid.Empty, id, null));
        Valid(new CreateSupplierCommandValidator(), new CreateSupplierCommand("Acme", null, "BR", "11222333000181"), new CreateSupplierCommand("Acme", null, "BRA", ""));
        Valid(new UpdateSupplierCommandValidator(), new UpdateSupplierCommand(id, 1, "Acme", null, "BR", "1"), new UpdateSupplierCommand(id, 1, "", null, "BR", "1"));
        Valid(new ReplaceSupplierContactsCommandValidator(), new ReplaceSupplierContactsCommand(id, 1, [new SupplierContactInput(null, "N", "n@example.invalid", null, null)]),
            new ReplaceSupplierContactsCommand(id, 1, [new SupplierContactInput(null, "N", "not-an-email", null, null)]));
        Valid(new ReplaceSupplierAddressesCommandValidator(), new ReplaceSupplierAddressesCommand(id, 1, []), new ReplaceSupplierAddressesCommand(id, 1, null));
        Valid(new SetSupplierCategoriesCommandValidator(), new SetSupplierCategoriesCommand(id, 1, [id]), new SetSupplierCategoriesCommand(id, 1, [Guid.Empty]));
        Valid(new ChangeSupplierStatusCommandValidator(), new ChangeSupplierStatusCommand(id, 1, SupplierStatusTransition.Suspend, null),
            new ChangeSupplierStatusCommand(id, 1, (SupplierStatusTransition)9, null));
        Valid(new AcceptSupplierEnrichmentCommandValidator(), new AcceptSupplierEnrichmentCommand(id, 1, "BrasilAPI", DateTime.UtcNow, "Acme", null, null, null),
            new AcceptSupplierEnrichmentCommand(id, 1, "BrasilAPI", DateTime.UtcNow, null, null, null, null));
    }

    [Fact]
    public void ResultPrimitives_GuardMisuse()
    {
        Should.Throw<ArgumentException>(() => EnrichmentResult<PostalAddressSuggestion>.Failed(EnrichmentOutcome.Found, "BrasilAPI", "x"));
        new DomainError("x", DomainErrorKind.NotFound, "missing").ToApplicationError().Category.ShouldBe(ErrorCategory.NotFound);
        new DomainError("x.y", DomainErrorKind.Conflict, "conflict", "R-1").ToApplicationError().Code.ShouldBe("x.y");
        new DomainError("v", DomainErrorKind.Validation, "bad").ToApplicationError().FieldErrors!.Keys.ShouldBe([string.Empty]);
        Should.Throw<ArgumentNullException>(() => DomainErrorMapping.ToApplicationError(null!));
        OrganizationAudit.State(true).ShouldBe("Active");
        OrganizationAudit.State(false).ShouldBe("Inactive");
        CatalogAudit.TryParseAmount(null, out var none).ShouldBeTrue();
        none.ShouldBeNull();
    }

    private static void Valid<T>(IValidator<T> validator, T valid, T invalid)
    {
        validator.Validate(valid).IsValid.ShouldBeTrue(typeof(T).Name);
        validator.Validate(invalid).IsValid.ShouldBeFalse(typeof(T).Name);
    }

    private Guid Category(Guid item) => doubles.Items.Items.Single(candidate => candidate.Id == item).CategoryId;

    private Guid Unit(Guid item) => doubles.Items.Items.Single(candidate => candidate.Id == item).UnitOfMeasureId;

    /// <summary>Creates the aggregate a command targets (version 1) without touching audit or unit-of-work counters.</summary>
    private async Task<Guid> ExistingAsync(string command)
    {
        var now = MasterDataTestDoubles.Now;
        var family = command.Split('.')[0];
        switch (family)
        {
            case "department":
                var department = Department.Create(Guid.NewGuid(), "FIN", "Finance", null, null, now).Department!;
                doubles.Departments.Add(department);
                return department.Id;
            case "costCenter":
                var owner = Department.Create(Guid.NewGuid(), "OPS", "Operations", null, null, now).Department!;
                doubles.Departments.Add(owner);
                var costCenter = CostCenter.Create(Guid.NewGuid(), "CC-1", "Treasury", owner, null, null, now).CostCenter!;
                doubles.CostCenters.Add(costCenter);
                return costCenter.Id;
            case "category":
                var category = Template.Domain.Catalog.Category.Create(Guid.NewGuid(), "FURN", "Furniture", null, now).Category!;
                doubles.Categories.Add(category);
                return category.Id;
            case "unit":
                var unit = UnitOfMeasure.Create(Guid.NewGuid(), "UN", "Unit", 0, null, now).Unit!;
                doubles.Units.Add(unit);
                return unit.Id;
            case "item":
                var itemCategory = Template.Domain.Catalog.Category.Create(Guid.NewGuid(), "FURN", "Furniture", null, now).Category!;
                var itemUnit = UnitOfMeasure.Create(Guid.NewGuid(), "UN", "Unit", 0, null, now).Unit!;
                doubles.Categories.Add(itemCategory);
                doubles.Units.Add(itemUnit);
                var item = CatalogItem.Create(Guid.NewGuid(), "SKU-1", "Desk", CatalogItemKind.Good, itemCategory, itemUnit, null, null, null, now).Item!;
                doubles.Items.Add(item);
                return item.Id;
            default:
                var supplier = Supplier.CreateDraft(Guid.NewGuid(), "Acme", "Acme", "BR", "11222333000181", null, now).Supplier!;
                doubles.Suppliers.Add(supplier);
                await Task.CompletedTask;
                return supplier.Id;
        }
    }

    /// <summary>Runs one command; <paramref name="unchanged"/> sends the current values. Returns the error or <c>null</c>.</summary>
    private async Task<ApplicationError?> RunAsync(string command, Guid id, long? version, bool unchanged = false)
    {
        var token = CancellationToken.None;
        Template.Application.Common.Results.Result result = command switch
        {
            "department.update" => await Departments.Handle(new UpdateDepartmentCommand(id, version, unchanged ? "Finance" : "Finance 2", null), token),
            "department.deactivate" => await Departments.Handle(new DeactivateDepartmentCommand(id, version, Reason), token),
            "department.reactivate" => await Departments.Handle(new ReactivateDepartmentCommand(id, version, null), token),
            "costCenter.update" => await CostCenters.Handle(new UpdateCostCenterCommand(id, version, unchanged ? "Treasury" : "Treasury 2", null), token),
            "costCenter.deactivate" => await CostCenters.Handle(new DeactivateCostCenterCommand(id, version, Reason), token),
            "costCenter.reactivate" => await CostCenters.Handle(new ReactivateCostCenterCommand(id, version, null), token),
            "category.update" => await Categories.Handle(new UpdateCategoryCommand(id, version, unchanged ? "Furniture" : "Furniture 2"), token),
            "category.deactivate" => await Categories.Handle(new DeactivateCategoryCommand(id, version, Reason), token),
            "category.reactivate" => await Categories.Handle(new ReactivateCategoryCommand(id, version, null), token),
            "unit.update" => await Units.Handle(new UpdateUnitOfMeasureCommand(id, version, unchanged ? "Unit" : "Unit 2"), token),
            "unit.deactivate" => await Units.Handle(new DeactivateUnitOfMeasureCommand(id, version, Reason), token),
            "unit.reactivate" => await Units.Handle(new ReactivateUnitOfMeasureCommand(id, version, null), token),
            "item.update" => await Items.Handle(ItemUpdate(id, version, unchanged), token),
            "item.deactivate" => await Items.Handle(new DeactivateCatalogItemCommand(id, version, Reason), token),
            "item.reactivate" => await Items.Handle(new ReactivateCatalogItemCommand(id, version, null), token),
            "supplier.update" => await Suppliers.Handle(new UpdateSupplierCommand(id, version, "Acme", unchanged ? "Acme" : "Other", "BR", "11222333000181"), token),
            "supplier.contacts" => await Suppliers.Handle(new ReplaceSupplierContactsCommand(id, version, [new SupplierContactInput(null, "N", "n@example.invalid", null, null)]), token),
            "supplier.addresses" => await Suppliers.Handle(new ReplaceSupplierAddressesCommand(id, version, []), token),
            "supplier.categories" => await Suppliers.Handle(new SetSupplierCategoriesCommand(id, version, []), token),
            "supplier.status" => await Suppliers.Handle(new ChangeSupplierStatusCommand(id, version, SupplierStatusTransition.Activate, null), token),
            _ => await Enrichment.Handle(new AcceptSupplierEnrichmentCommand(id, version, "BrasilAPI", MasterDataTestDoubles.Now, "Acme", null, null, null), token)
        };
        return result.Error;
    }

    private UpdateCatalogItemCommand ItemUpdate(Guid id, long? version, bool unchanged)
    {
        var item = doubles.Items.Items.SingleOrDefault(candidate => candidate.Id == id);
        return new UpdateCatalogItemCommand(id, version, unchanged ? "Desk" : "Desk 2", CatalogItemKind.Good,
            item?.CategoryId ?? Guid.NewGuid(), item?.UnitOfMeasureId ?? Guid.NewGuid(), null);
    }
}
