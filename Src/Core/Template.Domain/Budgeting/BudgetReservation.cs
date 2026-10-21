using System;
using System.Collections.Generic;

namespace Template.Domain.Budgeting;

/// <summary>Reservation lifecycle (D23-4): <c>Reserved → Committed</c> (final approval) and <c>Reserved/Committed → Released</c>.</summary>
public enum BudgetReservationStatus
{
    Reserved,
    Committed,
    Released
}

/// <summary>BUD-007 release reason codes.</summary>
public enum BudgetReleaseReason
{
    Withdrawn,
    Cancelled,
    Rejected,
    Expired,
    ShortClosed,
    OrderCancelled
}

/// <summary>Operation recorded in the immutable monetary history of a reservation.</summary>
public enum BudgetReservationOperation
{
    Reserved,
    Committed,
    Released
}

/// <summary>
/// Funds held by one requisition revision inside a <see cref="BudgetAllocation"/> (BUD-005..008). It lives inside the
/// allocation aggregate so availability is checked and changed under one row lock and one version token. Every
/// operation appends a <see cref="BudgetReservationChange"/>; amounts are never edited.
/// </summary>
public sealed class BudgetReservation
{
    private readonly List<BudgetReservationChange> changes = [];

    private BudgetReservation()
    {
        Currency = string.Empty;
    }

    internal BudgetReservation(Guid id, Guid allocationId, Guid requisitionId, int requisitionRevision, decimal amount, string currency, DateTime nowUtc)
        : this()
    {
        Id = id;
        AllocationId = allocationId;
        RequisitionId = requisitionId;
        RequisitionRevision = requisitionRevision;
        Amount = amount;
        Currency = currency;
        Status = BudgetReservationStatus.Reserved;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }
    public Guid AllocationId { get; private set; }
    public Guid RequisitionId { get; private set; }

    /// <summary>Requisition revision that created the reservation (BUD-007 idempotency key part).</summary>
    public int RequisitionRevision { get; private set; }

    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public BudgetReservationStatus Status { get; private set; }
    public BudgetReleaseReason? ReleaseReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? ReleasedAtUtc { get; private set; }

    public IReadOnlyCollection<BudgetReservationChange> Changes => changes;

    /// <summary>BUD-008: <c>Reserved</c> or <c>Committed</c>.</summary>
    public bool IsActive => Status is BudgetReservationStatus.Reserved or BudgetReservationStatus.Committed;

    internal void Record(
        BudgetReservationOperation operation, BudgetReservationStatus? from, BudgetReservationStatus to, BudgetReleaseReason? reason,
        Guid? actorId, DateTime nowUtc, string correlationId) =>
        changes.Add(new BudgetReservationChange(
            Guid.NewGuid(), Id, changes.Count + 1, operation, from, to, Amount, Currency, reason, actorId, nowUtc, correlationId));

    internal void MarkCommitted(DateTime nowUtc)
    {
        Status = BudgetReservationStatus.Committed;
        UpdatedAtUtc = nowUtc;
    }

    internal void MarkReleased(BudgetReleaseReason reason, DateTime nowUtc)
    {
        Status = BudgetReservationStatus.Released;
        ReleaseReason = reason;
        ReleasedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }
}

/// <summary>Append-only monetary history entry of a reservation (domain-model.md §3 Budgeting).</summary>
public sealed class BudgetReservationChange
{
    public const int CorrelationIdMaxLength = 128;

    private BudgetReservationChange()
    {
        Currency = string.Empty;
        CorrelationId = string.Empty;
    }

    internal BudgetReservationChange(
        Guid id, Guid reservationId, int sequence, BudgetReservationOperation operation, BudgetReservationStatus? from,
        BudgetReservationStatus to, decimal amount, string currency, BudgetReleaseReason? reason, Guid? actorId, DateTime atUtc,
        string correlationId)
    {
        Id = id;
        ReservationId = reservationId;
        Sequence = sequence;
        Operation = operation;
        From = from;
        To = to;
        Amount = amount;
        Currency = currency;
        Reason = reason;
        ActorId = actorId == Guid.Empty ? null : actorId;
        AtUtc = atUtc;
        CorrelationId = correlationId;
    }

    public Guid Id { get; private set; }
    public Guid ReservationId { get; private set; }
    public int Sequence { get; private set; }
    public BudgetReservationOperation Operation { get; private set; }
    public BudgetReservationStatus? From { get; private set; }
    public BudgetReservationStatus To { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public BudgetReleaseReason? Reason { get; private set; }
    public Guid? ActorId { get; private set; }
    public DateTime AtUtc { get; private set; }
    public string CorrelationId { get; private set; }
}

/// <summary>Append-only amount history of an allocation (BUD-003).</summary>
public sealed class BudgetAllocationAmountChange
{
    private BudgetAllocationAmountChange()
    {
    }

    internal BudgetAllocationAmountChange(Guid id, Guid allocationId, int sequence, decimal previousAmount, decimal newAmount, string? reason, Guid? actorId, DateTime atUtc)
    {
        Id = id;
        AllocationId = allocationId;
        Sequence = sequence;
        PreviousAmount = previousAmount;
        NewAmount = newAmount;
        Reason = reason;
        ActorId = actorId == Guid.Empty ? null : actorId;
        AtUtc = atUtc;
    }

    public Guid Id { get; private set; }
    public Guid AllocationId { get; private set; }
    public int Sequence { get; private set; }
    public decimal PreviousAmount { get; private set; }
    public decimal NewAmount { get; private set; }
    public string? Reason { get; private set; }
    public Guid? ActorId { get; private set; }
    public DateTime AtUtc { get; private set; }
}
