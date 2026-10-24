using Shouldly;
using Template.Application.Workflow;
using Template.Domain.Approvals;
using Template.Domain.Common;

namespace Template.Unit.Tests.DomainTests.Approvals;

/// <summary>Mock-free approval aggregate rules (APR-001..015, POL-001), named by the business-rule test IDs.</summary>
public sealed class ApprovalInstanceTests
{
    private const string Reason = "Synthetic reason for the approval rule test";
    private const string Finance = "FinanceApprover";
    private const string Escalation = "ProcurementManager";
    private static readonly DateTime Now = new(2027, 3, 1, 13, 0, 0, DateTimeKind.Utc);
    private static readonly ApprovalPolicy Policy = ApprovalPolicy.Version1(Finance, Escalation);

    private readonly Guid requester = Guid.NewGuid();
    private readonly Guid buyer = Guid.NewGuid();
    private readonly Guid manager = Guid.NewGuid();
    private readonly Guid head = Guid.NewGuid();
    private readonly Guid finance = Guid.NewGuid();

    [Theory]
    [InlineData("10000.00", new[] { 1 })]
    [InlineData("10000.01", new[] { 1, 2 })]
    [InlineData("100000.00", new[] { 1, 2 })]
    [InlineData("100000.01", new[] { 1, 2, 3 })]
    [InlineData("0.01", new[] { 1 })]
    public void T_APR_002_a_RequiredLevels_FollowTheExclusiveThresholds(string amount, int[] levels)
    {
        var instance = Start(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture));

