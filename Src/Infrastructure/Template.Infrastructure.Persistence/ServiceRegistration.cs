using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Repositories;
using Template.Infrastructure.Persistence.Contexts;
using Template.Infrastructure.Persistence.Repositories;

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

        return services;
    }

    /// <summary>Readiness probe of the application database (EF Core <c>CanConnectAsync</c>), without exposing the context.</summary>
    public static Task<bool> CanConnectToApplicationDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<ApplicationDbContext>().Database.CanConnectAsync(cancellationToken);
}
