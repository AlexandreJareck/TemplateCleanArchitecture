using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Features.Auditing.Queries;
using Template.Application.Interfaces.Auditing;
using Template.Application.Security;
using Template.WebApi.Infrastructure.Http;
using Template.WebApi.Infrastructure.Security;

namespace Template.WebApi.Controllers.v2;

/// <summary>Global audit trail (AUD-004). Read-only: no write endpoint exists (AUD-002).</summary>
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/audit-records")]
[StableProblemCodes]
[NoStoreSecurityHeaders]
[Produces("application/json")]
public sealed class AuditRecordsController(ISender sender, ApiResultMapper resultMapper) : BaseApiController
{
    /// <summary>List audit records, newest first by default.</summary>
    /// <param name="page">Page number, 1 or greater (default 1).</param>
    /// <param name="pageSize">Items per page, 1–100 (default 20).</param>
    /// <param name="sort">Comma-separated <c>occurredAt</c>, <c>action</c>, <c>resourceType</c>; prefix <c>-</c> for descending.</param>
    /// <param name="action">Exact audit action, for example <c>security.session.created</c>.</param>
    /// <param name="actorId">Actor user ID.</param>
    /// <param name="resourceType">Exact resource type.</param>
    /// <param name="resourceId">Exact resource ID.</param>
    /// <param name="from">Inclusive lower bound (UTC instant).</param>
    /// <param name="to">Exclusive upper bound (UTC instant).</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet]
    [HasPermission(Permissions.AuditRead)]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(PagedResult<AuditRecordView>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sort,
        [FromQuery] string? action,
        [FromQuery] Guid? actorId,
        [FromQuery] string? resourceType,
        [FromQuery] string? resourceId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new ListAuditRecordsQuery(page, pageSize, sort, action, actorId, resourceType, resourceId, from, to),
            cancellationToken);
        return resultMapper.ToActionResult(result, HttpContext, () => Ok(result.Value));
    }
}
