using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Features.Purchasing;
using Template.Application.Security;
using Template.WebApi.Infrastructure.Http;
using Template.WebApi.Infrastructure.Security;

namespace Template.WebApi.Controllers.v2;

/// <summary>
/// Receipts (RCV-001..008, Phase 32): posted on creation against an issued order, never edited or deleted (RCV-006);
/// corrections are appended compensating records (RCV-007). Posting and correcting recompute the order's accepted
/// totals and completion (PUR-009) in the same commit. Creating and correcting require <c>Idempotency-Key</c>; a
/// correction also requires <c>If-Match</c> with the receipt version.
/// </summary>
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/receipts")]
[StableProblemCodes]
[NoStoreSecurityHeaders]
[Produces("application/json")]
public sealed class ReceiptsController(ISender sender, ApiResultMapper resultMapper) : MasterDataControllerBase(sender, resultMapper)
{
    /// <summary>List receipts. Requires <c>receiving.receipts.read</c> or <c>receiving.receipts.create</c>.</summary>
    /// <param name="page">Page number, 1 or greater (default 1).</param>
    /// <param name="pageSize">Items per page, 1–100 (default 20).</param>
    /// <param name="sort">Comma-separated <c>number</c>, <c>receivedOn</c>, <c>postedAt</c>; prefix <c>-</c> for descending (default <c>-postedAt</c>).</param>
    /// <param name="purchaseOrderId">Order.</param>
    /// <param name="hasDiscrepancy">Only receipts with (or without) a rejected quantity.</param>
    /// <param name="q">Text matched against the receipt or order number.</param>
    /// <param name="receivedFrom">First local receipt date.</param>
    /// <param name="receivedTo">Last local receipt date.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet]
    [Authorize]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(PagedResult<ReceiptView>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    public Task<IActionResult> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] Guid? purchaseOrderId, [FromQuery] bool? hasDiscrepancy,
        [FromQuery] string? q, [FromQuery] DateOnly? receivedFrom, [FromQuery] DateOnly? receivedTo, CancellationToken cancellationToken) =>
        SendAsync(new ListReceiptsQuery(page, pageSize, sort, purchaseOrderId, hasDiscrepancy, q, receivedFrom, receivedTo), Ok, cancellationToken);

    /// <summary>Get one receipt with lines, discrepancies, corrections and evidence metadata.</summary>
    [HttpGet("{id:guid}")]
    [Authorize]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.SessionExpired)]
    [ProducesResponseType(typeof(ReceiptView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new GetReceiptQuery(id), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>
    /// Post a receipt (RCV-001..005/008): at least one line; accepted totals never exceed the receipt cap
    /// (<c>receipt.over-receipt</c>, tolerance of <c>ReceivingPolicy</c> v1 = 0 %). Returns the receipt and the
    /// recomputed order. Requires <c>Idempotency-Key</c>.
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.ReceivingReceiptsCreate)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.NotFound, ProblemCodes.ReceiptOrderNotReceivable, ProblemCodes.ReceiptOverReceipt,
        ProblemCodes.FileTooLarge, ProblemCodes.FileTypeNotAllowed, ProblemCodes.VersionMismatch, ProblemCodes.IdempotencyKeyRequired,
        ProblemCodes.IdempotencyKeyReused, ProblemCodes.IdempotencyInProgress, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(ReceiptPostingView), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<IActionResult> Post([FromBody] PostReceiptRequest request, CancellationToken cancellationToken) =>
        SendAsync(request.ToCommand(),
            view => CreatedVersioned($"/api/v2/receipts/{view.Receipt.Id}", view, view.Receipt.Version), cancellationToken);

    /// <summary>
    /// Append a correction (RCV-007): a signed accepted-quantity delta with reason; the corrected total stays between
    /// zero and the cap and completion is recomputed (PUR-009). Requires <c>receiving.receipts.correct</c>, <c>If-Match</c> and <c>Idempotency-Key</c>.
    /// </summary>
    [HttpPost("{id:guid}/corrections")]
    [HasPermission(Permissions.ReceivingReceiptsCorrect)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.ReasonRequired, ProblemCodes.NotFound, ProblemCodes.InvalidTransition,
        ProblemCodes.ReceiptCorrectionOutOfRange, ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired,
        ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(ReceiptPostingView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<IActionResult> Correct(Guid id, [FromBody] CorrectReceiptRequest request, CancellationToken cancellationToken) =>
        SendAsync(new CorrectReceiptCommand(id, IfMatch, request.PoLineNumber, request.AcceptedDelta, request.Reason),
            view => Versioned(view, view.Receipt.Version), cancellationToken);
}
