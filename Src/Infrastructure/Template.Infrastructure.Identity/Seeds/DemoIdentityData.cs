using Microsoft.AspNetCore.Identity;
using Template.Application.Demo;
using Template.Application.Security;
using Template.Infrastructure.Identity.Models;

namespace Template.Infrastructure.Identity.Seeds;

/// <summary>Opt-in academic personas. Passwords are supplied at runtime and are never committed.</summary>
public static class DemoIdentityData
{
    private sealed record Persona(Guid Id, string UserName, string Email, string Name, string Role);

    private static readonly Persona[] Personas =
    [
        new(DemoDataIds.Administrator, "demo.admin", "admin@demo.local", "Marina Costa", SeededRoles.Administrator),
        new(DemoDataIds.Requester, "demo.requester", "requester@demo.local", "Lucas Almeida", SeededRoles.Requester),
        new(DemoDataIds.CostCenterManager, "demo.manager", "manager@demo.local", "Camila Ribeiro", SeededRoles.CostCenterManager),
        new(DemoDataIds.DepartmentHead, "demo.department-head", "department.head@demo.local", "Renata Nogueira", SeededRoles.DepartmentHead),
        new(DemoDataIds.FinanceApprover, "demo.finance", "finance@demo.local", "Eduardo Martins", SeededRoles.FinanceApprover),
        new(DemoDataIds.BudgetController, "demo.budget", "budget@demo.local", "Patricia Lima", SeededRoles.BudgetController),
        new(DemoDataIds.Buyer, "demo.buyer", "buyer@demo.local", "Gabriel Souza", SeededRoles.Buyer),
        new(DemoDataIds.ProcurementManager, "demo.procurement", "procurement@demo.local", "Fernanda Rocha", SeededRoles.ProcurementManager),
        new(DemoDataIds.Receiver, "demo.receiver", "receiver@demo.local", "Rafael Santos", SeededRoles.Receiver),
        new(DemoDataIds.Auditor, "demo.auditor", "auditor@demo.local", "Beatriz Moreira", SeededRoles.Auditor)
    ];

    public static async Task SeedAsync(UserManager<ApplicationUser> userManager, string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("DemoData:Password is required when DemoData:Enabled is true.");

        foreach (var persona in Personas)
        {
            var user = await userManager.FindByNameAsync(persona.UserName);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    Id = persona.Id,
                    UserName = persona.UserName,
                    Email = persona.Email,
                    Name = persona.Name,
                    EmailConfirmed = true
                };
                EnsureSucceeded(await userManager.CreateAsync(user, password), persona.UserName);
            }
            else if (!await userManager.CheckPasswordAsync(user, password))
            {
                var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
                EnsureSucceeded(await userManager.ResetPasswordAsync(user, resetToken, password), persona.UserName);
            }

            if (!await userManager.IsInRoleAsync(user, persona.Role))
                EnsureSucceeded(await userManager.AddToRoleAsync(user, persona.Role), persona.UserName);
        }
    }

    private static void EnsureSucceeded(IdentityResult result, string userName)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Seeding demo user '{userName}' failed: {string.Join(", ", result.Errors.Select(error => error.Code))}.");
    }
}
