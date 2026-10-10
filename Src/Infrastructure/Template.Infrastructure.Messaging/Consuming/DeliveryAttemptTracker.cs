namespace Template.Infrastructure.Messaging.Consuming;

/// <summary>
/// Counts failed processing attempts per subscription and message id. Classic queues do not count redeliveries, so
/// the count lives in this process: it is bounded in size (oldest entries are evicted first) and resets on restart.
/// </summary>
public sealed class DeliveryAttemptTracker(int capacity = DeliveryAttemptTracker.DefaultCapacity)
{
    public const int DefaultCapacity = 10_000;

    private readonly object gate = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, int Failures)>> entries = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, int Failures)> insertionOrder = new();

    public int RecordFailure(string subscription, string messageId)
    {
        var key = Key(subscription, messageId);
        lock (gate)
        {
            if (entries.TryGetValue(key, out var node))
            {
                node.Value = (key, node.Value.Failures + 1);
                return node.Value.Failures;
            }

            while (entries.Count >= capacity && insertionOrder.First is { } oldest)
            {
                entries.Remove(oldest.Value.Key);
                insertionOrder.RemoveFirst();
            }

            entries[key] = insertionOrder.AddLast((key, 1));
            return 1;
        }
    }

    public void Forget(string subscription, string messageId)
    {
        lock (gate)
        {
            if (entries.Remove(Key(subscription, messageId), out var node))
                insertionOrder.Remove(node);
        }
    }

    private static string Key(string subscription, string messageId) => $"{subscription}\n{messageId}";
}
