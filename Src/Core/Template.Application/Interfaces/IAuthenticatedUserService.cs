#nullable enable
namespace Template.Application.Interfaces;

/// <summary>
/// Request-scoped view of the current caller, free of HTTP and claims types.
/// <list type="bullet">
/// <item>Unauthenticated callers, background work and design-time/test contexts have no user:
/// <see cref="UserId"/> and <see cref="UserName"/> are <c>null</c>.</item>
/// <item><see cref="UserId"/> is set only when the subject claim is present and is a valid GUID;
/// a missing or malformed identifier is treated as no user, never as a fabricated one.</item>
/// <item>An empty user name is reported as <c>null</c>.</item>
/// </list>
/// </summary>
public interface IAuthenticatedUserService
{
    string? UserId { get; }
    string? UserName { get; }
}
