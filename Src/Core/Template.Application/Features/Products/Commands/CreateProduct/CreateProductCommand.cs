using MediatR;
using Template.Application.Wrappers;
using Template.Domain.Products.DTOs;

namespace Template.Application.Features.Products.Commands.CreateProduct;
public class CreateProductCommand : IRequest<BaseResult<ProductDto>>
{
    public string Name { get; set; }
    public double Price { get; set; }
    public string BarCode { get; set; }
}
