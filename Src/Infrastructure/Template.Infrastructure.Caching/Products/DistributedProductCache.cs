using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Template.Application.DTOs.Products;
using Template.Application.Interfaces.Caching;

namespace Template.Infrastructure.Caching.Products;

/// <summary>
/// <see cref="IProductCache"/> over <see cref="IDistributedCache"/> (Redis outside the Test environment).
/// Failures and timeouts are logged without payload and reported as cache misses; only caller
/// cancellation propagates.
/// </summary>
public sealed class DistributedProductCache(
    IDistributedCache cache,
    IOptions<CacheSettings> options,
    ILogger<DistributedProductCache> logger) : IProductCache
{
    public const string KeyPrefix = "template:product:v1:";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly CacheSettings settings = options.Value;

    public static string KeyFor(Guid productId) => $"{KeyPrefix}{productId:D}";

    public async Task<ProductDto?> GetAsync(Guid productId, CancellationToken cancellationToken)
    {
        var key = KeyFor(productId);
        byte[]? payload;
        try
        {
            payload = await RunAsync(token => cache.GetAsync(key, token), cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, "get", key);
            return null;
        }

        if (payload is null)
        {
            return null;
        }

        ProductDto? product;
        try
        {
            product = JsonSerializer.Deserialize<ProductDto>(payload, SerializerOptions);
        }
        catch (JsonException)
        {
            product = null;
        }

        if (product is null || product.Id != productId)
        {
            logger.LogWarning("Unreadable product cache entry {CacheKey}; treating as miss and evicting.", key);
            await RemoveAsync(productId, cancellationToken);
            return null;
        }

        return product;
    }

    public async Task SetAsync(ProductDto product, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(product);
        var key = KeyFor(product.Id);
        var payload = JsonSerializer.SerializeToUtf8Bytes(product, SerializerOptions);
        var entryOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = settings.AbsoluteExpiration,
            SlidingExpiration = settings.SlidingExpiration
        };

        try
        {
            await RunAsync(token => cache.SetAsync(key, payload, entryOptions, token), cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, "set", key);
        }
    }

    public async Task RemoveAsync(Guid productId, CancellationToken cancellationToken)
    {
        var key = KeyFor(productId);
        try
        {
            await RunAsync(token => cache.RemoveAsync(key, token), cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, "remove", key);
        }
    }

    private Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        RunAsync(async token =>
        {
            await operation(token);
            return true;
        }, cancellationToken);

    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.OperationTimeout);
        return await operation(timeout.Token).WaitAsync(timeout.Token);
    }

    private void LogFailure(Exception exception, string operation, string key) =>
        logger.LogWarning(
            "Product cache {Operation} failed for {CacheKey} ({ExceptionType}); treating as miss.",
            operation,
            key,
            exception.GetType().Name);
}
