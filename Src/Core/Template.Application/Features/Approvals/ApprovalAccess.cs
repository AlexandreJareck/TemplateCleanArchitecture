#nullable enable
using Template.Application.Common.Concurrency;
using Template.Application.Interfaces.Approvals;
using Template.Application.Interfaces.Sourcing;
using Template.Application.Security;
using Template.Application.Services.Procurement;
using Template.Domain.Approvals;

namespace Template.Application.Features.Approvals;

/// <summary>
/// Approval authorization after the endpoint permission check (docs/product/permissions.md): an instance is readable by
/// <c>approvals.read-all</c> holders and by its participants (the requester, the submitting buyer, every assignee,
/// decider and member of an assigned role group). Allowed actions are computed from state, assignment, permissions and
/// separation of duties; the decision use case enforces the same rules again (APR-004..008, APR-015).
/// </summary>
public sealed class ApprovalAccess(IApproverDirectory directory, IRequestForQuotationRepository rfqs, ProcurementWriter writer)
{
    private IReadOnlySet<string>? roles;

    public async Task<IReadOnlySet<string>> RolesAsync(CancellationToken cancellationToken) =>
        roles ??= writer.ActorId is { } actor ? await directory.GetRolesAsync(actor, cancellationToken) : new HashSet<string>();

    public Task<bool> MayReadAllAsync(CancellationToken cancellationToken) => writer.HasPermissionAsync(Permissions.ApprovalsReadAll, cancellationToken);

    public async Task<bool> CanReadAsync(ApprovalInstance instance, CancellationToken cancellationToken)
    {
        if (await MayReadAllAsync(cancellationToken))
            return true;
        if (writer.ActorId is not { } actor)
            return false;
        if (instance.RequesterId == actor || instance.SubmittedBy == actor)
            return true;
        var actorGroups = await RolesAsync(cancellationToken);
        return instance.Steps.Any(step => step.AssigneeUserId == actor || step.DecidedBy == actor || (step.AssigneeRole is { } role && actorGroups.Contains(role)))
            || instance.History.Any(entry => entry.ActorId == actor || entry.AssigneeUserId == actor);
    }

    public async Task<ApprovalActor?> ActorAsync(CancellationToken cancellationToken) =>
        writer.ActorId is { } actor
            ? new ApprovalActor(actor, await RolesAsync(cancellationToken), await writer.HasPermissionAsync(Permissions.ApprovalsDecide, cancellationToken))
            : null;

    /// <summary>Actions the server would accept now from this actor.</summary>
    public async Task<IReadOnlyList<string>> AllowedActionsAsync(ApprovalInstance instance, CancellationToken cancellationToken)
    {
        if (instance.NextAction != ApprovalNextAction.Wait || await ActorAsync(cancellationToken) is not { } actor)
            return [];
        var permissions = await writer.PermissionsAsync(cancellationToken);
        var step = instance.CurrentStep!;
        var actions = new List<string>();
        var assignee = step.AssigneeUserId == actor.UserId || (step.AssigneeRole is { } role && actor.Roles.Contains(role));
        if (assignee && actor.CanDecide && instance.Steps.All(other => other.Level == step.Level || other.DecidedBy != actor.UserId))
        {
            if (instance.IsEligible(actor.UserId, step.Level))
                actions.Add(ApprovalActions.Approve);
            actions.Add(ApprovalActions.Reject);
        }

        if (assignee && permissions.Contains(Permissions.ApprovalsDelegate))
            actions.Add(ApprovalActions.Delegate);
        if (permissions.Contains(Permissions.ApprovalsAdminister))
            actions.AddRange([ApprovalActions.Reassign, ApprovalActions.Cancel]);
        return actions;
    }

    public async Task<ApprovalInstanceView> ViewAsync(ApprovalInstance instance, CancellationToken cancellationToken)
    {
        var ids = instance.Steps.SelectMany(step => new[] { step.AssigneeUserId, step.DecidedBy }).Append(instance.RequesterId)
            .OfType<Guid>().Distinct().ToArray();
        var names = await directory.GetDisplayNamesAsync(ids, cancellationToken);
        var rfq = await rfqs.FindAsync(instance.RfqId, cancellationToken);
        return new ApprovalInstanceView(
            instance.Id, VersionPrecondition.Format(instance.Version), instance.Status.ToString(), instance.RequisitionId, instance.RequisitionNumber,
            instance.RequesterId, ApprovalViewMapping.Name(names, instance.RequesterId),
            ApprovalViewMapping.Money(instance.AmountEvaluated, instance.BaseCurrency), ApprovalViewMapping.Money(instance.RequisitionAmount, instance.RequisitionCurrency),
            instance.PolicyReference, instance.CurrentLevel, instance.FinalLevel, instance.NextAction.ToString(), instance.StartedAtUtc, instance.EndedAtUtc,
            instance.EndCause, instance.EndReason,
            instance.Steps.OrderBy(step => step.Level).Select(step => ApprovalViewMapping.Step(step, names)).ToArray(),
            ApprovalViewMapping.Award(instance, rfq), await AllowedActionsAsync(instance, cancellationToken));
    }
}
