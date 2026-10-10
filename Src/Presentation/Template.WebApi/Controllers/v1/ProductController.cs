using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using MediatR;
using Template.Application.Features.Products.Commands;
using Template.Application.Features.Products.Queries;
using Template.Application.Services.Product;
using Template.Application.Wrappers;
using Template.Application.DTOs.Products;

namespace Template.WebApi.Controllers.v1;

[ApiVersion("1", Deprecated = true)]
public class ProductController(IProductService productService, ISender sender) : BaseApiController
{
    [HttpGet("{id}/cache")]
    public async Task<BaseResult<ProductDto>> Get([FromRoute] Guid id, CancellationToken cancellationToken)
        => await productService.GetProductAsync(id, cancellationToken);

    [HttpGet("get-paged-list")]
    public async Task<PagedResponse<ProductDto>> GetPagedListProduct([FromQuery] GetPagedListProductQuery model, CancellationToken cancellationToken)
        => await sender.Send(model, cancellationToken);

    [HttpGet("get-by-id")]
    public async Task<BaseResult<ProductDto>> GetProductById([FromQuery] GetProductByIdQuery model, CancellationToken cancellationToken)
        => await sender.Send(model, cancellationToken);

    [HttpPost, Authorize]
    public async Task<BaseResult<ProductDto>> CreateProduct([FromBody] CreateProductCommand model, CancellationToken cancellationToken)
        => await sender.Send(model, cancellationToken);

    [HttpPut, Authorize]
    public async Task<BaseResult> UpdateProduct([FromBody] UpdateProductCommand model, CancellationToken cancellationToken)
        => await sender.Send(model, cancellationToken);

    [HttpDelete, Authorize]
    public async Task<BaseResult> DeleteProduct([FromQuery] DeleteProductCommand model, CancellationToken cancellationToken)
        => await sender.Send(model, cancellationToken);
}
