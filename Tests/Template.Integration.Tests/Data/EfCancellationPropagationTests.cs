using Template.Domain.Products.Entities;
using Template.Infrastructure.Persistence.Repositories;

namespace Template.Integration.Tests.Data;

public class EfCancellationPropagationTests : BaseEfRepoTestFixture
{
    private static CancellationToken CancelledToken() => new(canceled: true);

    [Fact]
    public async Task UnitOfWork_SaveChangesAsync_HonorsCancelledToken()
    {
        var repository = GetRepository<ProductEntity>();
        await repository.AddAsync(new ProductEntity("Cancelled save", 1m, "CANCEL-001"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GetUnitOfWork().SaveChangesAsync(CancelledToken()));
    }

    [Fact]
    public async Task GenericRepository_GetAllAsync_HonorsCancelledToken()
    {
        var repository = GetRepository<ProductEntity>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.GetAllAsync(CancelledToken()));
    }

    [Fact]
    public async Task ProductRepository_GetPagedListAsync_HonorsCancelledToken()
    {
        var repository = new ProductRepository(dbContext);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.GetPagedListAsync(1, 10, null!, CancelledToken()));
    }
}
