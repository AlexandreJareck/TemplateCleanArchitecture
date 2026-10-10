using Moq;
using Shouldly;
using Template.Application.Features.Products.Commands;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Caching;
using Template.Application.Interfaces.Repositories;
using Template.Application.Wrappers;
using Template.Domain.Products.Entities;

namespace Template.Unit.Tests.ApplicationTests.Features.Products.Commands;

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
        productRepositoryMock.Setup(repo => repo.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                             .ReturnsAsync(new ProductEntity("Product", 100m, "123456789") { Id = productId });

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var translatorMock = new Mock<ITranslator>();
        var productCacheMock = new Mock<IProductCache>();

        var handler = new UpdateProductCommandHandler(productRepositoryMock.Object, unitOfWorkMock.Object, translatorMock.Object, productCacheMock.Object);

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

        productRepositoryMock.Verify(repo => repo.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        unitOfWorkMock.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        productCacheMock.Verify(cache => cache.RemoveAsync(productId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_InvalidatesCacheOnlyAfterSaveChanges()
    {
        // Arrange
        var productId = Guid.Parse("d40d3315-06d0-4ffa-8a52-976fb283cb3e");
        var calls = new List<string>();
        var productRepositoryMock = new Mock<IProductRepository>();
        productRepositoryMock.Setup(repo => repo.GetByIdAsync(productId, It.IsAny<CancellationToken>()))
                             .ReturnsAsync(new ProductEntity("Product", 100m, "123456789") { Id = productId });
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        unitOfWorkMock.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()))
                      .Callback(() => calls.Add("save"))
                      .ReturnsAsync(true);
        var productCacheMock = new Mock<IProductCache>();
        productCacheMock.Setup(cache => cache.RemoveAsync(productId, It.IsAny<CancellationToken>()))
                        .Callback(() => calls.Add("remove"))
                        .Returns(Task.CompletedTask);

        var handler = new UpdateProductCommandHandler(productRepositoryMock.Object, unitOfWorkMock.Object, new Mock<ITranslator>().Object, productCacheMock.Object);

        // Act
        await handler.Handle(new UpdateProductCommand { Id = productId, Name = "Name", Price = 1, BarCode = "1" }, CancellationToken.None);

        // Assert
        calls.ShouldBe(["save", "remove"]);
    }

    [Fact]
    public async Task Handle_ProductNotExists_ReturnsNotFoundResult()
    {
        // Arrange
        var productId = Guid.Parse("d40d3315-06d0-4ffa-8a52-976fb283cb3e");

        var productRepositoryMock = new Mock<IProductRepository>();
        productRepositoryMock.Setup(repo => repo.GetByIdAsync(productId, It.IsAny<CancellationToken>()));

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var translatorMock = new Mock<ITranslator>();
        var productCacheMock = new Mock<IProductCache>();
        translatorMock.Setup(translator => translator.GetString(It.IsAny<string>()))
                      .Returns("Product not found");

        var handler = new UpdateProductCommandHandler(productRepositoryMock.Object, unitOfWorkMock.Object, translatorMock.Object, productCacheMock.Object);

        var command = new UpdateProductCommand { Id = productId };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeFalse();
        result.Errors.ShouldContain(err => err.ErrorCode == ErrorCode.NotFound);

        productRepositoryMock.Verify(repo => repo.Update(It.IsAny<ProductEntity>()), Times.Never);
        unitOfWorkMock.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        productCacheMock.Verify(cache => cache.RemoveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
