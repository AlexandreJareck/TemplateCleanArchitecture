using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Template.Application.DTOs.Products;
using Template.Application.Interfaces.Caching;
using Template.Infrastructure.Caching;
using Template.Infrastructure.Caching.Products;

namespace Template.Integration.Tests.Caching;

public sealed class DistributedProductCacheTests
{
    private static readonly Guid ProductId = Guid.Parse("c9770569-499d-4b25-59af-08dd5e76cb62");

    private readonly MemoryDistributedCache backend = new(Options.Create(new MemoryDistributedCacheOptions()));

    private static ProductDto Product(string name = "Cached product") =>
        new() { Id = ProductId, Name = name, BarCode = "123456789", Price = 1234.56m, CreatedDateTime = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc) };

    private static DistributedProductCache CreateCache(IDistributedCache cache, CacheSettings? settings = null) =>
        new(cache, Options.Create(settings ?? new CacheSettings()), NullLogger<DistributedProductCache>.Instance);

    [Fact]
    public async Task Get_ReturnsNull_OnMiss()
    {
        var cache = CreateCache(backend);

        (await cache.GetAsync(ProductId, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Set_Then_Get_ReturnsEquivalentProduct_UnderVersionedKey()
    {
        var cache = CreateCache(backend);
        var product = Product();

        await cache.SetAsync(product, CancellationToken.None);
        var cached = await cache.GetAsync(ProductId, CancellationToken.None);

        DistributedProductCache.KeyFor(ProductId).ShouldBe($"template:product:v1:{ProductId:D}");
        (await backend.GetAsync(DistributedProductCache.KeyFor(ProductId))).ShouldNotBeNull();
        cached.ShouldNotBeNull();
        cached.Id.ShouldBe(product.Id);
        cached.Name.ShouldBe(product.Name);
        cached.BarCode.ShouldBe(product.BarCode);
        cached.Price.ShouldBe(product.Price);
        cached.CreatedDateTime.ShouldBe(product.CreatedDateTime);
    }

    [Fact]
    public async Task Get_ReturnsNull_AfterAbsoluteExpiration()
    {
        var cache = CreateCache(backend, new CacheSettings
        {
            AbsoluteExpiration = TimeSpan.FromMilliseconds(150),
            SlidingExpiration = TimeSpan.FromMilliseconds(150)
        });

        await cache.SetAsync(Product(), CancellationToken.None);
        (await cache.GetAsync(ProductId, CancellationToken.None)).ShouldNotBeNull();
        await Task.Delay(TimeSpan.FromMilliseconds(400));

        (await cache.GetAsync(ProductId, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Remove_InvalidatesEntry()
    {
        var cache = CreateCache(backend);
        await cache.SetAsync(Product(), CancellationToken.None);

        await cache.RemoveAsync(ProductId, CancellationToken.None);

        (await cache.GetAsync(ProductId, CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"id\":\"00000000-0000-0000-0000-000000000001\",\"name\":\"other\"}")]
    [InlineData("null")]
    public async Task Get_TreatsUnreadablePayloadAsMiss_AndEvictsIt(string payload)
    {
        var key = DistributedProductCache.KeyFor(ProductId);
        await backend.SetAsync(key, Encoding.UTF8.GetBytes(payload), new DistributedCacheEntryOptions());
        var cache = CreateCache(backend);

        (await cache.GetAsync(ProductId, CancellationToken.None)).ShouldBeNull();
        (await backend.GetAsync(key)).ShouldBeNull();
    }

    [Fact]
    public async Task Operations_TreatFailingBackendAsMiss()
    {
        var cache = CreateCache(new FailingDistributedCache());

        (await cache.GetAsync(ProductId, CancellationToken.None)).ShouldBeNull();
        await cache.SetAsync(Product(), CancellationToken.None);
        await cache.RemoveAsync(ProductId, CancellationToken.None);
    }

    [Fact]
    public async Task Operations_TreatSlowBackendAsMiss_AfterOperationTimeout()
    {
        var cache = CreateCache(new HangingDistributedCache(), new CacheSettings { OperationTimeout = TimeSpan.FromMilliseconds(100) });

        (await cache.GetAsync(ProductId, CancellationToken.None)).ShouldBeNull();
        await cache.SetAsync(Product(), CancellationToken.None);
        await cache.RemoveAsync(ProductId, CancellationToken.None);
    }

    [Fact]
    public async Task Operations_PropagateCallerCancellation()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();
        var cache = CreateCache(new HangingDistributedCache());

        await Should.ThrowAsync<OperationCanceledException>(() => cache.GetAsync(ProductId, cancellationTokenSource.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => cache.SetAsync(Product(), cancellationTokenSource.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => cache.RemoveAsync(ProductId, cancellationTokenSource.Token));
    }

    [Fact]
    public async Task Operations_TreatUnreachableRedisAsMiss()
    {
        // Port 1 on loopback is not a Redis server; no live service is contacted.
        using var redis = new RedisCache(Options.Create(new RedisCacheOptions
        {
            Configuration = "127.0.0.1:1,connectTimeout=200,abortConnect=true"
        }));
        var cache = CreateCache(redis, new CacheSettings { OperationTimeout = TimeSpan.FromSeconds(2) });

        (await cache.GetAsync(ProductId, CancellationToken.None)).ShouldBeNull();
        await cache.SetAsync(Product(), CancellationToken.None);
        await cache.RemoveAsync(ProductId, CancellationToken.None);
    }

    [Fact]
    public void Registration_UsesNoOpCache_WhenDisabled()
    {
        using var provider = BuildProvider(new() { ["CacheSettings:Enabled"] = "false" });

        provider.GetRequiredService<IProductCache>().ShouldBeOfType<NoOpProductCache>();
    }

    [Fact]
    public void Registration_UsesDistributedCache_WithDefaults()
    {
        using var provider = BuildProvider([]);

        provider.GetRequiredService<IProductCache>().ShouldBeOfType<DistributedProductCache>();
        var settings = provider.GetRequiredService<IOptions<CacheSettings>>().Value;
        settings.AbsoluteExpiration.ShouldBe(TimeSpan.FromMinutes(10));
        settings.SlidingExpiration.ShouldBe(TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData("CacheSettings:AbsoluteExpiration", "00:00:00")]
    [InlineData("CacheSettings:SlidingExpiration", "00:20:00")]
    [InlineData("CacheSettings:OperationTimeout", "-00:00:01")]
    public void Registration_RejectsInvalidSettings(string key, string value)
    {
        using var provider = BuildProvider(new() { [key] = value });

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<CacheSettings>>().Value);
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCachingInfrastructure(configuration, useInMemoryCache: true);
        return services.BuildServiceProvider();
    }

    private sealed class FailingDistributedCache : IDistributedCache
    {
        private static InvalidOperationException Failure() => new("backend unavailable");

        public byte[]? Get(string key) => throw Failure();
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromException<byte[]?>(Failure());
        public void Refresh(string key) => throw Failure();
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.FromException(Failure());
        public void Remove(string key) => throw Failure();
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.FromException(Failure());
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw Failure();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Task.FromException(Failure());
    }

    /// <summary>Ignores cancellation and never completes, like a stuck network call.</summary>
    private sealed class HangingDistributedCache : IDistributedCache
    {
        private static readonly TaskCompletionSource Never = new();

        public byte[]? Get(string key) => throw new NotSupportedException();
        public async Task<byte[]?> GetAsync(string key, CancellationToken token = default) { await Never.Task; return null; }
        public void Refresh(string key) => throw new NotSupportedException();
        public Task RefreshAsync(string key, CancellationToken token = default) => Never.Task;
        public void Remove(string key) => throw new NotSupportedException();
        public Task RemoveAsync(string key, CancellationToken token = default) => Never.Task;
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new NotSupportedException();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Never.Task;
    }
}
