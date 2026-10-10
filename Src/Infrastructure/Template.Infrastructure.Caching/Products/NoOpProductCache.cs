using Template.Application.DTOs.Products;
using Template.Application.Interfaces.Caching;

namespace Template.Infrastructure.Caching.Products;

/// <summary>Used when caching is disabled: every read is a miss and writes are ignored.</summary>
public sealed class NoOpProductCache : IProductCache
{
    public Task<ProductDto?> GetAsync(Guid productId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ProductDto?>(null);
    }

    public Task SetAsync(ProductDto product, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid productId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
