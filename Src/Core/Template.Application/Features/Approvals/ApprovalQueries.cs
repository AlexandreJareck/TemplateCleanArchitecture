#nullable enable
using MediatR;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Interfaces.Approvals;
using Template.Application.Security;
using Template.Application.Services.MasterData;
using Template.Application.Services.Procurement;
using Template.Domain.Approvals;

namespace Template.Application.Features.Approvals;

/// <summary>
/// <see cref="AssignedTo"/> <c>me</c> lists instances whose current pending step is assigned to the actor or one of the
/// actor's role groups (inbox, <c>approvals.decide</c>); without it, <c>approvals.read-all</c> lists every instance and
/// other actors see the instances they participate in.
/// </summary>
public sealed record ListApprovalInstancesQuery(
    int? Page, int? PageSize, string? Sort, string? Status, string? AssignedTo, Guid? RequisitionId, bool? Escalated)
    : IRequest<Result<PagedResult<ApprovalSummaryView>>>;

public sealed record GetApprovalInstanceQuery(Guid Id) : IRequest<Result<ApprovalInstanceView>>;

public sealed record GetApprovalHistoryQuery(Guid Id) : IRequest<Result<ApprovalHistoryView>>;

/// <summary>Delegation targets for the current step (APR-010): active approvers with their eligibility.</summary>
public sealed record SearchApprovalDelegatesQuery(Guid Id, int Level, string? Q, int? Page, int? PageSize)
    : IRequest<Result<PagedResult<ApprovalDelegateView>>>;

/// <summary>Durable runtime status next to the database status (APR-016, <c>approvals.administer</c>).</summary>
public sealed record GetApprovalWorkflowQuery(Guid Id) : IRequest<Result<ApprovalWorkflowView>>;

public sealed record ApprovalDelegateView(Guid UserId, string UserName, string? DisplayName, bool Eligible, string? IneligibleReason);

