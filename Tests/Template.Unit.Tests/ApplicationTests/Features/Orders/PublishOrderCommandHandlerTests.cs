using Moq;
using Shouldly;
using Template.Application.Features.Orders.Commands;
using Template.Application.Interfaces.Messaging;
using Template.Application.Messaging;
using Template.Application.Messaging.Orders;

namespace Template.Unit.Tests.ApplicationTests.Features.Orders;

public class PublishOrderCommandHandlerTests
{
    private readonly Mock<IOrderEventPublisher> publisher = new(MockBehavior.Strict);

    private PublishOrderCommandHandler Handler => new(publisher.Object);

    [Fact]
    public async Task Broadcast_PublishesToAllSubscribers_AndMapsTheContract()
    {
        using var cancellation = new CancellationTokenSource();
        OrderPlacedEvent? published = null;
        publisher.Setup(port => port.PublishBroadcastAsync(It.IsAny<OrderPlacedEvent>(), cancellation.Token))
            .Callback<OrderPlacedEvent, CancellationToken>((orderPlaced, _) => published = orderPlaced)
            .Returns(Task.CompletedTask);

        var orderId = await Handler.Handle(new PublishOrderCommand(OrderDispatch.Broadcast, "Keyboard", 3, 149.90m), cancellation.Token);

        published.ShouldNotBeNull();
        published.OrderId.ShouldBe(orderId);
        orderId.ShouldNotBe(Guid.Empty);
        published.ProductName.ShouldBe("Keyboard");
        published.Quantity.ShouldBe(3);
        published.Price.ShouldBe(149.90m);
        publisher.Verify(port => port.PublishBroadcastAsync(It.IsAny<OrderPlacedEvent>(), cancellation.Token), Times.Once);
        publisher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Direct_PublishesWithTheRoutingKey_AndToken()
    {
        using var cancellation = new CancellationTokenSource();
        publisher.Setup(port => port.PublishDirectAsync(It.IsAny<OrderPlacedEvent>(), "order.urgent", cancellation.Token))
            .Returns(Task.CompletedTask);

        var orderId = await Handler.Handle(new PublishOrderCommand(OrderDispatch.Direct, "Mouse", 1, 10m, "order.urgent"), cancellation.Token);

        publisher.Verify(port => port.PublishDirectAsync(
            It.Is<OrderPlacedEvent>(orderPlaced => orderPlaced.OrderId == orderId),
            "order.urgent",
            cancellation.Token), Times.Once);
        publisher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Topic_PublishesWithTheRoutingKey_AndToken()
    {
        using var cancellation = new CancellationTokenSource();
        publisher.Setup(port => port.PublishTopicAsync(It.IsAny<OrderPlacedEvent>(), "order.brazil.sp", cancellation.Token))
            .Returns(Task.CompletedTask);

        var orderId = await Handler.Handle(new PublishOrderCommand(OrderDispatch.Topic, "Monitor", 2, 999.99m, "order.brazil.sp"), cancellation.Token);

        publisher.Verify(port => port.PublishTopicAsync(
            It.Is<OrderPlacedEvent>(orderPlaced => orderPlaced.OrderId == orderId),
            "order.brazil.sp",
            cancellation.Token), Times.Once);
        publisher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EveryPublish_GetsANewOrderId()
    {
        publisher.Setup(port => port.PublishBroadcastAsync(It.IsAny<OrderPlacedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var command = new PublishOrderCommand(OrderDispatch.Broadcast, "Same", 1, 1m);

        var first = await Handler.Handle(command, CancellationToken.None);
        var second = await Handler.Handle(command, CancellationToken.None);

        first.ShouldNotBe(second);
    }

    [Fact]
    public async Task PortFailure_PropagatesAsMessagePublishException()
    {
        publisher.Setup(port => port.PublishTopicAsync(It.IsAny<OrderPlacedEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MessagePublishException("broker unavailable"));

        await Should.ThrowAsync<MessagePublishException>(() =>
            Handler.Handle(new PublishOrderCommand(OrderDispatch.Topic, "Item", 1, 1m, "order.brazil.rj"), CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_FromThePort_IsNotSwallowed()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        publisher.Setup(port => port.PublishBroadcastAsync(It.IsAny<OrderPlacedEvent>(), cancellation.Token))
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            Handler.Handle(new PublishOrderCommand(OrderDispatch.Broadcast, "Item", 1, 1m), cancellation.Token));
    }

    [Theory]
    [InlineData(OrderDispatch.Direct)]
    [InlineData(OrderDispatch.Topic)]
    public async Task RoutedDispatch_WithoutRoutingKey_IsRejectedBeforePublishing(OrderDispatch dispatch)
    {
        await Should.ThrowAsync<ArgumentException>(() =>
            Handler.Handle(new PublishOrderCommand(dispatch, "Item", 1, 1m, RoutingKey: null), CancellationToken.None));

        publisher.VerifyNoOtherCalls();
    }
}
