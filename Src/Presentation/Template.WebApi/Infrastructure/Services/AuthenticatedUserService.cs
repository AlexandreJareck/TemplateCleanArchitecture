using System.Security.Claims;
using Template.Application.Interfaces;

namespace Template.WebApi.Infrastructure.Services;

/// <summary>
/// HTTP adapter for <see cref="IAuthenticatedUserService"/>. Claims are read on access, not captured at
/// construction, and only from an authenticated principal. See the interface for the no-user policy.
/// </summary>
public sealed class AuthenticatedUserService(IHttpContextAccessor httpContextAccessor) : IAuthenticatedUserService
{
    public string? UserId =>
        AuthenticatedPrincipal()?.FindFirstValue(ClaimTypes.NameIdentifier) is { } value && Guid.TryParse(value, out var id)
            ? id.ToString()
            : null;

    public string? UserName =>
        AuthenticatedPrincipal()?.Identity?.Name is { Length: > 0 } name && !string.IsNullOrWhiteSpace(name)
            ? name
            : null;

    private ClaimsPrincipal? AuthenticatedPrincipal() =>
        httpContextAccessor.HttpContext?.User is { Identity.IsAuthenticated: true } user ? user : null;
}
