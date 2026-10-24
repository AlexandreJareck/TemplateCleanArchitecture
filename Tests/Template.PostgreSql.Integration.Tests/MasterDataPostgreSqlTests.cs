using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Features.Catalog;
using Template.Application.Features.Organization;
using Template.Application.Features.Suppliers;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Access;
using Template.Application.Interfaces.Catalog;
using Template.Application.Interfaces.Organization;
using Template.Application.Interfaces.Suppliers;
using Template.Application.Services.MasterData;
using Template.Domain.Auditing;
using Template.Domain.Common;
using Template.Domain.Organization;
using Template.Domain.Suppliers;
using Template.Infrastructure.Persistence;

namespace Template.PostgreSql.Integration.Tests;

/// <summary>
/// Phase 25 master data on real PostgreSQL 16.15: migration shape and reversibility, case-insensitive unique codes
/// and tax IDs under concurrency (ORG-001-b, CAT-001-a, SUP-001-b), restricted deletes of referenced master data
/// (ORG-005-a), optimistic concurrency of the supplier aggregate including its child rows (SP-1), atomic status change
/// plus audit (AUD-001), filters translated to SQL and exact decimal storage. All data is synthetic.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class MasterDataPostgreSqlTests(PostgreSqlFixture fixture) : IAsyncLifetime
{
    private const string Phase24Migration = "20261018085240_Phase24AccessAuditIdempotency";
    private const string Reason = "Synthetic reason for the PostgreSQL suite";
    private ServiceProvider provider = null!;

    public Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistenceInfrastructure(configuration, useInMemoryDatabase: false);
        services.AddSingleton<IClock>(new UtcClock());
        services.AddSingleton<IAuthenticatedUserService>(new Actor());
        services.AddSingleton<IRequestContext>(new Trace());
        services.AddSingleton<IAccountDirectory>(new NoUsers());
        services.AddScoped<MasterDataWriter>();
        services.AddScoped<DepartmentCommandHandlers>();
        services.AddScoped<CostCenterCommandHandlers>();
        services.AddScoped<CategoryCommandHandlers>();
        services.AddScoped<UnitOfMeasureCommandHandlers>();
        services.AddScoped<CatalogItemCommandHandlers>();
        services.AddScoped<SupplierCommandHandlers>();
        services.AddApprovalCascade();
        provider = services.BuildServiceProvider();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await provider.DisposeAsync();

    [Fact]
    public async Task Migration_CreatesMasterDataTables_UniqueIndexes_RestrictedForeignKeys_AndChecks()
    {
        await using var context = fixture.CreateContext();

        (await context.Database.GetAppliedMigrationsAsync()).ShouldContain(migration => migration.EndsWith("Phase25MasterData", StringComparison.Ordinal));
        (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context, """
            SELECT count(*) FROM pg_indexes
            WHERE indexname IN ('IX_OrganizationDepartments_Code', 'IX_OrganizationCostCenters_Code', 'IX_CatalogCategories_Code',
                                'IX_CatalogUnitsOfMeasure_Code', 'IX_CatalogItems_Code', 'IX_Suppliers_Country_TaxId',
                                'IX_SupplierStatusChanges_SupplierId_Sequence')
              AND indexdef LIKE 'CREATE UNIQUE INDEX%'
            """)).ShouldBe(7);
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context, """
            SELECT count(*) FROM pg_constraint
            WHERE contype = 'f' AND confdeltype = 'r'
              AND conname IN ('FK_OrganizationCostCenters_OrganizationDepartments_DepartmentId',
                              'FK_CatalogItems_CatalogCategories_CategoryId', 'FK_CatalogItems_CatalogUnitsOfMeasure_UnitOfMeasureId',
                              'FK_SupplierCategories_CatalogCategories_CategoryId')
            """)).ShouldBe(4);
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context,
            "SELECT count(*) FROM pg_constraint WHERE conname = 'CK_CatalogUnitsOfMeasure_AllowedDecimals'")).ShouldBe(1);
        (await PostgreSqlFixture.ExecuteScalarAsync<string>(context, """
            SELECT data_type FROM information_schema.columns WHERE table_name = 'CatalogItems' AND column_name = 'ReferencePriceAmount'
            """)).ShouldBe("numeric");
        (await PostgreSqlFixture.ExecuteScalarAsync<string>(context, """
            SELECT data_type FROM information_schema.columns WHERE table_name = 'OrganizationSettings' AND column_name = 'SupportedCurrencies'
            """)).ShouldBe("ARRAY");
    }

    [Fact]
    public async Task Migration_RollsBackToPhase24_AndReappliesIdempotently()
    {
        await using var context = fixture.CreateContext();
        var migrator = context.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(Phase24Migration);
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context,
            "SELECT count(*) FROM information_schema.tables WHERE table_name IN ('Suppliers', 'CatalogItems', 'OrganizationDepartments', 'OrganizationSettings')")).ShouldBe(0);
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context,
            "SELECT count(*) FROM information_schema.tables WHERE table_name IN ('AuditRecords', 'AccessSessionFamilies', 'Products')")).ShouldBe(3);

        await migrator.MigrateAsync();
        await migrator.MigrateAsync(); // a second run is a no-op
        (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.InitializeApplicationDatabaseAsync(applyMigrations: true);
        await scope.ServiceProvider.InitializeApplicationDatabaseAsync(applyMigrations: true);
        await using var verify = fixture.CreateContext();
        (await verify.Set<OrganizationSettings>().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task T_ORG_001_b_CodesAreUniqueCaseInsensitively_EvenUnderConcurrentCreation()
    {
        var code = UniqueCode("C");
        using var barrier = new Barrier(4);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(index => Task.Run(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            return await scope.ServiceProvider.GetRequiredService<DepartmentCommandHandlers>()
                .Handle(new CreateDepartmentCommand(index % 2 == 0 ? code : code.ToLowerInvariant(), $"Department {index}", null), CancellationToken.None);
        })));

        results.Count(result => result.IsSuccess).ShouldBe(1);
        results.Where(result => !result.IsSuccess).ShouldAllBe(result => result.Error!.Code == ProblemCodes.OrganizationCodeDuplicate);
        await using var context = fixture.CreateContext();
        (await context.Set<Department>().CountAsync(department => department.Code == code)).ShouldBe(1);
        var winner = results.Single(result => result.IsSuccess).Value.Id.ToString();
        (await context.Set<AuditRecord>().CountAsync(record => record.Action == OrganizationAudit.DepartmentCreated && record.ResourceId == winner)).ShouldBe(1);
    }

    [Fact]
    public async Task UnitOfWork_TranslatesUniqueViolations_WithTheIndexName()
    {
        var taxId = SyntheticCnpj();
        await AddSupplierAsync(taxId);

        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ISupplierRepository>().Add(
            Supplier.CreateDraft(Guid.NewGuid(), "Duplicate", null, "BR", taxId, null, DateTime.UtcNow).Supplier!);

        var error = await Should.ThrowAsync<UniqueConstraintViolationException>(() => scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync());
        error.ConstraintName.ShouldBe(ISupplierRepository.TaxIdUniqueKey);
    }

    [Fact]
    public async Task T_SUP_001_b_TaxIdIsUniquePerCountry_ThroughTheHandler()
    {
        var taxId = SyntheticCnpj();
        await AddSupplierAsync(taxId);

        await using var scope = provider.CreateAsyncScope();
        var handlers = scope.ServiceProvider.GetRequiredService<SupplierCommandHandlers>();

        (await handlers.Handle(new CreateSupplierCommand("Other", null, "BR", taxId), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.SupplierTaxIdDuplicate);
        (await handlers.Handle(new CreateSupplierCommand("Other", null, "PT", taxId), CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task T_ORG_005_a_ReferencedMasterDataCannotBeHardDeleted()
    {
        await using var scope = provider.CreateAsyncScope();
        var departments = scope.ServiceProvider.GetRequiredService<DepartmentCommandHandlers>();
        var costCenters = scope.ServiceProvider.GetRequiredService<CostCenterCommandHandlers>();
        var categories = scope.ServiceProvider.GetRequiredService<CategoryCommandHandlers>();
        var units = scope.ServiceProvider.GetRequiredService<UnitOfMeasureCommandHandlers>();
        var items = scope.ServiceProvider.GetRequiredService<CatalogItemCommandHandlers>();
        var department = (await departments.Handle(new CreateDepartmentCommand(UniqueCode("D"), "Finance", null), CancellationToken.None)).Value;
        (await costCenters.Handle(new CreateCostCenterCommand(UniqueCode("CC"), "Treasury", department.Id, null), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        var category = (await categories.Handle(new CreateCategoryCommand(UniqueCode("CAT"), "Office"), CancellationToken.None)).Value;
        var unit = (await units.Handle(new CreateUnitOfMeasureCommand(UniqueCode("U"), "Unit", 0), CancellationToken.None)).Value;
        (await items.Handle(new CreateCatalogItemCommand(UniqueCode("SKU"), "Chair", Domain.Catalog.CatalogItemKind.Good, category.Id, unit.Id, null), CancellationToken.None))
            .IsSuccess.ShouldBeTrue();

        await using var context = fixture.CreateContext();
        foreach (var (table, id) in new[] { ("OrganizationDepartments", department.Id), ("CatalogCategories", category.Id), ("CatalogUnitsOfMeasure", unit.Id) })
        {
#pragma warning disable EF1002 // the table name is one of three literals above; the ID is a parameter
            var error = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync($"DELETE FROM \"{table}\" WHERE \"Id\" = {{0}}", id));
#pragma warning restore EF1002
            error.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        }
    }

    [Fact]
    public async Task SP1_SupplierChildChange_RacesAgainstAStatusChange_AndExactlyOneWins()
    {
        var supplierId = await AddActiveSupplierAsync();

        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();
        var firstSupplier = await first.ServiceProvider.GetRequiredService<ISupplierRepository>().FindAsync(supplierId, CancellationToken.None);
        var secondSupplier = await second.ServiceProvider.GetRequiredService<ISupplierRepository>().FindAsync(supplierId, CancellationToken.None);
        firstSupplier!.Suspend(Reason, null, DateTime.UtcNow).ShouldBeNull();
        secondSupplier!.ReplaceContacts([new ContactInput(null, "New contact", "new@example.invalid", null, null)], DateTime.UtcNow).ShouldBeNull();

        await first.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        await Should.ThrowAsync<ConcurrencyConflictException>(() => second.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync());

        await using var context = fixture.CreateContext();
        var stored = await context.Set<Supplier>().Include(supplier => supplier.Contacts).SingleAsync(supplier => supplier.Id == supplierId);
        stored.Status.ShouldBe(SupplierStatus.Suspended);
        stored.Contacts.Single().Name.ShouldBe("Maria Synthetic");
    }

    [Fact]
    public async Task StatusChange_WithStaleVersion_Is412_AndWritesNeitherChangeNorAudit()
    {
        var supplierId = await AddActiveSupplierAsync();
        await using var scope = provider.CreateAsyncScope();
        var handlers = scope.ServiceProvider.GetRequiredService<SupplierCommandHandlers>();

        var stale = await handlers.Handle(new ChangeSupplierStatusCommand(supplierId, 1, SupplierStatusTransition.Suspend, Reason), CancellationToken.None);

        stale.Error!.Code.ShouldBe(ProblemCodes.VersionMismatch);
        await using var context = fixture.CreateContext();
        (await context.Set<AuditRecord>().CountAsync(record => record.ResourceId == supplierId.ToString() && record.Action == SupplierAudit.Suspended)).ShouldBe(0);
    }

    [Fact]
    public async Task T_SUP_004_StatusChange_AndItsAudit_CommitTogether_WithHistoryRow()
    {
        var supplierId = await AddActiveSupplierAsync();
        long version;
        await using (var context = fixture.CreateContext())
            version = await context.Set<Supplier>().Where(supplier => supplier.Id == supplierId).Select(supplier => supplier.Version).SingleAsync();

        await using (var scope = provider.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<SupplierCommandHandlers>()
                .Handle(new ChangeSupplierStatusCommand(supplierId, version, SupplierStatusTransition.Suspend, Reason), CancellationToken.None);
            result.Value.Status.ShouldBe("Suspended");
        }

        await using var verify = fixture.CreateContext();
        var audit = await verify.Set<AuditRecord>().SingleAsync(record => record.ResourceId == supplierId.ToString() && record.Action == SupplierAudit.Suspended);
        (audit.FromState, audit.ToState, audit.Reason, audit.CorrelationId).ShouldBe(("Active", "Suspended", Reason, "pg-master-data"));
        (await verify.Set<SupplierStatusChange>().CountAsync(change => change.SupplierId == supplierId)).ShouldBe(2);
    }

    [Fact]
    public async Task Filters_TranslateToSql_CaseInsensitivelyIncludingAccents_AndDecimalsAreExact()
    {
        var marker = UniqueCode("Z");
        var taxId = SyntheticCnpj();
        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ISupplierRepository>();
            repository.Add(Supplier.CreateDraft(Guid.NewGuid(), $"Comércio {marker} Ltda", null, "BR", taxId, null, DateTime.UtcNow).Supplier!);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using var read = provider.CreateAsyncScope();
        var suppliers = read.ServiceProvider.GetRequiredService<ISupplierRepository>();
        var page = PageRequest.Create(1, 10, "-legalName,status", ISupplierRepository.SortFields, "legalName").Value;

        (await suppliers.ListAsync(new SupplierFilter(Q: $"COMÉRCIO {marker.ToLowerInvariant()}"), page, CancellationToken.None)).TotalItems.ShouldBe(1);
        (await suppliers.ListAsync(new SupplierFilter(Statuses: [SupplierStatus.Draft], TaxId: taxId, Country: "BR"), page, CancellationToken.None))
            .Items.Single().LegalName.ShouldContain(marker);

        await using var catalog = provider.CreateAsyncScope();
        var category = (await catalog.ServiceProvider.GetRequiredService<CategoryCommandHandlers>().Handle(new CreateCategoryCommand(UniqueCode("P"), "Precision"), CancellationToken.None)).Value;
        var unit = (await catalog.ServiceProvider.GetRequiredService<UnitOfMeasureCommandHandlers>().Handle(new CreateUnitOfMeasureCommand(UniqueCode("KG"), "Kilogram", 3), CancellationToken.None)).Value;
        var item = (await catalog.ServiceProvider.GetRequiredService<CatalogItemCommandHandlers>().Handle(new CreateCatalogItemCommand(
            UniqueCode("SKU"), "Steel", Domain.Catalog.CatalogItemKind.Good, category.Id, unit.Id, new MoneyInput("99999999999999.9999", "USD")), CancellationToken.None)).Value;
        await using var verify = fixture.CreateContext();
        (await verify.Set<Domain.Catalog.CatalogItem>().Where(candidate => candidate.Id == item.Id).Select(candidate => candidate.ReferencePriceAmount).SingleAsync())
            .ShouldBe(99999999999999.9999m);
    }

    private async Task AddSupplierAsync(string taxId)
    {
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ISupplierRepository>().Add(
            Supplier.CreateDraft(Guid.NewGuid(), "Synthetic Supplier", null, "BR", taxId, null, DateTime.UtcNow).Supplier!);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private async Task<Guid> AddActiveSupplierAsync()
    {
        Guid categoryId;
        await using (var scope = provider.CreateAsyncScope())
            categoryId = (await scope.ServiceProvider.GetRequiredService<CategoryCommandHandlers>()
                .Handle(new CreateCategoryCommand(UniqueCode("S"), "Services"), CancellationToken.None)).Value.Id;

        var now = DateTime.UtcNow;
        var supplier = Supplier.CreateDraft(Guid.NewGuid(), "Synthetic Supplier", null, "BR", SyntheticCnpj(), null, now).Supplier!;
        supplier.ReplaceContacts([new ContactInput(null, "Maria Synthetic", "maria@example.invalid", null, null)], now).ShouldBeNull();
        supplier.ReplaceAddresses([new AddressInput(null, new AddressValues(
            SupplierAddressType.Headquarters, "01310100", "Avenida Paulista", "1000", null, "Bela Vista", "São Paulo", "SP", "BR"))], now).ShouldBeNull();
        supplier.SetCategories([new MasterDataReference(categoryId, true)], now).ShouldBeNull();
        supplier.Activate(null, now).ShouldBeNull();
        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ISupplierRepository>().Add(supplier);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        return supplier.Id;
    }

    private static string UniqueCode(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(20, prefix.Length + 13)].ToUpperInvariant();

    /// <summary>A structurally valid, random CNPJ (synthetic; check digits computed per SUP-001).</summary>
    private static string SyntheticCnpj()
    {
        var digits = Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture) + "0001";
        int Digit(string value, int[] weights)
        {
            var sum = value.Select((character, index) => (character - '0') * weights[index]).Sum() % 11;
            return sum < 2 ? 0 : 11 - sum;
        }

        var first = Digit(digits, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        var second = Digit(digits + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return $"{digits}{first}{second}";
    }

    private sealed class UtcClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    private sealed class Actor : IAuthenticatedUserService
    {
        public string? UserId { get; } = Guid.NewGuid().ToString();
        public string? UserName => "pg-master-data";
    }

    private sealed class Trace : IRequestContext
    {
        public string CorrelationId => "pg-master-data";
    }

    private sealed class NoUsers : IAccountDirectory
    {
        public Task<VerifiedAccount?> VerifyCredentialsAsync(string userName, string password, CancellationToken cancellationToken) => Task.FromResult<VerifiedAccount?>(null);
        public Task<string?> GetSecurityStampFingerprintAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<AccountProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<AccountProfile?>(null);
        public Task<bool> IsActiveUserAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<PagedResult<UserSummary>> SearchUsersAsync(string? search, PageRequest page, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<UserSummary>([], page.Page, page.PageSize, 0));
    }
}
