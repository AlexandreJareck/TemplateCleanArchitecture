using Template.Application.Interfaces.Messaging;

namespace Template.Infrastructure.Messaging.Deduplication;

/// <summary>
/// Bounded, per-process record of processed message ids (oldest entries are evicted first). It suppresses
/// redeliveries seen by this process; it is not shared between instances and is lost on restart, which is acceptable
/// while the only side effect is a log entry. A durable inbox is required before handlers get non-idempotent effects.
/// </summary>
public sealed class InMemoryProcessedMessageStore(int capacity = InMemoryProcessedMessageStore.DefaultCapacity) : IProcessedMessageStore
{
    public const int DefaultCapacity = 10_000;

    private readonly object gate = new();
    private readonly HashSet<string> processed = new(StringComparer.Ordinal);
    private readonly Queue<string> insertionOrder = new();

    public Task<bool> WasProcessedAsync(string subscription, string messageId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
            return Task.FromResult(processed.Contains(Key(subscription, messageId)));
    }

    public Task MarkProcessedAsync(string subscription, string messageId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = Key(subscription, messageId);
        lock (gate)
        {
            if (!processed.Add(key))
                return Task.CompletedTask;

            insertionOrder.Enqueue(key);
            while (processed.Count > capacity && insertionOrder.TryDequeue(out var oldest))
                processed.Remove(oldest);
        }

        return Task.CompletedTask;
    }

    private static string Key(string subscription, string messageId) => $"{subscription}\n{messageId}";
}
