namespace Template.Application.Interfaces;
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>
    /// Commits pending changes. A stale versioned aggregate raises
    /// <see cref="Template.Application.Common.Concurrency.ConcurrencyConflictException"/> and nothing is written.
    /// </summary>
    Task<bool> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task<bool> CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>Forgets every tracked change so a use case can reload current state after a concurrency conflict.</summary>
    void DiscardChanges();
}
