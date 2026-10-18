using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Template.Application.Common.Results;
using Template.Application.Features.Access.Queries;
using Template.WebApi.Infrastructure.Http;
using Template.WebApi.Infrastructure.Security;

namespace Template.WebApi.Controllers.v2;

/// <summary>Current user (SEC-009). Any authenticated user may read their own profile and effective permissions.</summary>
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/account/me")]
[StableProblemCodes]
[NoStoreSecurityHeaders]
[Produces("application/json")]
public sealed class CurrentAccountController(ISender sender, ApiResultMapper resultMapper) : BaseApiController
{
    /// <summary>Get the current user's ID, user name, display name, roles and effective permissions.</summary>
    [HttpGet]
    [Authorize]
    [ProblemCodes(ProblemCodes.SessionExpired)]
    [ProducesResponseType(typeof(CurrentUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetCurrentUserQuery(), cancellationToken);
        return resultMapper.ToActionResult(result, HttpContext, () => Ok(result.Value));
    }
}
