using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;
using Template.Functional.Tests.Common;

namespace Template.Functional.Tests.Observability;

/// <summary>Captures every event of one test host (registered as a DI sink that the host logger reads).</summary>
public sealed class CapturingSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> events = new();

    public IReadOnlyList<LogEvent> Events => [.. events];

    public void Emit(LogEvent logEvent) => events.Enqueue(logEvent);

    public IReadOnlyList<LogEvent> WithEventName(string name) =>
        Events.Where(logEvent => EventName(logEvent) == name).ToArray();

    /// <summary>The rendered message, the exception and every property, i.e. everything a sink could persist.</summary>
    public string AllText() => string.Join(Environment.NewLine, Events.Select(Render));

    public static string Render(LogEvent logEvent) =>
        logEvent.RenderMessage() + " " + logEvent.Exception + " "
        + string.Join(" ", logEvent.Properties.Select(property => $"{property.Key}={property.Value}"));

    public static string? EventName(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue("EventId", out var value) && value is StructureValue structure
            ? structure.Properties.FirstOrDefault(property => property.Name == "Name")?.Value is ScalarValue { Value: string name } ? name : null
            : null;

    public static int? EventIdOf(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue("EventId", out var value) && value is StructureValue structure
            ? structure.Properties.FirstOrDefault(property => property.Name == "Id")?.Value is ScalarValue { Value: int id } ? id : null
            : null;

    public static string? Scalar(LogEvent logEvent, string property) =>
        logEvent.Properties.TryGetValue(property, out var value) && value is ScalarValue scalar ? scalar.Value?.ToString() : null;
}

/// <summary>A Test host whose logger also writes to a <see cref="CapturingSink"/>.</summary>
public class LogCapturingFactory : CustomWebApplicationFactory<Program>
{
    public CapturingSink Sink { get; } = new();

    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.AddSingleton<ILogEventSink>(Sink);
}
