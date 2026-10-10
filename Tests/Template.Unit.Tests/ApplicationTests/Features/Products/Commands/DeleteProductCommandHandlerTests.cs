using Moq;
using Shouldly;
using Template.Application.Features.Products.Commands;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Repositories;
using Template.Application.Wrappers;
using Template.Domain.Products.Entities;

namespace Template.Unit.Tests.ApplicationTests.Features.Products.Commands;

public class DeleteProductCommandHandlerTests
{
    [Fact]
    public async Task Handle_ProductExists_ReturnsSuccessResult()
    {
        // Arrange
        var productId = Guid.Parse("d40d3315-06d0-4ffa-8a52-976fb283cb3e");
        var productRepositoryMock = new Mock<IProductRepository>();
        productRepositoryMock.Setup(repo => repo.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                             .ReturnsAsync(new ProductEntity("Product", 100m, "123456789") { Id = productId });

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var translatorMock = new Mock<ITranslator>();

        var handler = new DeleteProductCommandHandler(productRepositoryMock.Object, unitOfWorkMock.Object, translatorMock.Object);

        var command = new DeleteProductCommand { Id = productId };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();

        productRepositoryMock.Verify(repo => repo.Delete(It.IsAny<ProductEntity>()), Times.Once);
        unitOfWorkMock.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
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
        translatorMock.Setup(translator => translator.GetString(It.IsAny<string>()))
                      .Returns("Product not found");

        var handler = new DeleteProductCommandHandler(productRepositoryMock.Object, unitOfWorkMock.Object, translatorMock.Object);

        var command = new DeleteProductCommand { Id = productId };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeFalse();
        result.Errors.ShouldContain(err => err.ErrorCode == ErrorCode.NotFound);
        result.GetErrorCategory().ShouldBe(ErrorCategory.NotFound);

        productRepositoryMock.Verify(repo => repo.Delete(It.IsAny<ProductEntity>()), Times.Never);
        unitOfWorkMock.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PropagatesCancellationTokenToRepositoryAndUnitOfWork()
    {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;
        var productId = Guid.Parse("d40d3315-06d0-4ffa-8a52-976fb283cb3e");

        var productRepositoryMock = new Mock<IProductRepository>();
        productRepositoryMock.Setup(repo => repo.GetByIdAsync(productId, cancellationToken))
                             .ReturnsAsync(new ProductEntity("Product", 100m, "123456789") { Id = productId });
        var unitOfWorkMock = new Mock<IUnitOfWork>();

        var handler = new DeleteProductCommandHandler(productRepositoryMock.Object, unitOfWorkMock.Object, new Mock<ITranslator>().Object);

        // Act
        var result = await handler.Handle(new DeleteProductCommand { Id = productId }, cancellationToken);

        // Assert
        result.Success.ShouldBeTrue();
        productRepositoryMock.Verify(repo => repo.GetByIdAsync(productId, cancellationToken), Times.Once);
        unitOfWorkMock.Verify(unit => unit.SaveChangesAsync(cancellationToken), Times.Once);
    }
}
