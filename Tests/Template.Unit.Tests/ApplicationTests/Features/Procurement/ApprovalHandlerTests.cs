using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Template.Application.Common.Results;
using Template.Application.Features.Approvals;
using Template.Application.Features.Budgeting;
using Template.Application.Features.Requisitions;
using Template.Application.Features.Sourcing;
using Template.Application.Interfaces.Approvals;
using Template.Application.Messaging.Procurement;
using Template.Application.Security;
using Template.Application.Workflow;
using Template.Application.Wrappers;
using Template.Domain.Approvals;
using Template.Domain.Budgeting;
using Template.Domain.Requisitions;
using Template.Domain.Sourcing;
using Template.Unit.Tests.ApplicationTests.Features.Reporting;

namespace Template.Unit.Tests.ApplicationTests.Features.Procurement;

/// <summary>
/// Approval use cases against port fakes (APR-001..016, BUD-010, REQ-008). The scenario is a requisition of 18,000.00 BRL,
/// a competitive RFQ with three quotes and an award of the leading 16,200.00 BRL quote, which needs L1 (cost-center
/// manager) and L2 (department head) under policy version 1. All data is synthetic.
/// </summary>
public sealed class ApprovalHandlerTests
{
    private const string Reason = "Synthetic reason for the approval handler test";
    private readonly ReportingTestDoubles reporting = new();

    private ProcurementTestDoubles Doubles => reporting.Procurement;

    private ProcurementTestDoubles.Actor Manager { get; } = new();
    private ProcurementTestDoubles.Actor Head { get; } = new();
    private ProcurementTestDoubles.Actor Finance { get; } = new();
    private ProcurementTestDoubles.Actor ProcurementManager { get; } = new();

    [Fact]
    public async Task T_SC_01_TwoLevelApproval_CommitsTheReservation_AndApprovesEveryAggregateOnce()
    {
        var (rfq, award) = await AwardedAsync();
        var activities = Doubles.ApprovalActivities();

        var started = await activities.StartAsync(Input(award), default);
        started.Next.ShouldBe(ApprovalWorkflowStep.AssignStep);
        (await activities.StartAsync(Input(award), default)).InstanceId.ShouldBe(started.InstanceId);
        Doubles.Approvals.Items.ShouldHaveSingleItem();

        var assigned = await activities.AssignStepAsync(Request(award), default);
        assigned.Next.ShouldBe(ApprovalWorkflowStep.Wait);
        assigned.TimerKind.ShouldBe(ApprovalWorkflowTimer.Reminder);
        var instance = Doubles.Approvals.Items.Single();
        instance.CurrentStep!.AssigneeUserId.ShouldBe(Manager.Id);

        (await DecideAsync(instance, 1, Manager, "Approved")).IsSuccess.ShouldBeTrue();
        Doubles.Workflow.Signals.ShouldHaveSingleItem().Signal.Kind.ShouldBe(ApprovalSignalKinds.Decision);
        (await activities.GetProgressAsync(Request(award), default)).Next.ShouldBe(ApprovalWorkflowStep.AssignStep);
        await activities.AssignStepAsync(Request(award), default);
        instance.CurrentStep!.AssigneeUserId.ShouldBe(Head.Id);

        var decided = await DecideAsync(instance, 2, Head, "Approved");
        decided.IsSuccess.ShouldBeTrue(decided.Error?.Message);
        decided.Value.NextAction.ShouldBe(nameof(ApprovalNextAction.Finalize));
        var finalized = await activities.FinalizeAsync(Request(award), default);
        finalized.Next.ShouldBe(ApprovalWorkflowStep.Completed);
        finalized.Status.ShouldBe("Approved");
        (await activities.FinalizeAsync(Request(award), default)).Status.ShouldBe("Approved");

        var requisition = Doubles.Requisitions.Items.Single(item => item.Id == rfq.RequisitionId);
        requisition.Status.ShouldBe(RequisitionStatus.Approved);
        rfq.Status.ShouldBe(RfqStatus.Awarded);
        rfq.Awards.Single().Status.ShouldBe(AwardStatus.Approved);
        var reservation = Doubles.Allocation.Reservations.Single(item => item.RequisitionId == requisition.Id);
        reservation.Status.ShouldBe(BudgetReservationStatus.Committed);
        Doubles.Allocation.CommittedAmount.ShouldBe(16_200m);
        Doubles.Outbox.Events.Count(envelope => envelope.EventType == "procurement.budget.reservation-committed").ShouldBe(1);
        Doubles.Outbox.Events.Count(envelope => envelope.EventType == "procurement.approval.completed").ShouldBe(1);
        Doubles.Outbox.Events.Count(envelope => envelope.EventType == "procurement.approval.step-assigned").ShouldBe(2);
        Doubles.Audit.Records.Select(record => record.Action).ShouldContain(ApprovalAudit.Approved);
        Doubles.Audit.Records.Select(record => record.Action).ShouldContain(BudgetMoney.ReservationCommitted);
    }

