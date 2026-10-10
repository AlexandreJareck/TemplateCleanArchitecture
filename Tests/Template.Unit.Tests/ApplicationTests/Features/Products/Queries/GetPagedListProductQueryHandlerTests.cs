using Moq;
using Shouldly;
using Template.Application.DTOs;
using Template.Application.Features.Products.Queries;
using Template.Application.Interfaces.Repositories;
using Template.Application.DTOs.Products;

namespace Template.Unit.Tests.ApplicationTests.Features.Products.Queries;
public class GetPagedListProductQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsPagedListResponse()
    {
        // Arrange
        var pageNumber = 1;
        var pageSize = 10;
        var productName = "Test Product";
        var products = new List<ProductDto>
    {
        new ProductDto { Id = Guid.Parse("d40d3315-06d0-4ffa-8a52-976fb283cb3e"), Name = "Product 1", Price = 1000 },
        new ProductDto { Id = Guid.Parse("d40d3315-06d0-4ffa-8a52-976fb283cb3f"), Name = "Product 2", Price = 1500 }
    };

        var productRepositoryMock = new Mock<IProductRepository>();
        productRepositoryMock.Setup(repo => repo.GetPagedListAsync(pageNumber, pageSize, productName, It.IsAny<CancellationToken>()))
                             .ReturnsAsync(new PaginationResponseDto<ProductDto>(products, 100, pageNumber, pageSize));

        var handler = new GetPagedListProductQueryHandler(productRepositoryMock.Object);

        var query = new GetPagedListProductQuery
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            Name = productName
        };

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.Data.ShouldNotBeNull();
        result.Data.ShouldBeEquivalentTo(products);
        result.PageNumber.ShouldBe(pageNumber);
        result.PageSize.ShouldBe(pageSize);
    }

    [Fact]
    public async Task Handle_PropagatesCancellationTokenToRepository()
    {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;
        var productRepositoryMock = new Mock<IProductRepository>();
        productRepositoryMock.Setup(repo => repo.GetPagedListAsync(1, 10, null!, cancellationToken))
                             .ReturnsAsync(new PaginationResponseDto<ProductDto>([], 0, 1, 10));

        var handler = new GetPagedListProductQueryHandler(productRepositoryMock.Object);

        // Act
        await handler.Handle(new GetPagedListProductQuery { PageNumber = 1, PageSize = 10 }, cancellationToken);

        // Assert
        productRepositoryMock.Verify(repo => repo.GetPagedListAsync(1, 10, null!, cancellationToken), Times.Once);
    }
}
