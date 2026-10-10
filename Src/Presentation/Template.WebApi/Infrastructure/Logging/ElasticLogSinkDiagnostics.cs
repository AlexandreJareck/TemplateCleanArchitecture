namespace Template.WebApi.Infrastructure.Logging;

/// <summary>
/// Reports Elasticsearch sink delivery failures on standard error, at most once per interval. It deliberately
/// bypasses Serilog, so a failing sink can never log about itself recursively, and it writes only a failure category
/// (exception type or HTTP status), never endpoints, credentials or event contents.
/// </summary>
public sealed class ElasticLogSinkDiagnostics(TextWriter writer, TimeProvider timeProvider, TimeSpan interval)
{
    private long _lastReportTicks = long.MinValue;

    public static ElasticLogSinkDiagnostics Default { get; } =
        new(Console.Error, TimeProvider.System, TimeSpan.FromMinutes(1));

    public void ReportException(Exception error) => Report(error.GetType().Name);

    public void ReportFailedResponse(int? httpStatusCode) =>
        Report(httpStatusCode is null ? "no response" : $"HTTP {httpStatusCode}");

    private void Report(string reason)
    {
        try
        {
            var now = timeProvider.GetUtcNow().UtcTicks;
            var last = Interlocked.Read(ref _lastReportTicks);
            if (last != long.MinValue && now - last < interval.Ticks)
                return;
            if (Interlocked.CompareExchange(ref _lastReportTicks, now, last) != last)
                return;

            writer.WriteLine(
                $"[elastic-log-sink] Could not deliver log events to Elasticsearch ({reason}). Console logging continues; undelivered events may be dropped.");
        }
        catch
        {
            // Diagnostics must never break logging.
        }
    }
}
