using System.Globalization;
using Microsoft.AspNetCore.Mvc.Filters;
using Template.Application.Common.Results;
using Template.Application.Logging;
using Template.Application.Security;
using Template.Application.Wrappers;
using Template.WebApi.Infrastructure.Http;

namespace Template.WebApi.Infrastructure.Security;

/// <summary>Request bodies that expose the user name used for the login rate-limit partition.</summary>
public interface IRateLimitedLogin
{
    string? UserName { get; }
}

/// <summary>
/// Applies <see cref="AuthenticationRateLimiter"/> to one action (SEC-007). A rejected request gets 429 with
/// <c>Retry-After</c> and <c>code: auth.rate-limited</c>, a log entry and a sampled <c>security.rate-limited</c> audit.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AuthenticationRateLimitAttribute(AuthenticationRateLimitPolicy policy) : Attribute, IFilterFactory
{
    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) => new Filter(
        policy,
        serviceProvider.GetRequiredService<AuthenticationRateLimiter>(),
        serviceProvider.GetRequiredService<BoundarySecurityAudit>(),
        serviceProvider.GetRequiredService<ApiResultMapper>(),
        serviceProvider.GetRequiredService<ILogger<AuthenticationRateLimitAttribute>>());

    private sealed class Filter(
        AuthenticationRateLimitPolicy policy,
        AuthenticationRateLimiter limiter,
        BoundarySecurityAudit audit,
        ApiResultMapper resultMapper,
        ILogger logger) : IAsyncActionFilter
    {
        private static readonly ApplicationError RateLimited = new(
            ProblemCodes.RateLimited, ErrorCategory.RateLimited, "Too many authentication attempts; retry later.", "SEC-007");

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var userName = context.ActionArguments.Values.OfType<IRateLimitedLogin>().FirstOrDefault()?.UserName;
            var retryAfter = limiter.TryAcquire(policy, context.HttpContext, userName);
            if (retryAfter is null)
            {
                await next();
                return;
            }

            logger.LogWarning(LogEvents.AuthenticationRateLimited, "Authentication request rate-limited by policy {RateLimitPolicy} ({CorrelationId})",
                policy, context.HttpContext.TraceIdentifier);
            await audit.RecordAsync(
                SecurityAuditActions.RateLimited,
                context.HttpContext,
                AuthenticationRateLimiter.PartitionKey(policy, context.HttpContext, userName),
                new Dictionary<string, string> { ["policy"] = policy.ToString() });

            context.HttpContext.Response.Headers.RetryAfter =
                Math.Max(1, (int)Math.Ceiling(retryAfter.Value.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            context.Result = resultMapper.ToProblemResult(RateLimited, context.HttpContext);
        }
    }
}
