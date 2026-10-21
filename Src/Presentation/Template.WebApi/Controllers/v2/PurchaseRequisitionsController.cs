using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Features.Budgeting;
using Template.Application.Features.Requisitions;
using Template.Application.Security;
using Template.WebApi.Infrastructure.Http;
using Template.WebApi.Infrastructure.Security;

namespace Template.WebApi.Controllers.v2;

/// <summary>
/// Purchase requisitions (REQ-001..012) with budget reservation on submission (D23-4, BUD-004..008). Reads are
/// resource-scoped (REQ-011: unreadable IDs answer 404, collections filter silently) and therefore authorized by the use
/// case after authentication; writes declare their permission here and the use case adds ownership (REQ-001). Every
/// change of an existing requisition requires <c>If-Match</c>; every creating or transition <c>POST</c> requires
/// <c>Idempotency-Key</c>. Line and attachment changes answer with the whole requisition and its new <c>ETag</c>.
/// </summary>
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/purchase-requisitions")]
[StableProblemCodes]
[NoStoreSecurityHeaders]
[Produces("application/json")]
public sealed class PurchaseRequisitionsController(ISender sender, ApiResultMapper resultMapper) : MasterDataControllerBase(sender, resultMapper)
{
    /// <summary>List requisitions (REQ-011). Requires one of <c>requisitions.create</c>, <c>requisitions.read-team</c>, <c>requisitions.read-all</c>.</summary>
    /// <param name="page">Page number, 1 or greater (default 1).</param>
    /// <param name="pageSize">Items per page, 1–100 (default 20).</param>
    /// <param name="sort">Comma-separated <c>number</c>, <c>status</c>, <c>createdAt</c>, <c>updatedAt</c>, <c>submittedAt</c>, <c>neededBy</c>, <c>estimatedTotal</c>; prefix <c>-</c> for descending (default <c>-createdAt</c>).</param>
    /// <param name="scope"><c>mine</c> (default) or <c>visible</c> (everything the actor may read).</param>
    /// <param name="status">Comma-separated statuses, for example <c>Draft,Submitted</c>.</param>
    /// <param name="costCenterId">Cost center.</param>
    /// <param name="requesterId">Requester (with <c>scope=visible</c>).</param>
    /// <param name="q">Text matched against the requisition number.</param>
    /// <param name="from">Created at or after this UTC instant.</param>
    /// <param name="to">Created before this UTC instant.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet]
    [Authorize]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(PagedResult<RequisitionSummaryView>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    public Task<IActionResult> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? scope, [FromQuery] string? status,
        [FromQuery] Guid? costCenterId, [FromQuery] Guid? requesterId, [FromQuery] string? q, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        CancellationToken cancellationToken) =>
        SendAsync(new ListRequisitionsQuery(page, pageSize, sort, scope, status, costCenterId, requesterId, q, from, to), Ok, cancellationToken);

    /// <summary>Get one readable requisition with lines, attachment metadata, submission snapshots and allowed actions.</summary>
    [HttpGet("{id:guid}")]
    [Authorize]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RequisitionView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new GetRequisitionQuery(id), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>State and reservation timeline of a readable requisition (NFR-AUD-2, AUD-004).</summary>
    [HttpGet("{id:guid}/history")]
    [Authorize]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RequisitionTimelineView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<IActionResult> History(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new GetRequisitionTimelineQuery(id), Ok, cancellationToken);

