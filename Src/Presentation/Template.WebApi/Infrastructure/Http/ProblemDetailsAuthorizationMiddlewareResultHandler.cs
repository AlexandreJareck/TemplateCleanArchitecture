using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Authentication;
using Template.Application.Common.Results;
using Template.Application.Logging;
using Template.Application.Security;
using Template.WebApi.Infrastructure.Security;

namespace Template.WebApi.Infrastructure.Http;

/// <summary>
/// Writes 401/403 Problem Details and logs each outcome once, with method, path and correlation only: never the
/// token, claims, user identifiers or policy requirements. Endpoints marked <see cref="StableProblemCodesAttribute"/>
/// also get the stable <c>code</c> extension, and a denied permission is audited (sampled) as
/// <c>security.forbidden</c> (SEC-008). Legacy endpoints keep their exact previous body.
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
        var stableCodes = context.GetEndpoint()?.Metadata.GetMetadata<StableProblemCodesAttribute>() is not null;

        if (authorizeResult.Challenged)
        {
            logger.LogInformation(
                LogEvents.AuthenticationChallenged,
                "Request {RequestMethod} {RequestPath} was not authenticated ({CorrelationId})",
                context.Request.Method,
                context.Request.Path.Value,
                context.TraceIdentifier);
            await context.ChallengeAsync();
            if (stableCodes)
                NoStoreSecurityHeadersAttribute.Apply(context.Response.Headers);
            await resultMapper.WriteAsync(
                context,
                WithCode(resultMapper.CreateStatusProblem(StatusCodes.Status401Unauthorized, context), stableCodes, ProblemCodes.SessionExpired));
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

            var deniedPermission = authorizeResult.AuthorizationFailure?.FailedRequirements
                .OfType<PermissionRequirement>()
                .Select(requirement => requirement.Permission)
                .FirstOrDefault();
            if (deniedPermission is not null)
            {
                await context.RequestServices.GetRequiredService<BoundarySecurityAudit>().RecordAsync(
                    SecurityAuditActions.Forbidden,
                    context,
                    $"{context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value}|{deniedPermission}",
                    new Dictionary<string, string> { ["permission"] = deniedPermission });
            }

            await context.ForbidAsync();
            if (stableCodes)
                NoStoreSecurityHeadersAttribute.Apply(context.Response.Headers);
            await resultMapper.WriteAsync(
                context,
                WithCode(resultMapper.CreateStatusProblem(StatusCodes.Status403Forbidden, context), stableCodes, ProblemCodes.Forbidden));
            return;
        }

        await defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }

    private static Microsoft.AspNetCore.Mvc.ProblemDetails WithCode(Microsoft.AspNetCore.Mvc.ProblemDetails problem, bool stableCodes, string code)
    {
        if (stableCodes)
            problem.Extensions["code"] = code;
        return problem;
    }
}
