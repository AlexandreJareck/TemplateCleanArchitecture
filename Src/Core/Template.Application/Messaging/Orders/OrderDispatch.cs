namespace Template.Application.Messaging.Orders;

/// <summary>How an <see cref="OrderPlacedEvent"/> reaches its subscribers.</summary>
public enum OrderDispatch
{
    /// <summary>Every subscriber receives the event; no routing key is used.</summary>
    Broadcast,

    /// <summary>Subscribers whose binding key equals the routing key receive the event.</summary>
    Direct,

    /// <summary>Subscribers whose binding pattern matches the routing key receive the event.</summary>
    Topic
}
