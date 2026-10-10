using FluentValidation;
using MediatR;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Repositories;
using Template.Application.Wrappers;
using Template.Application.DTOs.Products;
using Template.Domain.Products.Entities;

namespace Template.Application.Features.Products.Commands;
public class CreateProductCommand : IRequest<BaseResult<ProductDto>>
{
    public string Name { get; set; }
    public decimal Price { get; set; }
    public string BarCode { get; set; }
}
public class CreateProductCommandHandler(IProductRepository productRepository, IUnitOfWork unitOfWork, IClock clock) : IRequestHandler<CreateProductCommand, BaseResult<ProductDto>>
{
    public async Task<BaseResult<ProductDto>> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        var product = new ProductEntity(request.Name, request.Price, request.BarCode);

        await productRepository.AddAsync(product, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ProductDto
        {
            BarCode = product.BarCode,
            Id = product.Id,
            CreatedDateTime = clock.UtcNow,
            Name = product.Name,
            Price = product.Price,
        };
    }
}
public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator(ITranslator translator)
    {
        RuleFor(p => p.Name)
            .NotNull()
            .NotEmpty()
            .MaximumLength(100)
            .WithName(p => translator[nameof(p.Name)]);

        RuleFor(x => x.BarCode)
            .NotNull()
            .NotEmpty()
            .MaximumLength(50)
            .WithName(p => translator[nameof(p.BarCode)]);
    }
}
