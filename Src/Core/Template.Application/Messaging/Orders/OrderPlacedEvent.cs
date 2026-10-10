#nullable enable
namespace Template.Application.Messaging.Orders;

/// <summary>
/// Integration event published when an order is submitted through the messaging endpoints.
/// Contract <see cref="Name"/> version <see cref="Version"/>: every field is required and consumers ignore unknown
/// fields. The transport encoding is owned by Infrastructure.
/// </summary>
public sealed record OrderPlacedEvent(Guid OrderId, string ProductName, int Quantity, decimal Price)
{
    public const string Name = "order.placed";

    public const int Version = 1;
}
