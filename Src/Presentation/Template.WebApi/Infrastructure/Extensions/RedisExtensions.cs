namespace Template.WebApi.Infrastructure.Extensions
{
    public static class RedisExtensions
    {
        public static IServiceCollection AddRedis(this IServiceCollection services, IConfiguration configuration)
        {
            var redisSettings = configuration.GetConnectionString("RedisConnection");
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisSettings;
                options.InstanceName = "redis-local";
            });
            return services;
        }
    }
}
