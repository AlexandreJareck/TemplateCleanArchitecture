#nullable enable
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Results;
using Template.Application.Interfaces.Approvals;
using Template.Application.Logging;
using Template.Application.Security;
using Template.Application.Services.Procurement;
using Template.Application.Workflow;
using Template.Application.Wrappers;
using Template.Domain.Approvals;
using Template.Domain.Common;

namespace Template.Application.Features.Approvals;

/// <summary>APR-004..009/014: approve or reject the current step. <see cref="Decision"/> is <c>Approved</c> or <c>Rejected</c>.</summary>
public sealed record DecideApprovalStepCommand(Guid Id, int Level, long? ExpectedVersion, string? Decision, string? Reason)
    : IRequest<Result<ApprovalInstanceView>>;

/// <summary>APR-010: the current assignee delegates the current step with a reason.</summary>
public sealed record DelegateApprovalStepCommand(Guid Id, int Level, long? ExpectedVersion, Guid DelegateUserId, string? Reason)
    : IRequest<Result<ApprovalInstanceView>>;

/// <summary>APR-015: administrative reassignment to a user or a role group, with a reason.</summary>
public sealed record ReassignApprovalStepCommand(Guid Id, int Level, long? ExpectedVersion, Guid? AssigneeUserId, string? AssigneeRole, string? Reason)
    : IRequest<Result<ApprovalInstanceView>>;

/// <summary>APR-015: administrative cancellation with a reason; the requisition returns to <c>InSourcing</c>.</summary>
public sealed record CancelApprovalCommand(Guid Id, long? ExpectedVersion, string? Reason) : IRequest<Result<ApprovalInstanceView>>;

public sealed class DecideApprovalStepCommandValidator : AbstractValidator<DecideApprovalStepCommand>
{
    public DecideApprovalStepCommandValidator()
    {
        RuleFor(command => command.Level).InclusiveBetween(1, ApprovalPolicy.MaxLevels);
        RuleFor(command => command.Decision).NotEmpty()
            .Must(value => value is "Approved" or "Rejected").WithMessage("decision accepts Approved or Rejected.");
        RuleFor(command => command.Reason).MaximumLength(MasterDataRules.ReasonMaxLength);
    }
}

public sealed class DelegateApprovalStepCommandValidator : AbstractValidator<DelegateApprovalStepCommand>
{
    public DelegateApprovalStepCommandValidator()
    {
        RuleFor(command => command.Level).InclusiveBetween(1, ApprovalPolicy.MaxLevels);
        RuleFor(command => command.DelegateUserId).NotEmpty();
    }
}

public sealed class ReassignApprovalStepCommandValidator : AbstractValidator<ReassignApprovalStepCommand>
{
    public ReassignApprovalStepCommandValidator()
    {
        RuleFor(command => command.Level).InclusiveBetween(1, ApprovalPolicy.MaxLevels);
        RuleFor(command => command)
            .Must(command => (command.AssigneeUserId is { } user && user != Guid.Empty) != !string.IsNullOrWhiteSpace(command.AssigneeRole))
            .WithName("assignee").WithMessage("Provide exactly one of assigneeUserId or assigneeRole.");
        RuleFor(command => command.AssigneeRole).MaximumLength(ApprovalPolicyLevel.RoleMaxLength);
    }
}

