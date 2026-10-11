using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Authentication;
using Template.Application.Logging;

namespace Template.WebApi.Infrastructure.Http;

/// <summary>
/// Writes 401/403 Problem Details and logs each outcome once, with method, path and correlation only: never the
/// token, claims, user identifiers or policy requirements.
/// </summary>
public sealed class ProblemDetailsAuthorizationMiddlewareResultHandler(
    ApiResultMapper resultMapper,
    ILogger<ProblemDetailsAuthorizationMiddlewareResultHandler> logger)
    : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            logger.LogInformation(
                LogEvents.AuthenticationChallenged,
                "Request {RequestMethod} {RequestPath} was not authenticated ({CorrelationId})",
                context.Request.Method,
                context.Request.Path.Value,
                context.TraceIdentifier);
            await context.ChallengeAsync();
            await resultMapper.WriteAsync(
                context,
                resultMapper.CreateStatusProblem(StatusCodes.Status401Unauthorized, context));
            return;
        }

        if (authorizeResult.Forbidden)
        {
            logger.LogInformation(
                LogEvents.AuthorizationForbidden,
                "Request {RequestMethod} {RequestPath} was forbidden ({CorrelationId})",
                context.Request.Method,
                context.Request.Path.Value,
                context.TraceIdentifier);
            await context.ForbidAsync();
            await resultMapper.WriteAsync(
                context,
                resultMapper.CreateStatusProblem(StatusCodes.Status403Forbidden, context));
            return;
        }

        await defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}
