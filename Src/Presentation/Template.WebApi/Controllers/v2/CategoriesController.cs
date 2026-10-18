using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Features.Catalog;
using Template.Application.Security;
using Template.WebApi.Infrastructure.Http;
using Template.WebApi.Infrastructure.Security;

namespace Template.WebApi.Controllers.v2;

/// <summary>Controlled categories of catalog items and suppliers (CAT-001, CAT-002). Never deleted (ORG-005).</summary>
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/categories")]
[StableProblemCodes]
[NoStoreSecurityHeaders]
[Produces("application/json")]
public sealed class CategoriesController(ISender sender, ApiResultMapper resultMapper) : MasterDataControllerBase(sender, resultMapper)
{
    /// <summary>List categories.</summary>
    /// <param name="page">Page number, 1 or greater (default 1).</param>
    /// <param name="pageSize">Items per page, 1–100 (default 20).</param>
    /// <param name="sort">Comma-separated <c>code</c>, <c>name</c>, <c>createdAt</c>, <c>updatedAt</c>; prefix <c>-</c> for descending (default <c>code</c>).</param>
    /// <param name="q">Case-insensitive text matched against code and name.</param>
    /// <param name="isActive">Active flag filter.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet]
    [HasPermission(Permissions.CatalogRead)]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(PagedResult<CategoryView>), StatusCodes.Status200OK)]
    public Task<IActionResult> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? q, [FromQuery] bool? isActive,
        CancellationToken cancellationToken) =>
        SendAsync(new ListCategoriesQuery(page, pageSize, sort, q, isActive), Ok, cancellationToken);

    /// <summary>Get one category; the response carries its <c>ETag</c>.</summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.CatalogRead)]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(CategoryView), StatusCodes.Status200OK)]
    public Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new GetCategoryQuery(id), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Create an active category (CAT-001). Requires <c>Idempotency-Key</c>.</summary>
    [HttpPost]
    [HasPermission(Permissions.CatalogManage)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.IdempotencyKeyReused, ProblemCodes.IdempotencyInProgress,
        ProblemCodes.CatalogCodeDuplicate, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(CategoryView), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<IActionResult> Create([FromBody] CreateCategoryRequest request, CancellationToken cancellationToken) =>
        SendAsync(new CreateCategoryCommand(request.Code, request.Name),
            view => CreatedVersioned($"/api/v2/categories/{view.Id}", view, view.Version), cancellationToken);

    /// <summary>Rename; the code is immutable. Requires <c>If-Match</c>.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.CatalogManage)]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.NotFound, ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired,
        ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(CategoryView), StatusCodes.Status200OK)]
    public Task<IActionResult> Update(Guid id, [FromBody] UpdateNameRequest request, CancellationToken cancellationToken) =>
        SendAsync(new UpdateCategoryCommand(id, IfMatch, request.Name), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Deactivate with reason (CAT-002). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(Permissions.CatalogManage)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.ReasonRequired, ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.VersionMismatch,
        ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(CategoryView), StatusCodes.Status200OK)]
    public Task<IActionResult> Deactivate(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReasonRequest? request, CancellationToken cancellationToken) =>
        SendAsync(new DeactivateCategoryCommand(id, IfMatch, request?.Reason), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Reactivate. Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/reactivate")]
    [HasPermission(Permissions.CatalogManage)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired,
        ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(CategoryView), StatusCodes.Status200OK)]
    public Task<IActionResult> Reactivate(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReasonRequest? request, CancellationToken cancellationToken) =>
        SendAsync(new ReactivateCategoryCommand(id, IfMatch, request?.Reason), view => Versioned(view, view.Version), cancellationToken);
}
