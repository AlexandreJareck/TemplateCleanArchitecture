using MediatR;
using Template.Application.Interfaces.Repositories;
using Template.Application.Parameters;
using Template.Application.Wrappers;
using Template.Domain.Products.DTOs;

namespace Template.Application.Features.Products.Queries;
public class GetPagedListProductQuery : PaginationRequestParameter, IRequest<PagedResponse<ProductDto>>
{
    public string Name { get; set; }
}

public class GetPagedListProductQueryHandler(IProductRepository productRepository) : IRequestHandler<GetPagedListProductQuery, PagedResponse<ProductDto>>
{
    public async Task<PagedResponse<ProductDto>> Handle(GetPagedListProductQuery request, CancellationToken cancellationToken)
    {
        return await productRepository.GetPagedListAsync(request.PageNumber, request.PageSize, request.Name);
    }
}
