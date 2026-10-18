#nullable enable
using Template.Domain.Access;

namespace Template.Application.Interfaces.Access;

/// <summary>
/// Persistence port of <see cref="SessionFamily"/>. Changes are committed through <see cref="IUnitOfWork"/> together
/// with their audit records; a stale version raises
/// <see cref="Template.Application.Common.Concurrency.ConcurrencyConflictException"/>.
/// </summary>
public interface ISessionFamilyRepository
{
    void Add(SessionFamily family);

    /// <summary>The family (with its tokens) that issued the token with this hash, or <c>null</c>.</summary>
    Task<SessionFamily?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>Families of the user that are neither revoked nor expired at <paramref name="nowUtc"/>, oldest first.</summary>
    Task<IReadOnlyList<SessionFamily>> ListActiveAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>Whether the family exists, belongs to the user and is active (used to validate access tokens).</summary>
    Task<bool> IsActiveAsync(Guid sessionFamilyId, Guid userId, DateTime nowUtc, CancellationToken cancellationToken);
}
