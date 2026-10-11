using System.Diagnostics;
using Template.Application.Logging;

namespace Template.WebApi.Infrastructure.Logging;

/// <summary>
/// Outermost middleware: opens a logging scope carrying <c>CorrelationId</c> (the request's
/// <see cref="HttpContext.TraceIdentifier"/>, the same value returned as <c>traceId</c> in Problem Details), so every
/// log written while the request runs inherits it, and writes one <see cref="LogEvents.RequestCompleted"/> event.
/// Only the method and path are logged: never the query string, headers, cookies or bodies.
/// </summary>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = context.TraceIdentifier
        });
        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            var path = context.Request.Path;
            // Probes run every few seconds; their outcome changes are logged by the health status tracker instead.
            var level = path.StartsWithSegments("/health") ? LogLevel.Debug : LogLevel.Information;
            logger.Log(
                level,
                LogEvents.RequestCompleted,
                "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {ElapsedMilliseconds} ms",
                context.Request.Method,
                path.Value,
                context.Response.StatusCode,
                Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 1));
        }
    }
}
