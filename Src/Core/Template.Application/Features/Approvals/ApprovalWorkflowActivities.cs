#nullable enable
using Microsoft.Extensions.Logging;
using Template.Application.Common.Results;
using Template.Application.Interfaces.Approvals;
using Template.Application.Interfaces.Organization;
using Template.Application.Interfaces.Requisitions;
using Template.Application.Interfaces.Sourcing;
using Template.Application.Logging;
using Template.Application.Services.Procurement;
using Template.Application.Workflow;
using Template.Application.Wrappers;
using Template.Domain.Approvals;
using Template.Domain.Common;

namespace Template.Application.Features.Approvals;

/// <summary>
/// The Application use cases behind the Durable activities (APR-016): every approval rule, threshold, assignment and
/// effect is evaluated here against the database, never in the orchestration. Each call is idempotent per award and
/// returns the next <see cref="ApprovalProgress"/>; a replayed or duplicated activity reads the persisted state and
/// changes nothing twice. System writes retry an optimistic-concurrency race (api-guidelines.md §4); infrastructure
/// failures throw so the orchestration's retry policy can retry them.
/// </summary>
public sealed class ApprovalWorkflowActivities(
    IApprovalInstanceRepository instances,
    IApprovalPolicyRepository policies,
    IRequestForQuotationRepository rfqs,
    IPurchaseRequisitionRepository requisitions,
    ICostCenterRepository costCenters,
    IDepartmentRepository departments,
    IApproverDirectory directory,
    ApprovalOutcomes outcomes,
    ProcurementWriter writer,
    ILogger<ApprovalWorkflowActivities> logger)
{
    private const int SystemRetries = 3;

    /// <summary>
    /// APR-001/002: creates the instance once per award, pinned to the policy effective at award submission. A second
    /// start returns the existing instance; an award that already left <c>PendingApproval</c> returns
    /// <see cref="ApprovalProgress.NotStarted"/> and the orchestration completes.
    /// </summary>
    public async Task<ApprovalProgress> StartAsync(ApprovalStartRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var input = request.Input;
        if (input.InputVersion != ApprovalWorkflowInput.Version1 || input.AwardId == Guid.Empty)
            throw new NotSupportedException("The approval workflow input version is not supported.");

        for (var attempt = 1; ; attempt++)
        {
            if (await instances.FindByAwardAsync(input.AwardId, cancellationToken) is { } existing)
                return Progress(existing);
            var pending = await instances.FindPendingAwardAsync(input.AwardId, cancellationToken);
            if (pending is null)
                return NotStarted(input.AwardId);

            var rfq = await rfqs.FindAsync(pending.RfqId, cancellationToken)
                ?? throw new InvalidOperationException("A pending award references a missing RFQ.");
            var award = rfq.Awards.Single(candidate => candidate.Id == input.AwardId);
            var requisition = await requisitions.FindAsync(pending.RequisitionId, cancellationToken)
                ?? throw new InvalidOperationException("A pending award references a missing requisition.");
            var policy = await policies.FindEffectiveAsync(award.SubmittedAtUtc, cancellationToken)
                ?? throw new InvalidOperationException("No approval policy version is effective at the award submission instant.");
            var facts = new ApprovalStartFacts(
                award.Id, rfq.Id, requisition.Id, requisition.Number, requisition.RequesterId, award.SubmittedBy, rfq.CostCenterId, rfq.DepartmentId,
                award.SupplierId, new Money(award.NormalizedTotal, award.BaseCurrency), new Money(award.RequisitionCurrencyTotal, award.RequisitionCurrency));
            var (instance, error) = ApprovalInstance.Start(Guid.NewGuid(), facts, policy, request.WorkflowVersion, writer.UtcNow, writer.CorrelationId);
            if (error is not null)
                throw new InvalidOperationException($"The approval cannot start: {error.Code}.");

            instances.Add(instance!);
            writer.Audit(ApprovalAudit.Started, ApprovalAudit.Resource, instance!.Id, toState: "Pending", ruleIds: ["APR-001", "APR-002"],
                details: new Dictionary<string, string>(ApprovalOutcomes.Details(instance))
                {
                    ["requiredLevels"] = string.Join(',', instance.Steps.OrderBy(step => step.Level).Select(step => ApprovalAudit.Text(step.Level)))
                });
            var events = writer.Stage(instance.DomainEvents);
            instance.ClearDomainEvents();
            var commit = await writer.CommitAsync(_ => Task.FromResult<long?>(null),
                constraint => constraint == IApprovalInstanceRepository.AwardUniqueKey ? new ApplicationError("approval.exists", ErrorCategory.Conflict, "The approval was started concurrently.") : null,
                cancellationToken);
            if (commit is null)
            {
                writer.Record(events);
                logger.LogInformation(LogEvents.ApprovalStarted, "Approval {ApprovalInstanceId} started for award {AwardId} with {Levels} levels",
                    instance.Id, instance.AwardId, instance.Steps.Count);
                return Progress(instance);
            }

            if (attempt >= SystemRetries)
                throw new InvalidOperationException($"The approval start did not commit: {commit.Code}.");
        }
    }

    /// <summary>APR-003/006: resolves and assigns the current level; returns the current progress when it is already assigned.</summary>
    public Task<ApprovalProgress> AssignStepAsync(ApprovalActivityRequest request, CancellationToken cancellationToken) =>
        MutateAsync(request.AwardId, async instance =>
        {
            if (instance.NextAction != ApprovalNextAction.AssignStep)
                return false;
            var step = instance.CurrentStep!;
            var (assignee, resolution) = await ResolveAsync(instance, step, cancellationToken);
            var (changed, error) = instance.AssignCurrentStep(assignee, resolution, writer.UtcNow, writer.CorrelationId);
            if (error is not null)
                throw new InvalidOperationException($"The resolved assignee was rejected: {error.Code}.");
            if (!changed)
                return false;
            writer.Audit(ApprovalAudit.StepAssigned, ApprovalAudit.Resource, instance.Id, reason: resolution, ruleIds: ["APR-003", "APR-006"],
                details: new Dictionary<string, string>
                {
                    ["level"] = ApprovalAudit.Text(step.Level),
                    ["assignee"] = assignee.UserId?.ToString() ?? $"role:{assignee.Role}",
                    ["dueAt"] = step.DueAtUtc!.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                    ["assignmentSequence"] = ApprovalAudit.Text(step.AssignmentSequence)
                });
            logger.LogInformation(LogEvents.ApprovalStepAssigned, "Approval {ApprovalInstanceId} level {Level} assigned by {Resolution}",
                instance.Id, step.Level, resolution);
            return true;
        }, cancellationToken);

    /// <summary>
    /// APR-011: the reminder or deadline timer of a level. The timer's own fire instant is the evaluation instant when the
    /// clock is behind it, so a timer never fires "too early" in a loop; a decided or reassigned step makes it a no-op.
    /// </summary>
    public Task<ApprovalProgress> ProcessTimerAsync(ApprovalTimerRequest request, CancellationToken cancellationToken) =>
        MutateAsync(request.AwardId, instance =>
        {
            var now = writer.UtcNow > request.FireAtUtc ? writer.UtcNow : DateTime.SpecifyKind(request.FireAtUtc, DateTimeKind.Utc);
            var details = new Dictionary<string, string> { ["level"] = ApprovalAudit.Text(request.Level) };
            if (request.Kind == ApprovalWorkflowTimer.Reminder)
            {
                if (!instance.RecordReminder(request.Level, now, writer.CorrelationId))
                    return Task.FromResult(false);
                writer.Audit(ApprovalAudit.StepReminded, ApprovalAudit.Resource, instance.Id, ruleIds: ["APR-011"], details: details);
            }
            else
            {
                var outcome = instance.HandleDeadline(request.Level, now, writer.CorrelationId);
                if (outcome == ApprovalDeadlineOutcome.NotDue)
                    return Task.FromResult(false);
                if (outcome == ApprovalDeadlineOutcome.Escalated)
                    writer.Audit(ApprovalAudit.StepEscalated, ApprovalAudit.Resource, instance.Id, ruleIds: ["APR-011"],
                        details: new Dictionary<string, string>(details)
                        {
                            ["escalationCount"] = ApprovalAudit.Text(instance.CurrentStep!.EscalationCount),
                            ["assignee"] = $"role:{instance.EscalationRole}"
                        });
            }

            logger.LogInformation(LogEvents.ApprovalTimerProcessed, "Approval {ApprovalInstanceId} level {Level} {Timer} timer applied",
                instance.Id, request.Level, request.Kind);
            return Task.FromResult(true);
        }, cancellationToken);

    /// <summary>APR-009/011/013: applies the settled outcome exactly once (commit or release the reservation).</summary>
    public async Task<ApprovalProgress> FinalizeAsync(ApprovalActivityRequest request, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var instance = await instances.FindByAwardAsync(request.AwardId, cancellationToken);
            if (instance is null)
                return NotStarted(request.AwardId);
            if (instance.NextAction != ApprovalNextAction.Finalize)
                return Progress(instance);

            var (events, error) = await outcomes.FinalizeAsync(instance, cancellationToken);
            if (error is not null)
                throw new InvalidOperationException($"The approval outcome cannot be applied: {error.Code}.");
            var commit = await writer.CommitAsync(token => instances.GetVersionAsync(instance.Id, token), null, cancellationToken);
            if (commit is null)
            {
                writer.Record(events);
                logger.LogInformation(LogEvents.ApprovalFinalized, "Approval {ApprovalInstanceId} finalized as {Status}", instance.Id, instance.Status);
                return Progress(instance);
            }

            if (attempt >= SystemRetries || commit.Category != ErrorCategory.PreconditionFailed)
                throw new InvalidOperationException($"The approval outcome did not commit: {commit.Code}.");
        }
    }

    /// <summary>Read-only progress, used after a signal: the database state decides what the signal meant.</summary>
    public async Task<ApprovalProgress> GetProgressAsync(ApprovalActivityRequest request, CancellationToken cancellationToken) =>
        await instances.FindByAwardAsync(request.AwardId, cancellationToken) is { } instance ? Progress(instance) : NotStarted(request.AwardId);

    private async Task<ApprovalProgress> MutateAsync(Guid awardId, Func<ApprovalInstance, Task<bool>> change, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var instance = await instances.FindByAwardAsync(awardId, cancellationToken);
            if (instance is null)
                return NotStarted(awardId);
            if (!await change(instance))
                return Progress(instance);

            var events = writer.Stage(instance.DomainEvents);
            instance.ClearDomainEvents();
            var commit = await writer.CommitAsync(token => instances.GetVersionAsync(instance.Id, token), null, cancellationToken);
            if (commit is null)
            {
                writer.Record(events);
                return Progress(instance);
            }

            if (attempt >= SystemRetries || commit.Category != ErrorCategory.PreconditionFailed)
                throw new InvalidOperationException($"The approval change did not commit: {commit.Code}.");
        }
    }

    /// <summary>
    /// APR-003: L1 cost-center manager, L2 department head, role-group levels their group. APR-006: a missing, inactive,
    /// non-approver or ineligible user resolves to the pinned escalation group instead.
    /// </summary>
    private async Task<(ApprovalAssignee Assignee, string Resolution)> ResolveAsync(ApprovalInstance instance, ApprovalStep step, CancellationToken cancellationToken)
    {
        Guid? userId;
        switch (step.Resolver)
        {
            case ApproverResolver.RoleGroup:
                return (ApprovalAssignee.ForRole(step.ResolverRole!), "RoleGroup");
            case ApproverResolver.CostCenterManager:
                userId = (await costCenters.FindAsync(instance.CostCenterId, cancellationToken))?.ManagerUserId;
                break;
            default:
                userId = (await departments.FindAsync(instance.DepartmentId, cancellationToken))?.HeadUserId;
                break;
        }

        var fallback = ApprovalAssignee.ForRole(instance.EscalationRole);
        if (userId is not { } user)
            return (fallback, "MissingAssigneeFallback");
        var candidate = await directory.GetCandidateAsync(user, cancellationToken);
        if (candidate is not { IsActive: true, CanDecide: true })
            return (fallback, "InactiveAssigneeFallback");
        return instance.IsEligible(user, step.Level)
            ? (ApprovalAssignee.ForUser(user), step.Resolver.ToString())
            : (fallback, "IneligibleAssigneeFallback");
    }

    private ApprovalProgress NotStarted(Guid awardId) =>
        new(awardId, null, ApprovalProgress.NotStarted, ApprovalWorkflowStep.Completed, 0, null, null, writer.CorrelationId);

    private ApprovalProgress Progress(ApprovalInstance instance) => ToProgress(instance, writer.CorrelationId);

    public static ApprovalProgress ToProgress(ApprovalInstance instance, string correlationId)
    {
        var timer = instance.NextTimer;
        var next = instance.NextAction switch
        {
            ApprovalNextAction.AssignStep => ApprovalWorkflowStep.AssignStep,
            ApprovalNextAction.Wait => ApprovalWorkflowStep.Wait,
            ApprovalNextAction.Finalize => ApprovalWorkflowStep.Finalize,
            _ => ApprovalWorkflowStep.Completed
        };
        return new ApprovalProgress(
            instance.AwardId, instance.Id, instance.Status.ToString(), next, instance.CurrentLevel,
            timer is null ? null : timer.Value.Kind == ApprovalTimerKind.Reminder ? ApprovalWorkflowTimer.Reminder : ApprovalWorkflowTimer.Deadline,
            timer?.FireAtUtc, correlationId);
    }
}
