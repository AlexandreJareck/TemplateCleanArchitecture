using System;
using System.Collections.Generic;

namespace Template.Domain.Common;

/// <summary>
/// Fact emitted by an aggregate transition (docs/product/event-catalog.md). <see cref="EventType"/> is the dotted wire
/// name of the integration event it will map to once the Outbox exists (Phase 28); until then the event is recorded
/// on the transition history and drives audit-safe logs and metrics only.
/// </summary>
public abstract record DomainEvent(DateTime OccurredAtUtc)
{
    public abstract string EventType { get; }
}

/// <summary>Collects the domain events of one aggregate instance until the use case has handled them.</summary>
public sealed class DomainEventBuffer
{
    private readonly List<DomainEvent> events = [];

    public IReadOnlyList<DomainEvent> Events => events;

    public void Raise(DomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        events.Add(domainEvent);
    }

    public void Clear() => events.Clear();
}
