using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Template.Application.Features.Products.Commands;
using Template.Application.Features.Products.Queries;
using Template.Application.Services.Product;
using Template.Application.Wrappers;
using Template.Domain.Products.DTOs;

namespace Template.WebApi.Controllers.v2;

[ApiVersion("2.0")]
public class ProductController : BaseApiController
{
    private readonly IMediator _mediator;
    private readonly IProductService _productService;

    public ProductController(
        IMediator mediator,
        IProductService productService)
    {
        _mediator = mediator;
        _productService = productService;
    }

    /// <summary>
    /// Get product by id with cache.
    /// </summary>
    /// <param name="id">Identifier Product</param>
    /// <response code="200">Product found</response>
    /// <response code="404">Product not found</response>
    /// <response code="500">Erro interno</response>
    [HttpGet("{id:guid}/cache")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetWithCache(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _productService.GetProductAsync(id, cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// get products paged
    /// </summary>
    /// <response code="200">List returned successfully.</response>
    /// <response code="400">Invalid parameters</response>
    [HttpGet("get-paged-list")]
    [ProducesResponseType(typeof(PagedResponse<ProductDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPagedListProduct(
        [FromQuery] GetPagedListProductQuery query)
    {
        var result = await _mediator.Send(query);
        return FromPagedResult(result);
    }

    /// <summary>
    /// Get product by ID.
    /// </summary>
    /// <param name="id">Identifier Product</param>
    /// <response code="200">Product found encontrado</response>
    /// <response code="404">Product not found</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProductById(
        [FromRoute] Guid id)
    {
        var result = await _mediator.Send(new GetProductByIdQuery { Id = id });
        return FromResult(result);
    }

    /// <summary>
    /// Create new product.
    /// </summary>
    /// <response code="201">Product created successfully</response>
    /// <response code="400">Invalid data</response>
    /// <response code="401">Not authenticated</response>
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateProduct(
        [FromBody] CreateProductCommand command)
    {
        var result = await _mediator.Send(command);
        return FromResult(result);
    }

    /// <summary>
    /// Update product existing.
    /// </summary>
    /// <response code="200">Updated product</response>
    /// <response code="404">Product not found</response>
    [HttpPut("{id:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProduct(
        [FromRoute] Guid id,
        [FromBody] UpdateProductCommand command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);
        return FromResult(result);
    }

    /// <summary>
    /// Delete product.
    /// </summary>
    /// <response code="204">Product removed</response>
    /// <response code="404">Product not found</response>
    [HttpDelete("{id:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteProduct(
        [FromRoute] Guid id)
    {
        var result = await _mediator.Send(new DeleteProductCommand
        {
            Id = id
        });

        return FromResult(result);
    }

}
