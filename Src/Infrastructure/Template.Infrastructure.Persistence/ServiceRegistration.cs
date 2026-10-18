using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Access;
using Template.Application.Interfaces.Auditing;
using Template.Application.Interfaces.Catalog;
using Template.Application.Interfaces.Idempotency;
using Template.Application.Interfaces.Organization;
using Template.Application.Interfaces.Repositories;
using Template.Application.Interfaces.Suppliers;
using Template.Infrastructure.Persistence.Contexts;
using Template.Infrastructure.Persistence.Idempotency;
using Template.Infrastructure.Persistence.Repositories;
using Template.Infrastructure.Persistence.Seeds;

namespace Template.Infrastructure.Persistence;

public static class ServiceRegistration
{
    public static IServiceCollection AddPersistenceInfrastructure(this IServiceCollection services, IConfiguration configuration, bool useInMemoryDatabase)
    {
        if (useInMemoryDatabase)
        {
            var databaseName = configuration["TestDatabaseName"] is { Length: > 0 } testDatabaseName
                ? $"{nameof(ApplicationDbContext)}-{testDatabaseName}"
                : nameof(ApplicationDbContext);
            var databaseRoot = new InMemoryDatabaseRoot();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(databaseName, databaseRoot));
        }
        else
        {
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));
        }

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ISessionFamilyRepository, SessionFamilyRepository>();
        services.AddScoped<AuditRecordRepository>();
        services.AddScoped<IAuditTrail>(provider => provider.GetRequiredService<AuditRecordRepository>());
        services.AddScoped<IAuditRecordReader>(provider => provider.GetRequiredService<AuditRecordRepository>());
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<ICostCenterRepository, CostCenterRepository>();
        services.AddScoped<IOrganizationSettingsRepository, OrganizationSettingsRepository>();
        services.AddScoped<IOrganizationSettingsLock, NoBudgetAllocationsYetSettingsLock>();
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IUnitOfMeasureRepository, UnitOfMeasureRepository>();
        services.AddScoped<ICatalogItemRepository, CatalogItemRepository>();

        return services;
    }

    /// <summary>
    /// Applies application migrations (relational providers only) and seeds the default products.
    /// Keeps <see cref="ApplicationDbContext"/> inside this project.
    /// </summary>
    public static async Task InitializeApplicationDatabaseAsync(this IServiceProvider services, bool applyMigrations)
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        if (applyMigrations)
            await context.Database.MigrateAsync();

        await DefaultData.SeedAsync(context);
    }

    /// <summary>Readiness probe of the application database (EF Core <c>CanConnectAsync</c>), without exposing the context.</summary>
    public static Task<bool> CanConnectToApplicationDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<ApplicationDbContext>().Database.CanConnectAsync(cancellationToken);
}
