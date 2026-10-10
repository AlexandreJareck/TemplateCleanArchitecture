using Template.Application.DTOs;
using Template.Application.Interfaces.Repositories;
using Template.Application.DTOs.Products;
using Template.Domain.Products.Entities;
using Template.Infrastructure.Persistence.Contexts;

namespace Template.Infrastructure.Persistence.Repositories;
public class ProductRepository(ApplicationDbContext dbContext) : GenericRepository<ProductEntity>(dbContext), IProductRepository
{
    public async Task<PaginationResponseDto<ProductDto>> GetPagedListAsync(int pageNumber, int pageSize, string name, CancellationToken cancellationToken = default)
    {
        var query = DbContext.Products
            .OrderBy(p => p.Created)
            .ThenBy(p => p.Id)
            .AsQueryable();

        if (!string.IsNullOrEmpty(name))
            query = query.Where(p => p.Name.Contains(name));

        return await Paged(
            query.Select(p => new ProductDto(p)),
            pageNumber,
            pageSize,
            cancellationToken);
    }
}
