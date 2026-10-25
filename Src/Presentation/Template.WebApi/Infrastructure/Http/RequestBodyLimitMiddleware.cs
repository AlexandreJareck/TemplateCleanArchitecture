namespace Template.WebApi.Infrastructure.Http;

/// <summary>
/// NFR-SEC-4 (Phase 33): request bodies are at most <see cref="MaxBodyBytes"/>. A declared <c>Content-Length</c> above
/// the limit is rejected before any body is read with <c>413</c> Problem Details (code <c>request.too-large</c>);
/// Kestrel's <c>MaxRequestBodySize</c> enforces the same limit for chunked bodies. No endpoint accepts file bytes, so
/// one global limit fits every route.
/// </summary>
public sealed class RequestBodyLimitMiddleware(RequestDelegate next, ApiResultMapper resultMapper)
{
    public const long MaxBodyBytes = 1024 * 1024;

    public const string ProblemCode = "request.too-large";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.ContentLength > MaxBodyBytes)
        {
            var problem = resultMapper.CreateStatusProblem(StatusCodes.Status413PayloadTooLarge, context);
            problem.Extensions["code"] = ProblemCode;
            await resultMapper.WriteAsync(context, problem);
            return;
        }

        await next(context);
    }
}
