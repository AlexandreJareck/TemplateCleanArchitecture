using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Template.Application.Security;
using Template.Infrastructure.Identity.Models;

namespace Template.Infrastructure.Identity.Seeds;

/// <summary>
/// Seeds the policy-version-1 roles of docs/product/permissions.md §3 as Identity roles whose permissions are role
/// claims of type <see cref="Permissions.ClaimType"/>. A role is created with its permissions only when it does not
/// exist, so later edits (Phase 25 role management) are never overwritten. The legacy <c>Admin</c> role is untouched.
/// </summary>
public static class PermissionRoles
{
    public static async Task SeedAsync(RoleManager<ApplicationRole> roleManager)
    {
        foreach (var (roleName, permissions) in SeededRoles.PermissionsByRole)
        {
            if (await roleManager.RoleExistsAsync(roleName))
                continue;

            var role = new ApplicationRole(roleName);
            EnsureSucceeded(await roleManager.CreateAsync(role), roleName);
            foreach (var permission in permissions)
                EnsureSucceeded(await roleManager.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission)), roleName);
        }
    }

    private static void EnsureSucceeded(IdentityResult result, string roleName)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"Seeding role '{roleName}' failed: {string.Join(", ", result.Errors.Select(error => error.Code))}.");
    }
}
