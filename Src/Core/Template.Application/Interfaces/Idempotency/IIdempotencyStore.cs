#nullable enable
namespace Template.Application.Interfaces.Idempotency;

/// <summary>
/// Identifies one idempotent request (docs/product/api-guidelines.md §6): the client key scoped to the caller, the
/// operation (method + route template) and the SHA-256 of the canonical request body.
/// </summary>
public sealed record IdempotencyRequest(Guid UserId, string Key, string Operation, string RequestHash);

/// <summary>A completed response that can be replayed byte for byte.</summary>
public sealed record StoredIdempotentResponse(int StatusCode, string? ContentType, string? Body, string? Location);

public enum IdempotencyBeginStatus
{
    /// <summary>No live record existed; the caller owns the key and must complete or abandon it.</summary>
    Started,

    /// <summary>Same key and same request completed earlier; replay <see cref="IdempotencyBeginResult.Response"/>.</summary>
    Replay,

    /// <summary>Same key, still executing (409 <c>idempotency.in-progress</c>).</summary>
    InProgress,

    /// <summary>Same key with a different operation or body (422 <c>idempotency.key-reused</c>).</summary>
    KeyReused
}

public sealed record IdempotencyBeginResult(IdempotencyBeginStatus Status, StoredIdempotentResponse? Response = null);

/// <summary>Durable idempotency records with a 24-hour lifetime (PostgreSQL in Persistence).</summary>
public interface IIdempotencyStore
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    Task<IdempotencyBeginResult> BeginAsync(IdempotencyRequest request, CancellationToken cancellationToken);

    Task CompleteAsync(IdempotencyRequest request, StoredIdempotentResponse response, CancellationToken cancellationToken);

    /// <summary>Releases the key after a failure that must not be replayed (unexpected error, cancellation).</summary>
    Task AbandonAsync(IdempotencyRequest request, CancellationToken cancellationToken);
}