public sealed class ApprovalQueryHandlers(
    IApprovalInstanceRepository instances,
    IApproverDirectory directory,
    IApprovalWorkflowClient workflow,
    ApprovalAccess access,
    ProcurementWriter writer) :
    IRequestHandler<ListApprovalInstancesQuery, Result<PagedResult<ApprovalSummaryView>>>,
    IRequestHandler<GetApprovalInstanceQuery, Result<ApprovalInstanceView>>,
    IRequestHandler<GetApprovalHistoryQuery, Result<ApprovalHistoryView>>,
    IRequestHandler<SearchApprovalDelegatesQuery, Result<PagedResult<ApprovalDelegateView>>>,
    IRequestHandler<GetApprovalWorkflowQuery, Result<ApprovalWorkflowView>>
{
    private const string DefaultSort = "-startedAt";

    public async Task<Result<PagedResult<ApprovalSummaryView>>> Handle(ListApprovalInstancesQuery request, CancellationToken cancellationToken)
    {
        if (writer.ActorId is not { } actor)
            return ProcurementWriter.Forbidden();
        var page = PageRequest.Create(request.Page, request.PageSize, request.Sort, IApprovalInstanceRepository.SortFields, DefaultSort);
        if (!page.IsSuccess)
            return page.Error!;
        var statuses = ProcurementQueries.ParseEnumList<ApprovalStatus>(request.Status, "status");
        if (!statuses.IsSuccess)
            return statuses.Error!;
        if (request.AssignedTo is { Length: > 0 } assignedTo && !string.Equals(assignedTo, "me", StringComparison.OrdinalIgnoreCase))
            return ApplicationError.Validation("assignedTo", "assignedTo accepts me.");

        var roles = (await access.RolesAsync(cancellationToken)).ToArray();
        ApprovalInstanceFilter filter;
        if (request.AssignedTo is { Length: > 0 })
        {
            if (!await writer.HasPermissionAsync(Permissions.ApprovalsDecide, cancellationToken))
                return ProcurementWriter.Forbidden("The approval inbox requires approvals.decide.");
            filter = new ApprovalInstanceFilter(statuses.Value ?? [ApprovalStatus.Pending], actor, roles, RequisitionId: request.RequisitionId,
                Escalated: request.Escalated);
        }
        else
        {
            var readAll = await access.MayReadAllAsync(cancellationToken);
            filter = new ApprovalInstanceFilter(statuses.Value, ParticipantUserId: readAll ? null : actor, ParticipantGroups: readAll ? null : roles,
                RequisitionId: request.RequisitionId, Escalated: request.Escalated);
        }

        return MasterDataQueries.Map(await instances.ListAsync(filter, page.Value, cancellationToken), ApprovalSummaryView.From);
    }

    public async Task<Result<ApprovalInstanceView>> Handle(GetApprovalInstanceQuery request, CancellationToken cancellationToken)
    {
        var instance = await instances.FindAsync(request.Id, cancellationToken);
        return instance is null || !await access.CanReadAsync(instance, cancellationToken)
            ? ApprovalAudit.NotFound()
            : await access.ViewAsync(instance, cancellationToken);
    }

    public async Task<Result<ApprovalHistoryView>> Handle(GetApprovalHistoryQuery request, CancellationToken cancellationToken)
    {
        var instance = await instances.FindAsync(request.Id, cancellationToken);
        if (instance is null || !await access.CanReadAsync(instance, cancellationToken))
            return ApprovalAudit.NotFound();
        var ids = instance.History.SelectMany(entry => new[] { entry.ActorId, entry.AssigneeUserId }).OfType<Guid>().Distinct().ToArray();
        var names = await directory.GetDisplayNamesAsync(ids, cancellationToken);
        return new ApprovalHistoryView(instance.Id, instance.History.OrderBy(entry => entry.Sequence)
            .Select(entry => new ApprovalHistoryEntryView(
                entry.Sequence, entry.Action, entry.Level, entry.ActorId, ApprovalViewMapping.Name(names, entry.ActorId), entry.AssigneeUserId,
                entry.AssigneeRole, entry.AtUtc, entry.Reason, entry.CorrelationId))
            .ToArray());
    }

    public async Task<Result<PagedResult<ApprovalDelegateView>>> Handle(SearchApprovalDelegatesQuery request, CancellationToken cancellationToken)
    {
        var instance = await instances.FindAsync(request.Id, cancellationToken);
        if (instance is null || !await access.CanReadAsync(instance, cancellationToken))
            return ApprovalAudit.NotFound();
        var allowed = await access.AllowedActionsAsync(instance, cancellationToken);
        if (request.Level != instance.CurrentLevel || !(allowed.Contains(ApprovalActions.Delegate) || allowed.Contains(ApprovalActions.Reassign)))
            return ProcurementWriter.Forbidden("Only the current assignee or an approval administrator lists delegation targets.");
        if (MasterDataQueries.InvalidQ(request.Q) is { } invalidQ)
            return invalidQ;
        var page = PageRequest.Create(request.Page, request.PageSize, null, ["userName"], "userName");
        if (!page.IsSuccess)
            return page.Error!;

        var step = instance.CurrentStep!;
        var users = await directory.SearchApproversAsync(MasterDataQueries.Trim(request.Q), page.Value, cancellationToken);
        return MasterDataQueries.Map(users, user =>
        {
            var reason = user.UserId == writer.ActorId || user.UserId == step.AssigneeUserId ? "current-assignee"
                : !instance.IsEligible(user.UserId, step.Level) ? "separation-of-duties"
                : null;
            return new ApprovalDelegateView(user.UserId, user.UserName, user.DisplayName, reason is null, reason);
        });
    }

    public async Task<Result<ApprovalWorkflowView>> Handle(GetApprovalWorkflowQuery request, CancellationToken cancellationToken)
    {
        var instance = await instances.FindAsync(request.Id, cancellationToken);
        if (instance is null)
            return ApprovalAudit.NotFound();
        try
        {
            var status = await workflow.GetStatusAsync(instance.AwardId, cancellationToken);
            return new ApprovalWorkflowView(instance.Id, instance.WorkflowInstanceId, instance.Status.ToString(), instance.NextAction.ToString(),
                status.RuntimeStatus.ToString(), status.Version, status.CreatedAtUtc, status.LastUpdatedAtUtc);
        }
        catch (ApprovalWorkflowUnavailableException)
        {
            return new ApplicationError(ProblemCodes.ApprovalWorkflowUnavailable, Wrappers.ErrorCategory.Unavailable,
                "The approval workflow is temporarily unavailable.", "APR-016");
        }
    }
}
