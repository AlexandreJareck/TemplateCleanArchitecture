using System.Text.Json;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Template.Application.Workflow;

namespace Template.WorkflowFunctions.Tests;

/// <summary>
/// A scripted, single-threaded <see cref="TaskOrchestrationContext"/> that mimics the Durable replay contract closely
/// enough to test the approval orchestration deterministically: activities complete from a handler, timers and the
/// signal event are completed by the test, external events are buffered until waited for (as Durable does), and every
/// scheduled command is recorded. <see cref="History"/> can seed a second context to replay the same decisions with
/// <see cref="IsReplaying"/> true, so replay-safe logging and command determinism can be asserted.
/// </summary>
internal sealed class ScriptedOrchestrationContext(
    Func<string, object?, ApprovalProgress> activity,
    string? version = "1.0",
    DateTime? now = null,
    IReadOnlyList<object?>? replayHistory = null) : TaskOrchestrationContext
{
    private readonly Queue<ApprovalSignal> bufferedEvents = new();
    private readonly CapturingLoggerFactory loggers = new();
    private readonly Queue<object?> replay = new(replayHistory ?? []);
    private TaskCompletionSource<ApprovalSignal>? pendingEvent;
    private TaskCompletionSource? pendingTimer;
    private CancellationToken pendingTimerToken;

    public List<string> Commands { get; } = [];

    /// <summary>Each completed command's result, in order (activity outputs, fired timers, received events).</summary>
    public List<object?> History { get; } = [];

    public List<(string Message, bool DuringReplay)> Logs => loggers.Entries;

    public object? ContinuedWith { get; private set; }

    public bool PreservedEvents { get; private set; }

    public Func<string, int, Exception?>? Failure { get; set; }

    public bool IsWaiting => pendingEvent is not null && pendingTimer is not null;

    public CancellationToken LastTimerToken { get; private set; }

    public override TaskName Name => new(ApprovalWorkflowNames.Orchestrator);

    public override string InstanceId => "approval-0000000000000000000000000000abcd";

    public override ParentOrchestrationInstance? Parent => null;

    public override DateTime CurrentUtcDateTime { get; } = now ?? new DateTime(2027, 3, 4, 13, 0, 0, DateTimeKind.Utc);

    public override bool IsReplaying => replay.Count > 0;

    public override string Version { get; } = version ?? string.Empty;

    public override TaskOrchestrationEntityFeature Entities => throw new NotSupportedException();

    protected override ILoggerFactory LoggerFactory => loggers;

    private int calls;

    public override Task<TResult> CallActivityAsync<TResult>(TaskName name, object? input = null, TaskOptions? options = null)
    {
        Commands.Add($"activity:{name.Name}:{JsonSerializer.Serialize(input)}");
        if (replay.Count > 0)
            return Task.FromResult((TResult)replay.Dequeue()!);
        calls++;
        if (Failure?.Invoke(name.Name, calls) is { } failure)
            return Task.FromException<TResult>(failure);
        var result = activity(name.Name, input);
        History.Add(result);
        return Task.FromResult((TResult)(object)result);
    }

    public override Task CreateTimer(DateTime fireAt, CancellationToken cancellationToken)
    {
        Commands.Add($"timer:{fireAt:O}");
        LastTimerToken = cancellationToken;
        if (replay.Count > 0 && replay.Peek() is "timer")
        {
            replay.Dequeue();
            return Task.CompletedTask;
        }

        // Poison back-off timers fire immediately; the race timer waits for the test.
        if (!cancellationToken.CanBeCanceled)
        {
            History.Add("timer");
            return Task.CompletedTask;
        }

        pendingTimer = new TaskCompletionSource();
        pendingTimerToken = cancellationToken;
        return pendingTimer.Task;
    }

    public override Task<T> WaitForExternalEvent<T>(string eventName, CancellationToken cancellationToken = default)
    {
        Commands.Add($"wait:{eventName}");
        if (replay.Count > 0 && replay.Peek() is ApprovalSignal replayed)
        {
            replay.Dequeue();
            return Task.FromResult((T)(object)replayed);
        }

        if (bufferedEvents.TryDequeue(out var buffered))
        {
            History.Add(buffered);
            return Task.FromResult((T)(object)buffered);
        }

        pendingEvent = new TaskCompletionSource<ApprovalSignal>();
        return pendingEvent.Task.ContinueWith(task => (T)(object)task.Result, TaskContinuationOptions.ExecuteSynchronously);
    }

    /// <summary>Delivers a signal: to the pending wait, or buffered for the next wait (Durable semantics).</summary>
    public void Raise(ApprovalSignal signal)
    {
        if (pendingEvent is { } waiting)
        {
            pendingEvent = null;
            pendingTimer = null;
            History.Add(signal);
            waiting.SetResult(signal);
            return;
        }

        bufferedEvents.Enqueue(signal);
    }

    public void FireTimer()
    {
        var timer = pendingTimer ?? throw new InvalidOperationException("No timer is pending.");
        pendingTimer = null;
        pendingEvent = null;
        History.Add("timer");
        timer.SetResult();
    }

    public bool PendingTimerCancelled => pendingTimerToken.IsCancellationRequested;

    public override void ContinueAsNew(object? newInput = null, bool preserveUnprocessedEvents = true)
    {
        Commands.Add("continue-as-new");
        ContinuedWith = newInput;
        PreservedEvents = preserveUnprocessedEvents;
    }

    public override T GetInput<T>() => throw new NotSupportedException();

    public override Task<TResult> CallSubOrchestratorAsync<TResult>(TaskName orchestratorName, object? input = null, TaskOptions? options = null) =>
        throw new NotSupportedException("The approval orchestration has no sub-orchestrations.");

    public override void SendEvent(string instanceId, string eventName, object payload) => throw new NotSupportedException();

    public override void SetCustomStatus(object? customStatus) => Commands.Add("custom-status");

    public override Guid NewGuid() => throw new NotSupportedException("The approval orchestration creates no IDs.");

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        public List<(string Message, bool DuringReplay)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(List<(string Message, bool DuringReplay)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Add((formatter(state, exception), false));
    }
}
