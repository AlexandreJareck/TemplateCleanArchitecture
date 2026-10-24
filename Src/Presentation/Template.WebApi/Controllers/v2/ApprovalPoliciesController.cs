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
/// Versioned approval policies (POL-001, APR-017): versions are immutable once used; a new version takes effect from its
/// effective instant and never changes in-flight instances. Reads need <c>approvals.policies.manage</c> or
/// <c>approvals.read-all</c>; creation needs <c>approvals.policies.manage</c>.
/// </summary>
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/approval-policies")]
[StableProblemCodes]
[NoStoreSecurityHeaders]
[Produces("application/json")]
public sealed class ApprovalPoliciesController(ISender sender, ApiResultMapper resultMapper) : MasterDataControllerBase(sender, resultMapper)
{
    /// <summary>Every policy version in ascending order.</summary>
    [HttpGet]
    [Authorize]
    [ProblemCodes(ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(IReadOnlyList<ApprovalPolicyView>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    public Task<IActionResult> List(CancellationToken cancellationToken) => SendAsync(new ListApprovalPoliciesQuery(), Ok, cancellationToken);

    /// <summary>One policy version.</summary>
    [HttpGet("{policyVersion:int}")]
    [Authorize]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(ApprovalPolicyView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<IActionResult> Get(int policyVersion, CancellationToken cancellationToken) => SendAsync(new GetApprovalPolicyQuery(policyVersion), Ok, cancellationToken);

    /// <summary>Create the next version (POL-001). Requires <c>Idempotency-Key</c>.</summary>
    [HttpPost]
    [HasPermission(Permissions.ApprovalsPoliciesManage)]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.PolicyVersionInUse, ProblemCodes.IdempotencyKeyRequired, ProblemCodes.IdempotencyKeyReused,
        ProblemCodes.IdempotencyInProgress, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(ApprovalPolicyView), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public Task<IActionResult> Create([FromBody] CreateApprovalPolicyRequest request, CancellationToken cancellationToken) =>
        SendAsync(new CreateApprovalPolicyVersionCommand(request.EffectiveFrom, request.Levels, request.StepDurationHours, request.ReminderAfterHours,
                request.EscalationRole, request.MaxEscalations, request.Reason),
            view => Created($"/api/v2/approval-policies/{view.Version}", view), cancellationToken);
}
