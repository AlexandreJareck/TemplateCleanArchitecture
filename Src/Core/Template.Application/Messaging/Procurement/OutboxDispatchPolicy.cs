#nullable enable

namespace Template.Application.Messaging.Procurement;

public static class OutboxDispatchPolicy
{
    public static DateTime NextAttemptAtUtc(
        DateTime nowUtc,
        int previousFailureCount,
        TimeSpan firstRetryDelay,
        TimeSpan maxRetryDelay)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(previousFailureCount);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(firstRetryDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRetryDelay, firstRetryDelay);

        var exponent = Math.Min(previousFailureCount, 20);
        var ticks = Math.Min(firstRetryDelay.Ticks * (1L << exponent), maxRetryDelay.Ticks);
        return nowUtc.AddTicks(ticks);
    }
}