public sealed class ApprovalDecisionHandlers(
    IApprovalInstanceRepository instances,
    IApproverDirectory directory,
    IApprovalWorkflowClient workflow,
    ApprovalAccess access,
    ApprovalOutcomes outcomes,
    ProcurementWriter writer,
    ILogger<ApprovalDecisionHandlers> logger) :
    IRequestHandler<DecideApprovalStepCommand, Result<ApprovalInstanceView>>,
    IRequestHandler<DelegateApprovalStepCommand, Result<ApprovalInstanceView>>,
    IRequestHandler<ReassignApprovalStepCommand, Result<ApprovalInstanceView>>,
    IRequestHandler<CancelApprovalCommand, Result<ApprovalInstanceView>>
{
    /// <summary>Role groups an administrator may assign: the seeded roles that hold <c>approvals.decide</c>.</summary>
    public static readonly IReadOnlySet<string> AssignableRoleGroups = SeededRoles.PermissionsByRole
        .Where(entry => entry.Value.Contains(Permissions.ApprovalsDecide))
        .Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);

    public async Task<Result<ApprovalInstanceView>> Handle(DecideApprovalStepCommand request, CancellationToken cancellationToken)
    {
        var decision = request.Decision == "Approved" ? ApprovalDecision.Approved : ApprovalDecision.Rejected;
        for (var attempt = 1; ; attempt++)
        {
            var (instance, failure) = await LoadAsync(request.Id, cancellationToken);
            if (failure is not null)
                return failure;
            if (await access.ActorAsync(cancellationToken) is not { } actor)
                return ProcurementWriter.Forbidden();

            // APR-014: a decided step answers the repeated or conflicting decision before any precondition (SC-08).
            if (instance!.Steps.FirstOrDefault(step => step.Level == request.Level) is { Decision: not null })
            {
                var (_, decidedError) = instance.Decide(request.Level, actor, decision, request.Reason, Guid.NewGuid(), writer.UtcNow, writer.CorrelationId);
                return decidedError is not null ? decidedError.ToApplicationError() : await access.ViewAsync(instance, cancellationToken);
            }

            if (VersionPrecondition.Check(request.ExpectedVersion, instance.Version) is { } precondition)
                return precondition;
            if (await WorkflowUnavailableAsync(instance, cancellationToken) is { } unavailable)
                return unavailable;

            var decisionId = Guid.NewGuid();
            var (result, error) = instance.Decide(request.Level, actor, decision, request.Reason, decisionId, writer.UtcNow, writer.CorrelationId);
            if (error is not null)
                return await DenyAsync(instance, request.Level, error, cancellationToken);
            if (result == ApprovalDecisionResult.Repeated)
                return await access.ViewAsync(instance, cancellationToken);

            var step = instance.Steps.Single(candidate => candidate.Level == request.Level);
            writer.Audit(decision == ApprovalDecision.Approved ? ApprovalAudit.StepApproved : ApprovalAudit.StepRejected, ApprovalAudit.Resource, instance.Id,
                reason: step.Reason, ruleIds: decision == ApprovalDecision.Approved ? ["APR-004", "APR-005", "APR-007", "APR-008"] : ["APR-004", "APR-009"],
                details: new Dictionary<string, string>
                {
                    ["level"] = ApprovalAudit.Text(request.Level),
                    ["decisionId"] = decisionId.ToString(),
                    ["finalLevel"] = ApprovalAudit.Text(instance.FinalLevel)
                });
            var commit = await writer.CommitAsync(token => instances.GetVersionAsync(instance.Id, token), null, cancellationToken);
            if (commit is not null)
            {
                // A concurrent decision on the same step won the race: re-read once and answer APR-014.
                if (attempt == 1 && commit.Category == ErrorCategory.PreconditionFailed)
                    continue;
                return commit;
            }

            logger.LogInformation(LogEvents.ApprovalDecisionRecorded, "Approval {ApprovalInstanceId} level {Level} decided {Decision}",
                instance.Id, request.Level, decision);
            await SignalAsync(instance, ApprovalSignalKinds.Decision, decisionId, cancellationToken);
            return await access.ViewAsync(instance, cancellationToken);
        }
    }

    public async Task<Result<ApprovalInstanceView>> Handle(DelegateApprovalStepCommand request, CancellationToken cancellationToken)
    {
        var (instance, failure) = await LoadForChangeAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        if (await access.ActorAsync(cancellationToken) is not { } actor)
            return ProcurementWriter.Forbidden();
        var candidate = await directory.GetCandidateAsync(request.DelegateUserId, cancellationToken)
            ?? new ApprovalCandidate(request.DelegateUserId, false, false);
        if (instance!.Delegate(request.Level, actor, candidate, request.Reason, writer.UtcNow, writer.CorrelationId) is { } error)
            return error.ToApplicationError();

        writer.Audit(ApprovalAudit.StepDelegated, ApprovalAudit.Resource, instance.Id, reason: MasterDataRules.NormalizeReason(request.Reason),
            ruleIds: ["APR-010"], details: new Dictionary<string, string>
            {
                ["level"] = ApprovalAudit.Text(request.Level),
                ["delegatedBy"] = actor.UserId.ToString(),
                ["delegateUserId"] = candidate.UserId.ToString()
            });
        return await CommitAndSignalAsync(instance, ApprovalSignalKinds.Delegated, cancellationToken);
    }

    public async Task<Result<ApprovalInstanceView>> Handle(ReassignApprovalStepCommand request, CancellationToken cancellationToken)
    {
        var (instance, failure) = await LoadForChangeAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        if (writer.ActorId is not { } administrator)
            return ProcurementWriter.Forbidden();
        var role = request.AssigneeRole?.Trim();
        if (role is not null && !AssignableRoleGroups.Contains(role))
            return ApprovalErrors.InvalidDelegate.ToApplicationError() with { RuleId = "APR-015" };
        var assignee = role is not null ? ApprovalAssignee.ForRole(role) : ApprovalAssignee.ForUser(request.AssigneeUserId!.Value);
        var candidate = assignee.UserId is { } user ? await directory.GetCandidateAsync(user, cancellationToken) : null;
        if (instance!.Reassign(request.Level, administrator, assignee, candidate, request.Reason, writer.UtcNow, writer.CorrelationId) is { } error)
            return error.ToApplicationError();

        writer.Audit(ApprovalAudit.Administered, ApprovalAudit.Resource, instance.Id, reason: MasterDataRules.NormalizeReason(request.Reason),
            ruleIds: ["APR-015"], details: new Dictionary<string, string>
            {
                ["operation"] = "reassign",
                ["level"] = ApprovalAudit.Text(request.Level),
                ["assignee"] = assignee.UserId?.ToString() ?? $"role:{assignee.Role}"
            });
        return await CommitAndSignalAsync(instance, ApprovalSignalKinds.Reassigned, cancellationToken);
    }

    public async Task<Result<ApprovalInstanceView>> Handle(CancelApprovalCommand request, CancellationToken cancellationToken)
    {
        var (instance, failure) = await LoadForChangeAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (failure is not null)
            return failure;
        if (writer.ActorId is not { } administrator)
            return ProcurementWriter.Forbidden();
        var (events, error) = await outcomes.CancelAdministrativelyAsync(instance!, administrator, request.Reason, cancellationToken);
        if (error is not null)
            return error;

        var commit = await writer.CommitAsync(token => instances.GetVersionAsync(instance!.Id, token), null, cancellationToken);
        if (commit is not null)
            return commit;
        writer.Record(events);
        logger.LogInformation(LogEvents.ApprovalEnded, "Approval {ApprovalInstanceId} ended as {Status}", instance!.Id, instance.Status);
        await SignalAsync(instance, ApprovalSignalKinds.Ended, Guid.NewGuid(), cancellationToken);
        return await access.ViewAsync(instance, cancellationToken);
    }

    /// <summary>
    /// APR-005 audit: a separation-of-duties or assignment denial is recorded (<c>approval.decision.denied</c>) in its own
    /// commit, then returned; the instance itself is unchanged.
    /// </summary>
    private async Task<ApplicationError> DenyAsync(ApprovalInstance instance, int level, DomainError error, CancellationToken cancellationToken)
    {
        if (error.Kind != DomainErrorKind.Forbidden)
            return error.ToApplicationError();
        writer.Audit(ApprovalAudit.DecisionDenied, ApprovalAudit.Resource, instance.Id, ruleIds: error.RuleId is null ? null : [error.RuleId],
            details: new Dictionary<string, string> { ["level"] = ApprovalAudit.Text(level), ["code"] = error.Code });
        await writer.CommitAsync(_ => Task.FromResult<long?>(null), null, cancellationToken);
        logger.LogInformation(LogEvents.ApprovalDecisionDenied, "Approval {ApprovalInstanceId} level {Level} decision denied with {Code}",
            instance.Id, level, error.Code);
        return error.ToApplicationError();
    }

    private async Task<Result<ApprovalInstanceView>> CommitAndSignalAsync(ApprovalInstance instance, string kind, CancellationToken cancellationToken)
    {
        var events = writer.Stage(instance.DomainEvents);
        var commit = await writer.CommitAsync(token => instances.GetVersionAsync(instance.Id, token), null, cancellationToken);
        if (commit is not null)
            return commit;
        writer.Record(events);
        instance.ClearDomainEvents();
        await SignalAsync(instance, kind, Guid.NewGuid(), cancellationToken);
        return await access.ViewAsync(instance, cancellationToken);
    }

    /// <summary>
    /// NFR availability: a decision on a started instance needs a reachable WorkflowFunctions host; otherwise 503
    /// <c>approval.workflow-unavailable</c> and nothing changes.
    /// </summary>
    private async Task<ApplicationError?> WorkflowUnavailableAsync(ApprovalInstance instance, CancellationToken cancellationToken)
    {
        try
        {
            await workflow.GetStatusAsync(instance.AwardId, cancellationToken);
            return null;
        }
        catch (ApprovalWorkflowUnavailableException)
        {
            return new ApplicationError(ProblemCodes.ApprovalWorkflowUnavailable, ErrorCategory.Unavailable,
                "The approval workflow is temporarily unavailable; nothing was changed. Try again later.", "APR-016");
        }
    }

    /// <summary>Best effort after the commit: a lost signal is repaired by the reconciliation sweep (APR-016).</summary>
    private async Task SignalAsync(ApprovalInstance instance, string kind, Guid signalId, CancellationToken cancellationToken)
    {
        try
        {
            await workflow.SignalAsync(instance.AwardId, new ApprovalSignal(signalId, kind, writer.CorrelationId), cancellationToken);
        }
        catch (ApprovalWorkflowUnavailableException)
        {
            logger.LogWarning(LogEvents.ApprovalSignalFailed, "Approval {ApprovalInstanceId} signal {Kind} was not delivered", instance.Id, kind);
        }
    }

    private async Task<(ApprovalInstance? Instance, ApplicationError? Error)> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var instance = await instances.FindAsync(id, cancellationToken);
        return instance is null || !await access.CanReadAsync(instance, cancellationToken) ? (null, ApprovalAudit.NotFound()) : (instance, null);
    }

    private async Task<(ApprovalInstance? Instance, ApplicationError? Error)> LoadForChangeAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken)
    {
        var (instance, failure) = await LoadAsync(id, cancellationToken);
        if (failure is not null)
            return (null, failure);
        return VersionPrecondition.Check(expectedVersion, instance!.Version) is { } precondition ? (null, precondition) : (instance, null);
    }
}
