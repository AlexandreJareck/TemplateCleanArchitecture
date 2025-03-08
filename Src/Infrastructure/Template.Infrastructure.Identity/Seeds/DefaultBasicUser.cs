using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Template.Infrastructure.Identity.Models;

namespace Template.Infrastructure.Identity.Seeds;
public static class DefaultBasicUser
{
    public static async Task SeedAsync(UserManager<ApplicationUser> userManager)
    {
        var defaultUser = new ApplicationUser
        {
            UserName = "Admin",
            Email = "Admin@Admin.com",
            Name = "Alexandre",
            PhoneNumber = "99999999999",
            EmailConfirmed = true,
            PhoneNumberConfirmed = true
        };

        if (!await userManager.Users.AnyAsync())
        {
            var user = await userManager.FindByEmailAsync(defaultUser.Email);
            if (user == null)
            {
                await userManager.CreateAsync(defaultUser, "Adm@12345");
            }
        }
    }
}
