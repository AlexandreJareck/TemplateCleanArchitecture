using MediatR;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Repositories;
using Template.Application.Wrappers;
using Template.Domain.Products.DTOs;
using Template.Domain.Products.Entities;

namespace Template.Application.Features.Products.Commands.CreateProduct
{
    public class CreateProductCommandHandler(IProductRepository productRepository, IUnitOfWork unitOfWork) : IRequestHandler<CreateProductCommand, BaseResult<ProductDto>>
    {
        public async Task<BaseResult<ProductDto>> Handle(CreateProductCommand request, CancellationToken cancellationToken)
        {
            var product = new ProductEntity(request.Name, request.Price, request.BarCode);

            await productRepository.AddAsync(product);
            await unitOfWork.SaveChangesAsync();

            return new ProductDto
            {
                BarCode = product.BarCode,
                Id = product.Id,
                CreatedDateTime = DateTime.UtcNow,
                Name = product.Name,
                Price = product.Price,
            };
        }
    }
}