    [Fact]
    public async Task T_APR_001_a_StartAfterTheAwardLeftApproval_ReturnsNotStarted()
    {
        var (_, award) = await AwardedAsync();
        Doubles.Permissions.Grant(ProcurementManager.Id, SeededRoles.PermissionsByRole[SeededRoles.ProcurementManager]);
        Doubles.ActAs(ProcurementManager);
        var requisition = Doubles.Requisitions.Items.Single();
        (await Doubles.Transitions().Handle(new CancelRequisitionCommand(requisition.Id, requisition.Version, Reason), default)).IsSuccess.ShouldBeTrue();

        var progress = await Doubles.ApprovalActivities().StartAsync(Input(award), default);

        progress.Status.ShouldBe(ApprovalProgress.NotStarted);
        progress.Next.ShouldBe(ApprovalWorkflowStep.Completed);
        Doubles.Approvals.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task T_APR_003_a_MissingOrInactiveAssignees_FallBackToTheEscalationGroup()
    {
        var (_, award) = await AwardedAsync();
        Doubles.Approvers.Deactivate(Manager.Id);
        var activities = Doubles.ApprovalActivities();
        await activities.StartAsync(Input(award), default);

        await activities.AssignStepAsync(Request(award), default);

        var step = Doubles.Approvals.Items.Single().CurrentStep!;
        step.AssigneeRole.ShouldBe(SeededRoles.ProcurementManager);
        Doubles.Audit.Records.Last(record => record.Action == ApprovalAudit.StepAssigned).Reason.ShouldBe("InactiveAssigneeFallback");
    }

    [Fact]
    public async Task T_APR_006_a_TheRequesterAsFinalApprover_ResolvesToTheEscalationGroup()
    {
        var (_, award) = await AwardedAsync();
        Doubles.Department.Update("Operations", Doubles.Requester.Id, ProcurementTestDoubles.Now).ShouldBeNull();
        Doubles.Approvers.AddApprover(Doubles.Requester.Id, SeededRoles.DepartmentHead);
        var activities = Doubles.ApprovalActivities();
        await activities.StartAsync(Input(award), default);
        await activities.AssignStepAsync(Request(award), default);
        var instance = Doubles.Approvals.Items.Single();
        await DecideAsync(instance, 1, Manager, "Approved");

        await activities.AssignStepAsync(Request(award), default);

        instance.CurrentStep!.AssigneeRole.ShouldBe(SeededRoles.ProcurementManager);
        Doubles.Audit.Records.Last(record => record.Action == ApprovalAudit.StepAssigned).Reason.ShouldBe("IneligibleAssigneeFallback");

        // SC-09: the requester is also a member of the escalation group; a direct decision call is denied and audited.
        Doubles.Approvers.AddApprover(Doubles.Requester.Id, SeededRoles.DepartmentHead, SeededRoles.ProcurementManager);
        Doubles.Permissions.Grant(Doubles.Requester.Id, [Permissions.ApprovalsDecide]);
        var denied = await DecideAsync(instance, 2, Doubles.Requester, "Approved");
        denied.Error!.Code.ShouldBe(ProblemCodes.ApprovalSelfApproval);
        denied.Error.Category.ShouldBe(ErrorCategory.Forbidden);
        Doubles.Audit.Records.Last().Action.ShouldBe(ApprovalAudit.DecisionDenied);
        Doubles.Audit.Records.Last().RuleIds.ShouldBe(["APR-005"]);
    }

    [Fact]
    public async Task T_APR_004_a_ADecisionByANonAssignee_IsDeniedAndAudited()
    {
        var instance = await AssignedInstanceAsync();
        Doubles.Approvers.AddApprover(Doubles.Requester.Id, SeededRoles.CostCenterManager);
        Doubles.Permissions.Grant(Doubles.Requester.Id, [Permissions.ApprovalsDecide]);

        var result = await DecideAsync(instance, 1, Doubles.Requester, "Approved");

        result.Error!.Code.ShouldBe(ProblemCodes.ApprovalNotAssignee);
        result.Error.Category.ShouldBe(ErrorCategory.Forbidden);
        Doubles.Audit.Records.Last().Action.ShouldBe(ApprovalAudit.DecisionDenied);
        instance.CurrentStep!.Status.ShouldBe(ApprovalStepStatus.Pending);
    }

    [Fact]
    public async Task T_APR_014_a_ConcurrentAndRepeatedDecisions_ReturnStableOutcomes()
    {
        var instance = await AssignedInstanceAsync();
        var version = instance.Version;

        (await DecideAsync(instance, 1, Manager, "Approved", version)).IsSuccess.ShouldBeTrue();
        var repeated = await DecideAsync(instance, 1, Manager, "Approved", version);
        repeated.IsSuccess.ShouldBeTrue();
        (await DecideAsync(instance, 1, Manager, "Rejected", version, Reason)).Error!.Code.ShouldBe(ProblemCodes.ApprovalStepAlreadyDecided);
        Doubles.Workflow.Signals.Count.ShouldBe(1);
        instance.History.Count(entry => entry.Action == ApprovalInstance.ActionApproved).ShouldBe(1);
    }

    [Fact]
    public async Task T_NFR_Availability_DecisionWithTheWorkflowUnavailable_Answers503WithoutChange()
    {
        var instance = await AssignedInstanceAsync();
        var version = instance.Version;
        Doubles.Workflow.Unavailable = true;

        var result = await DecideAsync(instance, 1, Manager, "Approved");

        result.Error!.Code.ShouldBe(ProblemCodes.ApprovalWorkflowUnavailable);
        result.Error.Category.ShouldBe(ErrorCategory.Unavailable);
        instance.Version.ShouldBe(version);
        instance.CurrentStep!.Decision.ShouldBeNull();
    }

    [Fact]
    public async Task T_APR_009_b_RejectionReleasesTheReservationOnce_AndEndsTheRequisition()
    {
        var instance = await AssignedInstanceAsync();
        var award = instance.AwardId;

        (await DecideAsync(instance, 1, Manager, "Rejected", reason: "short")).Error!.Code.ShouldBe(ProblemCodes.ReasonRequired);
        (await DecideAsync(instance, 1, Manager, "Rejected", reason: Reason)).IsSuccess.ShouldBeTrue();
        var activities = Doubles.ApprovalActivities();
        (await activities.FinalizeAsync(Request(award), default)).Status.ShouldBe("Rejected");
        (await activities.FinalizeAsync(Request(award), default)).Status.ShouldBe("Rejected");

        var requisition = Doubles.Requisitions.Items.Single();
        requisition.Status.ShouldBe(RequisitionStatus.Rejected);
        requisition.ActiveReservationId.ShouldBeNull();
        var reservation = Doubles.Allocation.Reservations.Single(item => item.RequisitionId == requisition.Id);
        reservation.Status.ShouldBe(BudgetReservationStatus.Released);
        reservation.ReleaseReason.ShouldBe(BudgetReleaseReason.Rejected);
        Doubles.Allocation.ReservedAmount.ShouldBe(0m);
        reservation.Changes.Count(change => change.Operation == BudgetReservationOperation.Released).ShouldBe(1);
        Doubles.Rfqs.Items.Single().Status.ShouldBe(RfqStatus.Cancelled);
        Doubles.Outbox.Events.Count(envelope => envelope.EventType == "procurement.budget.reservation-released").ShouldBe(1);
    }

    [Fact]
    public async Task T_APR_011_b_DeadlinesEscalateThenExpire_AndReleaseOnce()
    {
        var instance = await AssignedInstanceAsync();
        var award = instance.AwardId;
        var activities = Doubles.ApprovalActivities();
        var step = instance.CurrentStep!;

        var reminded = await activities.ProcessTimerAsync(Timer(award, ApprovalWorkflowTimer.Reminder, step.ReminderDueAtUtc!.Value), default);
        reminded.TimerKind.ShouldBe(ApprovalWorkflowTimer.Deadline);
        (await activities.ProcessTimerAsync(Timer(award, ApprovalWorkflowTimer.Reminder, step.ReminderDueAtUtc!.Value), default)).TimerKind
            .ShouldBe(ApprovalWorkflowTimer.Deadline);
        Doubles.Outbox.Events.Count(envelope => envelope.EventType == "procurement.approval.reminder-due").ShouldBe(1);

        var escalated = await activities.ProcessTimerAsync(Timer(award, ApprovalWorkflowTimer.Deadline, step.DueAtUtc!.Value), default);
        escalated.Next.ShouldBe(ApprovalWorkflowStep.Wait);
        instance.CurrentStep!.AssigneeRole.ShouldBe(SeededRoles.ProcurementManager);
        var expired = await activities.ProcessTimerAsync(Timer(award, ApprovalWorkflowTimer.Deadline, instance.CurrentStep.DueAtUtc!.Value), default);
        expired.Next.ShouldBe(ApprovalWorkflowStep.Finalize);
        (await activities.FinalizeAsync(Request(award), default)).Status.ShouldBe("Expired");

        Doubles.Requisitions.Items.Single().Status.ShouldBe(RequisitionStatus.Expired);
        Doubles.Allocation.Reservations.Single().ReleaseReason.ShouldBe(BudgetReleaseReason.Expired);
        Doubles.Outbox.Events.Count(envelope => envelope.EventType == "procurement.approval.expired").ShouldBe(1);
    }

    [Fact]
    public async Task T_APR_012_b_SupplierSuspension_InvalidatesTheApproval_AndReopensTheRfq()
    {
        var instance = await AssignedInstanceAsync();
        Doubles.ActAsSystem();

        var cascade = await Doubles.ApprovalCascade().InvalidateForSupplierAsync(instance.SupplierId, default);
        await Doubles.ApprovalCascade().CompleteAsync(cascade, default);

        instance.Status.ShouldBe(ApprovalStatus.Invalidated);
        var rfq = Doubles.Rfqs.Items.Single();
        rfq.Status.ShouldBe(RfqStatus.Closed);
        rfq.Awards.Single().Status.ShouldBe(AwardStatus.Invalidated);
        var requisition = Doubles.Requisitions.Items.Single();
        requisition.Status.ShouldBe(RequisitionStatus.InSourcing);
        requisition.AwardId.ShouldBeNull();
        Doubles.Allocation.Reservations.Single().Status.ShouldBe(BudgetReservationStatus.Reserved);
        Doubles.Workflow.Signals.Last().Signal.Kind.ShouldBe(ApprovalSignalKinds.Ended);
        (await Doubles.ApprovalCascade().InvalidateForSupplierAsync(instance.SupplierId, default)).AwardIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task T_REQ_008_c_CancelAny_EndsThePendingApprovalInTheSameCommit()
    {
        var instance = await AssignedInstanceAsync();
        Doubles.Permissions.Grant(ProcurementManager.Id, SeededRoles.PermissionsByRole[SeededRoles.ProcurementManager]);
        Doubles.ActAs(ProcurementManager);
        var requisition = Doubles.Requisitions.Items.Single();

        (await Doubles.Transitions().Handle(new CancelRequisitionCommand(requisition.Id, requisition.Version, Reason), default)).IsSuccess.ShouldBeTrue();

        instance.Status.ShouldBe(ApprovalStatus.Cancelled);
        instance.EndCause.ShouldBe(ApprovalInstance.CauseRequisitionCancelled);
        Doubles.Allocation.Reservations.Single().ReleaseReason.ShouldBe(BudgetReleaseReason.Cancelled);
        Doubles.Outbox.Events.Count(envelope => envelope.EventType == "procurement.approval.cancelled").ShouldBe(1);
        (await Doubles.ApprovalActivities().GetProgressAsync(Request(instance.AwardId), default)).Next.ShouldBe(ApprovalWorkflowStep.Completed);
    }

    [Fact]
    public async Task T_APR_015_a_AdministrativeCancel_ReturnsTheRequisitionToSourcing()
    {
        var instance = await AssignedInstanceAsync();
        Doubles.Permissions.Grant(ProcurementManager.Id, SeededRoles.PermissionsByRole[SeededRoles.ProcurementManager]);
        Doubles.Approvers.AddApprover(ProcurementManager.Id, SeededRoles.ProcurementManager);
        Doubles.ActAs(ProcurementManager);

        (await Doubles.ApprovalDecisions().Handle(new CancelApprovalCommand(instance.Id, instance.Version, null), default)).Error!.Code
            .ShouldBe(ProblemCodes.ReasonRequired);
        var cancelled = await Doubles.ApprovalDecisions().Handle(new CancelApprovalCommand(instance.Id, instance.Version, Reason), default);

        cancelled.IsSuccess.ShouldBeTrue(cancelled.Error?.Message);
        instance.Status.ShouldBe(ApprovalStatus.Cancelled);
        Doubles.Requisitions.Items.Single().Status.ShouldBe(RequisitionStatus.InSourcing);
        Doubles.Rfqs.Items.Single().Awards.Single().Status.ShouldBe(AwardStatus.Withdrawn);
        Doubles.Audit.Records.Last(record => record.ResourceType == ApprovalAudit.Resource).Action.ShouldBe(ApprovalAudit.Administered);
    }

    [Fact]
    public async Task T_APR_010_a_DelegationThroughTheUseCase_NotifiesTheDelegate()
    {
        var instance = await AssignedInstanceAsync();
        Doubles.Permissions.Grant(Manager.Id, [Permissions.ApprovalsDelegate]);
        var delegateUser = new ProcurementTestDoubles.Actor();
        Doubles.Approvers.AddApprover(delegateUser.Id, SeededRoles.CostCenterManager);
        Doubles.ActAs(Manager);

        var result = await Doubles.ApprovalDecisions().Handle(
            new DelegateApprovalStepCommand(instance.Id, 1, instance.Version, delegateUser.Id, Reason), default);

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        instance.CurrentStep!.AssigneeUserId.ShouldBe(delegateUser.Id);
        var assigned = Doubles.Outbox.Events.Last(envelope => envelope.EventType == "procurement.approval.step-assigned");
        RfqNotificationPlanner.Plan(assigned).Single().RecipientId.ShouldBe(delegateUser.Id);
        Doubles.Workflow.Signals.Last().Signal.Kind.ShouldBe(ApprovalSignalKinds.Delegated);
    }

    [Fact]
    public async Task Queries_ShowTheInboxToTheAssignee_AndHideTheInstanceFromOutsiders()
    {
        var instance = await AssignedInstanceAsync();
        Doubles.ActAs(Manager);

        var inbox = await Doubles.ApprovalQueries().Handle(new ListApprovalInstancesQuery(null, null, null, null, "me", null, null), default);
        inbox.Value.Items.ShouldHaveSingleItem().Id.ShouldBe(instance.Id);
        var view = await Doubles.ApprovalQueries().Handle(new GetApprovalInstanceQuery(instance.Id), default);
        view.Value.AllowedActions.ShouldBe([ApprovalActions.Approve, ApprovalActions.Reject, ApprovalActions.Delegate]);
        view.Value.Award!.Comparison.Count.ShouldBe(3);
        view.Value.Award.Comparison.Single(entry => entry.Awarded).Rank.ShouldBe(1);

        var outsider = new ProcurementTestDoubles.Actor();
        Doubles.ActAs(outsider);
        (await Doubles.ApprovalQueries().Handle(new GetApprovalInstanceQuery(instance.Id), default)).Error!.Code.ShouldBe(ProblemCodes.NotFound);
        (await Doubles.ApprovalQueries().Handle(new ListApprovalInstancesQuery(null, null, null, null, "me", null, null), default)).Error!.Category
            .ShouldBe(ErrorCategory.Forbidden);
    }

    [Fact]
    public async Task T_APR_016_b_Reconciliation_StartsMissingWorkflows_AndReSignalsStalledOnes()
    {
        var (_, award) = await AwardedAsync();
        Doubles.Clock.UtcNow = Doubles.Clock.UtcNow.AddMinutes(5);
        Doubles.Workflow.RuntimeStatus = ApprovalWorkflowRuntimeStatus.NotFound;

        var report = await Doubles.ApprovalReconciliation().RunAsync(repair: false, 100, default);
        report.Items.ShouldHaveSingleItem().Issue.ShouldBe("missing-workflow");
        Doubles.Workflow.Starts.ShouldBeEmpty();
        var repaired = await Doubles.ApprovalReconciliation().RunAsync(repair: true, 100, default);
        repaired.Items.Single().Repair.ShouldBe("started");
        Doubles.Workflow.Starts.Single().AwardId.ShouldBe(award);

        await Doubles.ApprovalActivities().StartAsync(Input(award), default);
        Doubles.Workflow.RuntimeStatus = ApprovalWorkflowRuntimeStatus.Running;
        Doubles.Clock.UtcNow = Doubles.Clock.UtcNow.AddMinutes(5);
        var stalled = await Doubles.ApprovalReconciliation().RunAsync(repair: true, 100, default);
        stalled.Items.Single().Issue.ShouldBe("workflow-stalled");
        Doubles.Workflow.Signals.Last().Signal.Kind.ShouldBe(ApprovalSignalKinds.Reconcile);

        Doubles.Workflow.Unavailable = true;
        (await Doubles.ApprovalReconciliation().RunAsync(repair: true, 100, default)).Items.Single().Issue.ShouldBe("workflow-unavailable");
    }

    [Fact]
    public async Task T_APR_001_a_TheStarter_TurnsAwardSubmittedIntoAnIdempotentStart()
    {
        var (rfq, award) = await AwardedAsync();
        var envelope = Doubles.Outbox.Events.Single(item => item.EventType == "procurement.award.submitted");
        var starter = new ApprovalWorkflowStarter(Doubles.Workflow, NullLogger<ApprovalWorkflowStarter>.Instance);

        (await starter.HandleAsync(envelope, default)).ShouldBe(Template.Application.Interfaces.Messaging.InboxApplyResult.Applied);

        var start = Doubles.Workflow.Starts.Single();
        start.AwardId.ShouldBe(award);
        start.RequisitionId.ShouldBe(rfq.RequisitionId);
        start.CorrelationId.ShouldBe(envelope.CorrelationId);
        Doubles.Workflow.Unavailable = true;
        await Should.ThrowAsync<ApprovalWorkflowUnavailableException>(() => starter.HandleAsync(envelope, default));
        await Should.ThrowAsync<JsonException>(() => starter.HandleAsync(envelope with { EventType = "procurement.rfq.closed" }, default));
    }

    [Fact]
    public void ApprovalNotifications_TargetTheAssigneeRoleOrRequester()
    {
        var requester = Guid.NewGuid();
        var role = ProcurementIntegrationEventContract.FromDomainEvent(
            new ApprovalStepEscalated(ProcurementTestDoubles.Now, Guid.NewGuid(), 1, 1, SeededRoles.ProcurementManager, ProcurementTestDoubles.Now), "trace");
        var outcome = ProcurementIntegrationEventContract.FromDomainEvent(
            new ApprovalCompleted(ProcurementTestDoubles.Now, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), requester), "trace");

        var roleRequest = RfqNotificationPlanner.Plan(role).Single();
        roleRequest.RecipientType.ShouldBe(RfqNotificationPlanner.RoleRecipient);
        roleRequest.RecipientId.ShouldBe(RfqNotificationPlanner.RoleGroupRecipientId);
        roleRequest.Template.ShouldBe(RfqNotificationPlanner.ApprovalEscalatedTemplate);
        RfqNotificationPlanner.Plan(outcome).Single().ShouldBe(new Template.Application.Interfaces.Messaging.NotificationRequest(
            RfqNotificationPlanner.UserRecipient, requester, RfqNotificationPlanner.ApprovalApprovedTemplate, "ApprovalInstance", outcome.AggregateId));
        ProcurementConsumers.NotificationEventTypes.ShouldContain("procurement.approval.step-assigned");
        ProcurementConsumers.ApprovalEventTypes.ShouldBe(["procurement.award.submitted"]);
    }

    private async Task<(RequestForQuotation Rfq, Guid AwardId)> AwardedAsync()
    {
        var (rfq, _) = await reporting.ClosedRfqAsync();
        var leading = rfq.CurrentComparison!.Entries.Single(entry => entry.Rank == 1).QuoteRevisionId;
        Doubles.ActAs(reporting.Buyer);
        var awarded = await Doubles.RfqCommands().Handle(new SubmitAwardCommand(rfq.Id, rfq.Version, leading, null, null, null), default);
        awarded.IsSuccess.ShouldBeTrue(awarded.Error?.Message);

        Doubles.CostCenter.Update("Operations", Manager.Id, ProcurementTestDoubles.Now).ShouldBeNull();
        Doubles.Department.Update("Operations", Head.Id, ProcurementTestDoubles.Now).ShouldBeNull();
        Doubles.Approvers.AddApprover(Manager.Id, SeededRoles.CostCenterManager);
        Doubles.Approvers.AddApprover(Head.Id, SeededRoles.DepartmentHead);
        Doubles.Approvers.AddApprover(Finance.Id, SeededRoles.FinanceApprover);
        foreach (var (actor, role) in new[] { (Manager, SeededRoles.CostCenterManager), (Head, SeededRoles.DepartmentHead), (Finance, SeededRoles.FinanceApprover) })
            Doubles.Permissions.Grant(actor.Id, SeededRoles.PermissionsByRole[role]);
        Doubles.ActAsSystem();
        return (rfq, rfq.Awards.Single().Id);
    }

    private async Task<ApprovalInstance> AssignedInstanceAsync()
    {
        var (_, award) = await AwardedAsync();
        var activities = Doubles.ApprovalActivities();
        await activities.StartAsync(Input(award), default);
        await activities.AssignStepAsync(Request(award), default);
        return Doubles.Approvals.Items.Single();
    }

    private async Task<Result<ApprovalInstanceView>> DecideAsync(
        ApprovalInstance instance, int level, ProcurementTestDoubles.Actor actor, string decision, long? version = null, string? reason = null)
    {
        Doubles.ActAs(actor);
        try
        {
            return await Doubles.ApprovalDecisions().Handle(new DecideApprovalStepCommand(instance.Id, level, version ?? instance.Version, decision, reason), default);
        }
        finally
        {
            Doubles.ActAsSystem();
        }
    }

    private static ApprovalStartRequest Input(Guid awardId) =>
        new(new ApprovalWorkflowInput(ApprovalWorkflowInput.Version1, awardId, Guid.NewGuid(), ProcurementTestDoubles.Now, "trace-approval"), "1.0");

    private static ApprovalActivityRequest Request(Guid awardId) => new(awardId, "trace-approval");

    private static ApprovalTimerRequest Timer(Guid awardId, ApprovalWorkflowTimer kind, DateTime fireAt) => new(awardId, 1, kind, fireAt, "trace-approval");
}
