using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Template.Application.Security;
using Template.Functional.Tests.Common;
using Template.Infrastructure.Identity.Models;
using Template.Infrastructure.Identity.Seeds;

namespace Template.Functional.Tests.Security;

/// <summary>
/// Opt-in academic personas (PR #33, covered in Phase 33): ten users with one business role each, idempotent, and a
/// password that must satisfy the Identity policy. A weak password failing at startup is what stopped the Compose
/// baseline, so the failure path is pinned too. Passwords here are synthetic and never committed elsewhere.
/// </summary>
public sealed class DemoIdentityDataTests(CustomWebApplicationFactory<Program> factory) : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private const string SyntheticPassword = "Demo!9synthetic-personas";

    private static readonly (string UserName, string Role)[] Personas =
    [
        ("demo.admin", SeededRoles.Administrator),
        ("demo.requester", SeededRoles.Requester),
        ("demo.manager", SeededRoles.CostCenterManager),
        ("demo.department-head", SeededRoles.DepartmentHead),
        ("demo.finance", SeededRoles.FinanceApprover),
        ("demo.budget", SeededRoles.BudgetController),
        ("demo.buyer", SeededRoles.Buyer),
        ("demo.procurement", SeededRoles.ProcurementManager),
        ("demo.receiver", SeededRoles.Receiver),
        ("demo.auditor", SeededRoles.Auditor)
    ];

    [Fact]
    public async Task EnabledDemoData_SeedsEveryPersonaWithItsRole_AtStartup_AndAgainWithoutDuplicates()
    {
        await using var host = factory.WithWebHostBuilder(builder => builder
            .UseSetting("DemoData:Enabled", "true")
            .UseSetting("DemoData:Password", SyntheticPassword));
        _ = host.CreateClient(); // starts the host, which seeds during Identity initialization

        await using var scope = host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await DemoIdentityData.SeedAsync(users, SyntheticPassword); // second run: nothing new

        foreach (var (userName, role) in Personas)
        {
            var user = await users.FindByNameAsync(userName);
            user.ShouldNotBeNull(userName);
            (await users.GetRolesAsync(user)).ShouldBe([role]);
            (await users.CheckPasswordAsync(user, SyntheticPassword)).ShouldBeTrue();
        }

        users.Users.Count(user => user.UserName!.StartsWith("demo.")).ShouldBe(Personas.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AMissingPassword_IsRefused(string password)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => DemoIdentityData.SeedAsync(users, password));

        exception.Message.ShouldContain("DemoData:Password");
    }

    [Fact]
    public async Task APasswordRejectedByThePolicy_FailsWithThePolicyCodes_AndNamesNoSecret()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => DemoIdentityData.SeedAsync(users, "lowercaseonly1"));

        exception.Message.ShouldContain("demo.admin");
        exception.Message.ShouldContain("PasswordRequiresUpper");
        exception.Message.ShouldNotContain("lowercaseonly1");
    }
}
