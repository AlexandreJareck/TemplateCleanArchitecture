using Template.Application.Wrappers;
using Template.Domain.Products.DTOs;

namespace Template.Application.Services.Product;

public interface IProductService
{
    Task<BaseResult<ProductDto>> GetProductAsync(Guid productId, CancellationToken cancellationToken);
}
