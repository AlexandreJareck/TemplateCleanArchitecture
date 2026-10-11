using System.Text.Json;
using Moq;
using Shouldly;
using StackExchange.Redis;
using Template.Application.Features.Products.Commands;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Repositories;
using Template.Domain.Products.Entities;
using Template.Infrastructure.Caching;
using Template.Infrastructure.Caching.Products;

namespace Template.Redis.Integration.Tests;

/// <summary>The Phase 8 product-cache contract against a real Redis 8 server.</summary>
public sealed class ProductCacheRedisTests(RedisServer server) : IClassFixture<RedisServer>
{
    [Fact]
    public async Task Get_ReturnsNull_OnMiss()
    {
        var (cache, _) = CacheFactory.Create(server.ConnectionString());

        (await cache.GetAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Set_Then_Get_RoundTrips_UnderVersionedKey_WithV1JsonPayload()
    {
        var id = Guid.NewGuid();
        var (cache, _) = CacheFactory.Create(server.ConnectionString());
        var product = CacheFactory.Product(id);

        await cache.SetAsync(product, CancellationToken.None);
        var cached = await cache.GetAsync(id, CancellationToken.None);

        cached.ShouldNotBeNull();
        cached.Name.ShouldBe(product.Name);
        cached.Price.ShouldBe(product.Price);
        cached.BarCode.ShouldBe(product.BarCode);
        cached.CreatedDateTime.ShouldBe(product.CreatedDateTime);

        // Wire format: a hash under template:product:v1:{id} whose data field is the web-cased v1 JSON document.
        await using var admin = await server.AdminAsync();
        var db = admin.GetDatabase();
        var key = DistributedProductCache.KeyFor(id);
        (await db.KeyTypeAsync(key)).ShouldBe(RedisType.Hash);
        var fields = (await db.HashGetAllAsync(key)).ToDictionary(e => e.Name.ToString(), e => e.Value);
        fields.Keys.OrderBy(k => k, StringComparer.Ordinal).ShouldBe(["absexp", "data", "sldexp"]);
        using var json = JsonDocument.Parse((byte[])fields["data"]!);
        json.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["id", "name", "price", "barCode", "createdDateTime"]);
        json.RootElement.GetProperty("id").GetGuid().ShouldBe(id);
    }

    [Fact]
    public async Task EntryInTheStoredHashLayout_WrittenByAnotherServer_IsReadable()
    {
        // The layout migrated from Redis 8.4.0 by the Phase 15 rehearsal: absexp/sldexp ticks plus the v1 JSON document.
        var id = Guid.NewGuid();
        var key = DistributedProductCache.KeyFor(id);
        var json = $"{{\"id\":\"{id:D}\",\"name\":\"Migrated product\",\"price\":12.34,\"barCode\":\"7890000000000\",\"createdDateTime\":\"2026-10-11T12:00:00Z\"}}";
        await using var admin = await server.AdminAsync();
        var db = admin.GetDatabase();
        await db.HashSetAsync(key, [new("absexp", DateTimeOffset.UtcNow.AddDays(1).UtcTicks), new("sldexp", TimeSpan.FromHours(12).Ticks), new("data", json)]);
        await db.KeyExpireAsync(key, TimeSpan.FromHours(12));
        var (cache, _) = CacheFactory.Create(server.ConnectionString());

        var cached = await cache.GetAsync(id, CancellationToken.None);

        cached.ShouldNotBeNull();
        cached.Name.ShouldBe("Migrated product");
        cached.Price.ShouldBe(12.34m);
        cached.CreatedDateTime.ShouldBe(new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Set_AppliesServerSideTtl_BoundedBySlidingExpiration()
    {
        var id = Guid.NewGuid();
        var (cache, _) = CacheFactory.Create(server.ConnectionString(), new CacheSettings { AbsoluteExpiration = TimeSpan.FromMinutes(10), SlidingExpiration = TimeSpan.FromMinutes(2) });

        await cache.SetAsync(CacheFactory.Product(id), CancellationToken.None);

        await using var admin = await server.AdminAsync();
        var ttl = await admin.GetDatabase().KeyTimeToLiveAsync(DistributedProductCache.KeyFor(id));
        ttl.ShouldNotBeNull();
        ttl.Value.ShouldBeInRange(TimeSpan.FromSeconds(100), TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task AbsoluteExpiration_EvictsEntry()
    {
        var id = Guid.NewGuid();
        var (cache, _) = CacheFactory.Create(server.ConnectionString(), new CacheSettings { AbsoluteExpiration = TimeSpan.FromMilliseconds(2500), SlidingExpiration = TimeSpan.FromMilliseconds(2500) });

        await cache.SetAsync(CacheFactory.Product(id), CancellationToken.None);
        (await cache.GetAsync(id, CancellationToken.None)).ShouldNotBeNull();
        // The client sets TTLs in whole seconds (rounded up) and the read above refreshes it: allow ceil(2.5 s) + 1 s.
        await Task.Delay(TimeSpan.FromMilliseconds(4500));

        (await cache.GetAsync(id, CancellationToken.None)).ShouldBeNull();
        await using var admin = await server.AdminAsync();
        (await admin.GetDatabase().KeyExistsAsync(DistributedProductCache.KeyFor(id))).ShouldBeFalse();
    }

    [Fact]
    public async Task SlidingExpiration_IsRefreshedByReads_ButCappedByAbsoluteExpiration()
    {
        var id = Guid.NewGuid();
        // The client applies TTLs in whole seconds (rounded up), so the windows are sized in seconds with >= 1 s margins.
        var (cache, _) = CacheFactory.Create(server.ConnectionString(), new CacheSettings { AbsoluteExpiration = TimeSpan.FromSeconds(6), SlidingExpiration = TimeSpan.FromSeconds(4) });
        await cache.SetAsync(CacheFactory.Product(id), CancellationToken.None);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        // Reads at 2 s, 4 s and 5 s keep the entry beyond the 4 s sliding window...
        foreach (var at in new[] { 2000, 4000, 5000 })
        {
            await Task.Delay(TimeSpan.FromMilliseconds(at) - clock.Elapsed);
            (await cache.GetAsync(id, CancellationToken.None)).ShouldNotBeNull($"read at {at} ms");
        }

        // ...but not beyond the 6 s absolute expiration (at most 7 s with rounding), although the refresh at 5 s
        // would otherwise slide it to 9 s.
        await Task.Delay(TimeSpan.FromMilliseconds(8000) - clock.Elapsed);
        (await cache.GetAsync(id, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task SlidingExpiration_EvictsIdleEntry()
    {
        var id = Guid.NewGuid();
        var (cache, _) = CacheFactory.Create(server.ConnectionString(), new CacheSettings { AbsoluteExpiration = TimeSpan.FromMinutes(1), SlidingExpiration = TimeSpan.FromSeconds(2) });
        await cache.SetAsync(CacheFactory.Product(id), CancellationToken.None);

        // Whole-second TTLs (rounded up): idle for the 2 s window plus a full second of margin.
        await Task.Delay(TimeSpan.FromMilliseconds(4000));

        (await cache.GetAsync(id, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task UpdateHandler_InvalidatesCachedProduct()
    {
        var entity = new ProductEntity(Guid.NewGuid(), "Before update", 10m, "111");
        var (cache, _) = CacheFactory.Create(server.ConnectionString());
        await cache.SetAsync(CacheFactory.Product(entity.Id), CancellationToken.None);
        (await cache.GetAsync(entity.Id, CancellationToken.None)).ShouldNotBeNull();
        var repository = new Mock<IProductRepository>();
        repository.Setup(r => r.GetByIdAsync(entity.Id, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        var handler = new UpdateProductCommandHandler(repository.Object, Mock.Of<IUnitOfWork>(), Mock.Of<ITranslator>(), cache);

        var result = await handler.Handle(new UpdateProductCommand { Id = entity.Id, Name = "After update", Price = 20m, BarCode = "222" }, CancellationToken.None);

        result.Success.ShouldBeTrue();
        (await cache.GetAsync(entity.Id, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task DeleteHandler_InvalidatesCachedProduct()
    {
        var entity = new ProductEntity(Guid.NewGuid(), "To delete", 10m, "333");
        var (cache, _) = CacheFactory.Create(server.ConnectionString());
        await cache.SetAsync(CacheFactory.Product(entity.Id), CancellationToken.None);
        (await cache.GetAsync(entity.Id, CancellationToken.None)).ShouldNotBeNull();
        var repository = new Mock<IProductRepository>();
        repository.Setup(r => r.GetByIdAsync(entity.Id, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        var handler = new DeleteProductCommandHandler(repository.Object, Mock.Of<IUnitOfWork>(), Mock.Of<ITranslator>(), cache);

        var result = await handler.Handle(new DeleteProductCommand { Id = entity.Id }, CancellationToken.None);

        result.Success.ShouldBeTrue();
        (await cache.GetAsync(entity.Id, CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"id\":\"00000000-0000-0000-0000-000000000001\",\"name\":\"other product\"}")]
    public async Task UnreadablePayload_IsMiss_AndEvicted(string payload)
    {
        var id = Guid.NewGuid();
        var key = DistributedProductCache.KeyFor(id);
        await using var admin = await server.AdminAsync();
        var db = admin.GetDatabase();
        await db.HashSetAsync(key, [new("absexp", -1), new("sldexp", -1), new("data", payload)]);
        var logger = new CapturingLogger<DistributedProductCache>();
        var (cache, _) = CacheFactory.Create(server.ConnectionString(), logger: logger);

        (await cache.GetAsync(id, CancellationToken.None)).ShouldBeNull();

        (await db.KeyExistsAsync(key)).ShouldBeFalse();
        logger.Messages.ShouldContain(m => m.Contains("Unreadable product cache entry"));
        logger.Messages.ShouldAllBe(m => !m.Contains(payload));
    }

    [Fact]
    public async Task OperationTimeout_IsMiss_WhenServerStalls()
    {
        var id = Guid.NewGuid();
        var (cache, _) = CacheFactory.Create(server.ConnectionString(), new CacheSettings { OperationTimeout = TimeSpan.FromMilliseconds(250) });
        await cache.SetAsync(CacheFactory.Product(id), CancellationToken.None);
        await using var admin = await server.AdminAsync();

        await admin.GetServer(server.Endpoint).ExecuteAsync("CLIENT", "PAUSE", "1500", "ALL");
        var started = DateTime.UtcNow;
        var result = await cache.GetAsync(id, CancellationToken.None);
        var elapsed = DateTime.UtcNow - started;
        await Task.Delay(TimeSpan.FromMilliseconds(1600));

        result.ShouldBeNull();
        elapsed.ShouldBeLessThan(TimeSpan.FromMilliseconds(1200));
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        var (cache, _) = CacheFactory.Create(server.ConnectionString(), new CacheSettings { OperationTimeout = TimeSpan.FromSeconds(5) });
        await cache.GetAsync(Guid.NewGuid(), CancellationToken.None); // connect first
        await using var admin = await server.AdminAsync();
        await admin.GetServer(server.Endpoint).ExecuteAsync("CLIENT", "PAUSE", "1500", "ALL");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Should.ThrowAsync<OperationCanceledException>(() => cache.GetAsync(Guid.NewGuid(), cancellation.Token));
        await Task.Delay(TimeSpan.FromMilliseconds(1600));
    }
}
