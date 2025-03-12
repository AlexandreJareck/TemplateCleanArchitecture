using Moq;
using Template.Application.Features.Products.Commands;
using Template.Application.Interfaces.Repositories;
using Template.Application.Interfaces;
using Shouldly;
using Template.Domain.Products.Entities;
using Template.Application.Wrappers;

namespace Template.UnitTests.Application.Tests.Features.Products.Commands;

public class UpdateProductCommandHandlerTests
{
    [Fact]
    public async Task Handle_ProductExists_ReturnsSuccessResult()
    {
        // Arrange
        var productId = Guid.Parse("d40d3315-06d0-4ffa-8a52-976fb283cb3e");
        var productName = "Updated Product";
        var productPrice = 1500;
        var productBarCode = "987654321";

        var productRepositoryMock = new Mock<IProductRepository>();
        productRepositoryMock.Setup(repo => repo.GetByIdAsync(It.IsAny<Guid>()))
                             .ReturnsAsync(new ProductEntity("", 100, "") { Id = productId });

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var translatorMock = new Mock<ITranslator>();

        var handler = new UpdateProductCommandHandler(productRepositoryMock.Object, unitOfWorkMock.Object, translatorMock.Object);

        var command = new UpdateProductCommand
        {
            Id = productId,
            Name = productName,
            Price = productPrice,
            BarCode = productBarCode
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();

        productRepositoryMock.Verify(repo => repo.GetByIdAsync(It.IsAny<Guid>()), Times.Once);
        unitOfWorkMock.Verify(unit => unit.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task Handle_ProductNotExists_ReturnsNotFoundResult()
    {
        // Arrange
        var productId = Guid.Parse("d40d3315-06d0-4ffa-8a52-976fb283cb3e");

        var productRepositoryMock = new Mock<IProductRepository>();
        productRepositoryMock.Setup(repo => repo.GetByIdAsync(productId));

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var translatorMock = new Mock<ITranslator>();
        translatorMock.Setup(translator => translator.GetString(It.IsAny<string>()))
                      .Returns("Product not found");

        var handler = new UpdateProductCommandHandler(productRepositoryMock.Object, unitOfWorkMock.Object, translatorMock.Object);

        var command = new UpdateProductCommand { Id = productId };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeFalse();
        result.Errors.ShouldContain(err => err.ErrorCode == ErrorCode.NotFound);

        productRepositoryMock.Verify(repo => repo.Update(It.IsAny<ProductEntity>()), Times.Never);
        unitOfWorkMock.Verify(unit => unit.SaveChangesAsync(), Times.Never);
    }
}
