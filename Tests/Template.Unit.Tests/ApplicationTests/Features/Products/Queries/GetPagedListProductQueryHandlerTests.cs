using Moq;
using Shouldly;
using Template.Application.DTOs;
using Template.Application.Features.Products.Queries;
using Template.Application.Interfaces.Repositories;
using Template.Domain.Products.DTOs;

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
        productRepositoryMock.Setup(repo => repo.GetPagedListAsync(pageNumber, pageSize, productName))
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
}
