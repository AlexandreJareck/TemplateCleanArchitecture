using Template.Application.Interfaces;
using Template.Application.Interfaces.Access;

namespace Template.Worker;

/// <summary>
/// One correlation ID per scope (one sweep, report attempt or consumed message), so every log entry and audit record
/// written by that unit of work shares it (it previously changed on every read). A consumed event adopts the
/// originating request's correlation through <see cref="IInboundCorrelation"/>.
/// </summary>
public sealed class WorkerRequestContext : IRequestContext, IInboundCorrelation
{
    public string CorrelationId { get; private set; } = Template.Application.Logging.CorrelationId.New();

    public void Adopt(string correlationId) =>
        CorrelationId = Template.Application.Logging.CorrelationId.Normalize(correlationId, CorrelationId);
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
