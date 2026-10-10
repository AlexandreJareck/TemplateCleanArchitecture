using FluentValidation;
using MediatR;
using Template.Application.Helpers;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Caching;
using Template.Application.Interfaces.Repositories;
using Template.Application.Wrappers;
using Template.Application.DTOs.Products;

namespace Template.Application.Features.Products.Commands;
public class UpdateProductCommand : IRequest<BaseResult<ProductDto>>
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    public string BarCode { get; set; }
}

public class UpdateProductCommandHandler(
    IProductRepository productRepository,
    IUnitOfWork unitOfWork,
    ITranslator translator,
    IProductCache productCache) : IRequestHandler<UpdateProductCommand, BaseResult<ProductDto>>
{
    public async Task<BaseResult<ProductDto>> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        var product = await productRepository.GetByIdAsync(request.Id, cancellationToken);

        if (product is null)
        {
            return new Error(ErrorCode.NotFound, translator.GetString(TranslatorMessages.ProductMessages.Product_NotFound_with_id(request.Id)), nameof(request.Id));
        }

        product.Update(request.Name, request.Price, request.BarCode);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await productCache.RemoveAsync(product.Id, cancellationToken);

        return new ProductDto
        {
            Id = product.Id,
            BarCode = product.BarCode,
            CreatedDateTime = product.Created,
            Name = product.Name,
            Price = product.Price,
        };
    }
}

public class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator(ITranslator translator)
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
