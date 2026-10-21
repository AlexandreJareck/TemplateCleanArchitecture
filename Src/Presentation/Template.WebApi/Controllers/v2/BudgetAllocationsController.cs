using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Features.Budgeting;
using Template.Application.Security;
using Template.WebApi.Infrastructure.Http;
using Template.WebApi.Infrastructure.Security;

namespace Template.WebApi.Controllers.v2;

/// <summary>
/// Budget allocations, availability and reservation visibility (BUD-002..012). Reservations are created and released
/// only by requisition transitions; this resource never exposes a direct reserve or release.
/// </summary>
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/budget-allocations")]
[StableProblemCodes]
[NoStoreSecurityHeaders]
[Produces("application/json")]
public sealed class BudgetAllocationsController(ISender sender, ApiResultMapper resultMapper) : MasterDataControllerBase(sender, resultMapper)
{
    /// <summary>List allocations with server-computed reserved, committed and available totals (CALC-003).</summary>
    /// <param name="page">Page number, 1 or greater (default 1).</param>
    /// <param name="pageSize">Items per page, 1–100 (default 20).</param>
    /// <param name="sort">Comma-separated <c>createdAt</c>, <c>updatedAt</c>, <c>currency</c>, <c>amount</c>, <c>available</c>; prefix <c>-</c> for descending (default <c>-createdAt</c>).</param>
    /// <param name="fiscalPeriodId">Fiscal period.</param>
    /// <param name="costCenterId">Cost center.</param>
    /// <param name="currency">ISO 4217 currency.</param>
    /// <param name="status"><c>Active</c> or <c>Closed</c>.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet]
    [HasPermission(Permissions.BudgetRead)]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(PagedResult<BudgetAllocationView>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    public Task<IActionResult> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] Guid? fiscalPeriodId, [FromQuery] Guid? costCenterId,
        [FromQuery] string? currency, [FromQuery] string? status, CancellationToken cancellationToken) =>
        SendAsync(new ListBudgetAllocationsQuery(page, pageSize, sort, fiscalPeriodId, costCenterId, currency, status), Ok, cancellationToken);

    /// <summary>Availability of a cost center and currency on a local date (default today, organization time zone) (BUD-012).</summary>
    /// <param name="costCenterId">Cost center.</param>
    /// <param name="currency">ISO 4217 currency.</param>
    /// <param name="date">Local date <c>YYYY-MM-DD</c>; defaults to today.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("availability")]
    [HasPermission(Permissions.BudgetRead)]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(BudgetAvailabilityView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    public Task<IActionResult> Availability([FromQuery] Guid costCenterId, [FromQuery] string? currency, [FromQuery] DateOnly? date, CancellationToken cancellationToken) =>
        SendAsync(new GetBudgetAvailabilityQuery(costCenterId, currency, date), Ok, cancellationToken);

    /// <summary>Get one allocation with its amount history; the response carries its <c>ETag</c>.</summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.BudgetRead)]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(BudgetAllocationView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new GetBudgetAllocationQuery(id), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Reservations of one allocation with their immutable monetary history.</summary>
    /// <param name="id">Allocation.</param>
    /// <param name="page">Page number, 1 or greater (default 1).</param>
    /// <param name="pageSize">Items per page, 1–100 (default 20).</param>
    /// <param name="sort">Comma-separated <c>createdAt</c>, <c>amount</c>, <c>status</c> (default <c>-createdAt</c>).</param>
    /// <param name="status">Comma-separated <c>Reserved</c>, <c>Committed</c>, <c>Released</c>.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("{id:guid}/reservations")]
    [HasPermission(Permissions.BudgetRead)]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.NotFound, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(PagedResult<BudgetReservationView>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<IActionResult> Reservations(
        Guid id, [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? status, CancellationToken cancellationToken) =>
        SendAsync(new ListBudgetReservationsQuery(id, page, pageSize, sort, status), Ok, cancellationToken);

    /// <summary>Create an allocation unique per (period, cost center, currency) on an active cost center (BUD-002). Requires <c>Idempotency-Key</c>.</summary>
    [HttpPost]
    [HasPermission(Permissions.BudgetAllocationsManage)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.IdempotencyKeyReused, ProblemCodes.IdempotencyInProgress,
        ProblemCodes.BudgetAllocationDuplicate, ProblemCodes.BudgetPeriodClosed, ProblemCodes.OrganizationInactive, ProblemCodes.SessionExpired,
        ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(BudgetAllocationView), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<IActionResult> Create([FromBody] CreateBudgetAllocationRequest request, CancellationToken cancellationToken) =>
        SendAsync(new CreateBudgetAllocationCommand(request.FiscalPeriodId, request.CostCenterId, request.Currency, request.Amount),
            view => CreatedVersioned($"/api/v2/budget-allocations/{view.Id}", view, view.Version), cancellationToken);

    /// <summary>Change the amount with a reason; a decrease cannot go below reserved + committed (BUD-003). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/amount-changes")]
    [HasPermission(Permissions.BudgetAllocationsManage)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.ReasonRequired, ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.BudgetBelowReserved,
        ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(BudgetAllocationView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<IActionResult> ChangeAmount(Guid id, [FromBody] ChangeBudgetAllocationAmountRequest request, CancellationToken cancellationToken) =>
        SendAsync(new ChangeBudgetAllocationAmountCommand(id, IfMatch, request.Amount, request.Reason), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Close an allocation without <c>Reserved</c> reservations, with reason (BUD-011). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/close")]
    [HasPermission(Permissions.BudgetAllocationsManage)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.ReasonRequired, ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.BudgetAllocationHasOpenReservations,
        ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(BudgetAllocationView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<IActionResult> Close(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReasonRequest? request, CancellationToken cancellationToken) =>
        SendAsync(new CloseBudgetAllocationCommand(id, IfMatch, request?.Reason), view => Versioned(view, view.Version), cancellationToken);
}