        instance.Steps.OrderBy(step => step.Level).Select(step => step.Level).ShouldBe(levels);
        instance.CurrentLevel.ShouldBe(1);
        instance.FinalLevel.ShouldBe(levels.Max());
    }

    [Fact]
    public void T_APR_002_a_ZeroAmount_IsRejected()
    {
        var (instance, error) = ApprovalInstance.Start(Guid.NewGuid(), Facts(0m), Policy, "1.0", Now, "trace");

        instance.ShouldBeNull();
        error!.RuleId.ShouldBe("APR-002");
    }

    [Fact]
    public void T_APR_001_b_Start_PinsThePolicy_DerivesTheWorkflowId_AndWaitsForAssignment()
    {
        var policy = ApprovalPolicy.Version1(Finance, Escalation);
        var facts = Facts(16_500m);
        var (instance, error) = ApprovalInstance.Start(Guid.NewGuid(), facts, policy, "1.0", Now, "trace");

        error.ShouldBeNull();
        instance!.PolicyVersion.ShouldBe(1);
        instance.PolicyReference.ShouldBe("ApprovalPolicy:1");
        instance.StepDuration.ShouldBe(TimeSpan.FromHours(120));
        instance.ReminderAfter.ShouldBe(TimeSpan.FromHours(72));
        instance.WorkflowInstanceId.ShouldBe(ApprovalWorkflowNames.InstanceIdFor(facts.AwardId));
        ApprovalWorkflowNames.AwardIdFrom(instance.WorkflowInstanceId).ShouldBe(facts.AwardId);
        instance.NextAction.ShouldBe(ApprovalNextAction.AssignStep);
        instance.Status.ShouldBe(ApprovalStatus.Pending);
        policy.IsInUse.ShouldBeTrue();
        instance.DomainEvents.Single().ShouldBeOfType<ApprovalStarted>().RequiredLevels.ShouldBe([1, 2]);
        instance.History.Single().Action.ShouldBe(ApprovalInstance.ActionStarted);
    }

    [Fact]
    public void T_APR_017_a_PolicyVersion_KeepsItsFirstUse_AndValidatesNewVersions()
    {
        var policy = ApprovalPolicy.Version1(Finance, Escalation);
        policy.MarkUsed(Now);
        policy.MarkUsed(Now.AddDays(1));
        policy.FirstUsedAtUtc.ShouldBe(Now);

        Create([new(1, 0m, ApproverResolver.CostCenterManager, null), new(2, 0m, ApproverResolver.DepartmentHead, null)]).Error!.Field.ShouldBe("levels");
        Create([new(1, 5m, ApproverResolver.CostCenterManager, null)]).Error!.Field.ShouldBe("levels");
        Create([new(1, 0m, ApproverResolver.RoleGroup, null)]).Error!.Field.ShouldBe("levels");
        Create([new(1, 0m, ApproverResolver.CostCenterManager, null)], reminder: TimeSpan.FromHours(200)).Error!.Field.ShouldBe("reminderAfter");
        Create([new(1, 0m, ApproverResolver.CostCenterManager, null)], effectiveFrom: Now.AddMinutes(-1)).Error!.Field.ShouldBe("effectiveFrom");
        Create([new(1, 0m, ApproverResolver.CostCenterManager, null), new(2, 50_000m, ApproverResolver.RoleGroup, Finance)]).Policy!.Version.ShouldBe(2);
    }

    [Fact]
    public void T_APR_006_a_AssigningAnIneligibleUser_IsRejected_ButARoleGroupIsAccepted()
    {
        var instance = Start(5_000m);

        instance.AssignCurrentStep(ApprovalAssignee.ForUser(requester), "CostCenterManager", Now, "trace").Error.ShouldBe(ApprovalErrors.IneligibleAssignee);
        instance.AssignCurrentStep(ApprovalAssignee.ForUser(buyer), "CostCenterManager", Now, "trace").Error.ShouldBe(ApprovalErrors.IneligibleAssignee);

        instance.AssignCurrentStep(ApprovalAssignee.ForRole(Escalation), "IneligibleAssigneeFallback", Now, "trace").Changed.ShouldBeTrue();
        instance.AssignCurrentStep(ApprovalAssignee.ForRole(Escalation), "IneligibleAssigneeFallback", Now, "trace").Changed.ShouldBeFalse();
        var step = instance.CurrentStep!;
        step.AssigneeRole.ShouldBe(Escalation);
        step.DueAtUtc.ShouldBe(Now.AddHours(120));
        step.ReminderDueAtUtc.ShouldBe(Now.AddHours(72));
        instance.NextAction.ShouldBe(ApprovalNextAction.Wait);
        instance.NextTimer.ShouldBe((ApprovalTimerKind.Reminder, Now.AddHours(72)));
        instance.DomainEvents.OfType<ApprovalStepAssigned>().Single().AssignmentSequence.ShouldBe(1);
    }

    [Fact]
    public void T_APR_004_a_OnlyTheAssigneeHoldingDecide_DecidesTheCurrentStep()
    {
        var instance = Assigned(16_500m);

        Decide(instance, 1, Actor(head), ApprovalDecision.Approved).Error.ShouldBe(ApprovalErrors.NotAssignee);
        Decide(instance, 1, new ApprovalActor(manager, new HashSet<string>(), CanDecide: false), ApprovalDecision.Approved).Error.ShouldBe(ApprovalErrors.NotAssignee);
        Decide(instance, 2, Actor(head), ApprovalDecision.Approved).Error.ShouldBe(ApprovalErrors.StepNotCurrent);
        Decide(instance, 9, Actor(head), ApprovalDecision.Approved).Error!.Kind.ShouldBe(DomainErrorKind.NotFound);

        Decide(instance, 1, Actor(manager), ApprovalDecision.Approved).Result.ShouldBe(ApprovalDecisionResult.Recorded);
        instance.CurrentLevel.ShouldBe(2);
        instance.NextAction.ShouldBe(ApprovalNextAction.AssignStep);
    }

    [Fact]
    public void T_APR_004_a_AnyMemberOfTheAssignedGroup_Decides()
    {
        var instance = Start(5_000m);
        instance.AssignCurrentStep(ApprovalAssignee.ForRole(Escalation), "MissingAssigneeFallback", Now, "trace");
        var member = Guid.NewGuid();

        Decide(instance, 1, Actor(member, Escalation), ApprovalDecision.Approved).Result.ShouldBe(ApprovalDecisionResult.Recorded);
        instance.PendingOutcome.ShouldBe(ApprovalStatus.Approved);
        instance.NextAction.ShouldBe(ApprovalNextAction.Finalize);
    }

    [Fact]
    public void T_APR_005_a_TheRequester_CannotGiveTheFinalApproval()
    {
        var instance = Start(5_000m);
        instance.AssignCurrentStep(ApprovalAssignee.ForRole(Escalation), "IneligibleAssigneeFallback", Now, "trace");

        Decide(instance, 1, Actor(requester, Escalation), ApprovalDecision.Approved).Error.ShouldBe(ApprovalErrors.SelfApproval);
        instance.CurrentStep!.Status.ShouldBe(ApprovalStepStatus.Pending);
    }

    [Fact]
    public void T_APR_005_a_TheRequester_MayApproveANonFinalLevel()
    {
        var instance = Start(16_500m);
        instance.AssignCurrentStep(ApprovalAssignee.ForUser(requester), "CostCenterManager", Now, "trace").Changed.ShouldBeTrue();

        Decide(instance, 1, Actor(requester), ApprovalDecision.Approved).Result.ShouldBe(ApprovalDecisionResult.Recorded);
        instance.IsEligible(requester, 2).ShouldBeFalse();
    }

    [Fact]
    public void T_APR_007_a_TheSameUser_CannotDecideTwoLevels()
    {
        var instance = Assigned(16_500m);
        Decide(instance, 1, Actor(manager, Escalation), ApprovalDecision.Approved);
        instance.AssignCurrentStep(ApprovalAssignee.ForRole(Escalation), "MissingAssigneeFallback", Now, "trace");

        Decide(instance, 2, Actor(manager, Escalation), ApprovalDecision.Approved).Error.ShouldBe(ApprovalErrors.DuplicateApprover);
        Decide(instance, 2, Actor(manager, Escalation), ApprovalDecision.Rejected, Reason).Error.ShouldBe(ApprovalErrors.DuplicateApprover);
        instance.IsEligible(manager, 2).ShouldBeFalse();
    }

    [Fact]
    public void T_APR_008_a_TheSubmittingBuyer_CannotGiveTheFinalApproval()
    {
        var instance = Start(5_000m);
        instance.AssignCurrentStep(ApprovalAssignee.ForRole(Escalation), "MissingAssigneeFallback", Now, "trace");

        Decide(instance, 1, Actor(buyer, Escalation), ApprovalDecision.Approved).Error.ShouldBe(ApprovalErrors.MakerChecker);
    }

    [Fact]
    public void T_APR_009_a_Rejection_NeedsAReason_AndSettlesTheOutcomeOnce()
    {
        var instance = Assigned(16_500m);

        Decide(instance, 1, Actor(manager), ApprovalDecision.Rejected, "short").Error!.Code.ShouldBe(DomainError.ReasonRequiredCode);
        Decide(instance, 1, Actor(manager), ApprovalDecision.Rejected, Reason).Result.ShouldBe(ApprovalDecisionResult.Recorded);
        instance.NextAction.ShouldBe(ApprovalNextAction.Finalize);
        instance.ClearDomainEvents();

        instance.Finalize(Now, "trace").Changed.ShouldBeTrue();
        instance.Finalize(Now, "trace").Changed.ShouldBeFalse();
        instance.Status.ShouldBe(ApprovalStatus.Rejected);
        instance.EndReason.ShouldBe(Reason);
        instance.Steps.Single(step => step.Level == 2).Status.ShouldBe(ApprovalStepStatus.Cancelled);
        instance.DomainEvents.Single().ShouldBeOfType<ApprovalRejected>().Level.ShouldBe(1);
        instance.NextAction.ShouldBe(ApprovalNextAction.Completed);
    }

    [Fact]
    public void T_APR_013_a_FinalApproval_SettlesApproved_ThenFinalizeRaisesCompleted()
    {
        var instance = Assigned(16_500m);
        Decide(instance, 1, Actor(manager), ApprovalDecision.Approved);
        instance.AssignCurrentStep(ApprovalAssignee.ForUser(head), "DepartmentHead", Now, "trace");

        Decide(instance, 2, Actor(head), ApprovalDecision.Approved, "Looks fine").Result.ShouldBe(ApprovalDecisionResult.Recorded);
        instance.PendingOutcome.ShouldBe(ApprovalStatus.Approved);
        instance.Finalize(Now, "trace").Changed.ShouldBeTrue();

        instance.Status.ShouldBe(ApprovalStatus.Approved);
        instance.EndCause.ShouldBe(ApprovalInstance.CauseApproved);
        instance.DomainEvents.OfType<ApprovalCompleted>().Single().RequesterId.ShouldBe(requester);
        instance.History.Select(entry => entry.Action).ShouldContain(ApprovalInstance.ActionCompleted);
    }

    [Fact]
    public void T_APR_014_a_RepeatedDecision_IsIdempotent_OtherDecisionsConflict()
    {
        var instance = Assigned(5_000m);
        var first = Decide(instance, 1, Actor(manager), ApprovalDecision.Approved);
        first.Result.ShouldBe(ApprovalDecisionResult.Recorded);
        var version = instance.Version;

        Decide(instance, 1, Actor(manager), ApprovalDecision.Approved).Result.ShouldBe(ApprovalDecisionResult.Repeated);
        Decide(instance, 1, Actor(manager), ApprovalDecision.Rejected, Reason).Error.ShouldBe(ApprovalErrors.StepAlreadyDecided);
        Decide(instance, 1, Actor(Guid.NewGuid()), ApprovalDecision.Approved).Error.ShouldBe(ApprovalErrors.StepAlreadyDecided);
        instance.Version.ShouldBe(version);

        instance.Finalize(Now, "trace");
        var ended = Assigned(5_000m);
        ended.Invalidate(ApprovalInstance.CauseSupplierUnavailable, Now, "trace");
        Decide(ended, 1, Actor(manager), ApprovalDecision.Approved).Error!.Code.ShouldBe("approval.instance-ended");
    }

    [Fact]
    public void T_APR_010_a_Delegation_KeepsTheDueTime_AndRejectsInvalidTargets()
    {
        var instance = Assigned(16_500m);
        var due = instance.CurrentStep!.DueAtUtc;
        var delegateUser = Guid.NewGuid();

        instance.Delegate(1, Actor(manager), new ApprovalCandidate(delegateUser, true, true), "short", Now, "trace")!.Code.ShouldBe(DomainError.ReasonRequiredCode);
        instance.Delegate(1, Actor(manager), new ApprovalCandidate(manager, true, true), Reason, Now, "trace").ShouldBe(ApprovalErrors.InvalidDelegate);
        instance.Delegate(1, Actor(manager), new ApprovalCandidate(delegateUser, false, true), Reason, Now, "trace").ShouldBe(ApprovalErrors.InvalidDelegate);
        instance.Delegate(1, Actor(manager), new ApprovalCandidate(delegateUser, true, false), Reason, Now, "trace").ShouldBe(ApprovalErrors.InvalidDelegate);
        instance.Delegate(1, Actor(head), new ApprovalCandidate(delegateUser, true, true), Reason, Now, "trace").ShouldBe(ApprovalErrors.NotAssignee);

        instance.Delegate(1, Actor(manager), new ApprovalCandidate(delegateUser, true, true), Reason, Now.AddHours(1), "trace").ShouldBeNull();
        var step = instance.CurrentStep!;
        step.AssigneeUserId.ShouldBe(delegateUser);
        step.DueAtUtc.ShouldBe(due);
        step.AssignmentSequence.ShouldBe(2);
        var entry = instance.History.Last();
        entry.Action.ShouldBe(ApprovalInstance.ActionDelegated);
        entry.ActorId.ShouldBe(manager);
        entry.AssigneeUserId.ShouldBe(delegateUser);
        Decide(instance, 1, Actor(manager), ApprovalDecision.Approved).Error.ShouldBe(ApprovalErrors.NotAssignee);
    }

    [Fact]
    public void T_APR_010_a_Delegation_ToAnIneligibleUser_IsRejected()
    {
        var instance = Start(5_000m);
        instance.AssignCurrentStep(ApprovalAssignee.ForUser(manager), "CostCenterManager", Now, "trace");

        instance.Delegate(1, Actor(manager), new ApprovalCandidate(requester, true, true), Reason, Now, "trace").ShouldBe(ApprovalErrors.InvalidDelegate);
        instance.Delegate(1, Actor(manager), new ApprovalCandidate(buyer, true, true), Reason, Now, "trace").ShouldBe(ApprovalErrors.InvalidDelegate);
    }

    [Fact]
    public void T_APR_011_a_Reminder_Escalation_AndExpiry_FollowThePinnedDurations()
    {
        var instance = Assigned(5_000m);

        instance.RecordReminder(1, Now.AddHours(71), "trace").ShouldBeFalse();
        instance.RecordReminder(1, Now.AddHours(72), "trace").ShouldBeTrue();
        instance.RecordReminder(1, Now.AddHours(73), "trace").ShouldBeFalse();
        instance.NextTimer.ShouldBe((ApprovalTimerKind.Deadline, Now.AddHours(120)));

        instance.HandleDeadline(1, Now.AddHours(119), "trace").ShouldBe(ApprovalDeadlineOutcome.NotDue);
        instance.HandleDeadline(1, Now.AddHours(120), "trace").ShouldBe(ApprovalDeadlineOutcome.Escalated);
        var step = instance.CurrentStep!;
        step.AssigneeRole.ShouldBe(Escalation);
        step.AssigneeUserId.ShouldBeNull();
        step.EscalationCount.ShouldBe(1);
        step.DueAtUtc.ShouldBe(Now.AddHours(240));
        instance.NextTimer.ShouldBe((ApprovalTimerKind.Reminder, Now.AddHours(192)));
        instance.DomainEvents.OfType<ApprovalStepEscalated>().Single().EscalationCount.ShouldBe(1);
        instance.DomainEvents.OfType<ApprovalReminderDue>().Single().AssigneeUserId.ShouldBe(manager);

        instance.HandleDeadline(1, Now.AddHours(240), "trace").ShouldBe(ApprovalDeadlineOutcome.Expired);
        instance.NextAction.ShouldBe(ApprovalNextAction.Finalize);
        instance.HandleDeadline(1, Now.AddHours(300), "trace").ShouldBe(ApprovalDeadlineOutcome.NotDue);
        instance.Finalize(Now.AddHours(240), "trace").Changed.ShouldBeTrue();
        instance.Status.ShouldBe(ApprovalStatus.Expired);
        instance.DomainEvents.OfType<ApprovalExpired>().ShouldHaveSingleItem();
    }

    [Fact]
    public void T_APR_011_a_TimersOfAnotherLevel_OrADecidedStep_AreNoOps()
    {
        var instance = Assigned(16_500m);
        Decide(instance, 1, Actor(manager), ApprovalDecision.Approved);

        instance.RecordReminder(1, Now.AddHours(80), "trace").ShouldBeFalse();
        instance.HandleDeadline(1, Now.AddHours(500), "trace").ShouldBe(ApprovalDeadlineOutcome.NotDue);
        instance.HandleDeadline(2, Now.AddHours(500), "trace").ShouldBe(ApprovalDeadlineOutcome.NotDue);
    }

    [Fact]
    public void T_APR_012_a_Invalidation_EndsAPendingInstance_Once()
    {
        var instance = Assigned(16_500m);

        instance.Invalidate(ApprovalInstance.CauseSupplierUnavailable, Now, "trace").ShouldBeTrue();
        instance.Invalidate(ApprovalInstance.CauseSupplierUnavailable, Now, "trace").ShouldBeFalse();
        instance.Status.ShouldBe(ApprovalStatus.Invalidated);
        instance.Steps.ShouldAllBe(step => step.Status == ApprovalStepStatus.Cancelled);
        instance.DomainEvents.OfType<ApprovalInvalidated>().Single().Cause.ShouldBe(ApprovalInstance.CauseSupplierUnavailable);
    }

    [Fact]
    public void T_APR_015_a_AdministrativeChanges_NeedAReason_AndNeverDecide()
    {
        var instance = Assigned(16_500m);
        var administrator = Guid.NewGuid();

        instance.Reassign(1, administrator, ApprovalAssignee.ForRole(Finance), null, null, Now, "trace")!.Code.ShouldBe(DomainError.ReasonRequiredCode);
        instance.Reassign(1, administrator, ApprovalAssignee.ForUser(requester), new ApprovalCandidate(requester, true, true), Reason, Now, "trace").ShouldBeNull();
        instance.Reassign(1, administrator, ApprovalAssignee.ForUser(buyer), new ApprovalCandidate(buyer, true, false), Reason, Now, "trace")!.RuleId
            .ShouldBe("APR-015");
        instance.Reassign(1, administrator, ApprovalAssignee.ForRole(Finance), null, Reason, Now, "trace").ShouldBeNull();
        instance.CurrentStep!.AssigneeRole.ShouldBe(Finance);
        instance.History.Last().ActorId.ShouldBe(administrator);

        instance.Cancel(ApprovalInstance.CauseAdministrativeCancel, administrator, null, Now, "trace").Error!.Code.ShouldBe(DomainError.ReasonRequiredCode);
        instance.Cancel(ApprovalInstance.CauseAdministrativeCancel, administrator, Reason, Now, "trace").Changed.ShouldBeTrue();
        instance.Status.ShouldBe(ApprovalStatus.Cancelled);
        instance.Cancel(ApprovalInstance.CauseAdministrativeCancel, administrator, Reason, Now, "trace").Error!.Code.ShouldBe("approval.instance-ended");
        instance.Cancel(ApprovalInstance.CauseRequisitionCancelled, null, null, Now, "trace").Changed.ShouldBeFalse();
    }

    [Fact]
    public void EveryChange_IncrementsTheVersion_AndAppendsHistory()
    {
        var instance = Start(16_500m);
        var versions = new List<long> { instance.Version };
        instance.AssignCurrentStep(ApprovalAssignee.ForUser(manager), "CostCenterManager", Now, "trace");
        versions.Add(instance.Version);
        instance.RecordReminder(1, Now.AddHours(72), "trace");
        versions.Add(instance.Version);
        Decide(instance, 1, Actor(manager), ApprovalDecision.Approved);
        versions.Add(instance.Version);

        versions.ShouldBe(versions.Order().Distinct().ToList());
        instance.History.Select(entry => entry.Sequence).ShouldBe([1, 2, 3, 4]);
    }

    private ApprovalStartFacts Facts(decimal amount) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "REQ-2027-000001", requester, buyer, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        new Money(amount, "BRL"), new Money(amount == 0 ? 1m : amount, "BRL"));

    private ApprovalInstance Start(decimal amount) =>
        ApprovalInstance.Start(Guid.NewGuid(), Facts(amount), ApprovalPolicy.Version1(Finance, Escalation), "1.0", Now, "trace").Instance!;

    private ApprovalInstance Assigned(decimal amount)
    {
        var instance = Start(amount);
        instance.AssignCurrentStep(ApprovalAssignee.ForUser(manager), "CostCenterManager", Now, "trace").Changed.ShouldBeTrue();
        return instance;
    }

    private static ApprovalActor Actor(Guid user, params string[] roles) => new(user, roles.ToHashSet(StringComparer.Ordinal), CanDecide: true);

    private static (ApprovalDecisionResult? Result, DomainError? Error) Decide(
        ApprovalInstance instance, int level, ApprovalActor actor, ApprovalDecision decision, string? reason = null) =>
        instance.Decide(level, actor, decision, reason, Guid.NewGuid(), Now, "trace");

    private static (ApprovalPolicy? Policy, DomainError? Error) Create(
        IReadOnlyList<ApprovalPolicyLevelInput> levels, TimeSpan? reminder = null, DateTime? effectiveFrom = null) =>
        ApprovalPolicy.Create(Guid.NewGuid(), 2, effectiveFrom ?? Now, "BRL", levels, TimeSpan.FromHours(120), reminder ?? TimeSpan.FromHours(72), Escalation, 1,
            null, null, Now, Now);
}
