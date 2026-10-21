using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Features.Budgeting;
using Template.Application.Security;
using Template.WebApi.Infrastructure.Http;
using Template.WebApi.Infrastructure.Security;

namespace Template.WebApi.Controllers.v2;

/// <summary>Fiscal periods (BUD-001, BUD-011): <c>Planned → Open → Closed</c>; never deleted.</summary>
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/fiscal-periods")]
[StableProblemCodes]
[NoStoreSecurityHeaders]
[Produces("application/json")]
public sealed class FiscalPeriodsController(ISender sender, ApiResultMapper resultMapper) : MasterDataControllerBase(sender, resultMapper)
{
    /// <summary>List fiscal periods.</summary>
    /// <param name="page">Page number, 1 or greater (default 1).</param>
    /// <param name="pageSize">Items per page, 1–100 (default 20).</param>
    /// <param name="sort">Comma-separated <c>code</c>, <c>startDate</c>, <c>status</c>, <c>createdAt</c>, <c>updatedAt</c>; prefix <c>-</c> for descending (default <c>-startDate</c>).</param>
    /// <param name="q">Case-insensitive text matched against code and name.</param>
    /// <param name="status">Comma-separated <c>Planned</c>, <c>Open</c>, <c>Closed</c>.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet]
    [HasPermission(Permissions.BudgetRead)]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(PagedResult<FiscalPeriodView>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    public Task<IActionResult> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? q, [FromQuery] string? status,
        CancellationToken cancellationToken) =>
        SendAsync(new ListFiscalPeriodsQuery(page, pageSize, sort, q, status), Ok, cancellationToken);

    /// <summary>Get one fiscal period; the response carries its <c>ETag</c>.</summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.BudgetRead)]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(FiscalPeriodView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new GetFiscalPeriodQuery(id), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Create a <c>Planned</c> period with a unique code and a non-overlapping local-date range (BUD-001). Requires <c>Idempotency-Key</c>.</summary>
    [HttpPost]
    [HasPermission(Permissions.BudgetPeriodsManage)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.IdempotencyKeyReused, ProblemCodes.IdempotencyInProgress,
        ProblemCodes.BudgetPeriodCodeDuplicate, ProblemCodes.BudgetPeriodOverlap, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(FiscalPeriodView), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<IActionResult> Create([FromBody] FiscalPeriodRequest request, CancellationToken cancellationToken) =>
        SendAsync(new CreateFiscalPeriodCommand(request.Code, request.Name, request.StartDate, request.EndDate),
            view => CreatedVersioned($"/api/v2/fiscal-periods/{view.Id}", view, view.Version), cancellationToken);

    /// <summary>Change the name (until closed) and the dates (only while <c>Planned</c>). The code is immutable. Requires <c>If-Match</c>.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.BudgetPeriodsManage)]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.BudgetPeriodOverlap,
        ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(FiscalPeriodView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired, "application/problem+json")]
    public Task<IActionResult> Update(Guid id, [FromBody] FiscalPeriodRequest request, CancellationToken cancellationToken) =>
        SendAsync(new UpdateFiscalPeriodCommand(id, IfMatch, request.Name, request.StartDate, request.EndDate), view => Versioned(view, view.Version), cancellationToken);

    /// <summary><c>Planned → Open</c> (BUD-001). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/open")]
    [HasPermission(Permissions.BudgetPeriodsManage)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired,
        ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(FiscalPeriodView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<IActionResult> Open(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new ChangeFiscalPeriodStatusCommand(id, IfMatch, FiscalPeriodTransition.Open), view => Versioned(view, view.Version), cancellationToken);

    /// <summary><c>Open → Closed</c> when no <c>Reserved</c> reservation remains (BUD-011). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/close")]
    [HasPermission(Permissions.BudgetPeriodsManage)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.BudgetPeriodHasOpenReservations, ProblemCodes.VersionMismatch,
        ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(FiscalPeriodView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<IActionResult> Close(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new ChangeFiscalPeriodStatusCommand(id, IfMatch, FiscalPeriodTransition.Close), view => Versioned(view, view.Version), cancellationToken);
}
