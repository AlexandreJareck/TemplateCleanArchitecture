using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Features.Purchasing;
using Template.Application.Security;
using Template.Domain.Reporting;
using Template.WebApi.Infrastructure.Http;
using Template.WebApi.Infrastructure.Security;

namespace Template.WebApi.Controllers.v2;

/// <summary>
/// Purchase orders (PUR-001..011, Phase 32): one draft per approved award with an immutable commercial snapshot,
/// issue with a gapless <c>PO-{yyyy}-{nnnnnn}</c> number, asynchronous document generation and supplier notification
/// (visible and retryable, never unissuing the order), cancellation, short close and the timeline. Reads apply PUR-011
/// visibility in the use case (purchasing roles with prices, receivers on issued orders without prices, requesters on
/// their own requisition's order without supplier contact). Every change requires <c>If-Match</c>; every transition
/// <c>POST</c> requires <c>Idempotency-Key</c>. Storage and broker details are never exposed.
/// </summary>
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/purchase-orders")]
[StableProblemCodes]
[NoStoreSecurityHeaders]
[Produces("application/json")]
public sealed class PurchaseOrdersController(ISender sender, ApiResultMapper resultMapper) : MasterDataControllerBase(sender, resultMapper)
{
    private readonly ISender sender = sender;
    private readonly ApiResultMapper resultMapper = resultMapper;

