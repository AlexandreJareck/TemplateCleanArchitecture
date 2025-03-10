using FluentValidation;
using MediatR;
using Template.Application.Helpers;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Repositories;
using Template.Application.Wrappers;

namespace Template.Application.Features.Products.Commands;
public class UpdateProductCommand : IRequest<BaseResult>
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public double Price { get; set; }
    public string BarCode { get; set; }
}

public class UpdateProductCommandHandler(IProductRepository productRepository, IUnitOfWork unitOfWork, ITranslator translator) : IRequestHandler<UpdateProductCommand, BaseResult>
{
    public async Task<BaseResult> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        var product = await productRepository.GetByIdAsync(request.Id);

        if (product is null)
        {
            return new Error(ErrorCode.NotFound, translator.GetString(TranslatorMessages.ProductMessages.Product_NotFound_with_id(request.Id)), nameof(request.Id));
        }

        product.Update(request.Name, request.Price, request.BarCode);
        await unitOfWork.SaveChangesAsync();

        return BaseResult.Ok();
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
