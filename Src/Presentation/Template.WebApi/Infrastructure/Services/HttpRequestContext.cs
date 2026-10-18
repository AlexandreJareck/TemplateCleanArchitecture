using Template.Application.Interfaces;
using Template.Application.Logging;

namespace Template.WebApi.Infrastructure.Services;

/// <summary>
/// HTTP adapter for <see cref="IRequestContext"/>: the correlation is <c>TraceIdentifier</c> (= Problem Details
/// <c>traceId</c> = logging <c>CorrelationId</c>). Outside a request a new safe identifier is used.
/// </summary>
public sealed class HttpRequestContext(IHttpContextAccessor httpContextAccessor) : IRequestContext
{
    private string? fallback;

    public string CorrelationId =>
        httpContextAccessor.HttpContext?.TraceIdentifier is { Length: > 0 } traceId
            ? Application.Logging.CorrelationId.Normalize(traceId, fallback ??= Application.Logging.CorrelationId.New())
            : fallback ??= Application.Logging.CorrelationId.New();
}
