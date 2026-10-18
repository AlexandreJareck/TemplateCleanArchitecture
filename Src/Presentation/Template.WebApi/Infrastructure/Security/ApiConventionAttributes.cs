using Microsoft.AspNetCore.Mvc.Filters;

namespace Template.WebApi.Infrastructure.Security;

/// <summary>
/// Marks endpoints that follow docs/product/api-guidelines.md (Phase 24+): their Problem Details carry the stable
/// <c>code</c> extension, including 400/401/403 produced outside the action. Legacy v1/v2 endpoints do not carry it,
/// so their responses stay byte-compatible.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class StableProblemCodesAttribute : Attribute;

/// <summary>Problem <c>code</c> values an operation can return; listed in its OpenAPI description.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ProblemCodesAttribute(params string[] codes) : Attribute
{
    public IReadOnlyList<string> Codes { get; } = codes;
}

/// <summary>
/// NFR-SEC-5 headers for authenticated or credential-bearing JSON: <c>Cache-Control: no-store</c>,
/// <c>X-Content-Type-Options: nosniff</c> and <c>Referrer-Policy: no-referrer</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class NoStoreSecurityHeadersAttribute : Attribute, IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context) => Apply(context.HttpContext.Response.Headers);

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }

    public static void Apply(IHeaderDictionary headers)
    {
        headers.CacheControl = "no-store";
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
    }
}
