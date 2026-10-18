#nullable enable
namespace Template.Application.Interfaces.Access;

/// <summary>A signed short-lived access token. The value is returned to the client once and never logged.</summary>
public sealed record IssuedAccessToken(string Token, DateTime ExpiresAtUtc);

/// <summary>Issues access tokens bound to a session family (the JWT implementation lives in Infrastructure.Identity).</summary>
public interface IAccessTokenIssuer
{
    Task<IssuedAccessToken> IssueAsync(Guid userId, Guid sessionFamilyId, CancellationToken cancellationToken);
}
