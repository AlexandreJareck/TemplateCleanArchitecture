#nullable enable
namespace Template.Application.Interfaces;

/// <summary>
/// Correlation of the current unit of work, free of HTTP types. For HTTP requests it equals the Problem Details
/// <c>traceId</c> and the logging <c>CorrelationId</c> (NFR-AUD-3).
/// </summary>
public interface IRequestContext
{
    string CorrelationId { get; }
}

/// <summary>
/// Lets a background host continue the correlation of the work that caused it (Phase 33): a consumer adopts the
/// validated correlation ID of the integration event before its effect runs, so logs and audit records of the effect
/// share the originating request's ID. Hosts without inbound correlation (WebApi) do not register it.
/// </summary>
public interface IInboundCorrelation
{
    /// <summary>Adopts <paramref name="correlationId"/> for the current scope; call before any work reads it.</summary>
    void Adopt(string correlationId);
}
