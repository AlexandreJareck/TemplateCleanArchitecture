using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Template.Application.Common.Results;
using Template.Application.Features.Access;
using Template.Application.Features.Access.Commands;
using Template.Application.Security;
using Template.WebApi.Infrastructure.Http;
using Template.WebApi.Infrastructure.Security;

namespace Template.WebApi.Controllers.v2;

/// <summary>Login body. Neither field is logged or echoed.</summary>
public sealed class CreateSessionRequest : IRateLimitedLogin
{
    public string? UserName { get; set; }
    public string? Password { get; set; }

    public override string ToString() => nameof(CreateSessionRequest);
}

/// <summary>
/// Access token of a session. The refresh token travels only in the HttpOnly cookie and the CSRF token in the
/// <c>XSRF-TOKEN</c> cookie; neither appears in the body.
/// </summary>
public sealed record SessionTokenResponse(string AccessToken, string TokenType, DateTime AccessTokenExpiresAt, DateTime SessionExpiresAt);

/// <summary>
/// Browser session endpoints (D23-11, SEC-001..007; docs/product/api-guidelines.md §7). Refresh and logout are
/// cookie-authenticated and require the double-submit CSRF token plus an allowed <c>Origin</c>; revoke-all is
/// bearer-authenticated. Existing <c>account/authenticate</c> and <c>account/start</c> routes are unchanged.
/// </summary>
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/auth/sessions")]
[StableProblemCodes]
[NoStoreSecurityHeaders]
[Produces("application/json")]
public sealed class AuthSessionsController(
    ISender sender,
    ApiResultMapper resultMapper,
    BrowserOriginValidator originValidator,
    BoundarySecurityAudit boundaryAudit) : BaseApiController
{
    /// <summary>Create a session (login).</summary>
    /// <remarks>Sets the refresh and CSRF cookies. Does not invalidate other sessions or legacy tokens.</remarks>
    [HttpPost]
    [AllowAnonymous]
    [AuthenticationRateLimit(AuthenticationRateLimitPolicy.Login)]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.InvalidCredentials, ProblemCodes.CsrfInvalid, ProblemCodes.RateLimited)]
    [ProducesResponseType(typeof(SessionTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<IActionResult> Create([FromBody] CreateSessionRequest request, CancellationToken cancellationToken)
    {
        // A browser always sends Origin on POST; when present it must be allowed (login CSRF). Non-browser clients may omit it.
        if (!originValidator.IsAllowed(Request, required: false))
            return await OriginRejectedAsync();

        var result = await sender.Send(new CreateSessionCommand(request.UserName ?? string.Empty, request.Password ?? string.Empty), cancellationToken);
        return resultMapper.ToActionResult(result, HttpContext, () => Issued(result.Value));
    }

    /// <summary>Rotate the refresh token and issue a new access token.</summary>
    /// <remarks>Requires the refresh cookie, the <c>X-XSRF-TOKEN</c> header equal to the <c>XSRF-TOKEN</c> cookie, and an allowed Origin.</remarks>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [AuthenticationRateLimit(AuthenticationRateLimitPolicy.Refresh)]
    [ProblemCodes(ProblemCodes.SessionExpired, ProblemCodes.SessionRevoked, ProblemCodes.CsrfInvalid, ProblemCodes.RefreshInProgress, ProblemCodes.RateLimited)]
    [ProducesResponseType(typeof(SessionTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        if (!originValidator.IsAllowed(Request, required: true))
            return await OriginRejectedAsync();

        var result = await sender.Send(
            new RefreshSessionCommand(SessionCookies.ReadRefreshToken(Request), SessionCookies.ReadDoubleSubmittedCsrfToken(Request)),
            cancellationToken);
        if (result.IsSuccess)
            return Issued(result.Value);

        // A dead session clears its cookies; CSRF and race failures keep them so a legitimate client can retry.
        if (result.Error!.Code is ProblemCodes.SessionExpired or ProblemCodes.SessionRevoked)
            SessionCookies.Clear(Response);
        return resultMapper.ToProblemResult(result.Error, HttpContext);
    }

    /// <summary>Revoke the current session (logout).</summary>
    /// <remarks>Repeatable: a missing or already revoked session also returns 204. Clears both cookies.</remarks>
    [HttpDelete("current")]
    [AllowAnonymous]
    [ProblemCodes(ProblemCodes.CsrfInvalid)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> RevokeCurrent(CancellationToken cancellationToken)
    {
        var refreshToken = SessionCookies.ReadRefreshToken(Request);
        if (refreshToken is not null && !originValidator.IsAllowed(Request, required: true))
            return await OriginRejectedAsync();

        var result = await sender.Send(
            new RevokeCurrentSessionCommand(refreshToken, SessionCookies.ReadDoubleSubmittedCsrfToken(Request)),
            cancellationToken);
        return resultMapper.ToActionResult(result, HttpContext, () =>
        {
            SessionCookies.Clear(Response);
            return NoContent();
        });
    }

    /// <summary>Revoke every session of the authenticated user.</summary>
    [HttpDelete]
    [Authorize]
    [ProblemCodes(ProblemCodes.SessionExpired)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> RevokeAll(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RevokeAllSessionsCommand(), cancellationToken);
        return resultMapper.ToActionResult(result, HttpContext, () =>
        {
            SessionCookies.Clear(Response);
            return NoContent();
        });
    }

    private OkObjectResult Issued(SessionIssued session)
    {
        SessionCookies.Write(Response, session.RefreshToken, session.CsrfToken, session.SessionExpiresAtUtc);
        return Ok(new SessionTokenResponse(session.AccessToken, "Bearer", session.AccessTokenExpiresAtUtc, session.SessionExpiresAtUtc));
    }

    private async Task<IActionResult> OriginRejectedAsync()
    {
        await boundaryAudit.RecordAsync(SecurityAuditActions.CsrfRejected, HttpContext, CorsSettings.Normalize(Request.Headers.Origin.ToString()) ?? "invalid-origin");
        return resultMapper.ToProblemResult(AccessErrors.CsrfInvalid, HttpContext);
    }
}
