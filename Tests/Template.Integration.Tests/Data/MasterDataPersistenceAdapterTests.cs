using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Template.Application.Common.Paging;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Catalog;
using Template.Application.Interfaces.Organization;
using Template.Application.Interfaces.Suppliers;
using Template.Domain.Catalog;
using Template.Domain.Common;
using Template.Domain.Organization;
using Template.Domain.Suppliers;
using Template.Infrastructure.Persistence;
using Template.Infrastructure.Persistence.Contexts;
using Template.Domain.Budgeting;
using Template.Domain.Requisitions;

namespace Template.Integration.Tests.Data;

/// <summary>
/// Phase 25 master-data repositories on EF InMemory through the real Persistence registration: aggregate round trips
/// with owned rows, filters, sort with the <c>id</c> tie-breaker and the settings seed. InMemory does not enforce unique
/// indexes, foreign keys or concurrency tokens; the PostgreSQL suite covers those semantics.
/// </summary>
public sealed class MasterDataPersistenceAdapterTests : IAsyncDisposable
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
    private const string Reason = "Synthetic deactivation reason";
    private readonly ServiceProvider provider;

    public MasterDataPersistenceAdapterTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TestDatabaseName"] = Guid.NewGuid().ToString("N") })
            .Build();
        var services = new ServiceCollection();
        services.AddPersistenceInfrastructure(configuration, useInMemoryDatabase: true);
        services.AddScoped<IAuthenticatedUserService>(_ => new AuthenticatedUserService(Guid.NewGuid().ToString(), "integration"));
        provider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Supplier_RoundTripsWithContactsAddressesCategoriesHistoryAndEnrichment()
    {
        var categoryId = Guid.NewGuid();
        var supplier = Supplier.CreateDraft(Guid.NewGuid(), "Acme Comércio Ltda", "Acme", "BR", "11222333000181", null, Now).Supplier!;
        supplier.ReplaceContacts([new ContactInput(null, "Maria Synthetic", "maria@example.invalid", "+55 11 0000-0000", "Sales")], Now).ShouldBeNull();
        supplier.ReplaceAddresses([new AddressInput(null, Address())], Now).ShouldBeNull();
        supplier.SetCategories([new MasterDataReference(categoryId, true)], Now).ShouldBeNull();
        supplier.RecordEnrichment("BrasilAPI", Now.AddMinutes(-1), ["legalName"], "ATIVA", null, Guid.NewGuid(), Now).ShouldBeNull();
        supplier.Activate(Guid.NewGuid(), Now).ShouldBeNull();
        await SaveAsync(scope => scope.GetRequiredService<ISupplierRepository>().Add(supplier));

        await using var read = provider.CreateAsyncScope();
        var loaded = await read.ServiceProvider.GetRequiredService<ISupplierRepository>().FindAsync(supplier.Id, CancellationToken.None);

        loaded!.Status.ShouldBe(SupplierStatus.Active);
        loaded.Version.ShouldBe(supplier.Version);
        loaded.Contacts.Single().Email.ShouldBe("maria@example.invalid");
        loaded.Addresses.Single().PostalCode.ShouldBe("01310100");
        loaded.Categories.Single().CategoryId.ShouldBe(categoryId);
        loaded.StatusHistory.Single().To.ShouldBe(SupplierStatus.Active);
        loaded.Enrichment!.Provider.ShouldBe("BrasilAPI");
        loaded.Enrichment.RegistrationStatus.ShouldBe("ATIVA");
    }

    [Fact]
    public async Task SupplierChildChanges_ArePersisted_InTheNextUnitOfWork()
    {
        var supplier = Supplier.CreateDraft(Guid.NewGuid(), "Acme", null, "BR", "11222333000181", null, Now).Supplier!;
        supplier.ReplaceContacts([new ContactInput(null, "First", "first@example.invalid", null, null)], Now).ShouldBeNull();
        await SaveAsync(scope => scope.GetRequiredService<ISupplierRepository>().Add(supplier));

        await using (var scope = provider.CreateAsyncScope())
        {
            var tracked = await scope.ServiceProvider.GetRequiredService<ISupplierRepository>().FindAsync(supplier.Id, CancellationToken.None);
            var first = tracked!.Contacts.Single();
            tracked.ReplaceContacts([
                new ContactInput(first.Id, "First renamed", "first@example.invalid", null, null),
                new ContactInput(null, "Second", "second@example.invalid", null, null)], Now).ShouldBeNull();
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using var read = provider.CreateAsyncScope();
        var loaded = await read.ServiceProvider.GetRequiredService<ISupplierRepository>().FindAsync(supplier.Id, CancellationToken.None);
        loaded!.Contacts.Select(contact => contact.Name).Order().ShouldBe(["First renamed", "Second"]);
        loaded.Version.ShouldBe(3);
    }

    [Fact]
    public async Task SupplierList_FiltersByStatusTaxIdCategoryAndText_AndSortsWithIdTieBreaker()
    {
        var categoryId = Guid.NewGuid();
        var alpha = Supplier.CreateDraft(Guid.NewGuid(), "Alpha Ltda", "Shop A", "BR", "11222333000181", null, Now).Supplier!;
        alpha.SetCategories([new MasterDataReference(categoryId, true)], Now);
        var beta = Supplier.CreateDraft(Guid.NewGuid(), "Beta SA", null, "BR", "12ABC34501DE35", null, Now).Supplier!;
        var gamma = Supplier.CreateDraft(Guid.NewGuid(), "Gamma Inc", "Alpha resale", "US", "123456789", null, Now).Supplier!;
        await SaveAsync(scope =>
        {
            var repository = scope.GetRequiredService<ISupplierRepository>();
            repository.Add(alpha);
            repository.Add(beta);
            repository.Add(gamma);
        });

        await using var read = provider.CreateAsyncScope();
        var repository = read.ServiceProvider.GetRequiredService<ISupplierRepository>();
        var page = Page("legalName", "legalName");

        (await repository.ListAsync(new SupplierFilter(Q: "alpha"), page, CancellationToken.None)).Items.Select(item => item.LegalName)
            .ShouldBe(["Alpha Ltda", "Gamma Inc"]);
        (await repository.ListAsync(new SupplierFilter(Country: "BR", TaxId: "12ABC34501DE35"), page, CancellationToken.None)).Items.Single().Id.ShouldBe(beta.Id);
        (await repository.ListAsync(new SupplierFilter(CategoryId: categoryId), page, CancellationToken.None)).Items.Single().Id.ShouldBe(alpha.Id);
        (await repository.ListAsync(new SupplierFilter(Statuses: [SupplierStatus.Active]), page, CancellationToken.None)).TotalItems.ShouldBe(0);
        (await repository.ListAsync(new SupplierFilter(Country: "US"), page, CancellationToken.None)).Items.Single().Id.ShouldBe(gamma.Id);
        (await repository.TaxIdExistsAsync("BR", "11222333000181", null, CancellationToken.None)).ShouldBeTrue();
        (await repository.TaxIdExistsAsync("BR", "11222333000181", alpha.Id, CancellationToken.None)).ShouldBeFalse();

        var descending = await repository.ListAsync(new SupplierFilter(), Page("-legalName", "legalName", pageSize: 2), CancellationToken.None);
        descending.Items.Select(item => item.LegalName).ShouldBe(["Gamma Inc", "Beta SA"]);
        (descending.TotalItems, descending.TotalPages).ShouldBe((3, 2));
    }

    [Fact]
    public async Task Organization_ListFiltersAndActiveCostCenterCheck()
    {
        var finance = Department.Create(Guid.NewGuid(), "FIN", "Finance", null, null, Now).Department!;
        var operations = Department.Create(Guid.NewGuid(), "OPS", "Operations", null, null, Now).Department!;
        var treasury = CostCenter.Create(Guid.NewGuid(), "CC-TRE", "Treasury", finance, Guid.NewGuid(), null, Now).CostCenter!;
        var payroll = CostCenter.Create(Guid.NewGuid(), "CC-PAY", "Payroll", finance, null, null, Now).CostCenter!;
        payroll.Deactivate(Reason, Now);
        await SaveAsync(scope =>
        {
            scope.GetRequiredService<IDepartmentRepository>().Add(finance);
            scope.GetRequiredService<IDepartmentRepository>().Add(operations);
            scope.GetRequiredService<ICostCenterRepository>().Add(treasury);
            scope.GetRequiredService<ICostCenterRepository>().Add(payroll);
        });

        await using var read = provider.CreateAsyncScope();
        var departments = read.ServiceProvider.GetRequiredService<IDepartmentRepository>();
        var costCenters = read.ServiceProvider.GetRequiredService<ICostCenterRepository>();

        (await departments.ListAsync(new DepartmentFilter(Q: "oper"), Page("code", "code"), CancellationToken.None)).Items.Single().Code.ShouldBe("OPS");
        (await departments.CodeExistsAsync("FIN", CancellationToken.None)).ShouldBeTrue();
        (await costCenters.ListAsync(new CostCenterFilter(DepartmentId: finance.Id, IsActive: false), Page("code", "code"), CancellationToken.None))
            .Items.Single().Code.ShouldBe("CC-PAY");
        (await costCenters.ListAsync(new CostCenterFilter(ManagerUserId: treasury.ManagerUserId), Page("code", "code"), CancellationToken.None))
            .Items.Single().Id.ShouldBe(treasury.Id);
        (await costCenters.AnyActiveInDepartmentAsync(finance.Id, CancellationToken.None)).ShouldBeTrue();
        (await costCenters.AnyActiveInDepartmentAsync(operations.Id, CancellationToken.None)).ShouldBeFalse();
        (await departments.GetVersionAsync(finance.Id, CancellationToken.None)).ShouldBe(1);
        (await departments.GetVersionAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Catalog_ListFiltersByKindCategoryAndText()
    {
        var category = Category.Create(Guid.NewGuid(), "IT", "Information technology", null, Now).Category!;
        var unit = UnitOfMeasure.Create(Guid.NewGuid(), "H", "Hour", 1, null, Now).Unit!;
        var laptop = CatalogItem.Create(Guid.NewGuid(), "SKU-LAP", "Laptop", CatalogItemKind.Good, category, unit, 4500.1234m, "BRL", null, Now).Item!;
        var support = CatalogItem.Create(Guid.NewGuid(), "SRV-SUP", "Support hours", CatalogItemKind.Service, category, unit, null, null, null, Now).Item!;
        await SaveAsync(scope =>
        {
            scope.GetRequiredService<ICategoryRepository>().Add(category);
            scope.GetRequiredService<IUnitOfMeasureRepository>().Add(unit);
            scope.GetRequiredService<ICatalogItemRepository>().Add(laptop);
            scope.GetRequiredService<ICatalogItemRepository>().Add(support);
        });

        await using var read = provider.CreateAsyncScope();
        var items = read.ServiceProvider.GetRequiredService<ICatalogItemRepository>();
        var categories = read.ServiceProvider.GetRequiredService<ICategoryRepository>();

        (await items.ListAsync(new CatalogItemFilter(Kind: CatalogItemKind.Service), Page("code", "code"), CancellationToken.None)).Items.Single().Id.ShouldBe(support.Id);
        (await items.ListAsync(new CatalogItemFilter(Q: "lap", CategoryId: category.Id), Page("code", "code"), CancellationToken.None))
            .Items.Single().ReferencePriceAmount.ShouldBe(4500.1234m);
        (await items.ListAsync(new CatalogItemFilter(UnitOfMeasureId: unit.Id), Page("-kind", "code"), CancellationToken.None))
            .Items.Select(item => item.Code).ShouldBe(["SRV-SUP", "SKU-LAP"]);
        (await categories.FindManyAsync([category.Id, Guid.NewGuid()], CancellationToken.None)).Single().Code.ShouldBe("IT");
        (await categories.FindManyAsync([], CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Initialization_SeedsTheOrganizationSettingsSingleton_Once()
    {
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.InitializeApplicationDatabaseAsync(applyMigrations: false);
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.InitializeApplicationDatabaseAsync(applyMigrations: false);

        await using var read = provider.CreateAsyncScope();
        var settings = await read.ServiceProvider.GetRequiredService<IOrganizationSettingsRepository>().GetAsync(CancellationToken.None);
        settings!.Id.ShouldBe(OrganizationSettings.SingletonId);
        (settings.BaseCurrency, settings.TimeZone).ShouldBe(("BRL", "America/Sao_Paulo"));
        (await read.ServiceProvider.GetRequiredService<IOrganizationSettingsLock>().HasBudgetAllocationsAsync(CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task Initialization_WhenDemoDataIsEnabled_SeedsARepeatableAcademicScenario()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TestDatabaseName"] = Guid.NewGuid().ToString("N"),
            ["DemoData:Enabled"] = "true"
        }).Build();
        var services = new ServiceCollection();
        services.AddPersistenceInfrastructure(configuration, useInMemoryDatabase: true);
        services.AddScoped<IAuthenticatedUserService>(_ => new AuthenticatedUserService(Guid.NewGuid().ToString(), "integration"));
        await using var demoProvider = services.BuildServiceProvider();

        await using (var scope = demoProvider.CreateAsyncScope())
            await scope.ServiceProvider.InitializeApplicationDatabaseAsync(applyMigrations: false);
        await using (var scope = demoProvider.CreateAsyncScope())
            await scope.ServiceProvider.InitializeApplicationDatabaseAsync(applyMigrations: false);

        await using var read = demoProvider.CreateAsyncScope();
        var context = read.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.Set<Department>().CountAsync()).ShouldBe(3);
        (await context.Set<CostCenter>().CountAsync()).ShouldBe(4);
        (await context.Set<Category>().CountAsync()).ShouldBe(4);
        (await context.Set<CatalogItem>().CountAsync()).ShouldBe(6);
        (await context.Set<Supplier>().CountAsync()).ShouldBe(3);
        (await context.Set<FiscalPeriod>().CountAsync()).ShouldBe(1);
        (await context.Set<BudgetAllocation>().CountAsync()).ShouldBe(4);
        (await context.Set<PurchaseRequisition>().CountAsync()).ShouldBe(3);
    }

    public async ValueTask DisposeAsync() => await provider.DisposeAsync();

    private async Task SaveAsync(Action<IServiceProvider> arrange)
    {
        await using var scope = provider.CreateAsyncScope();
        arrange(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private static PageRequest Page(string sort, string fallback, int pageSize = 20) =>
        PageRequest.Create(1, pageSize, sort, [fallback, "legalName", "code", "kind", "status"], fallback).Value;

    private static AddressValues Address() => new(
        SupplierAddressType.Headquarters, "01310100", "Avenida Paulista", "1000", null, "Bela Vista", "São Paulo", "SP", "BR");
}