    /// <summary>List visible orders (approved-order queue: <c>status=Draft</c>; receiving queue: <c>status=Issued,PartiallyReceived</c>).</summary>
    /// <param name="page">Page number, 1 or greater (default 1).</param>
    /// <param name="pageSize">Items per page, 1–100 (default 20).</param>
    /// <param name="sort">Comma-separated <c>number</c>, <c>status</c>, <c>createdAt</c>, <c>updatedAt</c>, <c>issuedAt</c>, <c>total</c>; prefix <c>-</c> for descending (default <c>-updatedAt</c>).</param>
    /// <param name="status">Comma-separated statuses: <c>Draft</c>, <c>Issued</c>, <c>PartiallyReceived</c>, <c>Completed</c>, <c>Cancelled</c>.</param>
    /// <param name="supplierId">Supplier.</param>
    /// <param name="requisitionId">Requisition.</param>
    /// <param name="q">Text matched against the order number, requisition number or supplier legal name.</param>
    /// <param name="documentStatus">Comma-separated document sub-statuses (<c>Pending</c>, <c>Ready</c>, <c>Failed</c>, ...).</param>
    /// <param name="notificationStatus">Comma-separated notification sub-statuses (<c>Pending</c>, <c>Sent</c>, <c>Failed</c>, ...).</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet]
    [Authorize]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.SessionExpired)]
    [ProducesResponseType(typeof(PagedResult<PurchaseOrderSummaryView>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    public Task<IActionResult> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? status, [FromQuery] Guid? supplierId,
        [FromQuery] Guid? requisitionId, [FromQuery] string? q, [FromQuery] string? documentStatus, [FromQuery] string? notificationStatus,
        CancellationToken cancellationToken) =>
        SendAsync(new ListPurchaseOrdersQuery(page, pageSize, sort, status, supplierId, requisitionId, q, documentStatus, notificationStatus), Ok,
            cancellationToken);

    /// <summary>Get one order with lines, received/accepted/remaining quantities, document and notification sub-statuses and allowed actions.</summary>
    [HttpGet("{id:guid}")]
    [Authorize]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.SessionExpired)]
    [ProducesResponseType(typeof(PurchaseOrderView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new GetPurchaseOrderQuery(id), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Append-only timeline: creation, edits, issue, document and notification outcomes, receipts, corrections, cancellation and completion.</summary>
    [HttpGet("{id:guid}/history")]
    [Authorize]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.SessionExpired)]
    [ProducesResponseType(typeof(PurchaseOrderHistoryView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<IActionResult> History(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new GetPurchaseOrderHistoryQuery(id), Ok, cancellationToken);

    /// <summary>Change the draft-only fields (PUR-002): delivery address, promised dates and notes. Requires <c>If-Match</c>.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.PurchasingOrdersManage)]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.NotFound, ProblemCodes.PurchaseOrderImmutable, ProblemCodes.VersionMismatch,
        ProblemCodes.PreconditionRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(PurchaseOrderView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired, "application/problem+json")]
    public Task<IActionResult> Update(Guid id, [FromBody] UpdatePurchaseOrderRequest request, CancellationToken cancellationToken) =>
        SendAsync(new UpdatePurchaseOrderDraftCommand(id, IfMatch, request.DeliveryAddress, request.Notes, request.ToChanges()),
            view => Versioned(view, view.Version), cancellationToken);

    /// <summary>
    /// Issue (PUR-003/005/006/007): Draft, Active supplier, approval Approved, reservation Committed and a complete
    /// snapshot. Allocates the gapless number and queues the document and the supplier notification. Repeating the issue
    /// returns the same order and number. Requires <c>If-Match</c> and <c>Idempotency-Key</c>.
    /// </summary>
    [HttpPost("{id:guid}/issue")]
    [HasPermission(Permissions.PurchasingOrdersIssue)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.SupplierNotEligible, ProblemCodes.PurchaseOrderNotIssuable,
        ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.IdempotencyKeyReused,
        ProblemCodes.IdempotencyInProgress, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(PurchaseOrderView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<IActionResult> Issue(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new IssuePurchaseOrderCommand(id, IfMatch), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>
    /// Cancel with a 10–500 character reason (PUR-008): a Draft returns the requisition to sourcing for re-award; an
    /// Issued order without accepted quantity releases the commitment and cancels the requisition (also needs
    /// <c>requisitions.cancel-any</c>). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.PurchasingOrdersCancel)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.ReasonRequired, ProblemCodes.NotFound, ProblemCodes.PurchaseOrderHasReceipts, ProblemCodes.VersionMismatch,
        ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(PurchaseOrderView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<IActionResult> Cancel(Guid id, [FromBody] ReasonRequest request, CancellationToken cancellationToken) =>
        SendAsync(new CancelPurchaseOrderCommand(id, IfMatch, request.Reason), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>
    /// Short close a PartiallyReceived order with the <c>ShortClose</c> waiver reason (PUR-010): unreceived lines are
    /// closed, the unreceived commitment released (CALC-008) and the order Completed. Requires <c>If-Match</c> and <c>Idempotency-Key</c>.
    /// </summary>
    [HttpPost("{id:guid}/short-close")]
    [HasPermission(Permissions.PurchasingOrdersShortClose)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.ReasonRequired, ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.VersionMismatch,
        ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(PurchaseOrderView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<IActionResult> ShortClose(Guid id, [FromBody] ReasonRequest request, CancellationToken cancellationToken) =>
        SendAsync(new ShortClosePurchaseOrderCommand(id, IfMatch, request.Reason), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Generate a failed document again (PUR-007); the number and state never change. Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/document/retry")]
    [HasPermission(Permissions.PurchasingOrdersIssue)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.PurchaseOrderNotRetryable, ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired,
        ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(PurchaseOrderView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<IActionResult> RetryDocument(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new RetryPurchaseOrderDocumentCommand(id, IfMatch), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Queue a failed supplier notification again (PUR-007). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/notification/retry")]
    [HasPermission(Permissions.PurchasingOrdersIssue)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.PurchaseOrderNotRetryable, ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired,
        ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(PurchaseOrderView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<IActionResult> RetryNotification(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new RetryPurchaseOrderNotificationCommand(id, IfMatch), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Stream the exact issued document (PUR-007). Requires <c>purchasing.orders.read</c> (it carries prices, PUR-011).</summary>
    [HttpGet("{id:guid}/document")]
    [HasPermission(Permissions.PurchasingOrdersRead)]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.PurchaseOrderDocumentNotReady, ProblemCodes.FileUnavailable, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, ReportCatalog.PdfMediaType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Document(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DownloadPurchaseOrderDocumentQuery(id), cancellationToken);
        return resultMapper.ToActionResult(result, HttpContext, () =>
            new AttachmentStreamResult(result.Value.Content, result.Value.MediaType, result.Value.FileName, result.Value.Length));
    }
}
