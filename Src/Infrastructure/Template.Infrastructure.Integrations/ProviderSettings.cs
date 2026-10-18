namespace Template.Infrastructure.Integrations;

/// <summary>
/// Bounded-resilience settings of one optional provider (INT-001 policy v1: 5 s timeout, at most 2 retries for
/// idempotent GETs on transient failures, circuit breaker). Providers are disabled by default in every committed
/// profile: no environment calls a production provider unless an operator enables it explicitly.
/// </summary>
public abstract class ProviderSettings
{
    public bool Enabled { get; set; }

    /// <summary>Absolute https base URL ending with <c>/</c>; it never carries credentials or a query.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Per-attempt timeout (policy v1: 5 s).</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Retries after the first attempt, only for transient failures (policy v1: 2).</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Base delay before a retry; attempt n waits n × this value.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Consecutive failed calls that open the circuit.</summary>
    public int CircuitBreakerFailureThreshold { get; set; } = 5;

    /// <summary>How long an open circuit short-circuits calls before one trial call is allowed.</summary>
    public TimeSpan CircuitBreakerBreakDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Validation used at startup; the message names keys only.</summary>
    public static bool IsValid(ProviderSettings settings) =>
        !settings.Enabled
        || Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query)
            && settings.BaseUrl.EndsWith('/')
            && settings.Timeout > TimeSpan.Zero && settings.Timeout <= TimeSpan.FromSeconds(30)
            && settings.MaxRetries is >= 0 and <= 2
            && settings.RetryDelay >= TimeSpan.Zero && settings.RetryDelay <= TimeSpan.FromSeconds(5)
            && settings.CircuitBreakerFailureThreshold is >= 1 and <= 100
            && settings.CircuitBreakerBreakDuration > TimeSpan.Zero && settings.CircuitBreakerBreakDuration <= TimeSpan.FromMinutes(30);

    public const string ValidationMessage =
        "{0} requires, when Enabled, an absolute https BaseUrl ending with '/' (http only for loopback) without credentials or query, " +
        "Timeout 0-30 s, MaxRetries 0-2, RetryDelay 0-5 s, CircuitBreakerFailureThreshold 1-100 and CircuitBreakerBreakDuration up to 30 min.";
}

/// <summary>BrasilAPI (CNPJ and CEP lookups), configuration section <c>BrasilApiSettings</c>.</summary>
public sealed class BrasilApiSettings : ProviderSettings
{
    public const string SectionName = "BrasilApiSettings";
}

/// <summary>Banco Central do Brasil PTAX OData service, configuration section <c>BcbPtaxSettings</c>.</summary>
public sealed class BcbPtaxSettings : ProviderSettings
{
    public const string SectionName = "BcbPtaxSettings";
}
