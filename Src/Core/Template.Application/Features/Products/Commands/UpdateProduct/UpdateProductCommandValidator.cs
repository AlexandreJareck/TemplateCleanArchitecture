using FluentValidation;
using Template.Application.Interfaces;

namespace Template.Application.Features.Products.Commands.UpdateProduct
{
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
}
