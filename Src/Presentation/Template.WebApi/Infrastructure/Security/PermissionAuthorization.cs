using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Template.Application.Interfaces.Access;
using Template.Application.Security;

namespace Template.WebApi.Infrastructure.Security;

/// <summary>
/// Authorizes an endpoint by one stable permission name (SEC-008); the policy has the permission's name. Roles are
/// never checked: they only group permissions in Identity. Business rules still run in the use case (B23-9).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public HasPermissionAttribute(string permission)
    {
        if (!Permissions.IsKnown(permission))
            throw new ArgumentException($"'{permission}' is not in the permission catalog.", nameof(permission));

        Permission = permission;
        Policy = permission;
    }

    public string Permission { get; }
}

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>Creates a policy for every catalog permission on demand; any other name falls back to the default provider.</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!Permissions.IsKnown(policyName))
            return await base.GetPolicyAsync(policyName);

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName))
            .Build();
    }
}

/// <summary>
/// Succeeds when the authenticated user's effective permissions (union over roles, read through
/// <see cref="IPermissionReader"/>) contain the required one. Permissions are read once per request.
/// </summary>
public sealed class PermissionAuthorizationHandler(IPermissionReader permissionReader) : AuthorizationHandler<PermissionRequirement>
{
    private IReadOnlySet<string>? cached;

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true
            || !Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return;

        var cancellationToken = context.Resource is HttpContext httpContext ? httpContext.RequestAborted : CancellationToken.None;
        cached ??= await permissionReader.GetEffectivePermissionsAsync(userId, cancellationToken);
        if (cached.Contains(requirement.Permission))
            context.Succeed(requirement);
    }
}
