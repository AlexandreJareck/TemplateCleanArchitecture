using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Template.Application.Interfaces;
using Template.Infrastructure.Persistence.Contexts;
using Testcontainers.PostgreSql;

namespace Template.PostgreSql.Integration.Tests;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private const string InitialMigration = "20250326224009_InitialPostgresMigration";
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16.15@sha256:ca0bd484cb98bf4b24eb1010e73fb3fcbd6714d240fbc1a10eea5b7dbecb641d")
        .WithDatabase("template_phase7_tests")
        .WithUsername("template_test_user")
        .WithPassword($"ephemeral-{Guid.NewGuid():N}")
        .Build();

    public async Task InitializeAsync()
    {
        await container.StartAsync();

        await using var context = CreateContext();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(InitialMigration);

        var preflightCount = await ExecuteScalarAsync<long>(context, """
            SELECT
                (SELECT count(*) FROM "Products" WHERE "Name" IS NULL OR length("Name") > 100) +
                (SELECT count(*) FROM "Products" WHERE "BarCode" IS NULL OR length("BarCode") > 50) +
                (SELECT count(*) FROM "Products" WHERE "Price" <> round("Price"::numeric, 2) OR abs("Price") >= 1e16)
            """);
        if (preflightCount != 0)
            throw new InvalidOperationException("The ProductPriceDecimal preflight found unsafe rows.");

        await migrator.MigrateAsync();
    }

    public async Task DisposeAsync() => await container.DisposeAsync();

    public ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(container.GetConnectionString())
            .Options;
        return new ApplicationDbContext(options, new TestAuthenticatedUser());
    }

    public async Task ResetProductsAsync()
    {
        await using var context = CreateContext();
        await context.Products.ExecuteDeleteAsync();
    }

    public static async Task<T> ExecuteScalarAsync<T>(ApplicationDbContext context, string sql)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(result!, typeof(T));
    }

    private sealed record TestAuthenticatedUser : IAuthenticatedUserService
    {
        public string UserId { get; } = Guid.Empty.ToString();
        public string UserName { get; } = "phase7-test";
    }
}

[CollectionDefinition(Name)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL integration";
}
