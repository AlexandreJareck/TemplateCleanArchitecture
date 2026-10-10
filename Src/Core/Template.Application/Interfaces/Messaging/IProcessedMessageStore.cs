#nullable enable
namespace Template.Application.Interfaces.Messaging;

/// <summary>Remembers which messages a subscription already processed, so redeliveries can be skipped.</summary>
public interface IProcessedMessageStore
{
    Task<bool> WasProcessedAsync(string subscription, string messageId, CancellationToken cancellationToken);

    Task MarkProcessedAsync(string subscription, string messageId, CancellationToken cancellationToken);
}
