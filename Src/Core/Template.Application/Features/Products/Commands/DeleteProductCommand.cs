using MediatR;
using Template.Application.Helpers;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Repositories;
using Template.Application.Wrappers;

namespace Template.Application.Features.Products.Commands;
public class DeleteProductCommand : IRequest<BaseResult>
{
    public Guid Id { get; set; }
}

public class DeleteProductCommandHandler(IProductRepository productRepository, IUnitOfWork unitOfWork, ITranslator translator) : IRequestHandler<DeleteProductCommand, BaseResult>
{
    public async Task<BaseResult> Handle(DeleteProductCommand request, CancellationToken cancellationToken)
    {
        var product = await productRepository.GetByIdAsync(request.Id);

        if (product is null)
        {
            return new Error(ErrorCode.NotFound, translator.GetString(TranslatorMessages.ProductMessages.Product_NotFound_with_id(request.Id)), nameof(request.Id));
        }

        productRepository.Delete(product);
        await unitOfWork.SaveChangesAsync();

        return BaseResult.Ok();
    }
}
