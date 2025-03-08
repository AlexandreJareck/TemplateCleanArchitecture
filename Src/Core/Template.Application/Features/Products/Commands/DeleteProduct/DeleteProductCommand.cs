using MediatR;
using Template.Application.Wrappers;

namespace Template.Application.Features.Products.Commands.DeleteProduct
{
    public class DeleteProductCommand : IRequest<BaseResult>
    {
        public Guid Id { get; set; }
    }
}
