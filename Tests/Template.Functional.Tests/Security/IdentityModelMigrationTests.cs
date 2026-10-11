using Microsoft.EntityFrameworkCore;
using Shouldly;
using Template.Infrastructure.Identity.Contexts;

namespace Template.Functional.Tests.Security;

public sealed class IdentityModelMigrationTests
{
    [Fact]
    public void IdentityModel_HasNoPendingModelChanges()
    {
        // Npgsql provider without a connection: compares the model with the migrations snapshot only.
        var options = new DbContextOptionsBuilder<IdentityContext>().UseNpgsql().Options;
        using var context = new IdentityContext(options);

        context.Database.HasPendingModelChanges().ShouldBeFalse();
        context.Database.GetMigrations().ShouldBe(["20250326223511_InitialPostgresMigration"]);
    }
}
