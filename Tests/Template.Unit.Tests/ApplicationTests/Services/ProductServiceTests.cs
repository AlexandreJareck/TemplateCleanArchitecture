using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Template.Application.DTOs.Products;
using Template.Application.Features.Products.Queries;
using Template.Application.Interfaces.Caching;
using Template.Application.Services.Product;
using Template.Application.Wrappers;

namespace Template.Unit.Tests.ApplicationTests.Services;

public class ProductServiceTests
{
    private static readonly Guid ProductId = Guid.Parse("d40d3315-06d0-4ffa-8a52-976fb283cb3e");

    private readonly Mock<IProductCache> productCacheMock = new();
    private readonly Mock<IMediator> mediatorMock = new();

    private ProductService CreateService() =>
        new(productCacheMock.Object, NullLogger<ProductService>.Instance, mediatorMock.Object);

    [Fact]
    public async Task GetProductAsync_CacheHit_ReturnsCachedProductWithoutQuery()
    {
        // Arrange
        var cached = new ProductDto { Id = ProductId, Name = "Cached", BarCode = "1", Price = 10m };
        productCacheMock.Setup(cache => cache.GetAsync(ProductId, It.IsAny<CancellationToken>())).ReturnsAsync(cached);

        // Act
        var result = await CreateService().GetProductAsync(ProductId, CancellationToken.None);

        // Assert
        result.Success.ShouldBeTrue();
        result.Data.ShouldBeSameAs(cached);
        mediatorMock.Verify(mediator => mediator.Send(It.IsAny<GetProductByIdQuery>(), It.IsAny<CancellationToken>()), Times.Never);
        productCacheMock.Verify(cache => cache.SetAsync(It.IsAny<ProductDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetProductAsync_CacheMiss_QueriesAndStoresProduct()
    {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;
        var product = new ProductDto { Id = ProductId, Name = "Stored", BarCode = "1", Price = 10m };
        mediatorMock.Setup(mediator => mediator.Send(It.Is<GetProductByIdQuery>(query => query.Id == ProductId), cancellationToken))
                    .ReturnsAsync(BaseResult<ProductDto>.Ok(product));

        // Act
        var result = await CreateService().GetProductAsync(ProductId, cancellationToken);

        // Assert
        result.Success.ShouldBeTrue();
        result.Data.ShouldBeSameAs(product);
        productCacheMock.Verify(cache => cache.GetAsync(ProductId, cancellationToken), Times.Once);
        productCacheMock.Verify(cache => cache.SetAsync(product, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task GetProductAsync_NotFound_ReturnsFailureWithoutCaching()
    {
        // Arrange
        mediatorMock.Setup(mediator => mediator.Send(It.IsAny<GetProductByIdQuery>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(BaseResult<ProductDto>.Failure(new Error(ErrorCode.NotFound, "Product not found", "Id")));

        // Act
        var result = await CreateService().GetProductAsync(ProductId, CancellationToken.None);

        // Assert
        result.Success.ShouldBeFalse();
        result.Errors.ShouldContain(error => error.ErrorCode == ErrorCode.NotFound);
        productCacheMock.Verify(cache => cache.SetAsync(It.IsAny<ProductDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetProductAsync_CallerCancellation_Propagates()
    {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();
        productCacheMock.Setup(cache => cache.GetAsync(ProductId, cancellationTokenSource.Token))
                        .ThrowsAsync(new OperationCanceledException(cancellationTokenSource.Token));

        // Act / Assert
        await Should.ThrowAsync<OperationCanceledException>(
            () => CreateService().GetProductAsync(ProductId, cancellationTokenSource.Token));
        mediatorMock.Verify(mediator => mediator.Send(It.IsAny<GetProductByIdQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
