using FluentValidation.TestHelper;
using Shouldly;
using Template.Application.Common.Results;
using Template.Application.Features.Approvals;
using Template.Application.Features.Sourcing;
using Template.Application.Interfaces.Approvals;
using Template.Application.Messaging.Procurement;
using Template.Application.Security;
using Template.Application.Workflow;
using Template.Application.Wrappers;
using Template.Domain.Approvals;
using Template.Unit.Tests.ApplicationTests.Features.Reporting;
using IntegrationEnvelope = Template.Application.Messaging.Procurement.IntegrationEventEnvelope;

namespace Template.Unit.Tests.ApplicationTests.Features.Procurement;

/// <summary>
/// Failure, precondition and administration paths of the approval use cases and workflow activities against port fakes
/// (validators, missing actor, stale version, workflow outage, lost commit races, reassignment, queries, policy
/// versions). The happy journeys are in <see cref="ApprovalHandlerTests"/>; all data is synthetic.
/// </summary>
public sealed class ApprovalHandlerEdgeCaseTests
{
    private const string Reason = "Synthetic reason for the approval edge case";
    private readonly ReportingTestDoubles reporting = new();

    private ProcurementTestDoubles Doubles => reporting.Procurement;

    private ProcurementTestDoubles.Actor Manager { get; } = new();
    private ProcurementTestDoubles.Actor Head { get; } = new();
    private ProcurementTestDoubles.Actor Administrator { get; } = new();

    [Fact]
    public void Validators_AcceptValidCommands_AndRejectInvalidOnes()
    {
        var id = Guid.NewGuid();
        new DecideApprovalStepCommandValidator().TestValidate(new DecideApprovalStepCommand(id, 1, 1, "Approved", null)).IsValid.ShouldBeTrue();
        var decision = new DecideApprovalStepCommandValidator().TestValidate(new DecideApprovalStepCommand(id, 0, 1, "Maybe", new string('x', 600)));
        decision.ShouldHaveValidationErrorFor(command => command.Level);
        decision.ShouldHaveValidationErrorFor(command => command.Decision);
        decision.ShouldHaveValidationErrorFor(command => command.Reason);

        new DelegateApprovalStepCommandValidator().TestValidate(new DelegateApprovalStepCommand(id, 1, 1, Guid.NewGuid(), Reason)).IsValid.ShouldBeTrue();
        new DelegateApprovalStepCommandValidator().TestValidate(new DelegateApprovalStepCommand(id, 9, 1, Guid.Empty, Reason))
            .ShouldHaveValidationErrorFor(command => command.DelegateUserId);

        var reassign = new ReassignApprovalStepCommandValidator();
        reassign.TestValidate(new ReassignApprovalStepCommand(id, 1, 1, null, SeededRoles.FinanceApprover, Reason)).IsValid.ShouldBeTrue();
        reassign.TestValidate(new ReassignApprovalStepCommand(id, 1, 1, Guid.NewGuid(), null, Reason)).IsValid.ShouldBeTrue();
        reassign.TestValidate(new ReassignApprovalStepCommand(id, 1, 1, Guid.NewGuid(), SeededRoles.FinanceApprover, Reason)).IsValid.ShouldBeFalse();
        reassign.TestValidate(new ReassignApprovalStepCommand(id, 1, 1, Guid.Empty, null, Reason)).IsValid.ShouldBeFalse();

        var policy = new CreateApprovalPolicyVersionCommandValidator();
        policy.TestValidate(PolicyCommand()).IsValid.ShouldBeTrue();
        var invalidPolicy = policy.TestValidate(new CreateApprovalPolicyVersionCommand(null, [], null, null, "", null, new string('x', 600)));
        invalidPolicy.ShouldHaveValidationErrorFor(command => command.EffectiveFrom);
        invalidPolicy.ShouldHaveValidationErrorFor(command => command.Levels);
        invalidPolicy.ShouldHaveValidationErrorFor(command => command.EscalationRole);
        invalidPolicy.ShouldHaveValidationErrorFor(command => command.MaxEscalations);
        ApprovalDecisionHandlers.AssignableRoleGroups.ShouldContain(SeededRoles.FinanceApprover);
        ApprovalDecisionHandlers.AssignableRoleGroups.ShouldNotContain(SeededRoles.Requester);
    }

