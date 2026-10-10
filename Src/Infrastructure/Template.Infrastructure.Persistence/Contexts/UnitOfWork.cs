using Microsoft.EntityFrameworkCore.Storage;
using Template.Application.Interfaces;

namespace Template.Infrastructure.Persistence.Contexts;

public sealed class UnitOfWork(ApplicationDbContext dbContext) : IUnitOfWork
{
    private IDbContextTransaction? transaction;

    public async Task<bool> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        await dbContext.SaveChangesAsync(cancellationToken) > 0;

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (transaction is not null)
            throw new InvalidOperationException("A unit-of-work transaction is already active.");

        transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task<bool> CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        var activeTransaction = transaction
            ?? throw new InvalidOperationException("No unit-of-work transaction is active.");

        try
        {
            var changed = await dbContext.SaveChangesAsync(cancellationToken) > 0;
            await activeTransaction.CommitAsync(cancellationToken);
            return changed;
        }
        catch
        {
            await activeTransaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            await DisposeTransactionAsync();
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        var activeTransaction = transaction
            ?? throw new InvalidOperationException("No unit-of-work transaction is active.");

        try
        {
            await activeTransaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
        }
        finally
        {
            await DisposeTransactionAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (transaction is null)
            return;

        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
            await DisposeTransactionAsync();
        }
    }

    private async ValueTask DisposeTransactionAsync()
    {
        if (transaction is null)
            return;

        await transaction.DisposeAsync();
        transaction = null;
    }
}
