using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Template.Application.Features.Approvals;
using Template.Application.Security;
using Template.Application.Workflow;
using Template.Infrastructure.Persistence.Contexts;
using Template.Infrastructure.Persistence.Messaging;
using Template.Infrastructure.Workflow;

namespace Template.Functional.Tests.ApiEndpoints.v2;

/// <summary>
/// Phase 31 HTTP contract on the self-contained Test host: the in-memory workflow client stands for the WorkflowFunctions
/// boundary and the Durable activities run in process through the same Application use cases. Covers the SC-01 two-level
/// journey, the inbox, server-computed allowed actions, If-Match/ETag, Idempotency-Key replay, APR-014 repeats,
/// separation of duties, the 503 path when the workflow host is unavailable, rejection with release and the Outbox events.
/// </summary>
public sealed class ApprovalFunctionalTests(ProcurementWebApplicationFactory factory) : IClassFixture<ProcurementWebApplicationFactory>
{
    private const string Reason = "Synthetic approval decision reason";
    private const string Instances = "/api/v2/approval-instances";

    [Fact]
    public async Task SC_01_TwoLevelApproval_ThroughTheInbox_CommitsTheBudgetAndApprovesTheRequisition()
    {
        var approval = await ApprovalScenario.CreateAsync(factory);
        var instance = await approval.StartedInstanceAsync();

        using var inbox = await approval.Manager.GetAsync($"{Instances}?assignedTo=me&sort=dueAt");
        inbox.StatusCode.ShouldBe(HttpStatusCode.OK, await inbox.Content.ReadAsStringAsync());
        var item = (await ApiUser.ReadAsync(inbox)).GetProperty("items").EnumerateArray().Single(row => row.GetProperty("id").GetGuid() == instance);
        item.GetProperty("currentLevel").GetInt32().ShouldBe(1);
        item.GetProperty("finalLevel").GetInt32().ShouldBe(2);
        item.GetProperty("amountEvaluated").GetProperty("amount").GetString().ShouldBe("16200.00");

        var (detail, etag) = await approval.GetAsync(approval.Manager, instance);
        Strings(detail.GetProperty("allowedActions")).ShouldBe(["approve", "reject", "delegate"]);
        detail.GetProperty("policy").GetString().ShouldBe("ApprovalPolicy:1");
        detail.GetProperty("award").GetProperty("comparison").GetArrayLength().ShouldBe(3);
        detail.GetProperty("steps")[0].GetProperty("assigneeUserId").GetGuid().ShouldBe(approval.ManagerId);

        using var missingIfMatch = await approval.Manager.PostAsync($"{Instances}/{instance}/steps/1/decision", new { decision = "Approved" });
        missingIfMatch.StatusCode.ShouldBe((HttpStatusCode)428);
        using var stale = await approval.Manager.PostAsync($"{Instances}/{instance}/steps/1/decision", new { decision = "Approved" }, "\"999\"");
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);