    /// <summary>Budget availability for the requisition's cost center and currency today: own draft or <c>budget.read</c> (BUD-012).</summary>
    [HttpGet("{id:guid}/budget-availability")]
    [Authorize]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(BudgetAvailabilityView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<IActionResult> BudgetAvailability(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new GetRequisitionBudgetAvailabilityQuery(id), Ok, cancellationToken);

    /// <summary>Create a draft (revision 1) for the caller on an active cost center (ORG-003). Requires <c>Idempotency-Key</c>.</summary>
    [HttpPost]
    [HasPermission(Permissions.RequisitionsCreate)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.IdempotencyKeyReused, ProblemCodes.IdempotencyInProgress,
        ProblemCodes.OrganizationInactive, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RequisitionView), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<IActionResult> Create([FromBody] RequisitionDraftRequest request, CancellationToken cancellationToken) =>
        SendAsync(new CreateRequisitionDraftCommand(request.CostCenterId, request.Currency, request.Purpose, request.NeededBy),
            view => CreatedVersioned($"/api/v2/purchase-requisitions/{view.Id}", view, view.Version), cancellationToken);

    /// <summary>Replace the draft header; requester or <c>requisitions.edit-any</c> proxy (REQ-001). Requires <c>If-Match</c>.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.RequisitionsCreate)]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.NotFound, ProblemCodes.RequisitionImmutable, ProblemCodes.OrganizationInactive,
        ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RequisitionView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired, "application/problem+json")]
    public Task<IActionResult> Update(Guid id, [FromBody] RequisitionDraftRequest request, CancellationToken cancellationToken) =>
        SendAsync(new UpdateRequisitionDraftCommand(id, IfMatch, request.CostCenterId, request.Currency, request.Purpose, request.NeededBy),
            view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Discard a never-submitted draft (REQ-010); submitted history is never deleted. Requires <c>If-Match</c>.</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.RequisitionsCreate)]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.RequisitionImmutable, ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired,
        ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<IActionResult> Discard(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new DiscardRequisitionDraftCommand(id, IfMatch), NoContent, cancellationToken);

    /// <summary>Add a draft line; totals are recalculated by the server (REQ-002, REQ-003). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/lines")]
    [HasPermission(Permissions.RequisitionsCreate)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.NotFound, ProblemCodes.RequisitionImmutable, ProblemCodes.CatalogInactive, ProblemCodes.VersionMismatch,
        ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RequisitionView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<IActionResult> AddLine(Guid id, [FromBody] RequisitionLineBody request, CancellationToken cancellationToken) =>
        SendAsync(new AddRequisitionLineCommand(id, IfMatch, request.ToRequest()), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Replace one draft line by its stable number. Requires <c>If-Match</c>.</summary>
    [HttpPut("{id:guid}/lines/{lineNumber:int}")]
    [HasPermission(Permissions.RequisitionsCreate)]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.NotFound, ProblemCodes.RequisitionImmutable, ProblemCodes.CatalogInactive, ProblemCodes.VersionMismatch,
        ProblemCodes.PreconditionRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RequisitionView), StatusCodes.Status200OK)]
    public Task<IActionResult> UpdateLine(Guid id, int lineNumber, [FromBody] RequisitionLineBody request, CancellationToken cancellationToken) =>
        SendAsync(new UpdateRequisitionLineCommand(id, IfMatch, lineNumber, request.ToRequest()), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Remove one draft line; other line numbers never change. Requires <c>If-Match</c>; answers the updated requisition.</summary>
    [HttpDelete("{id:guid}/lines/{lineNumber:int}")]
    [HasPermission(Permissions.RequisitionsCreate)]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.RequisitionImmutable, ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired,
        ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RequisitionView), StatusCodes.Status200OK)]
    public Task<IActionResult> RemoveLine(Guid id, int lineNumber, CancellationToken cancellationToken) =>
        SendAsync(new RemoveRequisitionLineCommand(id, IfMatch, lineNumber), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>
    /// Add attachment metadata in <c>Pending</c> status (FIL-002 placeholder; no bytes until Phase 30). Allowed while the
    /// requisition is non-terminal; after submission attachments are append-only (FIL-004). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.
    /// </summary>
    [HttpPost("{id:guid}/attachments")]
    [HasPermission(Permissions.RequisitionsCreate)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.FileTooLarge, ProblemCodes.FileTypeNotAllowed,
        ProblemCodes.FileLimitReached, ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired,
        ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RequisitionView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<IActionResult> AddAttachment(Guid id, [FromBody] RequisitionAttachmentRequest request, CancellationToken cancellationToken) =>
        SendAsync(new AddRequisitionAttachmentCommand(id, IfMatch, request.FileName, request.MediaType, request.Length),
            view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Remove attachment metadata that was never part of a submission (FIL-004). Requires <c>If-Match</c>.</summary>
    [HttpDelete("{id:guid}/attachments/{attachmentId:guid}")]
    [HasPermission(Permissions.RequisitionsCreate)]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.FileImmutable, ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired,
        ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RequisitionView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<IActionResult> RemoveAttachment(Guid id, Guid attachmentId, CancellationToken cancellationToken) =>
        SendAsync(new RemoveRequisitionAttachmentCommand(id, IfMatch, attachmentId), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>
    /// Submit an own draft and reserve its estimated total atomically (REQ-004/005, BUD-004..008). Insufficient budget,
    /// a missing open allocation or an unmet guard leaves the draft unchanged. Requires <c>If-Match</c> and <c>Idempotency-Key</c>.
    /// </summary>
    [HttpPost("{id:guid}/submit")]
    [HasPermission(Permissions.RequisitionsSubmit)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.RequisitionNotSubmittable, ProblemCodes.BudgetNoOpenAllocation,
        ProblemCodes.BudgetInsufficient, ProblemCodes.BudgetReservationExists, ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired,
        ProblemCodes.IdempotencyKeyRequired, ProblemCodes.IdempotencyKeyReused, ProblemCodes.IdempotencyInProgress, ProblemCodes.SessionExpired,
        ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RequisitionView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired, "application/problem+json")]
    public Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new SubmitRequisitionCommand(id, IfMatch), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Withdraw an own submitted requisition back to <c>Draft</c> (revision + 1) and release its reservation exactly once (REQ-007). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/withdraw")]
    [HasPermission(Permissions.RequisitionsSubmit)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired,
        ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RequisitionView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<IActionResult> Withdraw(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new WithdrawRequisitionCommand(id, IfMatch), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>
    /// Cancel with a 10–500 character reason and release the reservation exactly once (REQ-008). Requires
    /// <c>requisitions.cancel-own</c> on an own requisition or <c>requisitions.cancel-any</c> (checked by the use case),
    /// <c>If-Match</c> and <c>Idempotency-Key</c>.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.ReasonRequired, ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.VersionMismatch,
        ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RequisitionView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<IActionResult> Cancel(Guid id, [FromBody] ReasonRequest request, CancellationToken cancellationToken) =>
        SendAsync(new CancelRequisitionCommand(id, IfMatch, request.Reason), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Copy a terminal readable requisition into a new draft of the caller (REQ-012). Requires <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/copy")]
    [HasPermission(Permissions.RequisitionsCreate)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.OrganizationInactive, ProblemCodes.IdempotencyKeyRequired,
        ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RequisitionView), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<IActionResult> Copy(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new CopyRequisitionCommand(id), view => CreatedVersioned($"/api/v2/purchase-requisitions/{view.Id}", view, view.Version), cancellationToken);
}
