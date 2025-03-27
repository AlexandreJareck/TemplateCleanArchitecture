using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
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
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(nameof(ApplicationDbContext)));
        }
        else
        {
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));
        }

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.RegisterRepositories();

        return services;
    }
    private static void RegisterRepositories(this IServiceCollection services)
    {
        var interfaceType = typeof(IGenericRepository<>);
        var interfaces = Assembly.GetAssembly(interfaceType)!.GetTypes()
            .Where(p => p.GetInterface(interfaceType.Name) != null);

        var implementations = Assembly.GetAssembly(typeof(GenericRepository<>))!.GetTypes();

        foreach (var item in interfaces)
        {
            var implementation = implementations.FirstOrDefault(p => p.GetInterface(item.Name) != null);

            if (implementation is not null)
                services.AddTransient(item, implementation);
        }
    }
}

