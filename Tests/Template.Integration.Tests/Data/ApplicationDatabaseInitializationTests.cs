using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Template.Application.Interfaces;
using Template.Infrastructure.Persistence;
using Template.Infrastructure.Persistence.Contexts;

namespace Template.Integration.Tests.Data;

/// <summary>
/// Startup initialization of the application database through the Persistence registration (Phase 21: WebApi no longer
/// resolves <see cref="ApplicationDbContext"/> itself). The relational migration path is covered by the PostgreSQL suite.
/// </summary>
public sealed class ApplicationDatabaseInitializationTests
{
    [Fact]
    public async Task InitializeApplicationDatabaseAsync_SeedsDefaultProductsOnce()
    {
        await using var provider = CreateProvider();

        for (var run = 0; run < 2; run++)
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.InitializeApplicationDatabaseAsync(applyMigrations: false);
        }

        await using var verification = provider.CreateAsyncScope();
        var products = await verification.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .Products.AsNoTracking().OrderBy(product => product.BarCode).ToListAsync();

        products.Count.ShouldBe(5);
        products.Select(product => product.BarCode)
            .ShouldBe(["111111111111", "222222222222", "333333333333", "444444444444", "555555555555"]);
    }

    private static ServiceProvider CreateProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TestDatabaseName"] = Guid.NewGuid().ToString("N") })
            .Build();
        var services = new ServiceCollection();
        services.AddPersistenceInfrastructure(configuration, useInMemoryDatabase: true);
        services.AddScoped<IAuthenticatedUserService>(_ => new AuthenticatedUserService(Guid.NewGuid().ToString(), "seed-test"));
        return services.BuildServiceProvider();
    }
}
