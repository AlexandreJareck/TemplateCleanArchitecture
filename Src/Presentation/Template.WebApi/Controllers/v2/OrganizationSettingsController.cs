using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Template.Application.Common.Results;
using Template.Application.Features.Organization;
using Template.Application.Security;
using Template.WebApi.Infrastructure.Http;
using Template.WebApi.Infrastructure.Security;

namespace Template.WebApi.Controllers.v2;

/// <summary>The single organization's settings (D23-1, ORG-004).</summary>
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/organization-settings")]
[StableProblemCodes]
[NoStoreSecurityHeaders]
[Produces("application/json")]
public sealed class OrganizationSettingsController(ISender sender, ApiResultMapper resultMapper) : MasterDataControllerBase(sender, resultMapper)
{
    /// <summary>Get the settings; the response carries their <c>ETag</c>.</summary>
    [HttpGet]
    [HasPermission(Permissions.OrganizationRead)]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(OrganizationSettingsView), StatusCodes.Status200OK)]
    public Task<IActionResult> Get(CancellationToken cancellationToken) =>
        SendAsync(new GetOrganizationSettingsQuery(), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>Replace the settings; base currency and time zone lock after the first budget allocation (ORG-004). Requires <c>If-Match</c>.</summary>
    [HttpPut]
    [HasPermission(Permissions.OrganizationSettingsManage)]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.OrganizationSettingsLocked, ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired,
        ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(OrganizationSettingsView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired, "application/problem+json")]
    public Task<IActionResult> Update([FromBody] UpdateOrganizationSettingsRequest request, CancellationToken cancellationToken) =>
        SendAsync(new UpdateOrganizationSettingsCommand(IfMatch, request.DisplayName, request.BaseCurrency, request.TimeZone, request.SupportedCurrencies),
            view => Versioned(view, view.Version), cancellationToken);
}
