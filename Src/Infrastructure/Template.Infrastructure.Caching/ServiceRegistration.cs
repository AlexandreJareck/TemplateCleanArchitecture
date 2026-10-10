using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Template.Application.Interfaces.Caching;
using Template.Infrastructure.Caching.Products;

namespace Template.Infrastructure.Caching;

public static class ServiceRegistration
{
    public const string RedisConnectionStringName = "RedisConnection";

    /// <summary>
    /// Registers <see cref="IProductCache"/>. When <c>CacheSettings:Enabled</c> is false a no-op cache is used;
    /// otherwise an in-memory distributed cache (Test) or Redis (<c>ConnectionStrings:RedisConnection</c>).
    /// </summary>
    public static IServiceCollection AddCachingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool useInMemoryCache)
    {
        var section = configuration.GetSection(CacheSettings.SectionName);
        services.AddOptions<CacheSettings>()
            .Bind(section)
            .Validate(CacheSettings.IsValid, "CacheSettings requires positive expirations and timeout, with SlidingExpiration <= AbsoluteExpiration.")
            .ValidateOnStart();

        // The backend is registered regardless of Enabled; Redis connects lazily, so a disabled cache never contacts it.
        if (useInMemoryCache)
        {
            services.AddDistributedMemoryCache();
        }
        else
        {
            var connectionString = configuration.GetConnectionString(RedisConnectionStringName);
            services.AddStackExchangeRedisCache(options => options.Configuration = connectionString);
        }

        services.AddSingleton<IProductCache>(provider =>
            provider.GetRequiredService<IOptions<CacheSettings>>().Value.Enabled
                ? ActivatorUtilities.CreateInstance<DistributedProductCache>(provider)
                : new NoOpProductCache());
        return services;
    }
}
