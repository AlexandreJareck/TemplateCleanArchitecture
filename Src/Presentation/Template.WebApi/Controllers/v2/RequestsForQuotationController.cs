using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Features.Sourcing;
using Template.Application.Security;
using Template.WebApi.Infrastructure.Http;
using Template.WebApi.Infrastructure.Security;

namespace Template.WebApi.Controllers.v2;

/// <summary>
/// Requests for quotation (SRC-001..022, Phase 29): rounds created from submitted requisitions, invited suppliers,
/// buyer-entered quote revisions, deadline handling, deterministic comparison with persisted exchange-rate snapshots
/// and the award submitted for approval with BUD-009 reservation adjustment. Reads need <c>sourcing.rfqs.manage</c> or
/// <c>sourcing.quotes.read</c> (checked by the use case); quote commercial details and comparisons are returned only to
/// <c>sourcing.quotes.read</c> holders (SRC-021). Every change requires <c>If-Match</c>; every creating or transition
/// <c>POST</c> requires <c>Idempotency-Key</c>. Waivers need their dedicated permission (<c>waiver.permission-required</c>).
/// </summary>
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/requests-for-quotation")]
[StableProblemCodes]
[NoStoreSecurityHeaders]
[Produces("application/json")]
public sealed class RequestsForQuotationController(ISender sender, ApiResultMapper resultMapper) : MasterDataControllerBase(sender, resultMapper)
{
    /// <summary>List RFQs. Requires <c>sourcing.rfqs.manage</c> or <c>sourcing.quotes.read</c>.</summary>
    /// <param name="page">Page number, 1 or greater (default 1).</param>
    /// <param name="pageSize">Items per page, 1–100 (default 20).</param>
    /// <param name="sort">Comma-separated <c>number</c>, <c>status</c>, <c>createdAt</c>, <c>updatedAt</c>, <c>deadline</c>, <c>roundNumber</c>; prefix <c>-</c> for descending (default <c>-createdAt</c>).</param>
    /// <param name="status">Comma-separated statuses, for example <c>Published,Closed</c>.</param>
    /// <param name="requisitionId">Requisition.</param>
    /// <param name="supplierId">Actively invited supplier.</param>
    /// <param name="q">Text matched against the RFQ or requisition number.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet]
    [Authorize]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(PagedResult<RfqSummaryView>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    public Task<IActionResult> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? status, [FromQuery] Guid? requisitionId,
        [FromQuery] Guid? supplierId, [FromQuery] string? q, CancellationToken cancellationToken) =>
        SendAsync(new ListRfqsQuery(page, pageSize, sort, status, requisitionId, supplierId, q), Ok, cancellationToken);

    /// <summary>Get one RFQ with lines, invitations, waivers, rates, allowed actions and, for <c>sourcing.quotes.read</c>, quotes and the comparison.</summary>
    [HttpGet("{id:guid}")]
    [Authorize]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RfqView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new GetRfqQuery(id), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Append-only timeline of transitions and in-state actions (AUD-004).</summary>
    [HttpGet("{id:guid}/history")]
    [Authorize]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RfqHistoryView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<IActionResult> History(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new GetRfqHistoryQuery(id), Ok, cancellationToken);

    /// <summary>Create a Draft round from the requisition's latest submission snapshot (SRC-001/002/005). Requires <c>Idempotency-Key</c>.</summary>
    [HttpPost]
    [HasPermission(Permissions.SourcingRfqsManage)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.RfqRequisitionNotEligible, ProblemCodes.RfqAlreadyActive, ProblemCodes.IdempotencyKeyRequired,
        ProblemCodes.IdempotencyKeyReused, ProblemCodes.IdempotencyInProgress, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RfqView), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<IActionResult> Create([FromBody] CreateRfqRequest request, CancellationToken cancellationToken) =>
        SendAsync(new CreateRfqCommand(request.RequisitionId, request.Method),
            view => CreatedVersioned($"/api/v2/requests-for-quotation/{view.Id}", view, view.Version), cancellationToken);

