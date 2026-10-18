using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using Template.WebApi.Infrastructure.Security;

namespace Template.WebApi.Infrastructure.Extensions;

/// <summary>
/// Documents Phase 24+ conventions per operation (docs/product/api-guidelines.md §10): the required permission and the
/// Problem Details <c>code</c> values the operation can return, appended to its description. Operations without these
/// attributes (every legacy endpoint) are left untouched.
/// </summary>
public sealed class ApiConventionsOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var attributes = context.MethodInfo.GetCustomAttributes(true)
            .Concat(context.MethodInfo.DeclaringType?.GetCustomAttributes(true) ?? [])
            .ToArray();
        var lines = new List<string>();

        var permissions = attributes.OfType<HasPermissionAttribute>().Select(attribute => attribute.Permission).Distinct().ToArray();
        if (permissions.Length > 0)
            lines.Add($"Requires permission: {string.Join(", ", permissions.Select(permission => $"`{permission}`"))}.");
        else if (attributes.OfType<AuthorizeAttribute>().Any() && !attributes.OfType<AllowAnonymousAttribute>().Any()
            && attributes.OfType<StableProblemCodesAttribute>().Any())
            lines.Add("Requires an authenticated user.");

        var codes = attributes.OfType<ProblemCodesAttribute>().SelectMany(attribute => attribute.Codes).Distinct().ToArray();
        if (codes.Length > 0)
            lines.Add($"Problem codes: {string.Join(", ", codes.Select(code => $"`{code}`"))}.");

        if (lines.Count > 0)
            operation.Description = string.Join("\n\n", new[] { operation.Description }.Where(text => !string.IsNullOrWhiteSpace(text)).Concat(lines));
    }
}
