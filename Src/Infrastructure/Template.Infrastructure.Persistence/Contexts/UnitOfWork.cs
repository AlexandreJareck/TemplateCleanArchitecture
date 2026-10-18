using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Template.Application.Common.Concurrency;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Catalog;
using Template.Application.Interfaces.Organization;
using Template.Application.Interfaces.Suppliers;

namespace Template.Infrastructure.Persistence.Contexts;

public sealed class UnitOfWork(ApplicationDbContext dbContext) : IUnitOfWork
{
    /// <summary>
    /// Unique indexes that guard aggregate concurrency in addition to the version token: two rotations of one session
    /// family insert the same (family, sequence) pair, and PostgreSQL may report that before the version check.
    /// </summary>
    private static readonly HashSet<string> ConcurrencyGuardConstraints = new(StringComparer.Ordinal)
    {
        "IX_AccessRefreshTokens_FamilyId_Sequence",
        "IX_SupplierStatusChanges_SupplierId_Sequence"
    };

    /// <summary>
    /// Business-key unique indexes (ORG-001, CAT-001, SUP-001). A violation means a concurrent duplicate that raced past
    /// the use-case pre-check; it surfaces as <see cref="UniqueConstraintViolationException"/> so the use case answers
    /// with its duplicate code. Any other violation (primary keys, append-only audit IDs) keeps the original exception.
    /// </summary>
    private static readonly HashSet<string> BusinessKeyConstraints = new(StringComparer.Ordinal)
    {
        IDepartmentRepository.CodeUniqueKey,
        ICostCenterRepository.CodeUniqueKey,
        ICategoryRepository.CodeUniqueKey,
        IUnitOfMeasureRepository.CodeUniqueKey,
        ICatalogItemRepository.CodeUniqueKey,
        ISupplierRepository.TaxIdUniqueKey
    };

    private IDbContextTransaction? transaction;

    public async Task<bool> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken) > 0;
        }
        catch (DbUpdateConcurrencyException error)
        {
            throw new ConcurrencyConflictException("A versioned aggregate changed after it was read.", error);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: { } constraint
        } && ConcurrencyGuardConstraints.Contains(constraint))
        {
            throw new ConcurrencyConflictException("A concurrent change of the same aggregate won.", error);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: { } constraint
        } && BusinessKeyConstraints.Contains(constraint))
        {
            throw new UniqueConstraintViolationException(constraint, error);
        }
    }

    public void DiscardChanges() => dbContext.ChangeTracker.Clear();

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
            var changed = await SaveChangesAsync(cancellationToken);
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
