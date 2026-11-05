#nullable enable
namespace Template.Application.Interfaces.Access;

/// <summary>
/// Resolves user IDs already returned by a readable resource (timeline actors, audit actors) to display names only:
/// the user's name, or the user name when no name is set. No email, phone, claim or other personal datum crosses this
/// port. Unknown IDs are absent from the result. Callers expose the names only on resources the caller may already read.
/// </summary>
public interface IUserDisplayNames
{
    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}

public static class UserDisplayNamesExtensions
{
    /// <summary>Distinct non-null IDs resolved in one call; an empty input never reaches the directory.</summary>
    public static async Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(
        this IUserDisplayNames directory, IEnumerable<Guid?> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.OfType<Guid>().Distinct().ToArray();
        return ids.Length == 0 ? new Dictionary<Guid, string>() : await directory.GetDisplayNamesAsync(ids, cancellationToken);
    }

    public static string? NameOf(this IReadOnlyDictionary<Guid, string> names, Guid? userId) =>
        userId is { } id && names.TryGetValue(id, out var name) ? name : null;
}
