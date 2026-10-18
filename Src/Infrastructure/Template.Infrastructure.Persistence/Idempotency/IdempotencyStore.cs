using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Idempotency;
using Template.Infrastructure.Persistence.Contexts;

namespace Template.Infrastructure.Persistence.Idempotency;

/// <summary>
/// PostgreSQL-backed <see cref="IIdempotencyStore"/>. The unique index on (user, key) arbitrates concurrent first
/// requests: exactly one insert wins and the other sees the in-progress record. Every call uses its own
/// <see cref="ApplicationDbContext"/> scope, so it never commits or discards the caller's tracked changes.
/// </summary>
internal sealed class IdempotencyStore(IServiceScopeFactory scopeFactory, IClock clock) : IIdempotencyStore
{
    public async Task<IdempotencyBeginResult> BeginAsync(IdempotencyRequest request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var existing = await FindAsync(context, request, cancellationToken);
            if (existing is not null && existing.ExpiresAtUtc <= now)
            {
                context.Remove(existing);
                existing = null;
            }

            if (existing is null)
            {
                context.Add(new IdempotencyRecord
                {
                    Id = Guid.NewGuid(),
                    UserId = request.UserId,
                    Key = request.Key,
                    Operation = request.Operation,
                    RequestHash = request.RequestHash,
                    CreatedAtUtc = now,
                    ExpiresAtUtc = now + IIdempotencyStore.Lifetime
                });
                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                    return new IdempotencyBeginResult(IdempotencyBeginStatus.Started);
                }
                catch (DbUpdateException)
                {
                    // Lost the race on the unique (user, key) index: evaluate the winner's record below.
                }
            }
            else
            {
                return Evaluate(existing, request);
            }
        }

        await using var retryScope = scopeFactory.CreateAsyncScope();
        var winner = await FindAsync(retryScope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), request, cancellationToken)
            ?? throw new InvalidOperationException("The idempotency record could not be created or read.");
        return Evaluate(winner, request);
    }

    public async Task CompleteAsync(IdempotencyRequest request, StoredIdempotentResponse response, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var record = await FindAsync(context, request, cancellationToken);
        if (record is null || record.StatusCode is not null)
            return;

        record.StatusCode = response.StatusCode;
        record.ContentType = response.ContentType;
        record.ResponseBody = response.Body;
        record.Location = response.Location;
        record.CompletedAtUtc = clock.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task AbandonAsync(IdempotencyRequest request, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var record = await FindAsync(context, request, cancellationToken);
        if (record is null || record.StatusCode is not null)
            return;

        context.Remove(record);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static IdempotencyBeginResult Evaluate(IdempotencyRecord existing, IdempotencyRequest request)
    {
        if (!string.Equals(existing.Operation, request.Operation, StringComparison.Ordinal)
            || !string.Equals(existing.RequestHash, request.RequestHash, StringComparison.Ordinal))
            return new IdempotencyBeginResult(IdempotencyBeginStatus.KeyReused);

        return existing.StatusCode is { } statusCode
            ? new IdempotencyBeginResult(
                IdempotencyBeginStatus.Replay,
                new StoredIdempotentResponse(statusCode, existing.ContentType, existing.ResponseBody, existing.Location))
            : new IdempotencyBeginResult(IdempotencyBeginStatus.InProgress);
    }

    private static Task<IdempotencyRecord?> FindAsync(ApplicationDbContext context, IdempotencyRequest request, CancellationToken cancellationToken) =>
        context.Set<IdempotencyRecord>()
            .SingleOrDefaultAsync(record => record.UserId == request.UserId && record.Key == request.Key, cancellationToken);
}