    /// <summary>Invite an Active supplier while Draft, or while Published before the deadline (SRC-003/007). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/invitations")]
    [HasPermission(Permissions.SourcingRfqsManage)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.SupplierNotEligible,
        ProblemCodes.RfqSupplierAlreadyInvited, ProblemCodes.RfqDirectSingleSupplier, ProblemCodes.RfqDeadlinePassed, ProblemCodes.VersionMismatch,
        ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RfqView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired, "application/problem+json")]
    public Task<IActionResult> Invite(Guid id, [FromBody] InviteSupplierRequest request, CancellationToken cancellationToken) =>
        SendAsync(new InviteSupplierCommand(id, IfMatch, request.SupplierId), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Remove an invitation while Draft; the snapshot stays in history (SRC-007). Requires <c>If-Match</c>.</summary>
    [HttpDelete("{id:guid}/invitations/{supplierId:guid}")]
    [HasPermission(Permissions.SourcingRfqsManage)]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired,
        ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RfqView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<IActionResult> RemoveInvitation(Guid id, Guid supplierId, CancellationToken cancellationToken) =>
        SendAsync(new RemoveInvitationCommand(id, IfMatch, supplierId), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>
    /// Publish (SRC-004/005/006): deadline 24 h–30 days ahead, invited suppliers still Active, the competitive minimum of
    /// invitations or, for <c>Direct</c>, one invitation plus the waiver reason (<c>sourcing.waivers.low-value</c> or
    /// <c>sourcing.waivers.direct-purchase</c>). Moves the requisition to <c>InSourcing</c> and queues invitations.
    /// Requires <c>If-Match</c> and <c>Idempotency-Key</c>.
    /// </summary>
    [HttpPost("{id:guid}/publish")]
    [HasPermission(Permissions.SourcingRfqsManage)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.ReasonRequired, ProblemCodes.RfqDeadlineInvalid, ProblemCodes.NotFound, ProblemCodes.InvalidTransition,
        ProblemCodes.SupplierNotEligible, ProblemCodes.RfqInsufficientInvitations, ProblemCodes.RfqDirectSingleSupplier, ProblemCodes.WaiverRequired,
        ProblemCodes.WaiverPermissionRequired, ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired,
        ProblemCodes.IdempotencyKeyReused, ProblemCodes.IdempotencyInProgress, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RfqView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<IActionResult> Publish(Guid id, [FromBody] PublishRfqRequest request, CancellationToken cancellationToken) =>
        SendAsync(new PublishRfqCommand(id, IfMatch, request.Deadline, request.WaiverReason), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Extend the deadline before it passes, at most +30 days, with reason; suppliers are notified (SRC-011). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/extend-deadline")]
    [HasPermission(Permissions.SourcingRfqsManage)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.ReasonRequired, ProblemCodes.RfqDeadlineInvalid, ProblemCodes.RfqDeadlinePassed, ProblemCodes.NotFound,
        ProblemCodes.InvalidTransition, ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired,
        ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RfqView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<IActionResult> ExtendDeadline(Guid id, [FromBody] ExtendRfqDeadlineRequest request, CancellationToken cancellationToken) =>
        SendAsync(new ExtendRfqDeadlineCommand(id, IfMatch, request.Deadline, request.Reason), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Queue a reminder to invited suppliers without a current quote (SRC-022). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/reminders")]
    [HasPermission(Permissions.SourcingRfqsManage)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.RfqDeadlinePassed, ProblemCodes.RfqNoPendingSuppliers,
        ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RfqView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<IActionResult> Remind(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new RemindRfqSuppliersCommand(id, IfMatch), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>
    /// Enter revision 1 of an invited supplier's quote while Published, received by the deadline (SRC-008/010; late
    /// quotes are rejected, B23-6). Requires <c>sourcing.quotes.enter</c>, <c>If-Match</c> and <c>Idempotency-Key</c>.
    /// </summary>
    [HttpPost("{id:guid}/quotes")]
    [HasPermission(Permissions.SourcingQuotesEnter)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.QuoteLate, ProblemCodes.QuoteSupplierNotInvited,
        ProblemCodes.QuoteExists, ProblemCodes.QuoteImmutable, ProblemCodes.FileTooLarge, ProblemCodes.FileTypeNotAllowed, ProblemCodes.FileLimitReached,
        ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.IdempotencyKeyReused,
        ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RfqView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<IActionResult> EnterQuote(Guid id, [FromBody] QuoteBody request, CancellationToken cancellationToken) =>
        SendAsync(new EnterQuoteCommand(id, IfMatch, request.SupplierId ?? Guid.Empty, request.ToRequest()), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Record revision n+1, superseding the current one (SRC-009). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/quotes/{quoteId:guid}/revisions")]
    [HasPermission(Permissions.SourcingQuotesEnter)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.QuoteLate, ProblemCodes.QuoteImmutable,
        ProblemCodes.FileTooLarge, ProblemCodes.FileTypeNotAllowed, ProblemCodes.FileLimitReached, ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired,
        ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RfqView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<IActionResult> ReviseQuote(Guid id, Guid quoteId, [FromBody] QuoteBody request, CancellationToken cancellationToken) =>
        SendAsync(new ReviseQuoteCommand(id, IfMatch, quoteId, request.ToRequest()), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Withdraw the current revision with a 10–500 character reason (SRC-009). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/quotes/{quoteId:guid}/withdraw")]
    [HasPermission(Permissions.SourcingQuotesEnter)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.ReasonRequired, ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.QuoteImmutable, ProblemCodes.VersionMismatch,
        ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RfqView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<IActionResult> WithdrawQuote(Guid id, Guid quoteId, [FromBody] ReasonRequest request, CancellationToken cancellationToken) =>
        SendAsync(new WithdrawQuoteCommand(id, IfMatch, quoteId, request.Reason), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>
    /// Close (SRC-012): normal after the deadline; before it, early close needs <c>sourcing.rfqs.close-early</c> and a
    /// reason. Freezes revisions and computes the comparison with PTAX snapshots of the close date; an unavailable rate
    /// yields an <c>Incomplete</c> comparison (SRC-013). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.
    /// </summary>
    [HttpPost("{id:guid}/close")]
    [HasPermission(Permissions.SourcingRfqsManage)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.ReasonRequired, ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.VersionMismatch,
        ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RfqView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<IActionResult> Close(Guid id, [FromBody] CloseRfqRequest request, CancellationToken cancellationToken) =>
        SendAsync(new CloseRfqCommand(id, IfMatch, request.Reason), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Cancel from Draft, Published or Closed with reason; the requisition returns to <c>Submitted</c> (SRC-020). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.SourcingRfqsManage)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.ReasonRequired, ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.VersionMismatch,
        ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RfqView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<IActionResult> Cancel(Guid id, [FromBody] ReasonRequest request, CancellationToken cancellationToken) =>
        SendAsync(new CancelRfqCommand(id, IfMatch, request.Reason), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>
    /// Record a manual rate for a currency the comparison misses, with the <c>ManualExchangeRate</c> waiver
    /// (<c>sourcing.rates.manual</c>); the next comparison is computed from the same frozen revisions (SRC-013, CALC-004).
    /// Requires <c>If-Match</c> and <c>Idempotency-Key</c>.
    /// </summary>
    [HttpPost("{id:guid}/exchange-rates")]
    [HasPermission(Permissions.SourcingRatesManual)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.ReasonRequired, ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.RfqRateNotNeeded,
        ProblemCodes.WaiverPermissionRequired, ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired,
        ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RfqView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<IActionResult> RecordExchangeRate(Guid id, [FromBody] ManualExchangeRateRequest request, CancellationToken cancellationToken) =>
        SendAsync(new RecordManualExchangeRateCommand(id, IfMatch, request.Currency, request.Rate, request.QuoteDate, request.Reason),
            view => Versioned(view, view.Version), cancellationToken);

    /// <summary>
    /// Submit the selection of one valid quote revision for approval (SRC-016..019): the supplier must be Active and the
    /// quote not expired; fewer valid quotes than required (SRC-017) or a non-leading choice (SRC-018) need the matching
    /// waiver reason and permission. Adjusts the reservation to the award amount (BUD-009), moves the requisition to
    /// <c>PendingApproval</c> and queues <c>procurement.award.submitted</c>. Requires <c>If-Match</c> and <c>Idempotency-Key</c>.
    /// </summary>
    [HttpPost("{id:guid}/award")]
    [HasPermission(Permissions.SourcingAwardsSubmit)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.ReasonRequired, ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.AwardNotEligible,
        ProblemCodes.AwardPendingExists, ProblemCodes.AwardInsufficientQuotations, ProblemCodes.AwardNonLeadingRequiresWaiver,
        ProblemCodes.AwardComparisonIncomplete, ProblemCodes.QuoteExpired, ProblemCodes.SupplierNotEligible, ProblemCodes.WaiverPermissionRequired,
        ProblemCodes.BudgetInsufficient, ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired,
        ProblemCodes.IdempotencyKeyReused, ProblemCodes.IdempotencyInProgress, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(RfqView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<IActionResult> Award(Guid id, [FromBody] SubmitAwardRequest request, CancellationToken cancellationToken) =>
        SendAsync(new SubmitAwardCommand(id, IfMatch, request.QuoteRevisionId, request.Justification, request.InsufficientQuotationsReason, request.NonLeadingReason),
            view => Versioned(view, view.Version), cancellationToken);
}
