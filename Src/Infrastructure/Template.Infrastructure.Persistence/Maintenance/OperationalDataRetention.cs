#nullable enable
using Microsoft.EntityFrameworkCore;
using Template.Application.Interfaces.Maintenance;
using Template.Domain.Access;
using Template.Infrastructure.Persistence.Contexts;
using Template.Infrastructure.Persistence.Idempotency;
using Template.Infrastructure.Persistence.Messaging;

namespace Template.Infrastructure.Persistence.Maintenance;

/// <summary>
/// EF Core adapter of <see cref="IOperationalDataRetention"/>. Each call loads one bounded batch through an existing
/// index (<c>ExpiresAtUtc</c>, <c>CompletedAtUtc</c>) and removes it in one <c>SaveChanges</c>; refresh tokens go with
/// their family through the cascade. Works on PostgreSQL and on the in-memory provider used by the Test host.
/// </summary>
public sealed class OperationalDataRetention(ApplicationDbContext context) : IOperationalDataRetention
{
    private const int MaxBatch = 5000;

    public async Task<int> DeleteExpiredIdempotencyRecordsAsync(DateTime nowUtc, int maxRows, CancellationToken cancellationToken)
    {
        var rows = await context.Set<IdempotencyRecord>()
            .Where(record => record.ExpiresAtUtc <= nowUtc)
            .OrderBy(record => record.ExpiresAtUtc)
            .Take(Math.Clamp(maxRows, 1, MaxBatch))
            .ToListAsync(cancellationToken);
        return await RemoveAsync(rows, cancellationToken);
    }

    public async Task<int> DeleteCompletedInboxBeforeAsync(DateTime cutoffUtc, int maxRows, CancellationToken cancellationToken)
    {
        var rows = await context.Set<InboxMessage>()
            .Where(message => message.CompletedAtUtc < cutoffUtc)
            .OrderBy(message => message.CompletedAtUtc)
            .Take(Math.Clamp(maxRows, 1, MaxBatch))
            .ToListAsync(cancellationToken);
        return await RemoveAsync(rows, cancellationToken);
    }

    public async Task<int> DeleteSessionFamiliesExpiredBeforeAsync(DateTime cutoffUtc, int maxRows, CancellationToken cancellationToken)
    {
        var rows = await context.Set<SessionFamily>()
            .Include(family => family.Tokens)
            .Where(family => family.AbsoluteExpiresAtUtc < cutoffUtc)
            .OrderBy(family => family.AbsoluteExpiresAtUtc)
            .Take(Math.Clamp(maxRows, 1, MaxBatch))
            .ToListAsync(cancellationToken);
        return await RemoveAsync(rows, cancellationToken);
    }

    private async Task<int> RemoveAsync<T>(IReadOnlyCollection<T> rows, CancellationToken cancellationToken) where T : class
    {
        if (rows.Count == 0)
            return 0;
        context.RemoveRange(rows);
        await context.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }
}
