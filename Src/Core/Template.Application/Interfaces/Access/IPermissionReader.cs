#nullable enable
namespace Template.Application.Interfaces.Access;

/// <summary>Effective permissions of a user: the union of the permissions of every role the user holds (SEC-008).</summary>
public interface IPermissionReader
{
    Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken);
}
