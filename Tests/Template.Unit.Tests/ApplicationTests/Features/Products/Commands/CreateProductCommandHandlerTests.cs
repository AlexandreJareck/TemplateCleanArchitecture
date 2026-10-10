using Moq;
using Shouldly;
using Template.Application.Features.Products.Commands;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Repositories;
using Template.Domain.Products.Entities;

namespace Template.Unit.Tests.ApplicationTests.Features.Products.Commands;

public class CreateProductCommandHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 10, 10, 12, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccessResultWithProductId()
    {
        // Arrange
        var productRepositoryMock = new Mock<IProductRepository>();
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var clockMock = new Mock<IClock>();
        clockMock.SetupGet(clock => clock.UtcNow).Returns(FixedUtcNow);

        var handler = new CreateProductCommandHandler(productRepositoryMock.Object, unitOfWorkMock.Object, clockMock.Object);

        var command = new CreateProductCommand
        {
            Name = "Test Product",
            Price = 100,
            BarCode = "123456789"
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Name.ShouldBe(command.Name);
        result.Data.Price.ShouldBe(command.Price);
        result.Data.BarCode.ShouldBe(command.BarCode);
    }

    [Fact]
    public async Task Handle_ValidCommand_UsesInjectedClockForCreatedDateTime()
    {
        // Arrange
        var clockMock = new Mock<IClock>();
        clockMock.SetupGet(clock => clock.UtcNow).Returns(FixedUtcNow);

        var handler = new CreateProductCommandHandler(
            new Mock<IProductRepository>().Object,
            new Mock<IUnitOfWork>().Object,
            clockMock.Object);

        var command = new CreateProductCommand { Name = "Clock Product", Price = 10m, BarCode = "CLOCK-001" };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Data.CreatedDateTime.ShouldBe(FixedUtcNow);
        clockMock.VerifyGet(clock => clock.UtcNow, Times.Once);
    }

    [Fact]
    public async Task Handle_PropagatesCancellationTokenToRepositoryAndUnitOfWork()
    {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;

        var productRepositoryMock = new Mock<IProductRepository>();
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var clockMock = new Mock<IClock>();
        clockMock.SetupGet(clock => clock.UtcNow).Returns(FixedUtcNow);

        var handler = new CreateProductCommandHandler(productRepositoryMock.Object, unitOfWorkMock.Object, clockMock.Object);
        var command = new CreateProductCommand { Name = "Token Product", Price = 1m, BarCode = "TOKEN-001" };

        // Act
        await handler.Handle(command, cancellationToken);

        // Assert
        productRepositoryMock.Verify(repo => repo.AddAsync(It.IsAny<ProductEntity>(), cancellationToken), Times.Once);
        unitOfWorkMock.Verify(unit => unit.SaveChangesAsync(cancellationToken), Times.Once);
    }
}
