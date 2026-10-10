using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Authentication;

namespace Template.WebApi.Infrastructure.Http;

public sealed class ProblemDetailsAuthorizationMiddlewareResultHandler(ApiResultMapper resultMapper)
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
            await context.ChallengeAsync();
            await resultMapper.WriteAsync(
                context,
                resultMapper.CreateStatusProblem(StatusCodes.Status401Unauthorized, context));
            return;
        }

        if (authorizeResult.Forbidden)
        {
            await context.ForbidAsync();
            await resultMapper.WriteAsync(
                context,
                resultMapper.CreateStatusProblem(StatusCodes.Status403Forbidden, context));
            return;
        }

        await defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}
