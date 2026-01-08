using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Template.Application.Features.Products.Queries;
using Template.Application.Wrappers;
using Template.Domain.Products.DTOs;

namespace Template.Application.Services.Product
{
    public class ProductService : IProductService
    {
        private readonly IDistributedCache _cache;
        private readonly ILogger<IProductService> _logger;
        private readonly IMediator _mediator;
        public ProductService(IDistributedCache cache, ILogger<IProductService> logger, IMediator mediator)
        {
            _cache = cache;
            _logger = logger;
            _mediator = mediator;
        }

        public async Task<BaseResult<ProductDto>> GetProductAsync(Guid productId, CancellationToken cancellationToken)
        {
            string cacheKey = $"Product_{productId}";

            var cachedItem = await _cache.GetStringAsync(cacheKey);
            if (cachedItem != null)
            {
                _logger.LogInformation("product retrieved from cache!");
                return JsonConvert.DeserializeObject<ProductDto>(cachedItem);
            }

            var product = await GetProductByIdQuery(productId, cancellationToken);

            if (product is null)
            {
                return new Error(ErrorCode.NotFound, "Product not found", nameof(productId));
            }

            await _cache.SetStringAsync(
                cacheKey,
                JsonConvert.SerializeObject(product),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
                    SlidingExpiration = TimeSpan.FromMinutes(2)
                }
            );

            _logger.LogInformation("🔄 product added to cache.");
            return product;
        }

        private async Task<BaseResult<ProductDto>> GetProductByIdQuery(Guid productId, CancellationToken cancellationToken)
        {
            return await _mediator.Send(new GetProductByIdQuery
            {
                Id = productId,
            }, cancellationToken);
        }
    }
}
