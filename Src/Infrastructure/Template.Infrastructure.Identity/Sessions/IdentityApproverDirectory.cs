using Microsoft.EntityFrameworkCore;
using Template.Application.Common.Paging;
using Template.Application.Interfaces.Access;
using Template.Application.Interfaces.Approvals;
using Template.Application.Security;
using Template.Domain.Approvals;
using Template.Infrastructure.Identity.Contexts;

namespace Template.Infrastructure.Identity.Sessions;

/// <summary>
/// Identity adapter of <see cref="IApproverDirectory"/> (Phase 31): reads users, role memberships and the
/// <c>approvals.decide</c> role claim directly from the Identity store without tracking. A user is active when not
/// locked out (ORG-006). No email, phone, claim value or credential leaves the adapter.
/// </summary>
public sealed class IdentityApproverDirectory(IdentityContext identityContext) : IApproverDirectory
{
    public async Task<ApprovalCandidate?> GetCandidateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await identityContext.Users.AsNoTracking().Where(candidate => candidate.Id == userId)
            .Select(candidate => new { candidate.Id, candidate.LockoutEnd })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
            return null;
        var now = DateTimeOffset.UtcNow;
        return new ApprovalCandidate(user.Id, user.LockoutEnd is null || user.LockoutEnd <= now, await CanDecideAsync(userId, cancellationToken));
    }

    public async Task<IReadOnlySet<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var roles = await (
                from userRole in identityContext.UserRoles
                join role in identityContext.Roles on userRole.RoleId equals role.Id
                where userRole.UserId == userId
                select role.Name)
            .ToListAsync(cancellationToken);
        return roles.Where(role => role is not null).Select(role => role!).ToHashSet(StringComparer.Ordinal);
    }

    public async Task<PagedResult<UserSummary>> SearchApproversAsync(string? search, PageRequest page, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var approverRoleIds = identityContext.RoleClaims
            .Where(claim => claim.ClaimType == Permissions.ClaimType && claim.ClaimValue == Permissions.ApprovalsDecide)
            .Select(claim => claim.RoleId);
        var query = identityContext.Users.AsNoTracking()
            .Where(user => (user.LockoutEnd == null || user.LockoutEnd <= now)
                && identityContext.UserRoles.Any(userRole => userRole.UserId == user.Id && approverRoleIds.Contains(userRole.RoleId)));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(user => (user.NormalizedUserName != null && user.NormalizedUserName.Contains(term))
                || (user.Name != null && user.Name.ToUpper().Contains(term)));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var users = await query.OrderBy(user => user.UserName).ThenBy(user => user.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(user => new { user.Id, user.UserName, user.Name })
            .ToListAsync(cancellationToken);
        return new PagedResult<UserSummary>(
            users.Select(user => new UserSummary(user.Id, user.UserName ?? string.Empty, user.Name, true)).ToArray(), page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
            return new Dictionary<Guid, string>();
        var ids = userIds.ToArray();
        var users = await identityContext.Users.AsNoTracking().Where(user => ids.Contains(user.Id))
            .Select(user => new { user.Id, user.UserName, user.Name })
            .ToListAsync(cancellationToken);
        return users.ToDictionary(user => user.Id, user => string.IsNullOrWhiteSpace(user.Name) ? user.UserName ?? string.Empty : user.Name);
    }

    private Task<bool> CanDecideAsync(Guid userId, CancellationToken cancellationToken) =>
        (from userRole in identityContext.UserRoles
         join claim in identityContext.RoleClaims on userRole.RoleId equals claim.RoleId
         where userRole.UserId == userId && claim.ClaimType == Permissions.ClaimType && claim.ClaimValue == Permissions.ApprovalsDecide
         select claim.Id).AnyAsync(cancellationToken);
}
