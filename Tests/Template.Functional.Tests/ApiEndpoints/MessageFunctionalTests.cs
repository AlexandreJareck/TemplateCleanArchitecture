using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Template.Application.Interfaces.Messaging;
using Template.Application.Messaging;
using Template.Application.Messaging.Orders;
using Template.Functional.Tests.Common;
using Template.Infrastructure.Messaging.Publishing;

namespace Template.Functional.Tests.ApiEndpoints;

public sealed class MessageFunctionalTests(MessageFunctionalTests.RecordingPublisherFactory factory)
    : IClassFixture<MessageFunctionalTests.RecordingPublisherFactory>
{
    private const string Fanout = "/api/v1/message/create-order-fanout";
    private const string Direct = "/api/v1/message/create-order-direct";
    private const string Topic = "/api/v1/message/create-order-topic";

    private static readonly object OrderBody = new { id = Guid.Empty, productName = "Keyboard", quantity = 3, price = 149.90m };

    private readonly HttpClient client = factory.CreateClient();
    private RecordingOrderEventPublisher Publisher => factory.Publisher;

    [Fact]
    public async Task Fanout_PublishesToAllSubscribers_AndKeepsTheResponseText()
    {
        Publisher.Reset();

        using var response = await client.PostAsJsonAsync(Fanout, OrderBody);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var published = Publisher.Published.Single();
        published.Dispatch.ShouldBe(OrderDispatch.Broadcast);
        published.RoutingKey.ShouldBeNull();
        published.Event.ProductName.ShouldBe("Keyboard");
        published.Event.Quantity.ShouldBe(3);
        published.Event.Price.ShouldBe(149.90m);
        published.Event.OrderId.ShouldNotBe(Guid.Empty);
        published.TokenCanBeCanceled.ShouldBeTrue();
        (await response.Content.ReadAsStringAsync()).ShouldBe($"FANOUT - Order {published.Event.OrderId} sent to queue.");
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
    }

    [Fact]
    public async Task Direct_PublishesWithTheQueryRoutingKey_AndKeepsTheResponseText()
    {
        Publisher.Reset();

        using var response = await client.PostAsJsonAsync($"{Direct}?routingKey=order.urgent", OrderBody);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var published = Publisher.Published.Single();
        published.Dispatch.ShouldBe(OrderDispatch.Direct);
        published.RoutingKey.ShouldBe("order.urgent");
        (await response.Content.ReadAsStringAsync()).ShouldBe($"DIRECT - Order {published.Event.OrderId} sent with routing key 'order.urgent'.");
    }

    [Fact]
    public async Task Topic_PublishesWithTheQueryRoutingKey_AndKeepsTheResponseText()
    {
        Publisher.Reset();

        using var response = await client.PostAsJsonAsync($"{Topic}?routingKey=order.brazil.sp", OrderBody);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var published = Publisher.Published.Single();
        published.Dispatch.ShouldBe(OrderDispatch.Topic);
        published.RoutingKey.ShouldBe("order.brazil.sp");
        (await response.Content.ReadAsStringAsync()).ShouldBe($" TOPIC - Order {published.Event.OrderId} sent with routing key 'order.brazil.sp'.");
    }

    [Fact]
    public async Task ClientSuppliedId_IsReplacedByANewOrderId()
    {
        Publisher.Reset();
        var clientId = Guid.NewGuid();

        using var response = await client.PostAsJsonAsync(Fanout, new { id = clientId, productName = "Mouse", quantity = 1, price = 10m });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Publisher.Published.Single().Event.OrderId.ShouldNotBe(clientId);
    }

    [Theory]
    [InlineData(Direct)]
    [InlineData(Topic)]
    public async Task MissingRoutingKey_IsAValidationError_AndNothingIsPublished(string route)
    {
        Publisher.Reset();

        using var response = await client.PostAsJsonAsync(route, OrderBody);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        Publisher.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task PublisherFailure_Returns500_WithoutInternalDetails()
    {
        Publisher.Reset();
        Publisher.FailWith = new MessagePublishException("broker-sensitive-marker");

        using var response = await client.PostAsJsonAsync($"{Topic}?routingKey=order.brazil.sp", OrderBody);
        Publisher.FailWith = null;

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("broker-sensitive-marker");
    }

    [Fact]
    public async Task ClientCancellation_ReachesThePublisher()
    {
        Publisher.Reset();
        Publisher.BlockUntilCancelled = true;
        using var cancellation = new CancellationTokenSource();

        var request = client.PostAsJsonAsync(Fanout, OrderBody, cancellation.Token);
        await Publisher.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => request);
        await Publisher.ObservedCancellation.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Publisher.BlockUntilCancelled = false;
    }

    [Fact]
    public void TestHost_RunsNoMessagingHostedService()
    {
        factory.Services.GetServices<IHostedService>()
            .ShouldNotContain(service => service.GetType().Namespace!.StartsWith("Template.Infrastructure.Messaging", StringComparison.Ordinal));
    }

    public sealed class RecordingPublisherFactory : CustomWebApplicationFactory<Program>
    {
        public RecordingOrderEventPublisher Publisher { get; } = new();

        protected override void ConfigureTestServices(IServiceCollection services) =>
            services.AddSingleton<IOrderEventPublisher>(Publisher);
    }
}

