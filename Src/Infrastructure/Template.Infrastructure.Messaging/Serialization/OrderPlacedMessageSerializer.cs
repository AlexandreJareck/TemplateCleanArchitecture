using System.Text.Json;
using System.Text.Json.Serialization;
using Template.Application.Messaging.Orders;

namespace Template.Infrastructure.Messaging.Serialization;

public enum DecodeStatus
{
    Decoded,

    /// <summary>The body is not valid UTF-8 JSON for the contract, or a required field is missing or empty.</summary>
    InvalidPayload,

    /// <summary>The message declares a content type or contract version this consumer cannot read.</summary>
    IncompatibleContract
}

public readonly record struct DecodeResult(DecodeStatus Status, OrderPlacedEvent? Event)
{
    public static DecodeResult Invalid { get; } = new(DecodeStatus.InvalidPayload, null);

    public static DecodeResult Incompatible { get; } = new(DecodeStatus.IncompatibleContract, null);
}

/// <summary>
/// Wire format of <see cref="OrderPlacedEvent"/> v1: UTF-8 JSON with the PascalCase fields <c>Id</c>,
/// <c>ProductName</c>, <c>Quantity</c> and <c>Price</c>, byte-compatible with the messages published before Phase 9
/// (System.Text.Json defaults over the domain <c>Order</c>). Unknown fields are ignored. Messages without
/// <c>content_type</c> or <c>type</c> are legacy v1 messages and remain readable.
/// </summary>
public static class OrderPlacedMessageSerializer
{
    public const string ContentType = "application/json";
    public const string MessageType = "order.placed.v1";

    private static readonly JsonSerializerOptions Options = new();

    public static byte[] Serialize(OrderPlacedEvent orderPlaced) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new OrderPlacedMessageV1
            {
                Id = orderPlaced.OrderId,
                ProductName = orderPlaced.ProductName,
                Quantity = orderPlaced.Quantity,
                Price = orderPlaced.Price
            },
            Options);

    /// <summary>Decodes a body that the caller already copied out of the client-owned buffer.</summary>
    public static DecodeResult Decode(ReadOnlySpan<byte> body, string? contentType, string? messageType)
    {
        if (!IsSupportedContentType(contentType) || !IsSupportedMessageType(messageType))
            return DecodeResult.Incompatible;

        OrderPlacedMessageV1? message;
        try
        {
            message = JsonSerializer.Deserialize<OrderPlacedMessageV1>(body, Options);
        }
        catch (JsonException)
        {
            return DecodeResult.Invalid;
        }

        if (message is null || message.Id == Guid.Empty || message.ProductName is null)
            return DecodeResult.Invalid;

        return new DecodeResult(
            DecodeStatus.Decoded,
            new OrderPlacedEvent(message.Id, message.ProductName, message.Quantity, message.Price));
    }

    private static bool IsSupportedContentType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return true;

        var mediaType = contentType.Split(';', 2)[0].Trim();
        return string.Equals(mediaType, ContentType, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedMessageType(string? messageType) =>
        string.IsNullOrEmpty(messageType) || string.Equals(messageType, MessageType, StringComparison.Ordinal);

    private sealed class OrderPlacedMessageV1
    {
        [JsonRequired]
        public Guid Id { get; set; }

        [JsonRequired]
        public string? ProductName { get; set; }

        [JsonRequired]
        public int Quantity { get; set; }

        [JsonRequired]
        public decimal Price { get; set; }
    }
}
