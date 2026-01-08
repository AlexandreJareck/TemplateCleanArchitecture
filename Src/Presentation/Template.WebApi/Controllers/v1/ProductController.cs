using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Template.Application.Features.Products.Commands;
using Template.Application.Features.Products.Queries;
using Template.Application.Services.Product;
using Template.Application.Wrappers;
using Template.Domain.Products.DTOs;

namespace Template.WebApi.Controllers.v1;

[ApiVersion("1")]
public class ProductController(IProductService productService) : BaseApiController
{
    [HttpGet("{id}/cache")]
    public async Task<BaseResult<ProductDto>> Get([FromRoute] Guid id, CancellationToken cancellationToken)
        => await productService.GetProductAsync(id, cancellationToken);

    [HttpGet("get-paged-list")]
    public async Task<PagedResponse<ProductDto>> GetPagedListProduct([FromQuery] GetPagedListProductQuery model)
        => await Mediator.Send(model);

    [HttpGet("get-by-id")]
    public async Task<BaseResult<ProductDto>> GetProductById([FromQuery] GetProductByIdQuery model)
        => await Mediator.Send(model);

    [HttpPost, Authorize]
    public async Task<BaseResult<ProductDto>> CreateProduct(CreateProductCommand model)
        => await Mediator.Send(model);

    [HttpPut, Authorize]
    public async Task<BaseResult> UpdateProduct(UpdateProductCommand model)
        => await Mediator.Send(model);

    [HttpDelete, Authorize]
    public async Task<BaseResult> DeleteProduct([FromQuery] DeleteProductCommand model)
        => await Mediator.Send(model);
}
