#nullable enable
namespace Template.Application.Messaging;

/// <summary>
/// The message was not accepted by the message broker (messaging disabled, broker unreachable, message rejected or
/// confirmation timed out). The caller must treat the message as not published.
/// </summary>
public sealed class MessagePublishException(string message, Exception? innerException = null)
    : Exception(message, innerException);
