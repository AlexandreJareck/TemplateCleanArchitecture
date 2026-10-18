using Microsoft.EntityFrameworkCore;
using Template.Application.Interfaces.Access;
using Template.Domain.Access;
using Template.Infrastructure.Persistence.Contexts;

namespace Template.Infrastructure.Persistence.Repositories;

/// <summary>EF Core adapter of <see cref="ISessionFamilyRepository"/>; only token hashes are ever queried or stored.</summary>
public sealed class SessionFamilyRepository(ApplicationDbContext context) : ISessionFamilyRepository
{
    public void Add(SessionFamily family) => context.Add(family);

    public async Task<SessionFamily?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        var familyId = await context.Set<RefreshTokenRecord>()
            .AsNoTracking()
            .Where(token => token.TokenHash == tokenHash)
            .Select(token => (Guid?)token.FamilyId)
            .SingleOrDefaultAsync(cancellationToken);

        return familyId is null
            ? null
            : await context.Set<SessionFamily>()
                .Include(family => family.Tokens)
                .SingleOrDefaultAsync(family => family.Id == familyId.Value, cancellationToken);
    }

    public async Task<IReadOnlyList<SessionFamily>> ListActiveAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
        await context.Set<SessionFamily>()
            .Include(family => family.Tokens)
            .Where(family => family.UserId == userId
                && family.RevokedAtUtc == null
                && family.AbsoluteExpiresAtUtc > nowUtc
                && family.IdleExpiresAtUtc > nowUtc)
            .OrderBy(family => family.CreatedAtUtc)
            .ThenBy(family => family.Id)
            .ToListAsync(cancellationToken);

    public Task<bool> IsActiveAsync(Guid sessionFamilyId, Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
        context.Set<SessionFamily>()
            .AsNoTracking()
            .AnyAsync(family => family.Id == sessionFamilyId
                && family.UserId == userId
                && family.RevokedAtUtc == null
                && family.AbsoluteExpiresAtUtc > nowUtc
                && family.IdleExpiresAtUtc > nowUtc,
                cancellationToken);
}
