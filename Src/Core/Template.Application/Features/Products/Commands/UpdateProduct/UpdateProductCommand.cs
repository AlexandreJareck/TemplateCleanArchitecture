using MediatR;
using Template.Application.Wrappers;

namespace Template.Application.Features.Products.Commands.UpdateProduct;
public class UpdateProductCommand : IRequest<BaseResult>
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public double Price { get; set; }
    public string BarCode { get; set; }
}
