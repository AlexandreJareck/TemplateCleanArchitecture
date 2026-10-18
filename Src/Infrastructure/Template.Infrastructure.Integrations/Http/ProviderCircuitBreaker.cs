namespace Template.Infrastructure.Integrations.Http;

/// <summary>
/// Consecutive-failure circuit breaker of one provider (INT-001). Closed: calls pass and failures are counted. After
/// <c>threshold</c> consecutive failed calls it opens for the break duration and rejects calls without network access.
/// After that, exactly one trial call is admitted (half-open): success closes the circuit, failure opens it again.
/// Thread-safe; time comes from <see cref="TimeProvider"/> so tests are deterministic.
/// </summary>
internal sealed class ProviderCircuitBreaker(int threshold, TimeSpan breakDuration, TimeProvider timeProvider)
{
    private readonly Lock gate = new();
    private int consecutiveFailures;
    private DateTimeOffset? openedUntil;
    private bool trialInFlight;

    public bool IsOpen
    {
        get
        {
            lock (gate)
                return openedUntil is { } until && timeProvider.GetUtcNow() < until;
        }
    }

    /// <summary><c>false</c> while the circuit is open or a half-open trial is already running.</summary>
    public bool TryAcquire()
    {
        lock (gate)
        {
            if (openedUntil is not { } until)
                return true;
            if (timeProvider.GetUtcNow() < until || trialInFlight)
                return false;

            trialInFlight = true;
            return true;
        }
    }

    public void RecordSuccess()
    {
        lock (gate)
        {
            consecutiveFailures = 0;
            openedUntil = null;
            trialInFlight = false;
        }
    }

    /// <summary>Returns <c>true</c> when this failure opened (or re-opened) the circuit.</summary>
    public bool RecordFailure()
    {
        lock (gate)
        {
            consecutiveFailures++;
            var reopen = trialInFlight || consecutiveFailures >= threshold;
            trialInFlight = false;
            if (!reopen)
                return false;

            openedUntil = timeProvider.GetUtcNow() + breakDuration;
            return true;
        }
    }

    /// <summary>A call that ended for a reason that says nothing about provider health (caller cancellation).</summary>
    public void RecordNeutral()
    {
        lock (gate)
            trialInFlight = false;
    }
}
