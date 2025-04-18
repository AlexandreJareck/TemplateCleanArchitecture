﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Template.Application.Features.Products.Commands;
using Template.Application.Features.Products.Queries;
using Template.Application.Services.Product;
using Template.Application.Wrappers;
using Template.Domain.Products.DTOs;

namespace Template.WebApi.Controllers.v1;

[ApiVersion("1")]
public class ProductController : BaseApiController
{
    private readonly IProductService _productService;
    public ProductController(IProductService productService)
    {
        _productService = productService;
    }

    [HttpGet("{id}/GetWithCache")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var product = await _productService.GetProductAsync(id, cancellationToken);
        return Ok(product);
    }

    [HttpGet]
    public async Task<PagedResponse<ProductDto>> GetPagedListProduct([FromQuery] GetPagedListProductQuery model)
        => await Mediator.Send(model);

    [HttpGet]
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
