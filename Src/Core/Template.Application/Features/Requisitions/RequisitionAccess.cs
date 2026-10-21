#nullable enable
using Template.Application.Interfaces.Requisitions;
using Template.Application.Security;
using Template.Application.Services.Procurement;
using Template.Domain.Requisitions;

namespace Template.Application.Features.Requisitions;

/// <summary>
/// Resource-scoped authorization of requisitions after the endpoint permission check (docs/product/permissions.md §1):
/// visibility (REQ-011, unreadable IDs answer 404), draft ownership with the <c>requisitions.edit-any</c> proxy
/// (REQ-001), own-only submission/withdrawal (REQ-004, REQ-007) and own/any cancellation (REQ-008). It never bypasses a
/// business rule; the aggregate still decides whether the state allows the change.
/// </summary>
public sealed class RequisitionAccess(ProcurementWriter writer, IOrganizationScopeReader organizationScope)
{
    private RequisitionVisibility? visibility;

    public async Task<RequisitionVisibility> VisibilityAsync(CancellationToken cancellationToken)
    {
        if (visibility is not null)
            return visibility;
        var actor = writer.ActorId ?? Guid.Empty;
        var permissions = await writer.PermissionsAsync(cancellationToken);
        var scope = permissions.Contains(Permissions.RequisitionsReadTeam) && actor != Guid.Empty
            ? await organizationScope.GetScopeAsync(actor, cancellationToken)
            : new OrganizationScope([], []);
        return visibility = new RequisitionVisibility(actor, permissions.Contains(Permissions.RequisitionsReadAll), scope.ManagedCostCenterIds, scope.HeadedDepartmentIds);
    }

    /// <summary>Whether the actor holds any requisition permission at all (otherwise collection reads answer 403).</summary>
    public async Task<bool> MayReadAnyAsync(CancellationToken cancellationToken)
    {
        var permissions = await writer.PermissionsAsync(cancellationToken);
        return permissions.Contains(Permissions.RequisitionsCreate) || permissions.Contains(Permissions.RequisitionsReadTeam)
            || permissions.Contains(Permissions.RequisitionsReadAll);
    }

    public async Task<bool> CanReadAsync(PurchaseRequisition requisition, CancellationToken cancellationToken) =>
        (await VisibilityAsync(cancellationToken)).CanRead(requisition.RequesterId, requisition.CostCenterId, requisition.DepartmentId);

    public bool IsRequester(PurchaseRequisition requisition) => writer.ActorId == requisition.RequesterId;

    /// <summary>REQ-001: the requester (with <c>requisitions.create</c>) or a <c>requisitions.edit-any</c> proxy.</summary>
    public async Task<bool> CanEditAsync(PurchaseRequisition requisition, CancellationToken cancellationToken)
    {
        var permissions = await writer.PermissionsAsync(cancellationToken);
        return IsRequester(requisition) && permissions.Contains(Permissions.RequisitionsCreate)
            || permissions.Contains(Permissions.RequisitionsEditAny);
    }

    /// <summary>REQ-004/REQ-007: own requisitions with <c>requisitions.submit</c>.</summary>
    public async Task<bool> CanSubmitAsync(PurchaseRequisition requisition, CancellationToken cancellationToken) =>
        IsRequester(requisition) && await writer.HasPermissionAsync(Permissions.RequisitionsSubmit, cancellationToken);

    /// <summary>REQ-008 authority: cancel-any wins; otherwise the requester with cancel-own; otherwise none.</summary>
    public async Task<CancellationAuthority?> CancellationAuthorityAsync(PurchaseRequisition requisition, CancellationToken cancellationToken)
    {
        var permissions = await writer.PermissionsAsync(cancellationToken);
        if (permissions.Contains(Permissions.RequisitionsCancelAny))
            return CancellationAuthority.CancelAny;
        return IsRequester(requisition) && permissions.Contains(Permissions.RequisitionsCancelOwn) ? CancellationAuthority.Requester : null;
    }

    /// <summary>Actions the server would accept now from this actor (state and permission only; content guards still apply).</summary>
    public async Task<IReadOnlyList<string>> AllowedActionsAsync(PurchaseRequisition requisition, CancellationToken cancellationToken)
    {
        var actions = new List<string>();
        var canEdit = await CanEditAsync(requisition, cancellationToken);
        var canSubmit = await CanSubmitAsync(requisition, cancellationToken);
        if (requisition.Status == RequisitionStatus.Draft && canEdit)
        {
            actions.Add(RequisitionAudit.ActionEdit);
            if (requisition.EnsureDiscardable() is null)
                actions.Add(RequisitionAudit.ActionDiscard);
        }

        if (!requisition.IsTerminal && canEdit)
            actions.Add(RequisitionAudit.ActionAttach);
        if (requisition.Status == RequisitionStatus.Draft && canSubmit)
            actions.Add(RequisitionAudit.ActionSubmit);
        if (requisition.Status == RequisitionStatus.Submitted && canSubmit)
            actions.Add(RequisitionAudit.ActionWithdraw);

        var authority = await CancellationAuthorityAsync(requisition, cancellationToken);
        var cancellable = authority == CancellationAuthority.CancelAny
            ? requisition.Status is RequisitionStatus.Draft or RequisitionStatus.Submitted or RequisitionStatus.InSourcing
                or RequisitionStatus.PendingApproval or RequisitionStatus.Approved
            : authority == CancellationAuthority.Requester
              && requisition.Status is RequisitionStatus.Draft or RequisitionStatus.Submitted or RequisitionStatus.InSourcing;
        if (cancellable)
            actions.Add(RequisitionAudit.ActionCancel);
        if (requisition.IsTerminal && await writer.HasPermissionAsync(Permissions.RequisitionsCreate, cancellationToken))
            actions.Add(RequisitionAudit.ActionCopy);
        return actions;
    }

    public async Task<RequisitionView> ViewAsync(PurchaseRequisition requisition, CancellationToken cancellationToken) =>
        RequisitionView.From(requisition, await AllowedActionsAsync(requisition, cancellationToken));
}
