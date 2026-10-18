using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Template.Application.Common.Paging;
using Template.Application.Interfaces.Access;
using Template.Application.Security;
using Template.Infrastructure.Identity.Contexts;
using Template.Infrastructure.Identity.Models;

namespace Template.Infrastructure.Identity.Sessions;

/// <summary>
/// ASP.NET Core Identity adapter of <see cref="IAccountDirectory"/> and <see cref="IPermissionReader"/>.
/// Credential checks never sign in, never rotate the security stamp (unlike the legacy <c>authenticate</c> route) and
/// spend comparable work for unknown users and wrong passwords so the response does not reveal which applied.
/// </summary>
public sealed class IdentityAccountDirectory(
    UserManager<ApplicationUser> userManager,
    IdentityContext identityContext) : IAccountDirectory, IPermissionReader
{
    // A hash of a random throwaway password, computed once, so unknown users cost one password verification too.
    private static readonly Lazy<string> DecoyHash = new(() =>
        new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), Guid.NewGuid().ToString("N")));

    public async Task<VerifiedAccount?> VerifyCredentialsAsync(string userName, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByNameAsync(userName);
        if (user is null || string.IsNullOrEmpty(user.PasswordHash))
        {
            userManager.PasswordHasher.VerifyHashedPassword(new ApplicationUser(), DecoyHash.Value, password);
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!await userManager.CheckPasswordAsync(user, password))
            return null;

        return new VerifiedAccount(user.Id, Fingerprint(await userManager.GetSecurityStampAsync(user)));
    }

    public async Task<string?> GetSecurityStampFingerprintAsync(Guid userId, CancellationToken cancellationToken)
    {
        var stamp = await identityContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new { user.SecurityStamp })
            .SingleOrDefaultAsync(cancellationToken);
        return stamp is null ? null : Fingerprint(stamp.SecurityStamp);
    }

    public async Task<AccountProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await identityContext.Users
            .AsNoTracking()
            .Where(candidate => candidate.Id == userId)
            .Select(candidate => new { candidate.Id, candidate.UserName, candidate.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
            return null;

        var roles = await (
                from userRole in identityContext.UserRoles
                join role in identityContext.Roles on userRole.RoleId equals role.Id
                where userRole.UserId == userId
                select role.Name)
            .ToListAsync(cancellationToken);

        return new AccountProfile(user.Id, user.UserName ?? string.Empty, user.Name, roles.Where(role => role is not null).Select(role => role!).ToArray());
    }

    public async Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var values = await (
                from userRole in identityContext.UserRoles
                join claim in identityContext.RoleClaims on userRole.RoleId equals claim.RoleId
                where userRole.UserId == userId && claim.ClaimType == Permissions.ClaimType
                select claim.ClaimValue)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Unknown values (typos in edited role data) grant nothing.
        return values.Where(Permissions.IsKnown).Select(value => value!).ToHashSet(StringComparer.Ordinal);
    }

    public async Task<bool> IsActiveUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        return await identityContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == userId && (user.LockoutEnd == null || user.LockoutEnd <= now), cancellationToken);
    }

    public async Task<PagedResult<UserSummary>> SearchUsersAsync(string? search, PageRequest page, CancellationToken cancellationToken)
    {
        var query = identityContext.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(user => (user.NormalizedUserName != null && user.NormalizedUserName.Contains(term))
                || (user.Name != null && user.Name.ToUpper().Contains(term)));
        }

        var now = DateTimeOffset.UtcNow;
        var total = await query.LongCountAsync(cancellationToken);
        var users = await query
            .OrderBy(user => user.UserName)
            .ThenBy(user => user.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(user => new { user.Id, user.UserName, user.Name, user.LockoutEnd })
            .ToListAsync(cancellationToken);

        return new PagedResult<UserSummary>(
            users.Select(user => new UserSummary(user.Id, user.UserName ?? string.Empty, user.Name, user.LockoutEnd is null || user.LockoutEnd <= now)).ToArray(),
            page.Page,
            page.PageSize,
            total);
    }

    /// <summary>SHA-256 fingerprint of a security stamp; the stamp itself never leaves this adapter.</summary>
    public static string Fingerprint(string? securityStamp) =>
        SessionTokenService.Hash(Encoding.UTF8.GetBytes(securityStamp ?? string.Empty));
}
