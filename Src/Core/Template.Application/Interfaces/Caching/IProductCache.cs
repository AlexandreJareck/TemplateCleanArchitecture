#nullable enable
using Template.Application.DTOs.Products;

namespace Template.Application.Interfaces.Caching;

/// <summary>
/// Read-through cache for single products. Implementations treat every cache failure
/// (unavailable backend, timeout, unreadable payload) as a miss; only cancellation
/// requested by the caller is propagated.
/// </summary>
public interface IProductCache
{
    Task<ProductDto?> GetAsync(Guid productId, CancellationToken cancellationToken);

    Task SetAsync(ProductDto product, CancellationToken cancellationToken);

    Task RemoveAsync(Guid productId, CancellationToken cancellationToken);
}
