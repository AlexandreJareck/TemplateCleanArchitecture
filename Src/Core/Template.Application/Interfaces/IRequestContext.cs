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
