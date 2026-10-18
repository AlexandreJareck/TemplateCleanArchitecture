using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using Template.Application.Settings;

namespace Template.WebApi.Infrastructure.Security;

public enum AuthenticationRateLimitPolicy
{
    /// <summary>Partitioned by client IP and normalized user name (SEC-007).</summary>
    Login,

    /// <summary>Partitioned by client IP; the refresh token itself changes on every use.</summary>
    Refresh
}

/// <summary>
/// Fixed-window limiter of the authentication endpoints (SEC-007: 10 per minute by default), built on the
/// <c>System.Threading.RateLimiting</c> primitives that back ASP.NET Core rate limiting. It is applied by
/// <see cref="AuthenticationRateLimitAttribute"/> only, so no other endpoint is affected. Partition keys are SHA-256
/// digests: the limiter never holds a user name or address in clear text.
/// </summary>
public sealed class AuthenticationRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> limiter;
    private readonly TimeSpan window;

    public AuthenticationRateLimiter(IOptions<SessionSettings> settings)
    {
        var permitLimit = settings.Value.RateLimitPermitLimit;
        window = settings.Value.RateLimitWindow;
        limiter = PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    }

    /// <summary>Returns <c>null</c> when allowed, otherwise the delay to send in <c>Retry-After</c>.</summary>
    public TimeSpan? TryAcquire(AuthenticationRateLimitPolicy policy, HttpContext context, string? userName)
    {
        using var lease = limiter.AttemptAcquire(PartitionKey(policy, context, userName));
        if (lease.IsAcquired)
            return null;

        return lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) && retryAfter > TimeSpan.Zero ? retryAfter : window;
    }

    public static string PartitionKey(AuthenticationRateLimitPolicy policy, HttpContext context, string? userName)
    {
        var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var raw = policy == AuthenticationRateLimitPolicy.Login
            ? $"login|{address}|{(userName ?? string.Empty).Trim().ToUpperInvariant()}"
            : $"refresh|{address}";
        return $"{policy}:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))}";
    }

    public void Dispose() => limiter.Dispose();
}
