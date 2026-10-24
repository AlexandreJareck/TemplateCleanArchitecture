using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Features.Approvals;
using Template.Application.Security;
using Template.WebApi.Infrastructure.Http;
using Template.WebApi.Infrastructure.Security;

namespace Template.WebApi.Controllers.v2;

/// <summary>
/// Award approval instances (APR-001..017, Phase 31). Instances are created by the approval orchestration when an award
/// is submitted; humans read them, decide, delegate and, with <c>approvals.administer</c>, reassign or cancel. Reads are
/// limited to participants and <c>approvals.read-all</c> holders (checked by the use case). Every change requires
/// <c>If-Match</c> and <c>Idempotency-Key</c>; a repeated identical decision returns the current state (APR-014). When the
/// WorkflowFunctions host is unavailable a decision answers 503 <c>approval.workflow-unavailable</c> and changes nothing.
/// The browser never reaches the orchestration host.
/// </summary>
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/approval-instances")]
[StableProblemCodes]
[NoStoreSecurityHeaders]
[Produces("application/json")]
public sealed class ApprovalInstancesController(ISender sender, ApiResultMapper resultMapper) : MasterDataControllerBase(sender, resultMapper)
{
    /// <summary>List approval instances; <c>assignedTo=me</c> is the approval inbox (requires <c>approvals.decide</c>).</summary>
    /// <param name="page">Page number, 1 or greater (default 1).</param>
    /// <param name="pageSize">Items per page, 1–100 (default 20).</param>
    /// <param name="sort">Comma-separated <c>startedAt</c>, <c>dueAt</c>, <c>requisitionNumber</c>, <c>amount</c>, <c>status</c>, <c>updatedAt</c>; prefix <c>-</c> for descending (default <c>-startedAt</c>).</param>
    /// <param name="status">Comma-separated statuses, for example <c>Pending,Approved</c> (inbox default <c>Pending</c>).</param>
    /// <param name="assignedTo"><c>me</c>: the current pending step is assigned to the caller or one of the caller's role groups.</param>
    /// <param name="requisitionId">Requisition.</param>
    /// <param name="escalated">Only instances whose current step was escalated (<c>true</c>) or not (<c>false</c>).</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet]
    [Authorize]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(PagedResult<ApprovalSummaryView>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    public Task<IActionResult> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? status, [FromQuery] string? assignedTo,
        [FromQuery] Guid? requisitionId, [FromQuery] bool? escalated, CancellationToken cancellationToken) =>
        SendAsync(new ListApprovalInstancesQuery(page, pageSize, sort, status, assignedTo, requisitionId, escalated), Ok, cancellationToken);

