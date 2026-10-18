using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Features.Access.Queries;
using Template.Application.Interfaces.Access;
using Template.Application.Security;
using Template.WebApi.Infrastructure.Http;
using Template.WebApi.Infrastructure.Security;

namespace Template.WebApi.Controllers.v2;

/// <summary>User lookup for cost-center manager and department head assignment (ORG-006). Read-only.</summary>
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/users")]
[StableProblemCodes]
[NoStoreSecurityHeaders]
[Produces("application/json")]
public sealed class UsersController(ISender sender, ApiResultMapper resultMapper) : MasterDataControllerBase(sender, resultMapper)
{
    /// <summary>Search users by user name or display name; ID, user name, display name and active flag only.</summary>
    /// <param name="page">Page number, 1 or greater (default 1).</param>
    /// <param name="pageSize">Items per page, 1–100 (default 20).</param>
    /// <param name="q">Case-insensitive text matched against user name and display name.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet]
    [HasPermission(Permissions.AccessUsersManage)]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(PagedResult<UserSummary>), StatusCodes.Status200OK)]
    public Task<IActionResult> List([FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? q, CancellationToken cancellationToken) =>
        SendAsync(new ListUsersQuery(page, pageSize, q), Ok, cancellationToken);
}
