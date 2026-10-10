using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Template.Application.Interfaces;

namespace Template.Infrastructure.Persistence.Contexts;

public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql()
            .Options;

        return new ApplicationDbContext(options, DesignTimeAuthenticatedUser.Instance);
    }

    private sealed class DesignTimeAuthenticatedUser : IAuthenticatedUserService
    {
        public static DesignTimeAuthenticatedUser Instance { get; } = new();

        public string UserId => string.Empty;
        public string UserName => string.Empty;
    }
}