        var key = Guid.NewGuid().ToString();
        using var approved = await approval.Manager.SendAsync(HttpMethod.Post, $"{Instances}/{instance}/steps/1/decision", new { decision = "Approved" }, etag, key);
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        using var replay = await approval.Manager.SendAsync(HttpMethod.Post, $"{Instances}/{instance}/steps/1/decision", new { decision = "Approved" }, etag, key);
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await replay.Content.ReadAsStringAsync()).ShouldBe(await approved.Content.ReadAsStringAsync());
        var newer = approved.Headers.ETag!.Tag;
        using var repeated = await approval.Manager.PostAsync($"{Instances}/{instance}/steps/1/decision", new { decision = "Approved" }, newer);
        repeated.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var conflicting = await approval.Manager.PostAsync($"{Instances}/{instance}/steps/1/decision", new { decision = "Rejected", reason = Reason }, newer);
        conflicting.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiUser.CodeAsync(conflicting)).ShouldBe("approval.step-already-decided");
        approval.Workflow.Signals.Count(signal => signal.AwardId == approval.AwardId).ShouldBe(1);

        await approval.RunActivitiesAsync();
        var (second, secondTag) = await approval.GetAsync(approval.Head, instance);
        second.GetProperty("currentLevel").GetInt32().ShouldBe(2);
        using var managerAgain = await approval.Manager.PostAsync($"{Instances}/{instance}/steps/2/decision", new { decision = "Approved" }, secondTag);
        managerAgain.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ApiUser.CodeAsync(managerAgain)).ShouldBe("approval.not-assignee");
        using var final = await approval.Head.PostAsync($"{Instances}/{instance}/steps/2/decision", new { decision = "Approved", reason = "Within budget" }, secondTag);
        final.StatusCode.ShouldBe(HttpStatusCode.OK, await final.Content.ReadAsStringAsync());
        (await ApiUser.ReadAsync(final)).GetProperty("nextAction").GetString().ShouldBe("Finalize");

        await approval.RunActivitiesAsync();
        var (ended, _) = await approval.GetAsync(approval.Head, instance);
        ended.GetProperty("status").GetString().ShouldBe("Approved");
        (await approval.Sourcing.RequisitionStatusAsync(approval.RequisitionId)).ShouldBe("Approved");
        var allocation = await approval.Sourcing.Scenario.AllocationAsync();
        allocation.GetProperty("committed").GetProperty("amount").GetString().ShouldBe("16200.00");
        allocation.GetProperty("reserved").GetProperty("amount").GetString().ShouldBe("0.00");

        using var history = await approval.Head.GetAsync($"{Instances}/{instance}/history");
        Strings((await ApiUser.ReadAsync(history)).GetProperty("entries"), "action")
            .ShouldBe(["started", "step-assigned", "step-approved", "step-assigned", "step-approved", "approved"]);
        var events = await approval.OutboxEventsAsync(instance);
        events.ShouldBe(["procurement.approval.started", "procurement.approval.step-assigned", "procurement.approval.step-assigned",
            "procurement.approval.completed"], ignoreOrder: true);
        (await approval.OutboxPayloadsAsync(instance)).ShouldAllBe(payload => !payload.Contains("Within budget") && !payload.Contains("Synthetic"));
    }

    [Fact]
    public async Task T_APR_009_b_RejectionNeedsAReason_AndReleasesTheReservation()
    {
        var approval = await ApprovalScenario.CreateAsync(factory);
        var instance = await approval.StartedInstanceAsync();
        var (_, etag) = await approval.GetAsync(approval.Manager, instance);

        using var noReason = await approval.Manager.PostAsync($"{Instances}/{instance}/steps/1/decision", new { decision = "Rejected" }, etag);
        noReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiUser.CodeAsync(noReason)).ShouldBe("reason-required");
        using var rejected = await approval.Manager.PostAsync($"{Instances}/{instance}/steps/1/decision", new { decision = "Rejected", reason = Reason }, etag);
        rejected.StatusCode.ShouldBe(HttpStatusCode.OK, await rejected.Content.ReadAsStringAsync());
        await approval.RunActivitiesAsync();

        (await approval.Sourcing.RequisitionStatusAsync(approval.RequisitionId)).ShouldBe("Rejected");
        var allocation = await approval.Sourcing.Scenario.AllocationAsync();
        allocation.GetProperty("reserved").GetProperty("amount").GetString().ShouldBe("0.00");
        allocation.GetProperty("committed").GetProperty("amount").GetString().ShouldBe("0.00");
        var (ended, endedTag) = await approval.GetAsync(approval.Manager, instance);
        ended.GetProperty("status").GetString().ShouldBe("Rejected");
        ended.GetProperty("endReason").GetString().ShouldBe(Reason);
        using var late = await approval.Manager.PostAsync($"{Instances}/{instance}/steps/1/decision", new { decision = "Approved" }, endedTag);
        late.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task NFR_WorkflowUnavailable_DecisionAnswers503_AndNothingChanges()
    {
        var approval = await ApprovalScenario.CreateAsync(factory);
        var instance = await approval.StartedInstanceAsync();
        var (_, etag) = await approval.GetAsync(approval.Manager, instance);

        approval.Workflow.Unavailable = true;
        try
        {
            using var unavailable = await approval.Manager.PostAsync($"{Instances}/{instance}/steps/1/decision", new { decision = "Approved" }, etag);
            unavailable.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            (await ApiUser.CodeAsync(unavailable)).ShouldBe("approval.workflow-unavailable");
        }
        finally
        {
            approval.Workflow.Unavailable = false;
        }

        var (after, afterTag) = await approval.GetAsync(approval.Manager, instance);
        afterTag.ShouldBe(etag);
        after.GetProperty("steps")[0].GetProperty("status").GetString().ShouldBe("Pending");
    }

    [Fact]
    public async Task Visibility_RequesterReadsButCannotDecide_OutsidersSeeNothing()
    {
        var approval = await ApprovalScenario.CreateAsync(factory);
        var instance = await approval.StartedInstanceAsync();

        var (asRequester, etag) = await approval.GetAsync(approval.Sourcing.Scenario.Requester, instance);
        Strings(asRequester.GetProperty("allowedActions")).ShouldBeEmpty();
        using var denied = await approval.Sourcing.Scenario.Requester.PostAsync($"{Instances}/{instance}/steps/1/decision", new { decision = "Approved" }, etag);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ApiUser.CodeAsync(denied)).ShouldBe("auth.forbidden");

        var outsider = await ApiUser.CreateAsync(factory, SeededRoles.CostCenterManager);
        using var hidden = await outsider.GetAsync($"{Instances}/{instance}");
        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var auditor = await (await ApiUser.CreateAsync(factory, SeededRoles.Auditor)).GetAsync($"{Instances}/{instance}");
        auditor.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task T_APR_015_a_ManagerCancelsWithReason_AndTheRequisitionReturnsToSourcing()
    {
        var approval = await ApprovalScenario.CreateAsync(factory);
        var instance = await approval.StartedInstanceAsync();
        var (_, etag) = await approval.GetAsync(approval.Sourcing.Manager, instance);

        using var noReason = await approval.Sourcing.Manager.PostAsync($"{Instances}/{instance}/cancel", new { }, etag);
        noReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var cancelled = await approval.Sourcing.Manager.PostAsync($"{Instances}/{instance}/cancel", new { reason = Reason }, etag);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());

        (await ApiUser.ReadAsync(cancelled)).GetProperty("status").GetString().ShouldBe("Cancelled");
        (await approval.Sourcing.RequisitionStatusAsync(approval.RequisitionId)).ShouldBe("InSourcing");
        approval.Workflow.Signals.Last().Signal.Kind.ShouldBe(ApprovalSignalKinds.Ended);
        using var workflow = await approval.Sourcing.Manager.GetAsync($"{Instances}/{instance}/workflow");
        (await ApiUser.ReadAsync(workflow)).GetProperty("runtimeStatus").GetString().ShouldBe("Running");
    }

    [Fact]
    public async Task Policies_AreReadable_AndANewVersionCannotStartInThePast()
    {
        var manager = await ApiUser.CreateAsync(factory, SeededRoles.ProcurementManager);

        using var list = await manager.GetAsync("/api/v2/approval-policies");
        var first = (await ApiUser.ReadAsync(list))[0];
        first.GetProperty("version").GetInt32().ShouldBe(1);
        first.GetProperty("stepDurationHours").GetDouble().ShouldBe(120);
        first.GetProperty("reminderAfterHours").GetDouble().ShouldBe(72);
        first.GetProperty("levels").GetArrayLength().ShouldBe(3);

        using var past = await manager.PostAsync("/api/v2/approval-policies", new
        {
            effectiveFrom = DateTime.UtcNow.AddDays(-1),
            levels = new[] { new { level = 1, minimumAmountExclusive = 0m, resolver = "CostCenterManager" } },
            stepDurationHours = 120, reminderAfterHours = 72, escalationRole = "ProcurementManager", maxEscalations = 1
        });
        past.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiUser.ReadAsync(past)).GetProperty("ruleId").GetString().ShouldBe("APR-017");
    }

    [Fact]
    public async Task T_APR_010_b_DelegationTargets_ComeFromTheIdentityStore_AndDelegationKeepsTheDueTime()
    {
        var approval = await ApprovalScenario.CreateAsync(factory);
        var instance = await approval.StartedInstanceAsync();
        var headId = await ProcurementScenario.UserIdAsync(approval.Head);
        using var me = await approval.Head.GetAsync("/api/v2/account/me");
        var headName = (await ApiUser.ReadAsync(me)).GetProperty("userName").GetString()!;
        using var managerMe = await approval.Manager.GetAsync("/api/v2/account/me");
        var managerName = (await ApiUser.ReadAsync(managerMe)).GetProperty("userName").GetString()!;

        using var targets = await approval.Manager.GetAsync($"{Instances}/{instance}/steps/1/delegates?q={Uri.EscapeDataString(headName)}");
        targets.StatusCode.ShouldBe(HttpStatusCode.OK, await targets.Content.ReadAsStringAsync());
        var head = (await ApiUser.ReadAsync(targets)).GetProperty("items").EnumerateArray().Single();
        (head.GetProperty("userId").GetGuid(), head.GetProperty("eligible").GetBoolean()).ShouldBe((headId, true));
        head.TryGetProperty("email", out _).ShouldBeFalse();
        using var self = await approval.Manager.GetAsync($"{Instances}/{instance}/steps/1/delegates?q={Uri.EscapeDataString(managerName)}");
        (await ApiUser.ReadAsync(self)).GetProperty("items")[0].GetProperty("ineligibleReason").GetString().ShouldBe("current-assignee");
        using var none = await approval.Manager.GetAsync($"{Instances}/{instance}/steps/1/delegates?q=no-such-approver-{Guid.NewGuid():N}");
        (await ApiUser.ReadAsync(none)).GetProperty("totalItems").GetInt64().ShouldBe(0);
        using var otherLevel = await approval.Manager.GetAsync($"{Instances}/{instance}/steps/2/delegates");
        otherLevel.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var requesterTargets = await approval.Sourcing.Scenario.Requester.GetAsync($"{Instances}/{instance}/steps/1/delegates");
        requesterTargets.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var (before, etag) = await approval.GetAsync(approval.Manager, instance);
        using var unknownUser = await approval.Manager.PostAsync($"{Instances}/{instance}/steps/1/delegate", new { delegateUserId = Guid.NewGuid(), reason = Reason }, etag);
        (await ApiUser.CodeAsync(unknownUser)).ShouldBe("approval.invalid-delegate");
        using var delegated = await approval.Manager.PostAsync($"{Instances}/{instance}/steps/1/delegate", new { delegateUserId = headId, reason = Reason }, etag);
        delegated.StatusCode.ShouldBe(HttpStatusCode.OK, await delegated.Content.ReadAsStringAsync());
        var step = (await ApiUser.ReadAsync(delegated)).GetProperty("steps")[0];
        step.GetProperty("assigneeUserId").GetGuid().ShouldBe(headId);
        step.GetProperty("dueAt").GetDateTime().ShouldBe(before.GetProperty("steps")[0].GetProperty("dueAt").GetDateTime());
        approval.Workflow.Signals.Last().Signal.Kind.ShouldBe(ApprovalSignalKinds.Delegated);
        using var history = await approval.Head.GetAsync($"{Instances}/{instance}/history");
        Strings((await ApiUser.ReadAsync(history)).GetProperty("entries"), "action").ShouldContain("step-delegated");
    }

    [Fact]
    public async Task T_APR_015_b_AdministratorReassignsToARoleGroup_AndReadsTheReconciliation()
    {
        var approval = await ApprovalScenario.CreateAsync(factory);
        var instance = await approval.StartedInstanceAsync();
        var administrator = approval.Sourcing.Manager;
        var (_, etag) = await approval.GetAsync(administrator, instance);

        using var both = await administrator.PostAsync($"{Instances}/{instance}/steps/1/reassign",
            new { assigneeUserId = approval.ManagerId, assigneeRole = SeededRoles.FinanceApprover, reason = Reason }, etag);
        both.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var notApprover = await administrator.PostAsync($"{Instances}/{instance}/steps/1/reassign",
            new { assigneeRole = SeededRoles.Requester, reason = Reason }, etag);
        (await ApiUser.CodeAsync(notApprover)).ShouldBe("approval.invalid-delegate");
        using var reassigned = await administrator.PostAsync($"{Instances}/{instance}/steps/1/reassign",
            new { assigneeRole = SeededRoles.FinanceApprover, reason = Reason }, etag);
        reassigned.StatusCode.ShouldBe(HttpStatusCode.OK, await reassigned.Content.ReadAsStringAsync());
        (await ApiUser.ReadAsync(reassigned)).GetProperty("steps")[0].GetProperty("assigneeRole").GetString().ShouldBe(SeededRoles.FinanceApprover);
        approval.Workflow.Signals.Last().Signal.Kind.ShouldBe(ApprovalSignalKinds.Reassigned);

        var finance = await ApiUser.CreateAsync(factory, SeededRoles.FinanceApprover);
        using var inbox = await finance.GetAsync($"{Instances}?assignedTo=me&escalated=false");
        (await ApiUser.ReadAsync(inbox)).GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).ShouldContain(instance);
        using var invalidAssignedTo = await finance.GetAsync($"{Instances}?assignedTo=someone");
        invalidAssignedTo.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var invalidStatus = await finance.GetAsync($"{Instances}?status=Unknown");
        invalidStatus.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var requesterInbox = await approval.Sourcing.Scenario.Requester.GetAsync($"{Instances}?assignedTo=me");
        requesterInbox.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var participant = await approval.Sourcing.Scenario.Requester.GetAsync($"{Instances}?requisitionId={approval.RequisitionId}");
        (await ApiUser.ReadAsync(participant)).GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid().ShouldBe(instance);

        using var report = await administrator.GetAsync($"{Instances}/reconciliation");
        report.StatusCode.ShouldBe(HttpStatusCode.OK, await report.Content.ReadAsStringAsync());
        (await ApiUser.ReadAsync(report)).GetProperty("checked").GetInt32().ShouldBeGreaterThan(0);
        using var repair = await administrator.PostAsync($"{Instances}/reconciliation/repair", null);
        repair.StatusCode.ShouldBe(HttpStatusCode.OK, await repair.Content.ReadAsStringAsync());
        using var workflow = await finance.GetAsync($"{Instances}/{instance}/workflow");
        workflow.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task T_APR_017_b_ANewPolicyVersion_IsCreatedOnce_AndReadableByVersion()
    {
        var manager = await ApiUser.CreateAsync(factory, SeededRoles.ProcurementManager);
        var body = new
        {
            effectiveFrom = DateTime.UtcNow.AddYears(20),
            levels = new object[]
            {
                new { level = 1, minimumAmountExclusive = 0m, resolver = "CostCenterManager" },
                new { level = 2, minimumAmountExclusive = 50_000m, resolver = "RoleGroup", role = SeededRoles.FinanceApprover }
            },
            stepDurationHours = 96, reminderAfterHours = 48, escalationRole = SeededRoles.ProcurementManager, maxEscalations = 1,
            reason = "Synthetic future policy version"
        };

        using var created = await manager.PostAsync("/api/v2/approval-policies", body);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var version = (await ApiUser.ReadAsync(created)).GetProperty("version").GetInt32();
        version.ShouldBeGreaterThan(1);
        using var read = await manager.GetAsync($"/api/v2/approval-policies/{version}");
        (await ApiUser.ReadAsync(read)).GetProperty("levels")[1].GetProperty("role").GetString().ShouldBe(SeededRoles.FinanceApprover);
        using var missing = await manager.GetAsync("/api/v2/approval-policies/9999");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var badResolver = await manager.PostAsync("/api/v2/approval-policies", body with { levels = new object[] { new { level = 1, minimumAmountExclusive = 0m, resolver = "1" } } });
        badResolver.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var badEscalation = await manager.PostAsync("/api/v2/approval-policies", body with { escalationRole = SeededRoles.Requester });
        badEscalation.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var badDuration = await manager.PostAsync("/api/v2/approval-policies", body with { stepDurationHours = 0 });
        badDuration.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var auditorList = await (await ApiUser.CreateAsync(factory, SeededRoles.Auditor)).GetAsync("/api/v2/approval-policies");
        auditorList.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var requesterList = await (await ApiUser.CreateAsync(factory, SeededRoles.Requester)).GetAsync("/api/v2/approval-policies");
        requesterList.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static string[] Strings(JsonElement array, string? property = null) =>
        array.EnumerateArray().Select(element => (property is null ? element : element.GetProperty(property)).GetString()!).ToArray();
}

