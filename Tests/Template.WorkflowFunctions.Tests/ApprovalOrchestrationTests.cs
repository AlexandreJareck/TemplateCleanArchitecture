using Microsoft.DurableTask;
using Shouldly;
using Template.Application.Workflow;
using Template.WorkflowFunctions.Orchestration;

namespace Template.WorkflowFunctions.Tests;

/// <summary>
/// Deterministic tests of every orchestration branch (T-APR-0xx-D): start, assignment, signal and timer races,
/// finalization, duplicate and late signals, ContinueAsNew, poison retries, version guard and replay determinism. The
/// activity script stands for the Application use cases; the orchestration only reacts to their progress.
/// </summary>
public sealed class ApprovalOrchestrationTests
{
    private static readonly Guid Award = Guid.Parse("4f3a0f0e-0000-4000-8000-00000000abcd");
    private static readonly DateTime Reminder = new(2027, 3, 7, 13, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Deadline = new(2027, 3, 9, 13, 0, 0, DateTimeKind.Utc);
    private static readonly ApprovalWorkflowInput Input = new(ApprovalWorkflowInput.Version1, Award, Guid.NewGuid(), Reminder, "trace-orchestration");

    [Fact]
    public async Task T_APR_013_D_TwoLevelsDecidedBySignals_FinalizeAndComplete()
    {
        var script = new Script(
            Progress(ApprovalWorkflowStep.AssignStep, 1),
            Waiting(1),
            Progress(ApprovalWorkflowStep.AssignStep, 2),
            Waiting(2),
            Progress(ApprovalWorkflowStep.Finalize, 2),
            Done("Approved"));
        var context = new ScriptedOrchestrationContext(script.Next);

        var run = ApprovalOrchestration.RunAsync(context, Input);
        context.IsWaiting.ShouldBeTrue();
        context.Raise(Signal());
        context.IsWaiting.ShouldBeTrue();
        context.Raise(Signal());

        (await run).ShouldBe("Approved");
        script.Calls.ShouldBe([
            ApprovalWorkflowNames.StartActivity, ApprovalWorkflowNames.AssignStepActivity, ApprovalWorkflowNames.ProgressActivity,
            ApprovalWorkflowNames.AssignStepActivity, ApprovalWorkflowNames.ProgressActivity, ApprovalWorkflowNames.FinalizeActivity]);
        context.PendingTimerCancelled.ShouldBeTrue();
        context.Logs.ShouldHaveSingleItem().Message.ShouldContain("Approved");
    }

    [Fact]
    public async Task T_APR_011_D_ReminderThenDeadline_CallTheTimerActivityWithTheRecordedInstant()
    {
        var script = new Script(
            Progress(ApprovalWorkflowStep.AssignStep, 1),
            Waiting(1, ApprovalWorkflowTimer.Reminder, Reminder),
            Waiting(1, ApprovalWorkflowTimer.Deadline, Deadline),
            Progress(ApprovalWorkflowStep.Finalize, 1),
            Done("Expired"));
        var context = new ScriptedOrchestrationContext(script.Next);

        var run = ApprovalOrchestration.RunAsync(context, Input);
        context.FireTimer();
        context.FireTimer();

        (await run).ShouldBe("Expired");
        script.Inputs.OfType<ApprovalTimerRequest>().ShouldBe([
            new ApprovalTimerRequest(Award, 1, ApprovalWorkflowTimer.Reminder, Reminder, Input.CorrelationId),
            new ApprovalTimerRequest(Award, 1, ApprovalWorkflowTimer.Deadline, Deadline, Input.CorrelationId)]);
        context.Commands.Where(command => command.StartsWith("timer:", StringComparison.Ordinal))
            .ShouldBe([$"timer:{Reminder:O}", $"timer:{Deadline:O}"]);
    }

    [Fact]
    public async Task T_APR_014_D_DuplicateSignals_AreReadAgainstTheState_AndChangeNothing()
    {
        var script = new Script(
            Progress(ApprovalWorkflowStep.AssignStep, 1),
            Waiting(1),
            Waiting(1),
            Waiting(1),
            Done("Rejected"));
        var context = new ScriptedOrchestrationContext(script.Next);

        var run = ApprovalOrchestration.RunAsync(context, Input);
        context.Raise(Signal());
        context.Raise(Signal());
        context.Raise(Signal());

        (await run).ShouldBe("Rejected");
        script.Calls.Count(call => call == ApprovalWorkflowNames.ProgressActivity).ShouldBe(3);
        script.Calls.ShouldNotContain(ApprovalWorkflowNames.FinalizeActivity);
    }

    [Fact]
    public async Task T_APR_014_D_SignalsBufferedBeforeTheWait_AreConsumedInOrder()
    {
        var script = new Script(Progress(ApprovalWorkflowStep.AssignStep, 1), Waiting(1), Done("Cancelled"));
        var context = new ScriptedOrchestrationContext(script.Next);
        context.Raise(Signal());

        (await ApprovalOrchestration.RunAsync(context, Input)).ShouldBe("Cancelled");
        context.Commands.Count(command => command.StartsWith("timer:", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public async Task T_APR_001_D_StartForAnAwardThatLeftApproval_CompletesWithoutWaiting()
    {
        var context = new ScriptedOrchestrationContext(new Script(Done(ApprovalProgress.NotStarted)).Next);

        (await ApprovalOrchestration.RunAsync(context, Input)).ShouldBe(ApprovalProgress.NotStarted);
        context.Commands.ShouldHaveSingleItem().ShouldStartWith($"activity:{ApprovalWorkflowNames.StartActivity}");
    }

    [Fact]
    public async Task T_APR_001_D_TheStartActivityReceivesTheStampedVersion()
    {
        var script = new Script(Done("Approved"));
        await ApprovalOrchestration.RunAsync(new ScriptedOrchestrationContext(script.Next, version: null), Input);

        script.Inputs.Single().ShouldBe(new ApprovalStartRequest(Input, ApprovalOrchestration.Version1));
    }

    [Fact]
    public async Task ContinueAsNew_TruncatesHistoryAfterTheWaitBudget_AndPreservesSignals()
    {
        var steps = new List<ApprovalProgress> { Progress(ApprovalWorkflowStep.AssignStep, 1) };
        steps.AddRange(Enumerable.Range(0, ApprovalOrchestration.MaxWaitsPerExecution + 1).Select(_ => Waiting(1)));
        var context = new ScriptedOrchestrationContext(new Script([.. steps]).Next);

        var run = ApprovalOrchestration.RunAsync(context, Input);
        for (var index = 0; index < ApprovalOrchestration.MaxWaitsPerExecution; index++)
            context.Raise(Signal());

        (await run).ShouldBe(ApprovalOrchestration.ContinuedAsNew);
        context.ContinuedWith.ShouldBe(Input);
        context.PreservedEvents.ShouldBeTrue();
    }

    [Fact]
    public async Task PoisonActivity_IsRetriedAfterADurableBackoff_ThenFails()
    {
        var script = new Script(Progress(ApprovalWorkflowStep.Finalize, 1), Done("Approved"));
        var context = new ScriptedOrchestrationContext(script.Next)
        {
            Failure = (name, call) => name == ApprovalWorkflowNames.FinalizeActivity && call < 3 ? Failed() : null
        };

        (await ApprovalOrchestration.RunAsync(context, Input)).ShouldBe("Approved");
        context.Commands.Count(command => command == $"timer:{context.CurrentUtcDateTime.Add(ApprovalOrchestration.PoisonBackoff):O}").ShouldBe(1);

        var failing = new ScriptedOrchestrationContext(new Script(Progress(ApprovalWorkflowStep.Finalize, 1)).Next)
        {
            Failure = (name, _) => name == ApprovalWorkflowNames.FinalizeActivity ? Failed() : null
        };
        await Should.ThrowAsync<TaskFailedException>(() => ApprovalOrchestration.RunAsync(failing, Input));
        failing.Commands.Count(command => command.StartsWith("timer:", StringComparison.Ordinal)).ShouldBe(ApprovalOrchestration.MaxPoisonRounds - 1);
    }

    [Fact]
    public void RetryPolicy_RetriesTransientFailuresOnly()
    {
        var handle = ApprovalOrchestration.IsRetryable;

        handle(new TaskFailureDetails(typeof(TransientActivityException).FullName!, "database unavailable", null, null, null)).ShouldBeTrue();
        handle(new TaskFailureDetails(typeof(InvalidOperationException).FullName!, "rule broken", null, null, null)).ShouldBeFalse();
        ApprovalOrchestration.ActivityOptions.Retry!.Policy!.MaxNumberOfAttempts.ShouldBe(5);
    }

    [Theory]
    [InlineData("2.0")]
    [InlineData("1.1")]
    public async Task T_Versioning_UnknownOrchestrationVersions_HaveNoCodePath(string version)
    {
        var context = new ScriptedOrchestrationContext(new Script(Done("Approved")).Next, version);

        await Should.ThrowAsync<NotSupportedException>(() => ApprovalOrchestration.RunAsync(context, Input));
        context.Commands.ShouldBeEmpty();
    }

    [Fact]
    public async Task InvalidInput_IsRejectedBeforeAnyCommand()
    {
        var context = new ScriptedOrchestrationContext(new Script().Next);

        await Should.ThrowAsync<ArgumentException>(() => ApprovalOrchestration.RunAsync(context, null));
        await Should.ThrowAsync<ArgumentException>(() => ApprovalOrchestration.RunAsync(context, Input with { InputVersion = 2 }));
        context.Commands.ShouldBeEmpty();
    }

    [Fact]
    public async Task T_APR_016_D_ReplayOfTheRecordedHistory_SchedulesTheSameCommands_WithoutReplayLogs()
    {
        var script = new Script(
            Progress(ApprovalWorkflowStep.AssignStep, 1),
            Waiting(1, ApprovalWorkflowTimer.Reminder, Reminder),
            Waiting(1, ApprovalWorkflowTimer.Deadline, Deadline),
            Progress(ApprovalWorkflowStep.Finalize, 1),
            Done("Approved"));
        var first = new ScriptedOrchestrationContext(script.Next);
        var run = ApprovalOrchestration.RunAsync(first, Input);
        first.FireTimer();
        first.Raise(Signal());
        await run;

        // Replay the recorded episode up to the signal (as after a host restart), then run the rest live: the replayed
        // part re-schedules the same commands without executing activities, and only the live part logs.
        var recorded = first.History.Take(first.History.Count - 2).ToArray();
        var live = new Script(Progress(ApprovalWorkflowStep.Finalize, 1), Done("Approved"));
        var replayed = new ScriptedOrchestrationContext(live.Next, replayHistory: recorded);
        (await ApprovalOrchestration.RunAsync(replayed, Input)).ShouldBe("Approved");

        replayed.Commands.ShouldBe(first.Commands);
        live.Calls.ShouldBe([ApprovalWorkflowNames.ProgressActivity, ApprovalWorkflowNames.FinalizeActivity]);
        replayed.Logs.Count.ShouldBe(first.Logs.Count);
    }

    [Fact]
    public async Task WaitWithoutATimer_IsAProgrammingError()
    {
        var context = new ScriptedOrchestrationContext(new Script(Progress(ApprovalWorkflowStep.Wait, 1)).Next);

        await Should.ThrowAsync<InvalidOperationException>(() => ApprovalOrchestration.RunAsync(context, Input));
    }

    private static TaskFailedException Failed() =>
        new(ApprovalWorkflowNames.FinalizeActivity, 1, new TaskFailureDetails(typeof(TransientActivityException).FullName!, "synthetic", null, null, null));

    private static ApprovalSignal Signal() => new(Guid.NewGuid(), ApprovalSignalKinds.Decision, "trace-signal");

    private static ApprovalProgress Progress(ApprovalWorkflowStep next, int level) =>
        new(Award, Guid.NewGuid(), "Pending", next, level, null, null, Input.CorrelationId);

    private static ApprovalProgress Waiting(int level, ApprovalWorkflowTimer kind = ApprovalWorkflowTimer.Reminder, DateTime? at = null) =>
        new(Award, Guid.NewGuid(), "Pending", ApprovalWorkflowStep.Wait, level, kind, at ?? Reminder, Input.CorrelationId);

    private static ApprovalProgress Done(string status) => new(Award, Guid.NewGuid(), status, ApprovalWorkflowStep.Completed, 1, null, null, Input.CorrelationId);

    /// <summary>Returns the scripted progress values in order and records which activity received which input.</summary>
    private sealed class Script(params ApprovalProgress[] steps)
    {
        private readonly Queue<ApprovalProgress> queue = new(steps);

        public List<string> Calls { get; } = [];

        public List<object?> Inputs { get; } = [];

        public ApprovalProgress Next(string activity, object? input)
        {
            Calls.Add(activity);
            Inputs.Add(input);
            return queue.Dequeue();
        }
    }
}
