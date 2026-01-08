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
    /// Obtém um produto pelo ID utilizando cache.
    /// </summary>
    /// <param name="id">Identificador do produto</param>
    /// <response code="200">Produto encontrado</response>
    /// <response code="404">Produto não encontrado</response>
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
    /// Retorna uma lista paginada de produtos.
    /// </summary>
    /// <response code="200">Lista retornada com sucesso</response>
    /// <response code="400">Parâmetros inválidos</response>
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
    /// Obtém um produto pelo ID.
    /// </summary>
    /// <param name="id">Identificador do produto</param>
    /// <response code="200">Produto encontrado</response>
    /// <response code="404">Produto não encontrado</response>
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
    /// Cria um novo produto.
    /// </summary>
    /// <response code="201">Produto criado com sucesso</response>
    /// <response code="400">Dados inválidos</response>
    /// <response code="401">Não autenticado</response>
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
    /// Atualiza um produto existente.
    /// </summary>
    /// <response code="200">Produto atualizado</response>
    /// <response code="404">Produto não encontrado</response>
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
    /// Remove um produto.
    /// </summary>
    /// <response code="204">Produto removido</response>
    /// <response code="404">Produto não encontrado</response>
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
