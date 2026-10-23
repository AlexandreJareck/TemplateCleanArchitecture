using Template.Application.Interfaces;
using Template.Application.Interfaces.Access;

namespace Template.Worker;

public sealed class WorkerRequestContext : IRequestContext
{
    public string CorrelationId => Guid.NewGuid().ToString("N");
}

public sealed class WorkerAuthenticatedUser : IAuthenticatedUserService
{
    public string? UserId => null;
    public string? UserName => null;
}

/// <summary>
/// The Worker acts only as the system actor (no user session), so it holds no permission. Report attempts and the
/// retention pass never authorize by permission; authorization happened when the WebApi accepted the request.
/// </summary>
public sealed class WorkerPermissions : IPermissionReader
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(None);
}