/// <summary>
/// An award pending approval (SC-01: 18,000.00 BRL requisition, leading quote 16,200.00 BRL → L1 + L2) with a
/// cost-center manager and a department head who hold the seeded approver roles.
/// </summary>
internal sealed class ApprovalScenario
{
    private readonly ProcurementWebApplicationFactory factory;

    private ApprovalScenario(ProcurementWebApplicationFactory factory, SourcingScenario sourcing, ApiUser manager, ApiUser head)
    {
        this.factory = factory;
        Sourcing = sourcing;
        Manager = manager;
        Head = head;
    }

    public SourcingScenario Sourcing { get; }
    public ApiUser Manager { get; }
    public ApiUser Head { get; }
    public Guid ManagerId { get; private set; }
    public Guid RequisitionId { get; private set; }
    public Guid AwardId { get; private set; }

    public InMemoryApprovalWorkflowClient Workflow => factory.Services.GetRequiredService<InMemoryApprovalWorkflowClient>();

    public static async Task<ApprovalScenario> CreateAsync(ProcurementWebApplicationFactory factory)
    {
        var sourcing = await SourcingScenario.CreateAsync(factory, allocation: 50_000m);
        var scenario = new ApprovalScenario(factory, sourcing,
            await ApiUser.CreateAsync(factory, SeededRoles.CostCenterManager), await ApiUser.CreateAsync(factory, SeededRoles.DepartmentHead));
        scenario.ManagerId = await ProcurementScenario.UserIdAsync(scenario.Manager);
        await sourcing.Scenario.AssignManagerAsync(scenario.ManagerId);
        var headId = await ProcurementScenario.UserIdAsync(scenario.Head);
        using var department = await sourcing.Scenario.Administrator.GetAsync($"{MasterDataRoutes.Departments}/{sourcing.Scenario.DepartmentId}");
        using var headed = await sourcing.Scenario.Administrator.PutAsync($"{MasterDataRoutes.Departments}/{sourcing.Scenario.DepartmentId}",
            new { name = "Operations", headUserId = headId }, department.Headers.ETag!.Tag);
        headed.StatusCode.ShouldBe(HttpStatusCode.OK, await headed.Content.ReadAsStringAsync());

        scenario.RequisitionId = await sourcing.SubmittedRequisitionAsync("18", "1000.00");
        var suppliers = new[] { await sourcing.ActiveSupplierAsync(), await sourcing.ActiveSupplierAsync(), await sourcing.ActiveSupplierAsync() };
        var (rfq, etag) = await sourcing.Buyer.CreateAsync(SourcingRoutes.Rfqs, new { requisitionId = scenario.RequisitionId });
        var id = rfq.GetProperty("id").GetGuid();
        foreach (var supplier in suppliers)
            etag = await sourcing.InviteAsync(id, etag, supplier);
        etag = await sourcing.PublishAsync(id, etag);
        string[] prices = ["950.00", "900.00", "980.00"];
        for (var index = 0; index < suppliers.Length; index++)
            etag = await sourcing.EnterQuoteAsync(id, etag, suppliers[index], prices[index]);
        using var closed = await sourcing.Buyer.PostAsync($"{SourcingRoutes.Rfqs}/{id}/close", new { reason = "Documented sourcing justification" }, etag);
        var leading = (await ApiUser.ReadAsync(closed)).GetProperty("comparison").GetProperty("recommendedQuoteRevisionId").GetGuid();
        using var awarded = await sourcing.Buyer.PostAsync($"{SourcingRoutes.Rfqs}/{id}/award", new { quoteRevisionId = leading }, closed.Headers.ETag!.Tag);
        awarded.StatusCode.ShouldBe(HttpStatusCode.OK, await awarded.Content.ReadAsStringAsync());
        scenario.AwardId = (await ApiUser.ReadAsync(awarded)).GetProperty("awards")[0].GetProperty("id").GetGuid();
        return scenario;
    }

