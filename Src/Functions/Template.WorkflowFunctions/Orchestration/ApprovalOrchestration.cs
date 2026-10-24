using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Template.Application.Workflow;

namespace Template.WorkflowFunctions.Orchestration;

/// <summary>
/// The approval orchestration (ADR-0003, APR-016). It owns durable time and event waiting only: every rule, threshold,
/// assignment and effect is an activity backed by an Application use case that reads the database. The code is
/// deterministic and replay-safe: it uses only the orchestration context (no clock, random, I/O, DI or thread blocking),
/// logs through the replay-safe logger, and its timers come from activity outputs recorded in the history.
/// <para>
/// Loop: start → (assign → wait | finalize)* → completed. A wait races the <see cref="ApprovalWorkflowNames.SignalEvent"/>
/// external event against the next reminder or deadline timer; either way an activity re-reads the state, so duplicate,
/// late or out-of-order signals are harmless (APR-014). After <see cref="MaxWaitsPerExecution"/> waits the history is
/// truncated with <c>ContinueAsNew</c> (unprocessed signals preserved). Version <c>1.0</c> (and legacy <c>null</c>) run this
/// path; a later version adds its own branch and keeps this one unchanged while instances use it.
/// </para>
/// </summary>
public static class ApprovalOrchestration
{
    public const string Version1 = "1.0";
    public const int MaxWaitsPerExecution = 50;
    public const int MaxPoisonRounds = 3;
    public const string ContinuedAsNew = "ContinuedAsNew";

    /// <summary>Durable delay before an exhausted activity is tried again (poison handling).</summary>
    public static readonly TimeSpan PoisonBackoff = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Bounded retries for transient activity failures only (database or host unavailable); a permanent failure goes to
    /// the poison handling at once.
    /// </summary>
    public static TaskOptions ActivityOptions { get; } = TaskOptions.FromRetryPolicy(new RetryPolicy(
        maxNumberOfAttempts: 5, firstRetryInterval: TimeSpan.FromSeconds(5), backoffCoefficient: 2.0, maxRetryInterval: TimeSpan.FromMinutes(2))
    {
        HandleFailure = IsRetryable
    });

    /// <summary>Retry classification of an activity failure: only <see cref="TransientActivityException"/> is retried.</summary>
    public static bool IsRetryable(TaskFailureDetails failure) => failure.IsCausedBy<TransientActivityException>();

    public static async Task<string> RunAsync(TaskOrchestrationContext context, ApprovalWorkflowInput? input)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (input is null || input.AwardId == Guid.Empty || input.InputVersion != ApprovalWorkflowInput.Version1)
            throw new ArgumentException("The approval orchestration input is missing or has an unsupported version.", nameof(input));
        if (context.Version is { Length: > 0 } version && version != Version1)
            throw new NotSupportedException($"Orchestration version '{version}' has no code path in this build.");

        var logger = context.CreateReplaySafeLogger(nameof(ApprovalOrchestration));
        var request = new ApprovalActivityRequest(input.AwardId, input.CorrelationId);
        var progress = await CallAsync(context, ApprovalWorkflowNames.StartActivity,
            new ApprovalStartRequest(input, context.Version is { Length: > 0 } stamped ? stamped : Version1));
        var waits = 0;
        while (true)
        {
            switch (progress.Next)
            {
                case ApprovalWorkflowStep.Completed:
                    logger.LogInformation("Approval orchestration {InstanceId} completed with {Status}", context.InstanceId, progress.Status);
                    return progress.Status;
                case ApprovalWorkflowStep.AssignStep:
                    progress = await CallAsync(context, ApprovalWorkflowNames.AssignStepActivity, request);
                    break;
                case ApprovalWorkflowStep.Finalize:
                    progress = await CallAsync(context, ApprovalWorkflowNames.FinalizeActivity, request);
                    break;
                case ApprovalWorkflowStep.Wait:
                    if (waits++ >= MaxWaitsPerExecution)
                    {
                        context.ContinueAsNew(input, preserveUnprocessedEvents: true);
                        return ContinuedAsNew;
                    }

                    progress = await WaitAsync(context, request, progress);
                    break;
                default:
                    throw new InvalidOperationException("The approval progress has an unknown next step.");
            }
        }
    }

    /// <summary>Races the signal event against the durable reminder or deadline timer of the current level (APR-011).</summary>
    private static async Task<ApprovalProgress> WaitAsync(TaskOrchestrationContext context, ApprovalActivityRequest request, ApprovalProgress progress)
    {
        if (progress.TimerAtUtc is not { } fireAt || progress.TimerKind is not { } kind)
            throw new InvalidOperationException("A waiting approval has no timer.");

        using var cancellation = new CancellationTokenSource();
        var signal = context.WaitForExternalEvent<ApprovalSignal>(ApprovalWorkflowNames.SignalEvent, cancellation.Token);
        var timer = context.CreateTimer(DateTime.SpecifyKind(fireAt, DateTimeKind.Utc), cancellation.Token);
        var winner = await Task.WhenAny(signal, timer);
        cancellation.Cancel();
        if (winner == signal)
            return await CallAsync(context, ApprovalWorkflowNames.ProgressActivity, request);
        return await CallAsync(context, ApprovalWorkflowNames.TimerActivity,
            new ApprovalTimerRequest(request.AwardId, progress.Level, kind, fireAt, request.CorrelationId));
    }

    /// <summary>
    /// Calls an activity with the retry policy. When the retries are exhausted (or the failure is permanent) the
    /// orchestration waits <see cref="PoisonBackoff"/> on a durable timer and tries again, at most
    /// <see cref="MaxPoisonRounds"/> rounds; then it fails and the reconciliation query reports it (APR-016).
    /// </summary>
    private static async Task<ApprovalProgress> CallAsync(TaskOrchestrationContext context, string activity, object input)
    {
        for (var round = 1; ; round++)
        {
            try
            {
                return await context.CallActivityAsync<ApprovalProgress>(activity, input, ActivityOptions);
            }
            catch (TaskFailedException) when (round < MaxPoisonRounds)
            {
                await context.CreateTimer(context.CurrentUtcDateTime.Add(PoisonBackoff), CancellationToken.None);
            }
        }
    }
}

/// <summary>An activity failure worth retrying (database, broker or host temporarily unavailable).</summary>
public sealed class TransientActivityException(string message, Exception? innerException = null) : Exception(message, innerException);
