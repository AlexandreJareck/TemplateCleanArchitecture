using System;

namespace Template.Domain.Access;

/// <summary>
/// Refresh-session limits (SEC-002, SEC-003/004, SEC-010). Policy v1 defaults: absolute 7 days, idle 12 hours,
/// reuse grace 10 seconds, at most 5 active families per user.
/// </summary>
public sealed record SessionPolicy
{
    public static readonly SessionPolicy Version1 = new(TimeSpan.FromDays(7), TimeSpan.FromHours(12), TimeSpan.FromSeconds(10), 5);

    public SessionPolicy(TimeSpan absoluteLifetime, TimeSpan idleTimeout, TimeSpan reuseGraceWindow, int maxActiveFamilies)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(absoluteLifetime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(idleTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(idleTimeout, absoluteLifetime);
        ArgumentOutOfRangeException.ThrowIfLessThan(reuseGraceWindow, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(reuseGraceWindow, idleTimeout);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxActiveFamilies, 1);

        AbsoluteLifetime = absoluteLifetime;
        IdleTimeout = idleTimeout;
        ReuseGraceWindow = reuseGraceWindow;
        MaxActiveFamilies = maxActiveFamilies;
    }

    public TimeSpan AbsoluteLifetime { get; }
    public TimeSpan IdleTimeout { get; }
    public TimeSpan ReuseGraceWindow { get; }
    public int MaxActiveFamilies { get; }
}
