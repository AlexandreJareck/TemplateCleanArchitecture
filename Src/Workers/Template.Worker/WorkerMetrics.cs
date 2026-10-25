#nullable enable
using System.Diagnostics.Metrics;

namespace Template.Worker;

/// <summary>
/// Worker instruments (meter <c>Template.Worker</c>; read with <c>dotnet-counters monitor --counters Template.Worker</c>
/// or any OpenTelemetry collector). Tags carry sweep names and event types only, never record identifiers.
/// </summary>
public static class WorkerMetrics
{
    public const string MeterName = "Template.Worker";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> SweepItems = Meter.CreateCounter<long>(
        "worker.sweep_items", description: "Records changed by scheduled sweeps (RFQs closed, requisitions expired).");

    public static readonly Counter<long> OutboxDispatched = Meter.CreateCounter<long>(
        "worker.outbox_dispatched", description: "Outbox events confirmed by the broker, by event type.");

    public static readonly Counter<long> OutboxDispatchFailures = Meter.CreateCounter<long>(
        "worker.outbox_dispatch_failures", description: "Outbox publish attempts that failed and were rescheduled, by event type.");

    public static readonly Histogram<double> OutboxDispatchLatency = Meter.CreateHistogram<double>(
        "worker.outbox_dispatch_latency", unit: "s", description: "Time from the domain event to the broker confirmation.");

    public static readonly Counter<long> SweepFailures = Meter.CreateCounter<long>(
        "worker.sweep_failures", description: "Scheduled sweeps that failed and will retry at the next interval.");
}
