using System;

namespace Template.Domain.Common;

/// <summary>
/// Aggregate root with an explicit optimistic-concurrency version (spike SP-1, Phase 24). Every state change of the
/// aggregate, including changes to its child entities, calls <see cref="IncrementVersion"/>; Persistence maps
/// <see cref="Version"/> as the concurrency token and HTTP exposes it as the strong <c>ETag</c>.
/// </summary>
public abstract class VersionedAggregateRoot
{
    public Guid Id { get; protected set; }

    /// <summary>Starts at 1 for a new aggregate and grows by one per change.</summary>
    public long Version { get; private set; } = 1;

    protected void IncrementVersion() => Version++;
}