/// <summary>Default Test composition: no broker, so publishing fails explicitly while unrelated endpoints keep working.</summary>
public sealed class MessageWithoutBrokerFunctionalTests(CustomWebApplicationFactory<Program> factory)
    : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task WithoutBroker_PublishingFails_AndOtherEndpointsStillWork()
    {
        factory.Services.GetRequiredService<IOrderEventPublisher>().ShouldBeOfType<DisabledOrderEventPublisher>();

        using var publish = await client.PostAsJsonAsync("/api/v1/message/create-order-fanout", new { productName = "Keyboard", quantity = 1, price = 1m });
        using var products = await client.GetAsync(ApiRoutes.V2.Product.GetPagedListProduct);
        using var swagger = await client.GetAsync("/swagger/v2/swagger.json");

        publish.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        products.StatusCode.ShouldBe(HttpStatusCode.OK);
        swagger.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}

public sealed class RecordingOrderEventPublisher : IOrderEventPublisher
{
    private readonly ConcurrentQueue<(OrderDispatch Dispatch, OrderPlacedEvent Event, string? RoutingKey, bool TokenCanBeCanceled)> published = new();

    public IReadOnlyList<(OrderDispatch Dispatch, OrderPlacedEvent Event, string? RoutingKey, bool TokenCanBeCanceled)> Published => published.ToArray();

    public Exception? FailWith { get; set; }

    public bool BlockUntilCancelled { get; set; }

    public TaskCompletionSource Started { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource ObservedCancellation { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Reset()
    {
        published.Clear();
        Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ObservedCancellation = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public Task PublishBroadcastAsync(OrderPlacedEvent orderPlaced, CancellationToken cancellationToken) =>
        RecordAsync(OrderDispatch.Broadcast, orderPlaced, null, cancellationToken);

    public Task PublishDirectAsync(OrderPlacedEvent orderPlaced, string routingKey, CancellationToken cancellationToken) =>
        RecordAsync(OrderDispatch.Direct, orderPlaced, routingKey, cancellationToken);

    public Task PublishTopicAsync(OrderPlacedEvent orderPlaced, string routingKey, CancellationToken cancellationToken) =>
        RecordAsync(OrderDispatch.Topic, orderPlaced, routingKey, cancellationToken);

    private async Task RecordAsync(OrderDispatch dispatch, OrderPlacedEvent orderPlaced, string? routingKey, CancellationToken cancellationToken)
    {
        Started.TrySetResult();
        if (BlockUntilCancelled)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation.TrySetResult();
                throw;
            }
        }

        if (FailWith is not null)
            throw FailWith;

        published.Enqueue((dispatch, orderPlaced, routingKey, cancellationToken.CanBeCanceled));
    }
}
