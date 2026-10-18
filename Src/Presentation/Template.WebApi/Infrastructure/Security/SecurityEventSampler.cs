using System.Collections.Concurrent;
using Template.Application.Interfaces;

namespace Template.WebApi.Infrastructure.Security;

/// <summary>
/// Bounds audit writes for repeatable boundary events (rate limit, permission denial, origin rejection): at most one
/// record per key per <see cref="Window"/>, so an attacker cannot turn rejected requests into audit-table growth.
/// Keys are hashed partitions; they never contain credentials.
/// </summary>
public sealed class SecurityEventSampler(IClock clock)
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private const int MaxKeys = 10_000;
    private readonly ConcurrentDictionary<string, DateTime> lastRecorded = new(StringComparer.Ordinal);

    public bool ShouldRecord(string key)
    {
        var now = clock.UtcNow;
        if (lastRecorded.Count >= MaxKeys)
            lastRecorded.Clear();

        var recorded = false;
        lastRecorded.AddOrUpdate(
            key,
            _ =>
            {
                recorded = true;
                return now;
            },
            (_, previous) =>
            {
                if (now - previous < Window)
                    return previous;
                recorded = true;
                return now;
            });
        return recorded;
    }
}
