#nullable enable
namespace Template.Application.Messaging;

/// <summary>Transport-neutral facts about a received message.</summary>
/// <param name="Subscription">Logical subscriber name; deduplication is scoped to it.</param>
/// <param name="MessageId">Identifier shared by every delivery of the same message.</param>
/// <param name="IsRedelivery">True when the transport reports a previous delivery attempt.</param>
public sealed record IncomingMessageContext(string Subscription, string MessageId, bool IsRedelivery);
