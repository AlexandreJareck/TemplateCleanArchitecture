using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Template.Application.Common.Results;
using Template.Application.Interfaces.Approvals;
using Template.Application.Workflow;
using Template.Domain.Approvals;
using Template.Domain.Budgeting;
using Template.Domain.Requisitions;
using Template.Infrastructure.Persistence.Messaging;

namespace Template.WorkflowFunctions.Integration.Tests;

/// <summary>
/// The approval orchestration on the real Functions host with the Durable Task Azure Storage provider on Azurite
/// (T-APR-0xx-D): multi-step human approval with a durable escalation timer, expiry with a single release, duplicate and
/// late signals, host restart with replay, a lost signal repaired by the durable timer, cancellation, the internal-key
/// boundary and the deployment-version strategy with rollback repaired by reconciliation. Business truth is asserted in
/// PostgreSQL; the Durable runtime status is read through the same client the WebApi uses.
/// </summary>
[Collection(WorkflowCollection.Name)]
public sealed class ApprovalOrchestrationEmulatorTests(WorkflowEnvironment environment)
{
    private static readonly TimeSpan Short = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Long = TimeSpan.FromMinutes(4);

    [Fact]
    public async Task SC_01_TwoHumanLevels_WithADurableEscalation_CompleteAndCommitOnce()
    {
        await using var scenario = await ApprovalScenario.CreateAsync(environment);
        var (award, requisition) = await scenario.AwardAsync(12_000m);

        var watch = Stopwatch.StartNew();
        (await scenario.StartAsync(award, requisition)).ShouldBe(ApprovalWorkflowStartResult.Started);
        (await scenario.StartAsync(award, requisition)).ShouldBe(ApprovalWorkflowStartResult.AlreadyRunning);
        var assigned = await scenario.WaitForAsync(award, instance => instance.NextAction == ApprovalNextAction.Wait, Short);
        watch.Stop();
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10)); // NFR-PERF-7: award to first step assigned
        assigned.PolicyVersion.ShouldBe(2);
        assigned.WorkflowVersion.ShouldBe("1.0");
        assigned.Steps.Count.ShouldBe(2);

        var first = Guid.NewGuid();
        (await scenario.DecideAsync(award, first, "Approved")).IsSuccess.ShouldBeTrue();
        var second = await scenario.WaitForAsync(award, instance => instance.CurrentLevel == 2 && instance.NextAction == ApprovalNextAction.Wait, Short);
        second.CurrentStep!.EscalationCount.ShouldBe(0);

        // No decision on level 2: the reminder (15 s) and then the deadline (60 s) escalate it on durable timers.
        var escalated = await scenario.WaitForAsync(award, instance => instance.CurrentStep is { EscalationCount: 1 }, TimeSpan.FromSeconds(100));
        escalated.History.Select(entry => entry.Action).ShouldContain(ApprovalInstance.ActionReminded);
        (await scenario.DecideAsync(award, first, "Approved")).Error!.Code.ShouldBe(ProblemCodes.ApprovalDuplicateApprover);
        (await scenario.DecideAsync(award, Guid.NewGuid(), "Approved")).IsSuccess.ShouldBeTrue();

        var approved = await scenario.WaitForAsync(award, instance => instance.Status == ApprovalStatus.Approved, Short);
        approved.History.Select(entry => entry.Action).ShouldContain(ApprovalInstance.ActionEscalated);
        await scenario.WaitForRuntimeAsync(award, ApprovalWorkflowRuntimeStatus.Completed, Short);
        (await scenario.Workflow.SignalAsync(award, new ApprovalSignal(Guid.NewGuid(), ApprovalSignalKinds.Decision, "late"), default))
            .ShouldBe(ApprovalWorkflowSignalResult.NotRunning);

        await using var context = scenario.Context();
        (await context.Set<PurchaseRequisition>().SingleAsync(item => item.Id == requisition)).Status.ShouldBe(RequisitionStatus.Approved);
        var reservation = await context.Set<BudgetReservation>().Include(item => item.Changes).SingleAsync(item => item.RequisitionId == requisition);
        reservation.Status.ShouldBe(BudgetReservationStatus.Committed);
        reservation.Changes.Count(change => change.Operation == BudgetReservationOperation.Committed).ShouldBe(1);
        (await context.Set<OutboxMessage>().CountAsync(message => message.AggregateId == approved.Id && message.EventType == "procurement.approval.completed"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task T_APR_011_D_WithoutDecisions_TheApprovalExpires_AndReleasesTheReservationOnce()
    {
        await using var scenario = await ApprovalScenario.CreateAsync(environment);
        var (award, requisition) = await scenario.AwardAsync(3_000m);
        await scenario.StartAsync(award, requisition);

        var expired = await scenario.WaitForAsync(award, instance => instance.Status == ApprovalStatus.Expired, Long);

        expired.History.Select(entry => entry.Action).ShouldBe(
            [ApprovalInstance.ActionStarted, ApprovalInstance.ActionAssigned, ApprovalInstance.ActionReminded, ApprovalInstance.ActionEscalated,
                ApprovalInstance.ActionReminded, ApprovalInstance.ActionStepExpired, ApprovalInstance.ActionInstanceExpired]);
        await scenario.WaitForRuntimeAsync(award, ApprovalWorkflowRuntimeStatus.Completed, Short);
        await using var context = scenario.Context();
        var reservation = await context.Set<BudgetReservation>().Include(item => item.Changes).SingleAsync(item => item.RequisitionId == requisition);
        reservation.ReleaseReason.ShouldBe(BudgetReleaseReason.Expired);
        reservation.Changes.Count(change => change.Operation == BudgetReservationOperation.Released).ShouldBe(1);
        (await context.Set<PurchaseRequisition>().SingleAsync(item => item.Id == requisition)).Status.ShouldBe(RequisitionStatus.Expired);
    }

    [Fact]
    public async Task T_APR_014_D_DuplicateSignals_AndAHostRestart_ReplayWithoutDuplicatingEffects()
    {
        await using var scenario = await ApprovalScenario.CreateAsync(environment);
        var (award, requisition) = await scenario.AwardAsync(3_000m);
        await scenario.StartAsync(award, requisition);
        await scenario.WaitForAsync(award, instance => instance.NextAction == ApprovalNextAction.Wait, Short);
        for (var index = 0; index < 5; index++)
            (await scenario.Workflow.SignalAsync(award, new ApprovalSignal(Guid.NewGuid(), ApprovalSignalKinds.Reconcile, "duplicate"), default))
                .ShouldBe(ApprovalWorkflowSignalResult.Delivered);

        await environment.StartHostAsync(); // stop and start: the orchestration replays its history on the new host
        (await scenario.Workflow.GetStatusAsync(award, default)).IsActive.ShouldBeTrue();
        (await scenario.DecideAsync(award, Guid.NewGuid(), "Approved")).IsSuccess.ShouldBeTrue();

        // The new host takes over the task-hub partitions once the stopped host's blob leases expire (about 30 s); a queue message
        // the stopped host had dequeued becomes visible again after the 1-minute visibility timeout (host.json) and is redelivered.
        var approved = await scenario.WaitForAsync(award, instance => instance.Status == ApprovalStatus.Approved, TimeSpan.FromMinutes(3));
        approved.History.Count(entry => entry.Action == ApprovalInstance.ActionAssigned).ShouldBe(1);
        await scenario.WaitForRuntimeAsync(award, ApprovalWorkflowRuntimeStatus.Completed, Short);
        await using var context = scenario.Context();
        (await context.Set<BudgetReservation>().Include(item => item.Changes).SingleAsync(item => item.RequisitionId == requisition))
            .Changes.Count(change => change.Operation == BudgetReservationOperation.Committed).ShouldBe(1);
    }

    [Fact]
    public async Task T_APR_016_D_ALostSignal_IsRepairedByTheDurableReminderTimer()
    {
        await using var scenario = await ApprovalScenario.CreateAsync(environment, client => new DroppingSignals(client));
        var (award, requisition) = await scenario.AwardAsync(3_000m);
        await scenario.StartAsync(award, requisition);
        await scenario.WaitForAsync(award, instance => instance.NextAction == ApprovalNextAction.Wait, Short);

        (await scenario.DecideAsync(award, Guid.NewGuid(), "Approved")).IsSuccess.ShouldBeTrue(); // committed, signal dropped

        var approved = await scenario.WaitForAsync(award, instance => instance.Status == ApprovalStatus.Approved, TimeSpan.FromSeconds(60));
        approved.History.Select(entry => entry.Action).ShouldNotContain(ApprovalInstance.ActionReminded);
    }

    [Fact]
    public async Task T_APR_015_D_AdministrativeCancel_EndsTheOrchestration()
    {
        await using var scenario = await ApprovalScenario.CreateAsync(environment);
        var (award, requisition) = await scenario.AwardAsync(3_000m);
        await scenario.StartAsync(award, requisition);
        await scenario.WaitForAsync(award, instance => instance.NextAction == ApprovalNextAction.Wait, Short);

        (await scenario.CancelAsync(award)).IsSuccess.ShouldBeTrue();

        var status = await scenario.WaitForRuntimeAsync(award, ApprovalWorkflowRuntimeStatus.Completed, Short);
        status.InstanceId.ShouldBe(ApprovalWorkflowNames.InstanceIdFor(award));
        await using var context = scenario.Context();
        (await context.Set<PurchaseRequisition>().SingleAsync(item => item.Id == requisition)).Status.ShouldBe(RequisitionStatus.InSourcing);
    }

    [Fact]
    public async Task InternalBoundary_RejectsAWrongKey_AsUnavailable()
    {
        using var client = new HttpClient { BaseAddress = new Uri(environment.HostBaseUrl) };
        client.DefaultRequestHeaders.Add(ApprovalWorkflowHttp.KeyHeader, "wrong-key-0123456789abcdef0123456789");

        using var response = await client.GetAsync($"{ApprovalWorkflowHttp.WorkflowsRoute}/{ApprovalWorkflowNames.InstanceIdFor(Guid.NewGuid())}");
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Unauthorized);
        using var ready = await new HttpClient().GetAsync($"{environment.HostBaseUrl}health/ready");
        ready.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task T_Versioning_ANewerHostFinishesOlderInstances_AndARollbackIsRepairedByReconciliation()
    {
        await using var scenario = await ApprovalScenario.CreateAsync(environment);
        var (older, olderRequisition) = await scenario.AwardAsync(3_000m);
        await scenario.StartAsync(older, olderRequisition);
        await scenario.WaitForAsync(older, instance => instance.NextAction == ApprovalNextAction.Wait, Short);

        // Deploy a host stamping 2.0: in-flight 1.0 instances keep their version and finish on it (CurrentOrOlder).
        await environment.StartHostAsync(defaultVersion: "2.0");
        (await scenario.DecideAsync(older, Guid.NewGuid(), "Approved")).IsSuccess.ShouldBeTrue();
        (await scenario.WaitForAsync(older, instance => instance.Status == ApprovalStatus.Approved, Short)).WorkflowVersion.ShouldBe("1.0");

        // This build has no 2.0 code path: a new instance fails before any business effect.
        var (newer, newerRequisition) = await scenario.AwardAsync(3_000m);
        await scenario.StartAsync(newer, newerRequisition);
        var failed = await scenario.WaitForRuntimeAsync(newer, ApprovalWorkflowRuntimeStatus.Failed, Short);
        failed.Version.ShouldBeNull();
        (await scenario.InstanceAsync(newer)).ShouldBeNull();

        // Roll back to the 1.0 host: reconciliation restarts the failed run and it resumes from the database.
        await environment.StartHostAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));
        await using (var context = scenario.Context())
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"SourcingAwards\" SET \"SubmittedAtUtc\" = {DateTime.UtcNow.AddMinutes(-5)} WHERE \"Id\" = {newer}");
        var repaired = await scenario.ReconcileAsync(repair: true);
        repaired.Items.Single(item => item.AwardId == newer).Repair.ShouldBe("restarted");
        var resumed = await scenario.WaitForAsync(newer, instance => instance.NextAction == ApprovalNextAction.Wait, Short);
        resumed.WorkflowVersion.ShouldBe("1.0");
        (await scenario.DecideAsync(newer, Guid.NewGuid(), "Approved")).IsSuccess.ShouldBeTrue();
        await scenario.WaitForAsync(newer, instance => instance.Status == ApprovalStatus.Approved, Short);
    }

    /// <summary>A workflow client whose signals are silently lost (as during a network partition right after the commit).</summary>
    private sealed class DroppingSignals(IApprovalWorkflowClient inner) : IApprovalWorkflowClient
    {
        public Task<ApprovalWorkflowStartResult> StartAsync(ApprovalWorkflowInput input, CancellationToken cancellationToken) => inner.StartAsync(input, cancellationToken);

        public Task<ApprovalWorkflowSignalResult> SignalAsync(Guid awardId, ApprovalSignal signal, CancellationToken cancellationToken) =>
            Task.FromResult(ApprovalWorkflowSignalResult.Delivered);

        public Task<ApprovalWorkflowStatus> GetStatusAsync(Guid awardId, CancellationToken cancellationToken) => inner.GetStatusAsync(awardId, cancellationToken);

        public Task<bool> TerminateAsync(Guid awardId, string reason, CancellationToken cancellationToken) => inner.TerminateAsync(awardId, reason, cancellationToken);
    }
}
