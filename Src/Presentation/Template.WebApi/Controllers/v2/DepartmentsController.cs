using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Features.Organization;
using Template.Application.Security;
using Template.WebApi.Infrastructure.Http;
using Template.WebApi.Infrastructure.Security;

namespace Template.WebApi.Controllers.v2;

/// <summary>Departments (Organization module, ORG-001..006). Never deleted: deactivate instead (ORG-005).</summary>
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/departments")]
[StableProblemCodes]
[NoStoreSecurityHeaders]
[Produces("application/json")]
public sealed class DepartmentsController(ISender sender, ApiResultMapper resultMapper) : MasterDataControllerBase(sender, resultMapper)
{
    /// <summary>List departments.</summary>
    /// <param name="page">Page number, 1 or greater (default 1).</param>
    /// <param name="pageSize">Items per page, 1–100 (default 20).</param>
    /// <param name="sort">Comma-separated <c>code</c>, <c>name</c>, <c>createdAt</c>, <c>updatedAt</c>; prefix <c>-</c> for descending (default <c>code</c>).</param>
    /// <param name="q">Case-insensitive text matched against code and name.</param>
    /// <param name="isActive">Active flag filter.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet]
    [HasPermission(Permissions.OrganizationRead)]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(PagedResult<DepartmentView>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    public Task<IActionResult> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? q, [FromQuery] bool? isActive,
        CancellationToken cancellationToken) =>
        SendAsync(new ListDepartmentsQuery(page, pageSize, sort, q, isActive), Ok, cancellationToken);

    /// <summary>Get one department; the response carries its <c>ETag</c>.</summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.OrganizationRead)]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(DepartmentView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new GetDepartmentQuery(id), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Create an active department (ORG-001, ORG-006). Requires <c>Idempotency-Key</c>.</summary>
    [HttpPost]
    [HasPermission(Permissions.OrganizationManage)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.IdempotencyKeyReused, ProblemCodes.IdempotencyInProgress,
        ProblemCodes.OrganizationCodeDuplicate, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(DepartmentView), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<IActionResult> Create([FromBody] CreateDepartmentRequest request, CancellationToken cancellationToken) =>
        SendAsync(new CreateDepartmentCommand(request.Code, request.Name, request.HeadUserId),
            view => CreatedVersioned($"/api/v2/departments/{view.Id}", view, view.Version), cancellationToken);

    /// <summary>Change name and head; the code is immutable. Requires <c>If-Match</c>.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.OrganizationManage)]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.NotFound, ProblemCodes.OrganizationInactive, ProblemCodes.VersionMismatch,
        ProblemCodes.PreconditionRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(DepartmentView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired, "application/problem+json")]
    public Task<IActionResult> Update(Guid id, [FromBody] UpdateDepartmentRequest request, CancellationToken cancellationToken) =>
        SendAsync(new UpdateDepartmentCommand(id, IfMatch, request.Name, request.HeadUserId), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Deactivate with reason when every cost center is inactive (ORG-003). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(Permissions.OrganizationManage)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.ReasonRequired, ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.DepartmentHasActiveCostCenters,
        ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(DepartmentView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<IActionResult> Deactivate(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReasonRequest? request, CancellationToken cancellationToken) =>
        SendAsync(new DeactivateDepartmentCommand(id, IfMatch, request?.Reason), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Reactivate (optional reason). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/reactivate")]
    [HasPermission(Permissions.OrganizationManage)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired,
        ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(DepartmentView), StatusCodes.Status200OK)]
    public Task<IActionResult> Reactivate(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReasonRequest? request, CancellationToken cancellationToken) =>
        SendAsync(new ReactivateDepartmentCommand(id, IfMatch, request?.Reason), view => Versioned(view, view.Version), cancellationToken);
}
