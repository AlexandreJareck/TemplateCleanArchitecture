using Shouldly;
using StackExchange.Redis;
using Template.Infrastructure.Caching;

namespace Template.Redis.Integration.Tests;

/// <summary>Unavailability, outage recovery and restart persistence; each test owns its server.</summary>
public sealed class AvailabilityAndPersistenceTests
{
    private static readonly CacheSettings ShortTimeout = new() { OperationTimeout = TimeSpan.FromSeconds(2) };

    [Fact]
    public async Task InitialUnavailability_IsMiss_ThenRecovers_WhenServerStarts()
    {
        await using var server = new RedisServer();
        var id = Guid.NewGuid();
        var (cache, _) = CacheFactory.Create(server.ConnectionString(), ShortTimeout);

        // Nothing listens on the port yet.
        var started = DateTime.UtcNow;
        (await cache.GetAsync(id, CancellationToken.None)).ShouldBeNull();
        await cache.SetAsync(CacheFactory.Product(id), CancellationToken.None);
        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(6));

        await server.InitializeAsync();
        await cache.SetAsync(CacheFactory.Product(id), CancellationToken.None);

        (await RetryGetAsync(cache, id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Outage_IsMiss_AndRestartPreservesCacheSearchAndTimeSeriesData()
    {
        await using var server = new RedisServer();
        await server.InitializeAsync();
        var id = Guid.NewGuid();
        var (cache, _) = CacheFactory.Create(server.ConnectionString(), ShortTimeout);
        await cache.SetAsync(CacheFactory.Product(id), CancellationToken.None);
        await using (var admin = await server.AdminAsync())
        {
            var db = admin.GetDatabase();
            await db.ExecuteAsync("FT.CREATE", "idx:p15:restart", "ON", "HASH", "PREFIX", "1", "p15:restart:", "SCHEMA", "name", "TEXT");
            await db.HashSetAsync("p15:restart:1", "name", "persisted");
            await db.ExecuteAsync("TS.CREATE", "p15:restart:ts", "RETENTION", "0", "LABELS", "phase", "15");
            await db.ExecuteAsync("TS.ADD", "p15:restart:ts", "1000", "42");
        }

        await server.Container.StopAsync(); // SIGTERM: Redis writes the RDB snapshot on shutdown
        (await cache.GetAsync(id, CancellationToken.None)).ShouldBeNull();

        await server.Container.StartAsync();
        var recovered = await RetryGetAsync(cache, id);

        recovered.ShouldNotBeNull();
        recovered.Id.ShouldBe(id);
        await using var after = await server.AdminAsync();
        var afterDb = after.GetDatabase();
        (await AuthenticationAndAclTests.WaitForSearchTotalAsync(afterDb, "idx:p15:restart", "persisted", 1)).ShouldBe(1);
        var samples = (RedisResult[])(await afterDb.ExecuteAsync("TS.RANGE", "p15:restart:ts", "-", "+"))!;
        samples.Length.ShouldBe(1);
        ((RedisResult[])samples[0]!)[1].ToString().ShouldBe("42");
        var ttl = await afterDb.KeyTimeToLiveAsync(Template.Infrastructure.Caching.Products.DistributedProductCache.KeyFor(id));
        ttl.ShouldNotBeNull();
    }

    private static async Task<Application.DTOs.Products.ProductDto?> RetryGetAsync(Template.Infrastructure.Caching.Products.DistributedProductCache cache, Guid id)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var product = await cache.GetAsync(id, CancellationToken.None);
            if (product is not null) return product;
            await Task.Delay(500);
        }

        return null;
    }
}
