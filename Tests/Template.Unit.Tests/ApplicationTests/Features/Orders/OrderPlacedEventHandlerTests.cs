using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Template.Application.Features.Orders.EventHandlers;
using Template.Application.Interfaces.Messaging;
using Template.Application.Messaging;
using Template.Application.Messaging.Orders;

namespace Template.Unit.Tests.ApplicationTests.Features.Orders;

public class OrderPlacedEventHandlerTests
{
    private static readonly OrderPlacedEvent Order = new(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), "Keyboard", 1, 10m);
    private static readonly IncomingMessageContext Context = new("fanout-a", "message-1", IsRedelivery: false);

    private readonly Mock<IProcessedMessageStore> store = new();

    private OrderPlacedEventHandler Handler => new(store.Object, NullLogger<OrderPlacedEventHandler>.Instance);

    [Fact]
    public async Task NewMessage_IsProcessed_AndMarkedAfterwards_WithToken()
    {
        using var cancellation = new CancellationTokenSource();
        var sequence = new List<string>();
        store.Setup(s => s.WasProcessedAsync("fanout-a", "message-1", cancellation.Token))
            .Callback(() => sequence.Add("check")).ReturnsAsync(false);
        store.Setup(s => s.MarkProcessedAsync("fanout-a", "message-1", cancellation.Token))
            .Callback(() => sequence.Add("mark")).Returns(Task.CompletedTask);

        await Handler.HandleAsync(Order, Context, cancellation.Token);

        sequence.ShouldBe(["check", "mark"]);
    }

    [Fact]
    public async Task Duplicate_IsAcceptedWithoutBeingMarkedAgain()
    {
        store.Setup(s => s.WasProcessedAsync("fanout-a", "message-1", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Handler.HandleAsync(Order, Context with { IsRedelivery = true }, CancellationToken.None);

        store.Verify(s => s.MarkProcessedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SameMessage_OnAnotherSubscription_IsNotADuplicate()
    {
        store.Setup(s => s.WasProcessedAsync("fanout-a", "message-1", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        store.Setup(s => s.WasProcessedAsync("fanout-b", "message-1", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await Handler.HandleAsync(Order, Context with { Subscription = "fanout-b" }, CancellationToken.None);

        store.Verify(s => s.MarkProcessedAsync("fanout-b", "message-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StoreFailure_PropagatesSoTheMessageIsNotTreatedAsProcessed()
    {
        store.Setup(s => s.WasProcessedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store down"));

        await Should.ThrowAsync<InvalidOperationException>(() => Handler.HandleAsync(Order, Context, CancellationToken.None));
        store.Verify(s => s.MarkProcessedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        store.Setup(s => s.WasProcessedAsync(It.IsAny<string>(), It.IsAny<string>(), cancellation.Token))
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));

        await Should.ThrowAsync<OperationCanceledException>(() => Handler.HandleAsync(Order, Context, cancellation.Token));
    }
}
