#nullable enable
using MediatR;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Features.Budgeting;
using Template.Application.Interfaces.Budgeting;
using Template.Application.Interfaces.Requisitions;
using Template.Application.Security;
using Template.Application.Services.MasterData;
using Template.Application.Services.Procurement;
using Template.Domain.Requisitions;

namespace Template.Application.Features.Requisitions;

public sealed record GetRequisitionQuery(Guid Id) : IRequest<Result<RequisitionView>>;

/// <summary>
/// Requisition list. <see cref="Scope"/> is <c>mine</c> (default: the actor's own requisitions) or <c>visible</c>
/// (everything the actor may read, REQ-011); <see cref="Status"/> is a comma-separated list; <see cref="Q"/> matches the
/// requisition number; <see cref="From"/>/<see cref="To"/> filter the creation instant as [from, to).
/// </summary>
public sealed record ListRequisitionsQuery(
    int? Page = null,
    int? PageSize = null,
    string? Sort = null,
    string? Scope = null,
    string? Status = null,
    Guid? CostCenterId = null,
    Guid? RequesterId = null,
    string? Q = null,
    DateTime? From = null,
    DateTime? To = null) : IRequest<Result<PagedResult<RequisitionSummaryView>>>;

/// <summary>Full state and reservation history of a readable requisition (NFR-AUD-2, AUD-004).</summary>
public sealed record GetRequisitionTimelineQuery(Guid Id) : IRequest<Result<RequisitionTimelineView>>;

/// <summary>
/// BUD-012: availability for the requisition's cost center and currency today. The requester sees it for an own draft;
/// <c>budget.read</c> holders for any readable requisition.
/// </summary>
public sealed record GetRequisitionBudgetAvailabilityQuery(Guid Id) : IRequest<Result<BudgetAvailabilityView>>;

public sealed class RequisitionQueryHandlers(
    IPurchaseRequisitionRepository requisitions,
    IBudgetAllocationRepository allocations,
    IFiscalPeriodRepository periods,
    RequisitionAccess access,
    ProcurementWriter writer) :
    IRequestHandler<GetRequisitionQuery, Result<RequisitionView>>,
    IRequestHandler<ListRequisitionsQuery, Result<PagedResult<RequisitionSummaryView>>>,
    IRequestHandler<GetRequisitionTimelineQuery, Result<RequisitionTimelineView>>,
    IRequestHandler<GetRequisitionBudgetAvailabilityQuery, Result<BudgetAvailabilityView>>
{
    public const string DefaultSort = "-createdAt";
    public const string ScopeMine = "mine";
    public const string ScopeVisible = "visible";

    public async Task<Result<RequisitionView>> Handle(GetRequisitionQuery request, CancellationToken cancellationToken)
    {
        if (!await access.MayReadAnyAsync(cancellationToken))
            return ProcurementWriter.Forbidden();
        var requisition = await requisitions.FindAsync(request.Id, cancellationToken);
        return requisition is not null && await access.CanReadAsync(requisition, cancellationToken)
            ? await access.ViewAsync(requisition, cancellationToken)
            : RequisitionAudit.NotFound();
    }

    public async Task<Result<PagedResult<RequisitionSummaryView>>> Handle(ListRequisitionsQuery request, CancellationToken cancellationToken)
    {
        if (!await access.MayReadAnyAsync(cancellationToken))
            return ProcurementWriter.Forbidden();
        var page = PageRequest.Create(request.Page, request.PageSize, request.Sort, IPurchaseRequisitionRepository.SortFields, DefaultSort);
        if (!page.IsSuccess)
            return page.Error!;
        var scope = string.IsNullOrWhiteSpace(request.Scope) ? ScopeMine : request.Scope.Trim().ToLowerInvariant();
        if (scope is not (ScopeMine or ScopeVisible))
            return ApplicationError.Validation("scope", "scope accepts mine or visible.");
        if (MasterDataQueries.InvalidQ(request.Q) is { } invalidQ)
            return invalidQ;
        var statuses = ProcurementQueries.ParseEnumList<RequisitionStatus>(request.Status, "status");
        if (!statuses.IsSuccess)
            return statuses.Error!;
        if (request.From is { Kind: not DateTimeKind.Utc } || request.To is { Kind: not DateTimeKind.Utc })
            return ApplicationError.Validation("from", "from and to must be UTC instants (ISO 8601 with Z).");

        var visibility = await access.VisibilityAsync(cancellationToken);
        var filter = new RequisitionFilter(
            visibility,
            scope == ScopeMine ? visibility.UserId : request.RequesterId,
            statuses.Value,
            request.CostCenterId,
            MasterDataQueries.Trim(request.Q)?.ToUpperInvariant(),
            request.From,
            request.To);
        return MasterDataQueries.Map(await requisitions.ListAsync(filter, page.Value, cancellationToken), RequisitionSummaryView.From);
    }

    public async Task<Result<RequisitionTimelineView>> Handle(GetRequisitionTimelineQuery request, CancellationToken cancellationToken)
    {
        if (!await access.MayReadAnyAsync(cancellationToken))
            return ProcurementWriter.Forbidden();
        var requisition = await requisitions.FindAsync(request.Id, cancellationToken);
        if (requisition is null || !await access.CanReadAsync(requisition, cancellationToken))
            return RequisitionAudit.NotFound();

        return RequisitionTimelineView.From(requisition, await allocations.ListReservationsOfRequisitionAsync(requisition.Id, cancellationToken));
    }

    public async Task<Result<BudgetAvailabilityView>> Handle(GetRequisitionBudgetAvailabilityQuery request, CancellationToken cancellationToken)
    {
        if (!await access.MayReadAnyAsync(cancellationToken) && !await writer.HasPermissionAsync(Permissions.BudgetRead, cancellationToken))
            return ProcurementWriter.Forbidden();
        var requisition = await requisitions.FindAsync(request.Id, cancellationToken);
        if (requisition is null || !await access.CanReadAsync(requisition, cancellationToken))
            return RequisitionAudit.NotFound();
        var ownDraft = access.IsRequester(requisition) && requisition.Status == RequisitionStatus.Draft;
        if (!ownDraft && !await writer.HasPermissionAsync(Permissions.BudgetRead, cancellationToken))
            return ProcurementWriter.Forbidden("Budget availability is visible to budget.read holders or to the requester of an own draft (BUD-012).");

        return await BudgetAvailability.ReadAsync(
            periods, allocations, requisition.CostCenterId, requisition.Currency, await writer.TodayAsync(cancellationToken), cancellationToken);
    }
}
