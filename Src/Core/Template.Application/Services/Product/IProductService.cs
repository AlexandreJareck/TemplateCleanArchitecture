using Template.Application.Wrappers;
using Template.Application.DTOs.Products;

namespace Template.Application.Services.Product;

public interface IProductService
{
    Task<BaseResult<ProductDto>> GetProductAsync(Guid productId, CancellationToken cancellationToken);
}