    /// <summary>Approval reconciliation report: database versus orchestration divergences, without repair (APR-016).</summary>
    [HttpGet("reconciliation")]
    [HasPermission(Permissions.ApprovalsAdminister)]
    [ProblemCodes(ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(ApprovalReconciliationView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    public Task<IActionResult> Reconciliation(CancellationToken cancellationToken) =>
        SendAsync(new GetApprovalReconciliationQuery(), Ok, cancellationToken);

    /// <summary>Repair the divergences the report shows: start missing orchestrations and re-signal stalled ones (APR-016). Requires <c>Idempotency-Key</c>.</summary>
    [HttpPost("reconciliation/repair")]
    [HasPermission(Permissions.ApprovalsAdminister)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.IdempotencyKeyRequired, ProblemCodes.IdempotencyKeyReused, ProblemCodes.IdempotencyInProgress, ProblemCodes.SessionExpired,
        ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(ApprovalReconciliationView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    public Task<IActionResult> Repair(CancellationToken cancellationToken) =>
        SendAsync(new RepairApprovalWorkflowsCommand(), Ok, cancellationToken);

    /// <summary>Get one instance with steps, the award summary and the server-computed allowed actions.</summary>
    [HttpGet("{id:guid}")]
    [Authorize]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(ApprovalInstanceView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new GetApprovalInstanceQuery(id), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Append-only timeline: assignments, decisions, delegations, reminders, escalations and the end (NFR-AUD-2).</summary>
    [HttpGet("{id:guid}/history")]
    [Authorize]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(ApprovalHistoryView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<IActionResult> History(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new GetApprovalHistoryQuery(id), Ok, cancellationToken);

    /// <summary>Durable runtime status next to the database status (APR-016 diagnostics).</summary>
    [HttpGet("{id:guid}/workflow")]
    [HasPermission(Permissions.ApprovalsAdminister)]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.ApprovalWorkflowUnavailable, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(ApprovalWorkflowView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public Task<IActionResult> Workflow(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new GetApprovalWorkflowQuery(id), Ok, cancellationToken);

    /// <summary>
    /// Approve or reject the current step (APR-004..009, APR-014). Separation of duties is enforced by the server
    /// (<c>approval.self-approval</c>, <c>approval.duplicate-approver</c>, <c>approval.maker-checker</c>). Requires
    /// <c>If-Match</c> and <c>Idempotency-Key</c>.
    /// </summary>
    [HttpPost("{id:guid}/steps/{level:int}/decision")]
    [HasPermission(Permissions.ApprovalsDecide)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.ReasonRequired, ProblemCodes.NotFound, ProblemCodes.ApprovalNotAssignee,
        ProblemCodes.ApprovalSelfApproval, ProblemCodes.ApprovalDuplicateApprover, ProblemCodes.ApprovalMakerChecker, ProblemCodes.ApprovalStepNotCurrent,
        ProblemCodes.ApprovalStepAlreadyDecided, ProblemCodes.ApprovalInstanceEnded, ProblemCodes.ApprovalWorkflowUnavailable, ProblemCodes.VersionMismatch,
        ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.IdempotencyKeyReused, ProblemCodes.IdempotencyInProgress,
        ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(ApprovalInstanceView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public Task<IActionResult> Decide(Guid id, int level, [FromBody] ApprovalDecisionRequest request, CancellationToken cancellationToken) =>
        SendAsync(new DecideApprovalStepCommand(id, level, IfMatch, request.Decision, request.Reason), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Delegate the current step to another eligible approver; the due time is unchanged (APR-010). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/steps/{level:int}/delegate")]
    [HasPermission(Permissions.ApprovalsDelegate)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.ReasonRequired, ProblemCodes.NotFound, ProblemCodes.ApprovalNotAssignee,
        ProblemCodes.ApprovalInvalidDelegate, ProblemCodes.ApprovalStepNotCurrent, ProblemCodes.ApprovalInstanceEnded, ProblemCodes.VersionMismatch,
        ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(ApprovalInstanceView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<IActionResult> Delegate(Guid id, int level, [FromBody] ApprovalDelegationRequest request, CancellationToken cancellationToken) =>
        SendAsync(new DelegateApprovalStepCommand(id, level, IfMatch, request.DelegateUserId, request.Reason), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Active approvers for delegating the current step, with their eligibility (APR-010).</summary>
    /// <param name="id">Instance.</param>
    /// <param name="level">Current level.</param>
    /// <param name="q">Text matched against the user name or display name.</param>
    /// <param name="page">Page number, 1 or greater (default 1).</param>
    /// <param name="pageSize">Items per page, 1–100 (default 20).</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("{id:guid}/steps/{level:int}/delegates")]
    [Authorize]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.NotFound, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(PagedResult<ApprovalDelegateView>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    public Task<IActionResult> Delegates(Guid id, int level, [FromQuery] string? q, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        SendAsync(new SearchApprovalDelegatesQuery(id, level, q, page, pageSize), Ok, cancellationToken);

    /// <summary>Reassign the current step to a user or a role group with a reason; administrators never decide (APR-015). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/steps/{level:int}/reassign")]
    [HasPermission(Permissions.ApprovalsAdminister)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.ReasonRequired, ProblemCodes.NotFound, ProblemCodes.ApprovalInvalidDelegate,
        ProblemCodes.ApprovalStepNotCurrent, ProblemCodes.ApprovalInstanceEnded, ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired,
        ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(ApprovalInstanceView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<IActionResult> Reassign(Guid id, int level, [FromBody] ApprovalReassignmentRequest request, CancellationToken cancellationToken) =>
        SendAsync(new ReassignApprovalStepCommand(id, level, IfMatch, request.AssigneeUserId, request.AssigneeRole, request.Reason),
            view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Cancel the instance with a reason; the requisition returns to <c>InSourcing</c> for a new award (APR-015). Requires <c>If-Match</c> and <c>Idempotency-Key</c>.</summary>
    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.ApprovalsAdminister)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.ReasonRequired, ProblemCodes.NotFound, ProblemCodes.ApprovalInstanceEnded, ProblemCodes.VersionMismatch,
        ProblemCodes.PreconditionRequired, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(ApprovalInstanceView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<IActionResult> Cancel(Guid id, [FromBody] ApprovalCancellationRequest request, CancellationToken cancellationToken) =>
        SendAsync(new CancelApprovalCommand(id, IfMatch, request.Reason), view => Versioned(view, view.Version), cancellationToken);
}
