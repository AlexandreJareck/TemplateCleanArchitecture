using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Template.Infrastructure.Identity.Models;

namespace Template.Infrastructure.Identity.Seeds;

public static class DefaultRoles
{
    public static async Task SeedAsync(RoleManager<ApplicationRole> roleManager)
    {
        if (!await roleManager.Roles.AnyAsync() && !await roleManager.RoleExistsAsync("Admin"))
            await roleManager.CreateAsync(new ApplicationRole("Admin"));
    }
}

