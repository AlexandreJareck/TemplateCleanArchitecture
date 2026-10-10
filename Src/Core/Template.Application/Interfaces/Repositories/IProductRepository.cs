using Template.Application.DTOs;
using Template.Application.DTOs.Products;
using Template.Domain.Products.Entities;

namespace Template.Application.Interfaces.Repositories;
public interface IProductRepository : IGenericRepository<ProductEntity>
{
    Task<PaginationResponseDto<ProductDto>> GetPagedListAsync(int pageNumber, int pageSize, string name, CancellationToken cancellationToken = default);
}
