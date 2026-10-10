using System.Text;
using System.Text.Json;
using Shouldly;
using Template.Application.Messaging.Orders;
using Template.Infrastructure.Messaging.Serialization;

namespace Template.Integration.Tests.Messaging;

public sealed class OrderPlacedMessageSerializerTests
{
    private static readonly Guid OrderId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");
    private static readonly OrderPlacedEvent Order = new(OrderId, "Keyboard", 3, 149.90m);

    private static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);

    [Fact]
    public void Serialize_IsByteCompatible_WithThePrePhase9Payload()
    {
        // Before Phase 9 the publisher serialized the domain Order with System.Text.Json defaults.
        var legacy = JsonSerializer.SerializeToUtf8Bytes(new Template.Domain.Order.Order
        {
            Id = OrderId,
            ProductName = "Keyboard",
            Quantity = 3,
            Price = 149.90m
        });

        OrderPlacedMessageSerializer.Serialize(Order).ShouldBe(legacy);
        Encoding.UTF8.GetString(legacy).ShouldBe("""{"Id":"6f9619ff-8b86-d011-b42d-00c04fc964ff","ProductName":"Keyboard","Quantity":3,"Price":149.90}""");
    }

    [Fact]
    public void RoundTrip_PreservesEveryField()
    {
        var result = OrderPlacedMessageSerializer.Decode(
            OrderPlacedMessageSerializer.Serialize(Order),
            OrderPlacedMessageSerializer.ContentType,
            OrderPlacedMessageSerializer.MessageType);

        result.Status.ShouldBe(DecodeStatus.Decoded);
        result.Event.ShouldBe(Order);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("application/json; charset=utf-8", null)]
    [InlineData("APPLICATION/JSON", "order.placed.v1")]
    public void LegacyAndEquivalentHeaders_AreReadable(string? contentType, string? messageType)
    {
        var result = OrderPlacedMessageSerializer.Decode(OrderPlacedMessageSerializer.Serialize(Order), contentType, messageType);

        result.Status.ShouldBe(DecodeStatus.Decoded);
    }

    [Fact]
    public void UnknownFields_AreIgnored()
    {
        var body = Utf8("""{"Id":"6f9619ff-8b86-d011-b42d-00c04fc964ff","ProductName":"Keyboard","Quantity":3,"Price":149.90,"Coupon":"X"}""");

        OrderPlacedMessageSerializer.Decode(body, null, null).Event.ShouldBe(Order);
    }

    [Theory]
    [InlineData("""{"ProductName":"Keyboard","Quantity":3,"Price":1}""")]
    [InlineData("""{"Id":"6f9619ff-8b86-d011-b42d-00c04fc964ff","Quantity":3,"Price":1}""")]
    [InlineData("""{"Id":"6f9619ff-8b86-d011-b42d-00c04fc964ff","ProductName":"Keyboard","Price":1}""")]
    [InlineData("""{"Id":"6f9619ff-8b86-d011-b42d-00c04fc964ff","ProductName":"Keyboard","Quantity":3}""")]
    [InlineData("""{"Id":"00000000-0000-0000-0000-000000000000","ProductName":"Keyboard","Quantity":3,"Price":1}""")]
    [InlineData("""{"Id":"6f9619ff-8b86-d011-b42d-00c04fc964ff","ProductName":null,"Quantity":3,"Price":1}""")]
    [InlineData("""{"id":"6f9619ff-8b86-d011-b42d-00c04fc964ff","productName":"Keyboard","quantity":3,"price":1}""")]
    [InlineData("""{"Id":"not-a-guid","ProductName":"Keyboard","Quantity":3,"Price":1}""")]
    [InlineData("""{"Id":"6f9619ff-8b86-d011-b42d-00c04fc964ff","ProductName":"Keyboard","Quantity":"three","Price":1}""")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("plain text")]
    [InlineData("")]
    public void InvalidPayloads_AreNotDecoded(string json)
    {
        var result = OrderPlacedMessageSerializer.Decode(Utf8(json), OrderPlacedMessageSerializer.ContentType, null);

        result.Status.ShouldBe(DecodeStatus.InvalidPayload);
        result.Event.ShouldBeNull();
    }

    [Theory]
    [InlineData("text/plain", null)]
    [InlineData("application/xml", "order.placed.v1")]
    [InlineData(null, "order.placed.v2")]
    [InlineData("application/json", "order.cancelled.v1")]
    public void IncompatibleContracts_AreNotDecoded(string? contentType, string? messageType)
    {
        var result = OrderPlacedMessageSerializer.Decode(OrderPlacedMessageSerializer.Serialize(Order), contentType, messageType);

        result.Status.ShouldBe(DecodeStatus.IncompatibleContract);
        result.Event.ShouldBeNull();
    }
}
