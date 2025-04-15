using MediatR;
using Template.Application.Helpers;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Repositories;
using Template.Application.Wrappers;
using Template.Domain.Products.DTOs;

namespace Template.Application.Features.Products.Queries;
public class GetProductByIdQuery : IRequest<BaseResult<ProductDto>>
{
    public Guid Id { get; set; }
}

public class GetProductByIdQueryHandler(IProductRepository productRepository, ITranslator translator) : IRequestHandler<GetProductByIdQuery, BaseResult<ProductDto>>
{
    public async Task<BaseResult<ProductDto>> Handle(GetProductByIdQuery request, CancellationToken cancellationToken)
    {
        var product = await productRepository.GetByIdAsync(request.Id);

        if (product is null)
        {
            return new Error(ErrorCode.NotFound, translator.GetString(TranslatorMessages.ProductMessages.Product_NotFound_with_id(request.Id)), nameof(request.Id));
        }

        return new ProductDto(product);
    }
}