    [Fact]
    public async Task Decision_AnswersNotFound_Stale_AndUnavailable_WithoutChangingTheStep()
    {
        var instance = await AssignedInstanceAsync();
        var decisions = Doubles.ApprovalDecisions();

        (await decisions.Handle(new DecideApprovalStepCommand(Guid.NewGuid(), 1, 1, "Approved", null), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
        // Without an actor the instance is not visible at all (no existence disclosure).
        (await decisions.Handle(new DecideApprovalStepCommand(instance.Id, 1, instance.Version, "Approved", null), default)).Error!.Category
            .ShouldBe(ErrorCategory.NotFound);

        Doubles.ActAs(Manager);
        (await Doubles.ApprovalDecisions().Handle(new DecideApprovalStepCommand(instance.Id, 1, instance.Version + 7, "Approved", null), default)).Error!
            .Category.ShouldBe(ErrorCategory.PreconditionFailed);
        Doubles.Workflow.Unavailable = true;
        var unavailable = await Doubles.ApprovalDecisions().Handle(new DecideApprovalStepCommand(instance.Id, 1, instance.Version, "Approved", null), default);
        unavailable.Error!.Code.ShouldBe(ProblemCodes.ApprovalWorkflowUnavailable);
        unavailable.Error.Category.ShouldBe(ErrorCategory.Unavailable);
        instance.CurrentStep!.Decision.ShouldBeNull();
    }

    [Fact]
    public async Task Decision_LosingTheCommitRace_ReReadsOnce_AndAnswersTheDecidedStep_WithoutSignalling()
    {
        var instance = await AssignedInstanceAsync();
        Doubles.ActAs(Manager);
        Doubles.UnitOfWork.ThrowConflict = true;

        var result = await Doubles.ApprovalDecisions().Handle(new DecideApprovalStepCommand(instance.Id, 1, instance.Version, "Approved", null), default);

        // The fake keeps the in-memory change, so the re-read sees the step decided by "the winner" and answers APR-014.
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        Doubles.Workflow.Signals.ShouldBeEmpty();
    }

    [Fact]
    public async Task Delegation_ToAnUnknownUser_IsRejected_AndChangesWithoutAnActorSeeNothing()
    {
        var instance = await AssignedInstanceAsync();
        var decisions = Doubles.ApprovalDecisions();
        (await decisions.Handle(new DelegateApprovalStepCommand(instance.Id, 1, instance.Version, Guid.NewGuid(), Reason), default)).Error!.Category
            .ShouldBe(ErrorCategory.NotFound);
        (await decisions.Handle(new ReassignApprovalStepCommand(instance.Id, 1, instance.Version, null, SeededRoles.FinanceApprover, Reason), default)).Error!
            .Category.ShouldBe(ErrorCategory.NotFound);
        (await decisions.Handle(new CancelApprovalCommand(instance.Id, instance.Version, Reason), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
        (await decisions.Handle(new CancelApprovalCommand(Guid.NewGuid(), 1, Reason), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);

        Doubles.ActAs(Manager);
        var unknown = await Doubles.ApprovalDecisions().Handle(new DelegateApprovalStepCommand(instance.Id, 1, instance.Version, Guid.NewGuid(), Reason), default);
        unknown.Error!.Code.ShouldBe(ProblemCodes.ApprovalInvalidDelegate);
        (await Doubles.ApprovalDecisions().Handle(new DelegateApprovalStepCommand(instance.Id, 1, 999, Head.Id, Reason), default)).Error!.Category
            .ShouldBe(ErrorCategory.PreconditionFailed);
        instance.CurrentStep!.AssigneeUserId.ShouldBe(Manager.Id);
    }

    [Fact]
    public async Task T_APR_015_b_Reassignment_ToAUserOrAnApproverGroup_IsAudited_AndOtherGroupsAreRejected()
    {
        var instance = await AssignedInstanceAsync();
        Doubles.Permissions.Grant(Administrator.Id, SeededRoles.PermissionsByRole[SeededRoles.ProcurementManager]);
        Doubles.ActAs(Administrator);

        var notApprover = await Doubles.ApprovalDecisions().Handle(
            new ReassignApprovalStepCommand(instance.Id, 1, instance.Version, null, SeededRoles.Requester, Reason), default);
        notApprover.Error!.Code.ShouldBe(ProblemCodes.ApprovalInvalidDelegate);
        notApprover.Error.RuleId.ShouldBe("APR-015");

        var toUser = await Doubles.ApprovalDecisions().Handle(new ReassignApprovalStepCommand(instance.Id, 1, instance.Version, Head.Id, null, Reason), default);
        toUser.IsSuccess.ShouldBeTrue(toUser.Error?.Message);
        instance.CurrentStep!.AssigneeUserId.ShouldBe(Head.Id);
        var toGroup = await Doubles.ApprovalDecisions().Handle(
            new ReassignApprovalStepCommand(instance.Id, 1, instance.Version, null, $" {SeededRoles.FinanceApprover} ", Reason), default);
        toGroup.IsSuccess.ShouldBeTrue(toGroup.Error?.Message);
        instance.CurrentStep!.AssigneeRole.ShouldBe(SeededRoles.FinanceApprover);
        Doubles.Audit.Records.Count(record => record.Action == ApprovalAudit.Administered).ShouldBe(2);
        Doubles.Workflow.Signals.Select(signal => signal.Signal.Kind).ShouldBe([ApprovalSignalKinds.Reassigned, ApprovalSignalKinds.Reassigned]);
    }

    [Fact]
    public async Task Queries_ValidateTheirInput_AndReportTheWorkflowRuntime()
    {
        var instance = await AssignedInstanceAsync();
        var queries = Doubles.ApprovalQueries();
        (await queries.Handle(new ListApprovalInstancesQuery(1, 20, null, null, null, null, null), default)).Error!.Category.ShouldBe(ErrorCategory.Forbidden);

        Doubles.ActAs(Manager);
        (await Doubles.ApprovalQueries().Handle(new ListApprovalInstancesQuery(1, 20, "unknownField", null, null, null, null), default)).Error!.Category
            .ShouldBe(ErrorCategory.Validation);
        (await Doubles.ApprovalQueries().Handle(new ListApprovalInstancesQuery(1, 20, null, "Sleeping", null, null, null), default)).Error!.Category
            .ShouldBe(ErrorCategory.Validation);
        (await Doubles.ApprovalQueries().Handle(new ListApprovalInstancesQuery(1, 20, null, null, "others", null, null), default)).Error!.Category
            .ShouldBe(ErrorCategory.Validation);
        var participant = await Doubles.ApprovalQueries().Handle(new ListApprovalInstancesQuery(1, 20, null, "Pending", null, instance.RequisitionId, false), default);
        participant.IsSuccess.ShouldBeTrue();
        Doubles.Approvals.LastFilter!.ParticipantUserId.ShouldBe(Manager.Id);
        Doubles.Approvals.LastFilter.Escalated.ShouldBe(false);

        (await Doubles.ApprovalQueries().Handle(new GetApprovalHistoryQuery(Guid.NewGuid()), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
        var history = await Doubles.ApprovalQueries().Handle(new GetApprovalHistoryQuery(instance.Id), default);
        history.Value.Entries.Select(entry => entry.Action).ShouldBe([ApprovalInstance.ActionStarted, ApprovalInstance.ActionAssigned]);
        history.Value.Entries[1].AssigneeUserId.ShouldBe(Manager.Id);
        (await Doubles.ApprovalQueries().Handle(new SearchApprovalDelegatesQuery(Guid.NewGuid(), 1, null, 1, 20), default)).Error!.Category
            .ShouldBe(ErrorCategory.NotFound);
        (await Doubles.ApprovalQueries().Handle(new SearchApprovalDelegatesQuery(instance.Id, 1, new string('q', 300), 1, 20), default)).Error!.Category
            .ShouldBe(ErrorCategory.Validation);
        (await Doubles.ApprovalQueries().Handle(new SearchApprovalDelegatesQuery(instance.Id, 1, null, 0, 20), default)).Error!.Category
            .ShouldBe(ErrorCategory.Validation);

        (await Doubles.ApprovalQueries().Handle(new GetApprovalWorkflowQuery(Guid.NewGuid()), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
        var runtime = await Doubles.ApprovalQueries().Handle(new GetApprovalWorkflowQuery(instance.Id), default);
        (runtime.Value.RuntimeStatus, runtime.Value.DatabaseStatus, runtime.Value.NextAction).ShouldBe(("Running", "Pending", "Wait"));
        runtime.Value.WorkflowInstanceId.ShouldBe(ApprovalWorkflowNames.InstanceIdFor(instance.AwardId));
        Doubles.Workflow.Unavailable = true;
        (await Doubles.ApprovalQueries().Handle(new GetApprovalWorkflowQuery(instance.Id), default)).Error!.Code.ShouldBe(ProblemCodes.ApprovalWorkflowUnavailable);
    }

    [Fact]
    public async Task T_APR_017_b_PolicyVersions_NeedPermission_ValidateTheirLevels_AndReportAConcurrentVersion()
    {
        var handlers = new ApprovalPolicyHandlers(Doubles.Policies, Doubles.NewWriter());
        Doubles.ActAs(Manager);
        (await handlers.Handle(new ListApprovalPoliciesQuery(), default)).Error!.Category.ShouldBe(ErrorCategory.Forbidden);
        (await handlers.Handle(new GetApprovalPolicyQuery(1), default)).Error!.Category.ShouldBe(ErrorCategory.Forbidden);

        Doubles.Permissions.Grant(Administrator.Id, SeededRoles.PermissionsByRole[SeededRoles.ProcurementManager]);
        Doubles.ActAs(Administrator);
        handlers = new ApprovalPolicyHandlers(Doubles.Policies, Doubles.NewWriter());
        (await handlers.Handle(new ListApprovalPoliciesQuery(), default)).Value.Select(policy => policy.Version).ShouldBe([1]);
        (await handlers.Handle(new GetApprovalPolicyQuery(1), default)).Value.Levels.Count.ShouldBe(3);
        (await handlers.Handle(new GetApprovalPolicyQuery(42), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);

        var levels = PolicyCommand().Levels!;
        (await handlers.Handle(PolicyCommand() with { Levels = [levels[0] with { Resolver = "Manager" }] }, default)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await handlers.Handle(PolicyCommand() with { Levels = [levels[0] with { Resolver = "0" }] }, default)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await handlers.Handle(PolicyCommand() with { Levels = [levels[0], levels[1] with { Role = SeededRoles.Requester }] }, default)).Error!.Category
            .ShouldBe(ErrorCategory.Validation);
        (await handlers.Handle(PolicyCommand() with { EscalationRole = SeededRoles.Auditor }, default)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await handlers.Handle(PolicyCommand() with { ReminderAfterHours = 5000 }, default)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await handlers.Handle(PolicyCommand() with { EffectiveFrom = ProcurementTestDoubles.Now.AddDays(-1) }, default)).Error!.RuleId.ShouldBe("APR-017");

        Doubles.UnitOfWork.UniqueViolation = IApprovalPolicyRepository.VersionUniqueKey;
        (await handlers.Handle(PolicyCommand(), default)).Error!.Code.ShouldBe(ProblemCodes.PolicyVersionInUse);
        Doubles.UnitOfWork.UniqueViolation = null;
        var created = await new ApprovalPolicyHandlers(Doubles.Policies, Doubles.NewWriter()).Handle(PolicyCommand(), default);
        created.IsSuccess.ShouldBeTrue(created.Error?.Message);
        created.Value.Version.ShouldBe(3);
        Doubles.Audit.Records.Last().Action.ShouldBe(ApprovalAudit.PolicyVersionCreated);
    }

    [Fact]
    public async Task Activities_RejectUnsupportedInput_AndTreatUnknownOrSettledAwardsAsNoOps()
    {
        var activities = Doubles.ApprovalActivities();
        await Should.ThrowAsync<NotSupportedException>(() => activities.StartAsync(
            new ApprovalStartRequest(new ApprovalWorkflowInput(2, Guid.NewGuid(), Guid.NewGuid(), ProcurementTestDoubles.Now, "trace"), "1.0"), default));
        await Should.ThrowAsync<NotSupportedException>(() => activities.StartAsync(
            new ApprovalStartRequest(new ApprovalWorkflowInput(ApprovalWorkflowInput.Version1, Guid.Empty, Guid.NewGuid(), ProcurementTestDoubles.Now, "trace"), "1.0"),
            default));
        var unknown = Guid.NewGuid();
        (await activities.AssignStepAsync(Request(unknown), default)).Status.ShouldBe(ApprovalProgress.NotStarted);
        (await activities.FinalizeAsync(Request(unknown), default)).Status.ShouldBe(ApprovalProgress.NotStarted);
        (await activities.GetProgressAsync(Request(unknown), default)).Next.ShouldBe(ApprovalWorkflowStep.Completed);

        var instance = await AssignedInstanceAsync();
        var waiting = await Doubles.ApprovalActivities().AssignStepAsync(Request(instance.AwardId), default);
        waiting.Next.ShouldBe(ApprovalWorkflowStep.Wait);
        (await Doubles.ApprovalActivities().FinalizeAsync(Request(instance.AwardId), default)).Next.ShouldBe(ApprovalWorkflowStep.Wait);
        var early = await Doubles.ApprovalActivities().ProcessTimerAsync(
            new ApprovalTimerRequest(instance.AwardId, 1, ApprovalWorkflowTimer.Deadline, ProcurementTestDoubles.Now, "trace"), default);
        early.Next.ShouldBe(ApprovalWorkflowStep.Wait);
        instance.CurrentStep!.EscalationCount.ShouldBe(0);
    }

    [Fact]
    public async Task Activities_AnswerConcurrentStartsAndAssignments_FromTheWinnersState()
    {
        var (_, award) = await AwardedAsync();
        // A concurrent start won the award's unique key: the retry reads the winner's instance instead of creating one.
        Doubles.UnitOfWork.UniqueViolation = IApprovalInstanceRepository.AwardUniqueKey;
        var concurrent = await Doubles.ApprovalActivities().StartAsync(Input(award), default);
        concurrent.InstanceId.ShouldBe(Doubles.Approvals.Items.Single().Id);
        Doubles.UnitOfWork.UniqueViolation = null;
        Doubles.UnitOfWork.ThrowConflict = true;
        (await Doubles.ApprovalActivities().AssignStepAsync(Request(award), default)).Next.ShouldBe(ApprovalWorkflowStep.Wait);
        Doubles.UnitOfWork.ThrowConflict = false;
    }

    [Fact]
    public async Task T_APR_003_b_ACostCenterWithoutManager_ResolvesToTheEscalationGroup()
    {
        var (_, award) = await AwardedAsync();
        Doubles.CostCenter.Update("Operations", null, ProcurementTestDoubles.Now).ShouldBeNull();
        var activities = Doubles.ApprovalActivities();
        await activities.StartAsync(Input(award), default);

        await activities.AssignStepAsync(Request(award), default);

        Doubles.Approvals.Items.Single().CurrentStep!.AssigneeRole.ShouldBe(SeededRoles.ProcurementManager);
        Doubles.Audit.Records.Last(record => record.Action == ApprovalAudit.StepAssigned).Reason.ShouldBe("MissingAssigneeFallback");
    }

    [Fact]
    public async Task T_APR_016_c_ASignalLostAfterTheCommit_IsLogged_AndTheDecisionStands()
    {
        var instance = await AssignedInstanceAsync();
        Doubles.Workflow.SignalsFail = true;
        Doubles.ActAs(Manager);

        var decided = await Doubles.ApprovalDecisions().Handle(new DecideApprovalStepCommand(instance.Id, 1, instance.Version, "Approved", null), default);

        decided.IsSuccess.ShouldBeTrue(decided.Error?.Message);
        instance.Steps.Single(step => step.Level == 1).Decision.ShouldBe(ApprovalDecision.Approved);
        Doubles.Workflow.Signals.ShouldBeEmpty();
    }

    [Fact]
    public async Task DelegationAndCancellation_ThatLoseTheCommitRace_ReturnThePreconditionFailure()
    {
        var instance = await AssignedInstanceAsync();
        Doubles.ActAs(Manager);
        Doubles.UnitOfWork.ThrowConflict = true;
        (await Doubles.ApprovalDecisions().Handle(new DelegateApprovalStepCommand(instance.Id, 1, instance.Version, Head.Id, Reason), default)).Error!.Category
            .ShouldBe(ErrorCategory.PreconditionFailed);
        Doubles.UnitOfWork.ThrowConflict = false;

        var other = await AssignedInstanceInNewScenarioAsync();
        Doubles.Permissions.Grant(Administrator.Id, SeededRoles.PermissionsByRole[SeededRoles.ProcurementManager]);
        other.Doubles.Permissions.Grant(Administrator.Id, SeededRoles.PermissionsByRole[SeededRoles.ProcurementManager]);
        other.Doubles.ActAs(Administrator);
        other.Doubles.UnitOfWork.ThrowConflict = true;
        (await other.Doubles.ApprovalDecisions().Handle(new CancelApprovalCommand(other.Instance.Id, other.Instance.Version, Reason), default)).Error!.Category
            .ShouldBe(ErrorCategory.PreconditionFailed);
        other.Doubles.Workflow.Signals.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reassignment_OfAStepThatIsNotCurrent_IsRejected_AndEndedInstancesChangeNothing()
    {
        var instance = await AssignedInstanceAsync();
        Doubles.Approvers.AddApprover(Doubles.Requester.Id, SeededRoles.CostCenterManager);
        Doubles.Permissions.Grant(Administrator.Id, SeededRoles.PermissionsByRole[SeededRoles.ProcurementManager]);
        Doubles.ActAs(Administrator);

        var notCurrent = await Doubles.ApprovalDecisions().Handle(new ReassignApprovalStepCommand(instance.Id, 2, instance.Version, Head.Id, null, Reason), default);
        notCurrent.IsSuccess.ShouldBeFalse();
        instance.Steps.Single(step => step.Level == 2).AssigneeUserId.ShouldBeNull();

        (await Doubles.ApprovalDecisions().Handle(new CancelApprovalCommand(instance.Id, instance.Version, Reason), default)).IsSuccess.ShouldBeTrue();
        var again = await Doubles.ApprovalDecisions().Handle(new CancelApprovalCommand(instance.Id, instance.Version, Reason), default);
        again.Error!.Category.ShouldBe(ErrorCategory.Conflict);
        Doubles.ApprovalOutcomes(Doubles.NewWriter()).CancelForRequisition(instance).ShouldBeEmpty();
        (await Doubles.ApprovalOutcomes(Doubles.NewWriter()).InvalidateAsync(instance, ApprovalInstance.CauseSupplierUnavailable, default)).ShouldBeEmpty();
        (await Doubles.ApprovalCascade().CancelForRequisitionAsync(instance.AwardId, default)).ShouldBe(ApprovalCascadeResult.None);
        (await Doubles.ApprovalCascade().CancelForRequisitionAsync(null, default)).ShouldBe(ApprovalCascadeResult.None);
        (await Doubles.ApprovalCascade().InvalidateForSupplierAsync(Guid.NewGuid(), default)).ShouldBe(ApprovalCascadeResult.None);
    }

    [Fact]
    public async Task T_APR_016_d_Reconciliation_ReportsWithoutRepairing_AndNamesEveryStartResult()
    {
        var (_, award) = await AwardedAsync();
        Doubles.Clock.UtcNow = Doubles.Clock.UtcNow.AddMinutes(5);
        Doubles.Workflow.RuntimeStatus = ApprovalWorkflowRuntimeStatus.Running;
        (await Doubles.ApprovalReconciliation().RunAsync(repair: true, 100, default)).Items.ShouldBeEmpty();

        Doubles.Workflow.RuntimeStatus = ApprovalWorkflowRuntimeStatus.Completed;
        var report = await Doubles.ApprovalReconciliation().RunAsync(repair: false, 0, default);
        report.Items.Single().ShouldBe(new ApprovalReconciliationItemView(award, null, "AwardPendingApproval", "Completed", "missing-workflow", "none"));
        Doubles.Workflow.Starts.ShouldBeEmpty();

        await Doubles.ApprovalActivities().StartAsync(Input(award), default);
        Doubles.Workflow.RuntimeStatus = ApprovalWorkflowRuntimeStatus.Failed;
        Doubles.Workflow.StartResult = ApprovalWorkflowStartResult.Restarted;
        (await Doubles.ApprovalReconciliation().RunAsync(repair: true, 100, default)).Items.Single()
            .ShouldBe(new ApprovalReconciliationItemView(award, Doubles.Approvals.Items.Single().Id, "Pending", "Failed", "workflow-not-running", "restarted"));
        Doubles.Workflow.StartResult = ApprovalWorkflowStartResult.AlreadyRunning;
        (await Doubles.ApprovalReconciliation().RunAsync(repair: true, 100, default)).Items.Single().Repair.ShouldBe("already-running");
        (await Doubles.ApprovalReconciliation().RunAsync(repair: false, 100, default)).Items.Single().Repair.ShouldBe("none");

        Doubles.Workflow.RuntimeStatus = ApprovalWorkflowRuntimeStatus.Running;
        Doubles.Workflow.SignalResult = ApprovalWorkflowSignalResult.NotRunning;
        Doubles.Clock.UtcNow = Doubles.Clock.UtcNow.AddMinutes(5);
        (await Doubles.ApprovalReconciliation().RunAsync(repair: true, 100, default)).Items.Single().Repair.ShouldBe("not-running");
        (await Doubles.ApprovalReconciliation().RunAsync(repair: false, 100, default)).Items.Single().Repair.ShouldBe("none");
    }

    [Fact]
    public async Task Cascade_CompletesEvenWhenTheEndSignalIsLost()
    {
        var instance = await AssignedInstanceAsync();
        var cascade = Doubles.ApprovalCascade();
        var result = await cascade.CancelForRequisitionAsync(instance.AwardId, default);
        result.AwardIds.ShouldBe([instance.AwardId]);
        Doubles.Workflow.SignalsFail = true;

        await cascade.CompleteAsync(result, default);

        instance.Status.ShouldBe(ApprovalStatus.Cancelled);
        Doubles.Workflow.Signals.ShouldBeEmpty();
    }

    [Fact]
    public async Task Finalize_FailsLoudly_WhenTheAwardLeftApprovalOutsideTheWorkflow()
    {
        var instance = await AssignedInstanceAsync();
        Doubles.ActAs(Manager);
        (await Doubles.ApprovalDecisions().Handle(new DecideApprovalStepCommand(instance.Id, 1, instance.Version, "Approved", null), default)).IsSuccess.ShouldBeTrue();
        await Doubles.ApprovalActivities().AssignStepAsync(Request(instance.AwardId), default);
        Doubles.ActAs(Head);
        (await Doubles.ApprovalDecisions().Handle(new DecideApprovalStepCommand(instance.Id, 2, instance.Version, "Approved", null), default)).IsSuccess.ShouldBeTrue();
        var rfq = Doubles.Rfqs.Items.Single();
        rfq.EndPendingAward(instance.AwardId, Template.Domain.Sourcing.AwardStatus.Withdrawn, reopen: true, "Synthetic", ProcurementTestDoubles.Now, "trace",
            "synthetic").ShouldBeTrue();
        Doubles.ActAsSystem();

        await Should.ThrowAsync<InvalidOperationException>(() => Doubles.ApprovalActivities().FinalizeAsync(Request(instance.AwardId), default));
        Doubles.Allocation.CommittedAmount.ShouldBe(0m);
    }

    [Fact]
    public async Task T_APR_003_c_ARoleGroupLevel_IsAssignedToItsGroup()
    {
        var (policy, error) = ApprovalPolicy.Create(
            Guid.NewGuid(), 2, ProcurementTestDoubles.Now.AddDays(-30), "BRL",
            [new ApprovalPolicyLevelInput(1, 0m, ApproverResolver.RoleGroup, SeededRoles.FinanceApprover)],
            TimeSpan.FromHours(120), TimeSpan.FromHours(72), SeededRoles.ProcurementManager, 1, "Synthetic role-group policy", null,
            ProcurementTestDoubles.Now.AddDays(-30), ProcurementTestDoubles.Now.AddDays(-30));
        error.ShouldBeNull();
        Doubles.Policies.Add(policy!);
        var (_, award) = await AwardedAsync();
        var activities = Doubles.ApprovalActivities();
        await activities.StartAsync(Input(award), default);

        await activities.AssignStepAsync(Request(award), default);

        var step = Doubles.Approvals.Items.Single().CurrentStep!;
        (step.Level, step.AssigneeRole, step.AssigneeUserId).ShouldBe((1, SeededRoles.FinanceApprover, (Guid?)null));
        Doubles.Audit.Records.Last(record => record.Action == ApprovalAudit.StepAssigned).Reason.ShouldBe("RoleGroup");
    }

    [Fact]
    public async Task Starter_RejectsAnIncompletePayload_AndReportsADuplicateStart()
    {
        var (_, _) = await AwardedAsync();
        var envelope = Doubles.Outbox.Events.Single(item => item.EventType == "procurement.award.submitted");
        var starter = new ApprovalWorkflowStarter(Doubles.Workflow, Microsoft.Extensions.Logging.Abstractions.NullLogger<ApprovalWorkflowStarter>.Instance);
        Doubles.Workflow.StartResult = ApprovalWorkflowStartResult.AlreadyRunning;

        (await starter.HandleAsync(envelope, default)).ShouldBe(Template.Application.Interfaces.Messaging.InboxApplyResult.Duplicate);
        var empty = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone();
        await Should.ThrowAsync<System.Text.Json.JsonException>(() => starter.HandleAsync(envelope with { Payload = empty }, default));
    }

    [Fact]
    public async Task InstanceView_WithoutItsRfq_HasNoAwardSummary()
    {
        var instance = await AssignedInstanceAsync();
        Doubles.Rfqs.Items.Clear();
        Doubles.Permissions.Grant(Administrator.Id, [Permissions.ApprovalsReadAll]);
        Doubles.ActAs(Administrator);

        var view = await Doubles.ApprovalQueries().Handle(new GetApprovalInstanceQuery(instance.Id), default);

        view.Value.Award.ShouldBeNull();
        view.Value.AllowedActions.ShouldBeEmpty();
        view.Value.Steps[0].AssigneeName.ShouldNotBeNull();
    }

    [Fact]
    public void ApprovalNotifications_ChooseTheTemplateAndRecipient_AndRejectIncompletePayloads()
    {
        var instanceId = Guid.NewGuid();
        var requester = Guid.NewGuid();
        var assignee = Guid.NewGuid();
        var envelope = ProcurementIntegrationEventContractFrom(
            new ApprovalStepEscalated(ProcurementTestDoubles.Now, instanceId, 1, 1, SeededRoles.ProcurementManager, ProcurementTestDoubles.Now));
        IntegrationEnvelope With(string type, string json) =>
            envelope with { EventType = type, Payload = System.Text.Json.JsonDocument.Parse(json).RootElement.Clone() };

        var cases = new (string Type, string Template)[]
        {
            ("procurement.approval.step-assigned", RfqNotificationPlanner.ApprovalAssignedTemplate),
            ("procurement.approval.reminder-due", RfqNotificationPlanner.ApprovalReminderTemplate),
            ("procurement.approval.step-escalated", RfqNotificationPlanner.ApprovalEscalatedTemplate)
        };
        foreach (var (type, template) in cases)
        {
            var request = RfqNotificationPlanner.Plan(With(type, $$"""{"assigneeUserId":"{{assignee}}"}""")).Single();
            (request.RecipientType, request.RecipientId, request.Template).ShouldBe((RfqNotificationPlanner.UserRecipient, assignee, template));
        }

        RfqNotificationPlanner.Plan(With("procurement.approval.rejected", $$"""{"requesterId":"{{requester}}"}""")).Single().Template
            .ShouldBe(RfqNotificationPlanner.ApprovalRejectedTemplate);
        RfqNotificationPlanner.Plan(With("procurement.approval.expired", $$"""{"requesterId":"{{requester}}"}""")).Single().RecipientId.ShouldBe(requester);
        RfqNotificationPlanner.Plan(With("procurement.approval.step-assigned", """{"assigneeRole":"FinanceApprover"}""")).Single().RecipientId
            .ShouldBe(RfqNotificationPlanner.RoleGroupRecipientId);
        var supplier = Guid.NewGuid();
        var extended = RfqNotificationPlanner.Plan(With("procurement.rfq.deadline-extended", $$"""{"invitedSupplierIds":["{{supplier}}","{{supplier}}"]}"""));
        (extended.Single().Template, extended.Single().RecipientId).ShouldBe((RfqNotificationPlanner.DeadlineExtendedTemplate, supplier));
        RfqNotificationPlanner.Plan(With("procurement.rfq.reminder-requested", $$"""{"supplierIds":["{{supplier}}"]}""")).Single().Template
            .ShouldBe(RfqNotificationPlanner.ReminderTemplate);
        Should.Throw<System.Text.Json.JsonException>(() => RfqNotificationPlanner.Plan(With("procurement.rfq.closed", "{}")));
        Should.Throw<System.Text.Json.JsonException>(() => RfqNotificationPlanner.Plan(With("procurement.approval.started", "{}")));
        Should.Throw<System.Text.Json.JsonException>(() => RfqNotificationPlanner.Plan(With("procurement.approval.step-assigned", """{"assigneeRole":""}""")));
        Should.Throw<System.Text.Json.JsonException>(() => RfqNotificationPlanner.Plan(With("procurement.report.ready", """{"requestedBy":1}""")));
        Should.Throw<System.Text.Json.JsonException>(() => RfqNotificationPlanner.Plan(With("procurement.rfq.published", """{"invitedSupplierIds":"x"}""")));
    }

    [Fact]
    public async Task Outcomes_FinalizeNothingTwice_AndFailWhenTheRequisitionOrReservationIsGone()
    {
        var instance = await AssignedInstanceAsync();
        var outcomes = Doubles.ApprovalOutcomes(Doubles.NewWriter());
        // A waiting instance has no settled outcome: nothing is staged and the reservation is untouched.
        var (waiting, _) = await outcomes.FinalizeAsync(instance, default);
        waiting.ShouldBeEmpty();
        Doubles.Allocation.CommittedAmount.ShouldBe(0m);

        Doubles.ActAs(Manager);
        (await Doubles.ApprovalDecisions().Handle(new DecideApprovalStepCommand(instance.Id, 1, instance.Version, "Rejected", Reason), default)).IsSuccess.ShouldBeTrue();
        Doubles.ActAsSystem();
        var requisition = Doubles.Requisitions.Items.Single();
        requisition.EndApproval(instance.AwardId, Template.Domain.Requisitions.RequisitionStatus.Rejected, Reason, ProcurementTestDoubles.Now, "trace", "synthetic");
        var (events, error) = await Doubles.ApprovalOutcomes(Doubles.NewWriter()).FinalizeAsync(instance, default);
        error.ShouldBeNull();
        events.ShouldNotBeEmpty();
        Doubles.Audit.Records.ShouldNotContain(record => record.Action == "requisition.rejected");
        var (again, againError) = await Doubles.ApprovalOutcomes(Doubles.NewWriter()).FinalizeAsync(instance, default);
        (again.Count, againError).ShouldBe((0, (ApplicationError?)null));

        var approved = await AssignedInstanceInNewScenarioAsync();
        var other = approved.Doubles;
        other.ActAs(approved.Manager);
        (await other.ApprovalDecisions().Handle(new DecideApprovalStepCommand(approved.Instance.Id, 1, approved.Instance.Version, "Approved", null), default))
            .IsSuccess.ShouldBeTrue();
        await other.ApprovalActivities().AssignStepAsync(Request(approved.Instance.AwardId), default);
        other.ActAs(approved.Head);
        (await other.ApprovalDecisions().Handle(new DecideApprovalStepCommand(approved.Instance.Id, 2, approved.Instance.Version, "Approved", null), default))
            .IsSuccess.ShouldBeTrue();
        other.ActAsSystem();
        other.Allocations.Items.Clear();
        var (_, missingReservation) = await other.ApprovalOutcomes(other.NewWriter()).FinalizeAsync(approved.Instance, default);
        missingReservation!.RuleId.ShouldBe("BUD-010");
    }

    [Fact]
    public async Task ADecisionOnALaterLevel_IsAConflict_NotADeniedAttempt()
    {
        var instance = await AssignedInstanceAsync();
        Doubles.ActAs(Head);

        var early = await Doubles.ApprovalDecisions().Handle(new DecideApprovalStepCommand(instance.Id, 2, instance.Version, "Approved", null), default);

        early.IsSuccess.ShouldBeFalse();
        early.Error!.Category.ShouldNotBe(ErrorCategory.Forbidden);
        Doubles.Audit.Records.ShouldNotContain(record => record.Action == ApprovalAudit.DecisionDenied);
    }

    [Fact]
    public async Task APolicyWithoutLevels_IsRejectedByTheDomain()
    {
        Doubles.Permissions.Grant(Administrator.Id, SeededRoles.PermissionsByRole[SeededRoles.ProcurementManager]);
        Doubles.ActAs(Administrator);

        var result = await new ApprovalPolicyHandlers(Doubles.Policies, Doubles.NewWriter()).Handle(PolicyCommand() with { Levels = null }, default);

        result.IsSuccess.ShouldBeFalse();
        Doubles.Policies.Items.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("approval-123")]
    [InlineData("approvalX0123456789abcdef0123456789abcdef")]
    [InlineData("approval-zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    [InlineData("approval-00000000000000000000000000000000")]
    public void AwardIdFrom_AcceptsOnlyTheDeterministicInstanceIds(string? instanceId) =>
        ApprovalWorkflowNames.AwardIdFrom(instanceId).ShouldBeNull();

    [Fact]
    public void AwardIdFrom_RoundTripsTheInstanceId()
    {
        var award = Guid.NewGuid();
        ApprovalWorkflowNames.AwardIdFrom(ApprovalWorkflowNames.InstanceIdFor(award)).ShouldBe(award);
    }

    private static IntegrationEnvelope ProcurementIntegrationEventContractFrom(Template.Domain.Common.DomainEvent domainEvent) =>
        Template.Application.Messaging.Procurement.ProcurementIntegrationEventContract.FromDomainEvent(domainEvent, "trace");

    private static async Task<(ProcurementTestDoubles Doubles, ApprovalInstance Instance, ProcurementTestDoubles.Actor Manager, ProcurementTestDoubles.Actor Head)>
        AssignedInstanceInNewScenarioAsync()
    {
        var other = new ApprovalHandlerEdgeCaseTests();
        var instance = await other.AssignedInstanceAsync();
        return (other.Doubles, instance, other.Manager, other.Head);
    }

    private static CreateApprovalPolicyVersionCommand PolicyCommand() => new(
        ProcurementTestDoubles.Now.AddDays(30),
        [
            new ApprovalPolicyLevelInputModel(1, 0m, "CostCenterManager", null),
            new ApprovalPolicyLevelInputModel(2, 50_000m, "RoleGroup", SeededRoles.FinanceApprover)
        ],
        96, 48, SeededRoles.ProcurementManager, 1, "Synthetic policy version");

    private async Task<(Template.Domain.Sourcing.RequestForQuotation Rfq, Guid AwardId)> AwardedAsync()
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
        Doubles.Permissions.Grant(Manager.Id, SeededRoles.PermissionsByRole[SeededRoles.CostCenterManager]);
        Doubles.Permissions.Grant(Head.Id, SeededRoles.PermissionsByRole[SeededRoles.DepartmentHead]);
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

    private static ApprovalStartRequest Input(Guid awardId) =>
        new(new ApprovalWorkflowInput(ApprovalWorkflowInput.Version1, awardId, Guid.NewGuid(), ProcurementTestDoubles.Now, "trace-approval"), "1.0");

    private static ApprovalActivityRequest Request(Guid awardId) => new(awardId, "trace-approval");
}