    /// <summary>What the Worker and the orchestration would do: start the workflow, create the instance, assign level 1.</summary>
    public async Task<Guid> StartedInstanceAsync()
    {
        var input = new ApprovalWorkflowInput(ApprovalWorkflowInput.Version1, AwardId, RequisitionId, DateTime.UtcNow, "functional-approval");
        await Workflow.StartAsync(input, default);
        await using var scope = factory.Services.CreateAsyncScope();
        var activities = scope.ServiceProvider.GetRequiredService<ApprovalWorkflowActivities>();
        var started = await activities.StartAsync(new ApprovalStartRequest(input, ApprovalWorkflowNames.CurrentVersion), default);
        (await activities.AssignStepAsync(new ApprovalActivityRequest(AwardId, input.CorrelationId), default)).Next.ShouldBe(ApprovalWorkflowStep.Wait);
        return started.InstanceId!.Value;
    }

    /// <summary>One orchestration turn after a signal: read progress, then assign or finalize as the progress says.</summary>
    public async Task RunActivitiesAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var activities = scope.ServiceProvider.GetRequiredService<ApprovalWorkflowActivities>();
        var request = new ApprovalActivityRequest(AwardId, "functional-approval");
        var progress = await activities.GetProgressAsync(request, default);
        if (progress.Next == ApprovalWorkflowStep.AssignStep)
            await activities.AssignStepAsync(request, default);
        else if (progress.Next == ApprovalWorkflowStep.Finalize)
            await activities.FinalizeAsync(request, default);
    }

    public async Task<(JsonElement Body, string ETag)> GetAsync(ApiUser user, Guid instance)
    {
        using var response = await user.GetAsync($"/api/v2/approval-instances/{instance}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await ApiUser.ReadAsync(response), response.Headers.ETag!.Tag);
    }

    public async Task<IReadOnlyList<string>> OutboxEventsAsync(Guid instance)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Set<OutboxMessage>().Where(message => message.AggregateId == instance).Select(message => message.EventType).ToListAsync();
    }

    public async Task<IReadOnlyList<string>> OutboxPayloadsAsync(Guid instance)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Set<OutboxMessage>().Where(message => message.AggregateId == instance).Select(message => message.PayloadJson).ToListAsync();
    }
}
