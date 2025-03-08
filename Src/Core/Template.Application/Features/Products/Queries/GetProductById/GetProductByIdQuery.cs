using MediatR;
using Template.Application.Wrappers;
using Template.Domain.Products.DTOs;

namespace Template.Application.Features.Products.Queries.GetProductById
{
    public class GetProductByIdQuery : IRequest<BaseResult<ProductDto>>
    {
        public Guid Id { get; set; }
    }
}
