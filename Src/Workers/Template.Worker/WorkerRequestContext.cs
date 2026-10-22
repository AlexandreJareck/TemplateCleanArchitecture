using Template.Application.Interfaces;

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
