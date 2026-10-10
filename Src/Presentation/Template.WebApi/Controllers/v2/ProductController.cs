using MediatR;
using Microsoft.AspNetCore.Authorization;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Template.Application.Features.Products.Commands;
using Template.Application.Features.Products.Queries;
using Template.Application.Services.Product;
using Template.Application.Wrappers;
using Template.Application.DTOs.Products;
using Template.WebApi.Infrastructure.Http;

namespace Template.WebApi.Controllers.v2;

[ApiVersion("2.0")]
public class ProductController(
    ISender sender,
    IProductService productService,
    ApiResultMapper resultMapper) : BaseApiController
{
    /// <summary>
    /// Get product by id with cache.
    /// </summary>
    /// <param name="id">Identifier Product</param>
    /// <response code="200">Product found</response>
    /// <response code="404">Product not found</response>
    /// <response code="500">Erro interno</response>
    [HttpGet("{id:guid}/cache")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<IActionResult> GetWithCache(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var result = await productService.GetProductAsync(id, cancellationToken);
        return resultMapper.ToActionResult(this, result);
    }

    /// <summary>
    /// get products paged
    /// </summary>
    /// <response code="200">List returned successfully.</response>
    /// <response code="400">Invalid parameters</response>
    [HttpGet("get-paged-list")]
    [ProducesResponseType(typeof(PagedResponse<ProductDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> GetPagedListProduct(
        [FromQuery] GetPagedListProductQuery query,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(query, cancellationToken);
        return resultMapper.ToActionResult(this, result);
    }

    /// <summary>
    /// Get product by ID.
    /// </summary>
    /// <param name="id">Identifier Product</param>
    /// <response code="200">Product found encontrado</response>
    /// <response code="404">Product not found</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> GetProductById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetProductByIdQuery { Id = id }, cancellationToken);
        return resultMapper.ToActionResult(this, result);
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
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> CreateProduct(
        [FromBody] CreateProductCommand command,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return resultMapper.ToActionResult(
            this,
            result,
            () => Created($"/api/v2/product/{result.Data.Id}", result));
    }

    /// <summary>
    /// Update product existing.
    /// </summary>
    /// <response code="200">Updated product</response>
    /// <response code="404">Product not found</response>
    [HttpPut("{id:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> UpdateProduct(
        [FromRoute] Guid id,
        [FromBody] UpdateProductCommand command,
        CancellationToken cancellationToken)
    {
        command.Id = id;
        var result = await sender.Send(command, cancellationToken);
        return resultMapper.ToActionResult(this, result);
    }

    /// <summary>
    /// Delete product.
    /// </summary>
    /// <response code="204">Product removed</response>
    /// <response code="404">Product not found</response>
    [HttpDelete("{id:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> DeleteProduct(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteProductCommand
        {
            Id = id
        }, cancellationToken);

        return resultMapper.ToActionResult(this, result, NoContent);
    }

}
