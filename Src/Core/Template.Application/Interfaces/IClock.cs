#nullable enable
namespace Template.Application.Interfaces;

/// <summary>
/// Application-owned time source. Use it instead of reading <see cref="DateTime.UtcNow"/>
/// directly so observable timestamps are deterministic in tests.
/// </summary>
public interface IClock
{
    /// <summary>Current instant in UTC (<see cref="DateTimeKind.Utc"/>).</summary>
    DateTime UtcNow { get; }
}
