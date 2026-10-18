using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Features.Catalog;
using Template.Application.Security;
using Template.Domain.Catalog;
using Template.WebApi.Infrastructure.Http;
using Template.WebApi.Infrastructure.Security;

namespace Template.WebApi.Controllers.v2;

/// <summary>Catalog items (CAT-001..004): goods and services with an active category and unit. Never deleted (ORG-005).</summary>
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/catalog-items")]
[StableProblemCodes]
[NoStoreSecurityHeaders]
[Produces("application/json")]
public sealed class CatalogItemsController(ISender sender, ApiResultMapper resultMapper) : MasterDataControllerBase(sender, resultMapper)
{
    /// <summary>List catalog items.</summary>
    /// <param name="page">Page number, 1 or greater (default 1).</param>
    /// <param name="pageSize">Items per page, 1–100 (default 20).</param>
    /// <param name="sort">Comma-separated <c>code</c>, <c>description</c>, <c>kind</c>, <c>createdAt</c>, <c>updatedAt</c>; prefix <c>-</c> for descending (default <c>code</c>).</param>
    /// <param name="q">Case-insensitive text matched against code and description.</param>
    /// <param name="isActive">Active flag filter.</param>
    /// <param name="kind"><c>Good</c> or <c>Service</c>.</param>
    /// <param name="categoryId">Category filter.</param>
    /// <param name="unitOfMeasureId">Unit filter.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet]
    [HasPermission(Permissions.CatalogRead)]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(PagedResult<CatalogItemView>), StatusCodes.Status200OK)]
    public Task<IActionResult> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? q, [FromQuery] bool? isActive,
        [FromQuery] CatalogItemKind? kind, [FromQuery] Guid? categoryId, [FromQuery] Guid? unitOfMeasureId, CancellationToken cancellationToken) =>
        SendAsync(new ListCatalogItemsQuery(page, pageSize, sort, q, isActive, kind, categoryId, unitOfMeasureId), Ok, cancellationToken);

    /// <summary>Get one catalog item; the response carries its <c>ETag</c>.</summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.CatalogRead)]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(CatalogItemView), StatusCodes.Status200OK)]
    public Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new GetCatalogItemQuery(id), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Create an active item referencing an active category and unit (CAT-001, CAT-002). Requires <c>Idempotency-Key</c>.</summary>
    [HttpPost]
    [HasPermission(Permissions.CatalogManage)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.IdempotencyKeyReused, ProblemCodes.IdempotencyInProgress,
        ProblemCodes.CatalogCodeDuplicate, ProblemCodes.CatalogInactive, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(CatalogItemView), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<IActionResult> Create([FromBody] CatalogItemRequest request, CancellationToken cancellationToken) =>
        SendAsync(new CreateCatalogItemCommand(request.Code, request.Description, request.Kind, request.CategoryId, request.UnitOfMeasureId, request.ReferencePrice),
            view => CreatedVersioned($"/api/v2/catalog-items/{view.Id}", view, view.Version), cancellationToken);

    /// <summary>Update description, kind, references and advisory reference price; the code is immutable. Requires <c>If-Match</c>.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.CatalogManage)]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.NotFound, ProblemCodes.CatalogInactive, ProblemCodes.VersionMismatch,
        ProblemCodes.PreconditionRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(CatalogItemView), StatusCodes.Status200OK)]
    public Task<IActionResult> Update(Guid id, [FromBody] CatalogItemRequest request, CancellationToken cancellationToken) =>
        SendAsync(new UpdateCatalogItemCommand(id, IfMatch, request.Description, request.Kind, request.CategoryId, request.UnitOfMeasureId, request.ReferencePrice),
            view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Deactivate with reason (CAT-002). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(Permissions.CatalogManage)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.ReasonRequired, ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.VersionMismatch,
        ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(CatalogItemView), StatusCodes.Status200OK)]
    public Task<IActionResult> Deactivate(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReasonRequest? request, CancellationToken cancellationToken) =>
        SendAsync(new DeactivateCatalogItemCommand(id, IfMatch, request?.Reason), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Reactivate; category and unit must be active (CAT-002). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/reactivate")]
    [HasPermission(Permissions.CatalogManage)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.CatalogInactive, ProblemCodes.VersionMismatch,
        ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(CatalogItemView), StatusCodes.Status200OK)]
    public Task<IActionResult> Reactivate(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReasonRequest? request, CancellationToken cancellationToken) =>
        SendAsync(new ReactivateCatalogItemCommand(id, IfMatch, request?.Reason), view => Versioned(view, view.Version), cancellationToken);
}
