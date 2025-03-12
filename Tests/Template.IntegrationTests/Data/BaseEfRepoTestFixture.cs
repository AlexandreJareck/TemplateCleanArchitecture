using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Template.Application.Interfaces;
using Template.Infrastructure.Persistence.Contexts;
using Template.Infrastructure.Persistence.Repositories;

namespace Template.IntegrationTests.Data;

public abstract class BaseEfRepoTestFixture
{
    protected ApplicationDbContext dbContext;

    protected BaseEfRepoTestFixture()
    {
        var options = CreateNewContextOptions();
        IAuthenticatedUserService authenticatedUserService = new AuthenticatedUserService(Guid.NewGuid().ToString(), "UserName");

        dbContext = new ApplicationDbContext(options, authenticatedUserService);
    }

    protected static DbContextOptions<ApplicationDbContext> CreateNewContextOptions()
    {
        var serviceProvider = new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .BuildServiceProvider();

        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        builder.UseInMemoryDatabase(nameof(ApplicationDbContext))
               .UseInternalServiceProvider(serviceProvider);

        return builder.Options;
    }

    protected GenericRepository<T> GetRepository<T>() where T : class
    {
        return new GenericRepository<T>(dbContext);
    }

    protected IUnitOfWork GetUnitOfWork()
    {
        return new UnitOfWork(dbContext);
    }
}
internal record AuthenticatedUserService(string UserId, string UserName) : IAuthenticatedUserService;
