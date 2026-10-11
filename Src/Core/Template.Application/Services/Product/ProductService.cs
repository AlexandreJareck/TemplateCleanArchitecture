#nullable enable
using Template.Application.Logging;
using MediatR;
using Microsoft.Extensions.Logging;
using Template.Application.DTOs.Products;
using Template.Application.Features.Products.Queries;
using Template.Application.Interfaces.Caching;
using Template.Application.Wrappers;

namespace Template.Application.Services.Product;

public class ProductService(IProductCache productCache, ILogger<ProductService> logger, IMediator mediator) : IProductService
{
    public async Task<BaseResult<ProductDto>> GetProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        var cachedProduct = await productCache.GetAsync(productId, cancellationToken);
        if (cachedProduct is not null)
        {
            logger.LogDebug(LogEvents.ProductCacheHit, "Product {ProductId} retrieved from cache.", productId);
            return cachedProduct;
        }

        var result = await mediator.Send(new GetProductByIdQuery { Id = productId }, cancellationToken);

        if (result.Success && result.Data is not null)
        {
            await productCache.SetAsync(result.Data, cancellationToken);
            logger.LogDebug(LogEvents.ProductCached, "Product {ProductId} added to cache.", productId);
        }

        return result;
    }
}
