using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Template.Application.Interfaces;
using Template.Domain.Products.Entities;
using Template.Infrastructure.Persistence;
using Template.Infrastructure.Persistence.Contexts;
using Template.Infrastructure.Persistence.Repositories;

namespace Template.PostgreSql.Integration.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlPersistenceTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Migrations_IncludeDecimalMigration_WithNumeric18_2()
    {
        await using var context = fixture.CreateContext();

        var applied = await context.Database.GetAppliedMigrationsAsync();
        var dataType = await PostgreSqlFixture.ExecuteScalarAsync<string>(context, """
            SELECT data_type
            FROM information_schema.columns
            WHERE table_name = 'Products' AND column_name = 'Price'
            """);
        var precision = await PostgreSqlFixture.ExecuteScalarAsync<int>(context, """
            SELECT numeric_precision
            FROM information_schema.columns
            WHERE table_name = 'Products' AND column_name = 'Price'
            """);
        var scale = await PostgreSqlFixture.ExecuteScalarAsync<int>(context, """
            SELECT numeric_scale
            FROM information_schema.columns
            WHERE table_name = 'Products' AND column_name = 'Price'
            """);

        applied.ShouldContain(migration => migration.EndsWith("ProductPriceDecimal", StringComparison.Ordinal));
        dataType.ShouldBe("numeric");
        precision.ShouldBe(18);
        scale.ShouldBe(2);
    }

    [Fact]
    public async Task Crud_AndDecimalPrecision_UsePostgreSqlSemantics()
    {
        await fixture.ResetProductsAsync();
        var id = Guid.NewGuid();

        await using (var context = fixture.CreateContext())
        await using (var unitOfWork = new UnitOfWork(context))
        {
            var repository = new ProductRepository(context);
            await repository.AddAsync(new ProductEntity(id, "Initial", 12.345m, "PG-CRUD"));
            (await unitOfWork.SaveChangesAsync()).ShouldBeTrue();
        }

        await using (var context = fixture.CreateContext())
        await using (var unitOfWork = new UnitOfWork(context))
        {
            var repository = new ProductRepository(context);
            var product = await repository.GetByIdAsync(id);
            product.ShouldNotBeNull();
            product.Price.ShouldBe(12.35m);

            product.Update("Updated", 22.10m, "PG-CRUD-2");
            (await unitOfWork.SaveChangesAsync()).ShouldBeTrue();
            repository.Delete(product);
            (await unitOfWork.SaveChangesAsync()).ShouldBeTrue();
        }

        await using var verification = fixture.CreateContext();
        (await verification.Products.FindAsync(id)).ShouldBeNull();
    }

    [Fact]
    public async Task Pagination_IsDeterministic_WhenCreatedTimestampsTie()
    {
        await fixture.ResetProductsAsync();
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).Order().ToArray();

        await using (var context = fixture.CreateContext())
        await using (var unitOfWork = new UnitOfWork(context))
        {
            var repository = new ProductRepository(context);
            foreach (var id in ids.Reverse())
                await repository.AddAsync(new ProductEntity(id, $"Paged-{id:N}", 1m, $"PAGE-{id:N}"));
            await unitOfWork.SaveChangesAsync();
            await context.Database.ExecuteSqlRawAsync("UPDATE \"Products\" SET \"Created\" = {0}", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }

        await using var queryContext = fixture.CreateContext();
        var queryRepository = new ProductRepository(queryContext);
        var first = await queryRepository.GetPagedListAsync(1, 3, "Paged-");
        var second = await queryRepository.GetPagedListAsync(2, 3, "Paged-");

        first.Data.Select(product => product.Id).ShouldBe(ids.Take(3));
        second.Data.Select(product => product.Id).ShouldBe(ids.Skip(3));
    }

    [Fact]
    public async Task ExplicitTransaction_CommitsAcrossRepositories_AndRollsBack()
    {
        await fixture.ResetProductsAsync();
        var committedIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var rolledBackId = Guid.NewGuid();

        await using (var context = fixture.CreateContext())
        await using (var unitOfWork = new UnitOfWork(context))
        {
            await unitOfWork.BeginTransactionAsync();
            await new ProductRepository(context).AddAsync(new ProductEntity(committedIds[0], "Committed A", 1m, "TX-A"));
            await new GenericRepository<ProductEntity>(context).AddAsync(new ProductEntity(committedIds[1], "Committed B", 2m, "TX-B"));
            (await unitOfWork.CommitTransactionAsync()).ShouldBeTrue();
        }

        await using (var context = fixture.CreateContext())
        await using (var unitOfWork = new UnitOfWork(context))
        {
            await unitOfWork.BeginTransactionAsync();
            await new ProductRepository(context).AddAsync(new ProductEntity(rolledBackId, "Rolled back", 3m, "TX-R"));
            await unitOfWork.SaveChangesAsync();
            await unitOfWork.RollbackTransactionAsync();
        }

        await using var verification = fixture.CreateContext();
        (await verification.Products.CountAsync(product => committedIds.Contains(product.Id))).ShouldBe(2);
        (await verification.Products.AnyAsync(product => product.Id == rolledBackId)).ShouldBeFalse();
    }

    [Fact]
    public async Task InitializeApplicationDatabaseAsync_IsIdempotent_OnMigratedPostgreSql()
    {
        await fixture.ResetProductsAsync();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString })
            .Build();
        var services = new ServiceCollection();
        services.AddPersistenceInfrastructure(configuration, useInMemoryDatabase: false);
        services.AddScoped<IAuthenticatedUserService>(_ => new PostgreSqlFixture.TestAuthenticatedUser());
        await using var provider = services.BuildServiceProvider();

        for (var run = 0; run < 2; run++)
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.InitializeApplicationDatabaseAsync(applyMigrations: true);
        }

        await using var verification = fixture.CreateContext();
        (await verification.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        (await verification.Products.CountAsync()).ShouldBe(5);
    }

    [Fact]
    public async Task ConcurrentWrites_ArePersisted_AndCancellationIsHonored()
    {
        await fixture.ResetProductsAsync();
        var ids = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();

        await Task.WhenAll(ids.Select(async (id, index) =>
        {
            await using var context = fixture.CreateContext();
            await using var unitOfWork = new UnitOfWork(context);
            await new ProductRepository(context).AddAsync(new ProductEntity(id, $"Concurrent {index}", index, $"CON-{index}"));
            await unitOfWork.SaveChangesAsync();
        }));

        await using var cancelledContext = fixture.CreateContext();
        var repository = new ProductRepository(cancelledContext);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            repository.GetPagedListAsync(1, 10, "Concurrent", cancellation.Token));

        await using var verification = fixture.CreateContext();
        (await verification.Products.CountAsync(product => ids.Contains(product.Id))).ShouldBe(ids.Length);
    }
}
